using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Remote;

public enum TunnelState
{
    Stopped,
    Downloading,
    Starting,
    Running,
    Failed
}

public class CloudflareTunnelService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<CloudflareTunnelService> InstanceHolder = new Lazy<CloudflareTunnelService>(() => new CloudflareTunnelService());
    public static CloudflareTunnelService Instance => InstanceHolder.Value;

    private static readonly Regex TunnelUrlRegex = new Regex(@"https://[a-zA-Z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly object _lock = new object();
    private Process _process;
    private CancellationTokenSource _cts;

    public TunnelState State { get; private set; } = TunnelState.Stopped;
    public string TunnelUrl { get; private set; } = "";
    public string StatusMessage { get; private set; } = Words.RemoteDisabled;
    public int DownloadPercentage { get; private set; } = 0;

    public event Action<TunnelState, string> StateChanged;
    public event Action<int> DownloadProgressChanged;

    public CloudflareTunnelService()
    {
        try
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();
        }
        catch
        {
            // Ignore if unable to register process exit in test runners
        }
    }

    public static string ExtractTunnelUrl(string logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine)) return null;
        var match = TunnelUrlRegex.Match(logLine);
        return match.Success ? match.Value : null;
    }

    public string GetExecutablePath()
    {
        string[] candidates = new[]
        {
            Path.Combine(AppHelper.SettingsFolder, "Tools", "cloudflared.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudflared.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "cloudflared.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Check if available on system PATH
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.Combine(dir.Trim(), "cloudflared.exe");
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Path.Combine(AppHelper.SettingsFolder, "Tools", "cloudflared.exe");
    }

    public bool IsBinaryInstalled()
    {
        string exe = GetExecutablePath();
        return File.Exists(exe);
    }

    public async Task StartAsync(int localPort)
    {
        lock (_lock)
        {
            if (State == TunnelState.Running || State == TunnelState.Starting || State == TunnelState.Downloading)
            {
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
        }

        var ct = _cts.Token;

        try
        {
            KillOrphanedCloudflaredProcesses();

            string exePath = GetExecutablePath();

            if (!File.Exists(exePath))
            {
                SetState(TunnelState.Downloading, "Cloudflared downloaden (eenmalig)...");
                await DownloadBinaryAsync(exePath, ct);
            }

            if (ct.IsCancellationRequested) return;

            SetState(TunnelState.Starting, "Cloudflare Quick Tunnel starten...");
            StartProcess(exePath, localPort);
        }
        catch (OperationCanceledException)
        {
            SetState(TunnelState.Stopped, Words.CloudflareStopped);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fout bij starten van Cloudflare Tunnel: {0}", ex.Message);
            SetState(TunnelState.Failed, string.Format(Words.CloudflareError, ex.Message));
        }
    }

    private static void KillOrphanedCloudflaredProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("cloudflared"))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.Dispose();
                }
                catch { }
            }
        }
        catch { }
    }

    private async Task DownloadBinaryAsync(string targetPath, CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string downloadUrl = Environment.Is64BitOperatingSystem
            ? "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
            : "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-386.exe";

        string tempPath = targetPath + ".tmp";

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                int percent = (int)((totalRead * 100) / totalBytes.Value);
                DownloadPercentage = percent;
                try
                {
                    DownloadProgressChanged?.Invoke(percent);
                }
                catch { }
                SetState(TunnelState.Downloading, $"Downloaden component... {percent}%");
            }
        }

        fileStream.Close();

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
        Log.Info("Cloudflared successfully downloaded to {0}", targetPath);
    }

    private void StartProcess(string exePath, int localPort)
    {
        lock (_lock)
        {
            if (_cts == null || _cts.IsCancellationRequested)
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"tunnel --url http://127.0.0.1:{localPort} --no-autoupdate",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.OutputDataReceived += OnProcessOutput;
            _process.ErrorDataReceived += OnProcessOutput;

            _process.Exited += (s, e) =>
            {
                lock (_lock)
                {
                    if (_process != null && (State == TunnelState.Running || State == TunnelState.Starting))
                    {
                        Log.Warn("Cloudflared process unexpectedly exited.");
                        SetState(TunnelState.Stopped, "Verbinding verbroken");
                    }
                }
            };

            if (!_process.Start())
            {
                throw new InvalidOperationException("Kon cloudflared.exe proces niet starten.");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Log.Info("Cloudflared process started (PID={0}) for port {1}", _process.Id, localPort);
        }
    }

    private void OnProcessOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;

        string line = e.Data;
        string url = ExtractTunnelUrl(line);

        if (!string.IsNullOrEmpty(url))
        {
            lock (_lock)
            {
                TunnelUrl = url;
                SetState(TunnelState.Running, url);
                Log.Info("Cloudflare Quick Tunnel established: {0}", url);
            }
        }
    }

    public void Stop()
    {
        Process procToKill = null;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;

            if (_process != null)
            {
                procToKill = _process;
                _process = null;
            }

            TunnelUrl = "";
            SetState(TunnelState.Stopped, Words.CloudflareStopped);
            Log.Info("Cloudflare Tunnel stopped.");
        }

        if (procToKill != null)
        {
            Task.Run(() =>
            {
                try
                {
                    procToKill.EnableRaisingEvents = false;
                    try { procToKill.CancelOutputRead(); } catch { }
                    try { procToKill.CancelErrorRead(); } catch { }

                    if (!procToKill.HasExited)
                    {
                        procToKill.Kill(entireProcessTree: true);
                        procToKill.WaitForExit(1000);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Error stopping cloudflared process: {0}", ex.Message);
                }
                finally
                {
                    try { procToKill.Dispose(); } catch { }
                }
            });
        }
    }

    private void SetState(TunnelState state, string message)
    {
        State = state;
        StatusMessage = message;
        try
        {
            StateChanged?.Invoke(state, message);
        }
        catch (Exception ex)
        {
            Log.Warn("Error in StateChanged handler: {0}", ex.Message);
        }
    }
}
