using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Deployment;

/// <summary>
/// Keeps an installed copy current from the project's own repository. The manifest names
/// the release and carries the gate that decides whether clients may have it; this class
/// owns when to look, what the user is asked, and how the installer is handed control.
///
/// Only installed copies take part. A build running out of a development output has no
/// setup to replace it and is left alone.
/// </summary>
internal static class AppUpdater
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Used only when the check on the splash screen did not get an answer. Long enough
    /// that a retry never competes with the first database work.
    /// </summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1.0);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4.0);

    /// <summary>
    /// How long startup will wait for the update server. Short: the splash screen is
    /// holding the application open, and a slow or unreachable server must cost the user
    /// a moment, not a wait. Whatever does not arrive in time is picked up by the timer.
    /// </summary>
    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(3.0);

    private static Timer _timer;
    private static int _busy;
    private static bool _startupCheckAnswered;

    /// <summary>
    /// Set once Setup has been started and is waiting for this process to let go of its
    /// files. Shutdown ends the process outright in that case rather than leaving threads
    /// to wind down in their own time, which Setup can only sit and wait for.
    /// </summary>
    internal static bool HandoverInProgress { get; private set; }

    /// <summary>Raised on the pool thread when a check found something to offer.</summary>
    internal static event Action<UpdateManifest, UpdateDecision> UpdateOffered;

    internal static bool IsSupported => InstalledProfile.Enabled;

    /// <summary>
    /// Where the manifest lives. A settings entry overrides it, which is how a release is
    /// rehearsed against a private copy of the file before the real one is published.
    /// </summary>
    internal static Uri ManifestUrl
    {
        get
        {
            string configured = Settings.Default.UpdateManifestUrl;
            if (!string.IsNullOrWhiteSpace(configured)
                && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri overridden))
            {
                return overridden;
            }
            return new Uri(Configuration.UpdateManifestUrl, UriKind.Absolute);
        }
    }

    internal static string DownloadDirectory => Path.Combine(InstalledProfile.Root, "Updates");

    internal static void StartPeriodicCheck()
    {
        if (!IsSupported)
        {
            Log.Debug("Automatic updates are for installed copies only; this one is not.");
            return;
        }
        if (_timer != null) return;
        // Startup already asked, so the next one is a whole interval away. Only a check
        // that never got an answer is retried soon.
        TimeSpan first = _startupCheckAnswered ? CheckInterval : FirstCheckDelay;
        _timer = new Timer(OnTimer, null, first, CheckInterval);
        Log.Debug("Update checks scheduled: first in {0}, then every {1}.", first, CheckInterval);
    }

    /// <summary>Ask before any main-window, provider or database initialization.</summary>
    internal static async Task<bool> CheckOnStartupAsync(Action<string, string> showMessage,
        Func<UpdateManifest, UpdateDecision, Task<bool>> prompt)
    {
        if (!IsSupported || !Settings.Default.AutoUpdateEnabled) return true;
        showMessage?.Invoke("Controleren op updates...", "Checking for updates...");
        var gate = new StartupUpdateGate();
        bool proceed = await gate.RunAsync(CheckAsync, prompt, StartupBudget);
        _startupCheckAnswered = gate.Answered;
        return proceed;
    }
    internal static void StopPeriodicCheck()
    {
        Timer timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
    }

    private static void OnTimer(object state)
    {
        if (!Settings.Default.AutoUpdateEnabled)
        {
            Log.Debug("Automatic updates are switched off.");
            return;
        }
        // One check at a time. A slow download must not have a second check start behind it.
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                (UpdateManifest manifest, UpdateDecision decision, string error) =
                    await CheckAsync(CancellationToken.None).ConfigureAwait(false);
                if (error != null)
                {
                    // Offline, private repository, server trouble: all normal here.
                    Log.Debug("Update check: {0}", error);
                    return;
                }
                Log.Debug("Update check: {0}", decision.Reason);
                if (decision.ShouldPrompt) UpdateOffered?.Invoke(manifest, decision);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    /// <summary>
    /// Reads the manifest and applies the release rules to it. Never throws; a failure to
    /// reach the server comes back as a reason.
    /// </summary>
    internal static async Task<(UpdateManifest Manifest, UpdateDecision Decision, string Error)> CheckAsync(
        CancellationToken cancellationToken)
    {
        using var client = new UpdateClient(ManifestUrl);
        (UpdateManifest manifest, string error) =
            await client.FetchManifestWithReasonAsync(cancellationToken).ConfigureAwait(false);
        if (manifest == null)
        {
            return (null, new UpdateDecision(UpdateAction.None, error), error);
        }
        UpdateDecision decision = UpdatePolicy.Evaluate(manifest, AppHelper.AppVersion, Settings.Default.UpdateSkippedVersion);
        return (manifest, decision, null);
    }

    internal static void SkipVersion(Version version)
    {
        if (version == null) return;
        Settings.Default.UpdateSkippedVersion = version.ToString();
        Settings.Default.Save();
        Log.Info("The user chose to skip Spotnet {0}.", version);
    }

    /// <summary>
    /// Hands the verified installer control and closes Spotnet so its files can be
    /// replaced. Setup shows its own progress and starts Spotnet again when it is done.
    /// </summary>
    internal static void InstallAndRestart(string setupPath)
    {
        if (string.IsNullOrEmpty(setupPath) || !File.Exists(setupPath))
        {
            throw new FileNotFoundException("The downloaded installer is gone.", setupPath);
        }

        string logPath = Path.Combine(DownloadDirectory,
            "install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
        // /SILENT keeps Setup's progress window, which is the only progress that can be
        // shown once this process is gone. /RELAUNCH is Spotnet's own switch and starts
        // the application again at the end.
        string arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH "
            + "/LOG=\"" + logPath + "\"";
        Log.Info("Starting the update installer: {0} {1}", setupPath, arguments);

        Process.Start(new ProcessStartInfo(setupPath, arguments) { UseShellExecute = true });

        // Leave at once. Setup waits on this process before it touches the files, and a
        // clean shutdown is what closes the spot database properly. The flag lets the
        // window's teardown finish the job by ending the process, instead of leaving
        // Setup to wait out the threads that outlive the window.
        HandoverInProgress = true;
        Sys.Shutdown();
    }

    /// <summary>
    /// Removes installers left behind by earlier updates, keeping anything for the version
    /// that is being offered now. Failure here is never worth reporting.
    /// </summary>
    internal static void CleanupOldDownloads(Version keep = null)
    {
        try
        {
            if (!Directory.Exists(DownloadDirectory)) return;
            string keepName = keep == null ? null : "Spotnet-3.0-x64-Setup-" + keep + ".exe";
            foreach (string file in Directory.EnumerateFiles(DownloadDirectory))
            {
                string name = Path.GetFileName(file);
                if (keepName != null && name.Equals(keepName, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Could not tidy the update folder: {0}", ex.Message);
        }
    }
}
