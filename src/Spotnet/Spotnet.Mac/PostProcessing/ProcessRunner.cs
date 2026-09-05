using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Mac.PostProcessing;

/// <summary>
/// Runs a command-line tool and streams its stdout/stderr back line by line.
/// The macOS counterpart of Spotnet.Helpers.ProcessEx: the post-process steps are
/// driven entirely by parsing tool output, so line callbacks — not a buffered
/// string at the end — are the whole point.
/// </summary>
public sealed class ProcessRunner
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Characters that force a quoted argument in <see cref="CommandLine"/>.</summary>
    private static readonly SearchValues<char> NeedsQuoting = SearchValues.Create(" \"'*?");

    /// <summary>Exit code used when the tool could not be started at all.</summary>
    public const int CouldNotStart = -1;

    public string FileName { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string WorkingDirectory { get; }

    public ProcessRunner(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        FileName = fileName;
        Arguments = new List<string>(arguments);
        WorkingDirectory = workingDirectory;
    }

    /// <summary>The command as it would be typed, for the log.</summary>
    public string CommandLine
    {
        get
        {
            var sb = new StringBuilder(Quote(FileName));
            foreach (string a in Arguments) sb.Append(' ').Append(Quote(a));
            return sb.ToString();
        }
    }

    private static string Quote(string s) =>
        s.Length > 0 && s.AsSpan().IndexOfAny(NeedsQuoting) < 0 ? s : "\"" + s.Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// Starts the tool, pumps both output streams through the callbacks and returns
    /// its exit code. On cancellation the process is killed and the token's
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    public async Task<int> RunAsync(
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FileName,
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string a in Arguments) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) onError?.Invoke(e.Data); };

        try
        {
            if (!proc.Start()) return CouldNotStart;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to start {0}", FileName);
            return CouldNotStart;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // unrar and 7z prompt on stdin when they want a password; closing stdin makes
        // them fail with their password exit code instead of hanging forever.
        try { proc.StandardInput.Close(); } catch (Exception) { /* already gone */ }

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        // WaitForExit() with no timeout after WaitForExitAsync flushes the async readers.
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
        catch (Exception ex) { Log.Debug(ex, "Kill failed"); }
    }
}
