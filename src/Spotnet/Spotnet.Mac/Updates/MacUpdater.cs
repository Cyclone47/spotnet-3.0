using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Mac.Updates;

public sealed class MacUpdater : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    public const string DefaultManifestUrl = "https://raw.githubusercontent.com/Cyclone47/spotnet-3.0/macos-client/updates/latest-mac.json";

    private readonly string _dataFolder;
    private readonly string _applicationPath;
    private readonly Uri _manifestUrl;
    private readonly MacUpdateClient _client;

    public MacUpdater(string dataFolder, Uri? manifestUrl = null, string? applicationPath = null,
        HttpMessageHandler? handler = null)
    {
        _dataFolder = dataFolder ?? throw new ArgumentNullException(nameof(dataFolder));
        _manifestUrl = manifestUrl ?? new Uri(DefaultManifestUrl);
        _applicationPath = applicationPath ?? AppContext.BaseDirectory;
        _client = new MacUpdateClient(_manifestUrl, handler);
    }

    public bool IsInstalledApp =>
        ResolveApplicationPath(_applicationPath).EndsWith(".app", StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(ResolveApplicationPath(_applicationPath), "Contents", "Info.plist"));

    public async Task<(MacUpdateManifest? Manifest, MacUpdateDecision Decision, string Error)> CheckAsync(
        Version currentVersion, string? skippedVersion = null, CancellationToken cancellationToken = default)
    {
        if (!IsInstalledApp)
        {
            return (null, new MacUpdateDecision(MacUpdateAction.None, "Updates zijn alleen beschikbaar voor geïnstalleerde .app-bundels."), "");
        }

        (MacUpdateManifest? manifest, string error) = await _client.FetchManifestAsync(cancellationToken).ConfigureAwait(false);
        if (manifest == null)
            return (null, new MacUpdateDecision(MacUpdateAction.None, error), error);
        MacUpdateDecision decision = MacUpdatePolicy.Evaluate(manifest, currentVersion, skippedVersion);
        return (manifest, decision, "");
    }

    public async Task<string> DownloadAsync(MacUpdateManifest manifest, IProgress<MacUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(_dataFolder, "Updates");
        return await _client.DownloadAsync(manifest, directory, progress, cancellationToken).ConfigureAwait(false);
    }

    public void SkipVersion(MacUpdateManifest manifest)
    {
        File.WriteAllText(Path.Combine(_dataFolder, "update-skipped-version.txt"), manifest.Version.ToString());
    }

    public string? ReadSkippedVersion()
    {
        try
        {
            string path = Path.Combine(_dataFolder, "update-skipped-version.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void InstallAndRestart(string zipPath)
    {
        if (!IsInstalledApp) throw new InvalidOperationException("Installeer updates vanuit de geïnstalleerde Spotnet.app.");
        if (!File.Exists(zipPath)) throw new FileNotFoundException("De update-download ontbreekt.", zipPath);
        if (!Directory.Exists(_applicationPath)) throw new DirectoryNotFoundException(_applicationPath);

        string script = Path.Combine(Path.GetTempPath(), "spotnet-update-" + Guid.NewGuid().ToString("N") + ".sh");
        string currentApp = ResolveApplicationPath(_applicationPath);
        if (!currentApp.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("De huidige Spotnet.app kon niet worden bepaald.");
        string escapedScriptPath = ShellQuote(script);
        string escapedZipPath = ShellQuote(zipPath);
        string escapedAppPath = ShellQuote(currentApp);
        string escapedPid = Environment.ProcessId.ToString();
        string escapedDataFolder = ShellQuote(_dataFolder);

        string scriptContent = $"#!/bin/sh\n"
            + "set -eu\n"
            + $"ZIP={escapedZipPath}\nAPP={escapedAppPath}\nPID={escapedPid}\nDATA={escapedDataFolder}\n"
            + "while kill -0 \"$PID\" 2>/dev/null; do sleep 1; done\n"
            + "TMP=\"$(mktemp -d \"${TMPDIR:-/tmp}/spotnet-update.XXXXXX\")\"\n"
            + "trap 'rm -rf \"$TMP\"' EXIT\n"
            + "ditto -x -k \"$ZIP\" \"$TMP\"\n"
            + "NEW=\"$(find \"$TMP\" -maxdepth 2 -name 'Spotnet.app' -type d -print -quit)\"\n"
            + "if [ -z \"$NEW\" ]; then exit 20; fi\n"
            + "BACKUP=\"$APP.previous\"\n"
            + "rm -rf \"$BACKUP\"\n"
            + "mv \"$APP\" \"$BACKUP\"\n"
            + "mv \"$NEW\" \"$APP\"\n"
            + "open \"$APP\"\n"
            + "rm -rf \"$BACKUP\"\n";

        File.WriteAllText(script, scriptContent);
        Process.Start(new ProcessStartInfo("/bin/chmod", "+x " + escapedScriptPath) { UseShellExecute = false });
        Process.Start(new ProcessStartInfo("/bin/sh", escapedScriptPath) { UseShellExecute = false });
        Log.Info("MacOS-update gestart vanuit {0}.", zipPath);
    }

    private static string ResolveApplicationPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (fullPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return fullPath;

        DirectoryInfo? directory = new DirectoryInfo(fullPath);
        while (directory != null)
        {
            if (directory.Name.Equals("Contents", StringComparison.OrdinalIgnoreCase)
                && directory.Parent != null)
            {
                return directory.Parent.FullName;
            }
            directory = directory.Parent;
        }
        return fullPath;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    public void Dispose() => _client.Dispose();
}

public static class MacUpdateVersion
{
    public static Version Current => typeof(MacUpdater).Assembly.GetName().Version ?? new Version(3, 0, 0, 0);
}
