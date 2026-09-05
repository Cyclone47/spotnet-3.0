using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Mac.PostProcessing;

/// <summary>Result of handing an archive to an external unpacker.</summary>
public sealed record ExternalUnpackResult(bool Succeeded, bool PasswordProblem);

/// <summary>
/// Optional external helpers.
///
/// The Windows client ships UnRAR.exe, 7za.exe and phpar2.exe next to the binary
/// and depends on them absolutely. The macOS client does not: verification, repair
/// and unpacking are all built in (see <see cref="Par2Repairer"/> and
/// <see cref="ManagedArchiveExtractor"/>), so nothing here needs to exist. What this
/// class finds is used only as a fallback for archives the built-in extractor cannot
/// read — a nicety on a developer's machine, never a prerequisite for a user's.
/// </summary>
public sealed class PostProcessToolset
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Directories searched, in order, before $PATH.</summary>
    private static readonly string[] SearchDirs =
    {
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/opt/local/bin",   // MacPorts
        "/opt/pkg/bin",     // pkgsrc
        "/usr/bin"
    };

    /// <summary>unrar, if the machine has one.</summary>
    public string? UnrarPath { get; }

    /// <summary>7-Zip (7zz/7z/7za), if the machine has one.</summary>
    public string? SevenZipPath { get; }

    /// <summary>The bundle's own tools directory, searched first.</summary>
    public string BundledToolsDir { get; }

    /// <summary>True when some external unpacker is available as a fallback.</summary>
    public bool HasAnyUnpacker => UnrarPath != null || SevenZipPath != null;

    public PostProcessToolset(string? bundledToolsDir = null)
    {
        BundledToolsDir = bundledToolsDir ?? DefaultBundledToolsDir();

        UnrarPath = Find("unrar");
        SevenZipPath = Find("7zz", "7z", "7za");

        Log.Info("Optional external unpackers: unrar={0} 7z={1}",
            UnrarPath ?? "(none)", SevenZipPath ?? "(none)");
    }

    /// <summary>
    /// Hands one archive to an external unpacker. Only called when the built-in
    /// extractor could not read it, so a false return simply means "no luck".
    /// </summary>
    public async Task<ExternalUnpackResult> TryExternalExtractAsync(
        string archivePath,
        string targetDir,
        string password,
        Action<string> log,
        CancellationToken ct)
    {
        string workingDir = Path.GetDirectoryName(archivePath) ?? ".";
        string archive = Path.GetFileName(archivePath);
        Directory.CreateDirectory(targetDir);

        if (UnrarPath != null && ArchiveNaming.IsRarFile(archive))
        {
            var args = new List<string> { "x", "-y", "-o+", "-kb" };
            args.Add(password.Length == 0 ? "-p-" : "-p" + password);
            // "./" so a release named "-something.rar" is not read as a switch.
            args.Add("./" + archive);
            args.Add(targetDir + Path.DirectorySeparatorChar);

            var runner = new ProcessRunner(UnrarPath, args, workingDir);
            bool allOk = false, wrongPassword = false;
            int exit = await runner.RunAsync(
                onOutput: line =>
                {
                    if (line.Trim() == "All OK") allOk = true;
                    if (line.Contains("wrong password", StringComparison.OrdinalIgnoreCase)) wrongPassword = true;
                },
                onError: line =>
                {
                    if (line.Length == 0) return;
                    log("unrar: " + line);
                    if (line.Contains("wrong password", StringComparison.OrdinalIgnoreCase)) wrongPassword = true;
                },
                ct: ct).ConfigureAwait(false);

            if (exit == 11 || wrongPassword) return new ExternalUnpackResult(false, true);
            if (exit == 0 && allOk) return new ExternalUnpackResult(true, false);
            log($"unrar exitcode {exit} voor {archive}");
        }

        if (SevenZipPath != null)
        {
            var args = new List<string>
            {
                "x", "-y",
                password.Length == 0 ? "-p" : "-p" + password,
                "-o" + targetDir,
                "./" + archive
            };

            var runner = new ProcessRunner(SevenZipPath, args, workingDir);
            bool wrongPassword = false, errors = false;
            int exit = await runner.RunAsync(
                onOutput: line =>
                {
                    if (line.StartsWith("Archives with Errors:", StringComparison.Ordinal)) errors = true;
                    if (line.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)) wrongPassword = true;
                },
                onError: line =>
                {
                    if (line.Length == 0) return;
                    log("7z: " + line);
                    if (line.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)) wrongPassword = true;
                },
                ct: ct).ConfigureAwait(false);

            if (wrongPassword) return new ExternalUnpackResult(false, true);
            if (exit == 0 && !errors) return new ExternalUnpackResult(true, false);
            log($"7-Zip exitcode {exit} voor {archive}");
        }

        return new ExternalUnpackResult(false, false);
    }

    private static string DefaultBundledToolsDir()
    {
        // AppContext.BaseDirectory inside a .app is Contents/MacOS/, so tools would
        // live one level up in Contents/Resources/tools. Nothing ships there today.
        string baseDir = AppContext.BaseDirectory;
        string bundled = Path.Combine(baseDir, "..", "Resources", "tools");
        return Directory.Exists(bundled) ? Path.GetFullPath(bundled) : Path.Combine(baseDir, "tools");
    }

    private string? Find(params string[] names)
    {
        foreach (string name in names)
        {
            foreach (string dir in Directories())
            {
                if (dir.Length == 0) continue;
                string candidate = Path.Combine(dir, name);
                if (IsExecutable(candidate)) return candidate;
            }
        }
        return null;
    }

    private IEnumerable<string> Directories()
    {
        yield return BundledToolsDir;
        foreach (string d in SearchDirs) yield return d;

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string d in path.Split(':', StringSplitOptions.RemoveEmptyEntries)) yield return d;
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (OperatingSystem.IsWindows()) return true;   // no mode bits to consult
            UnixFileMode mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.OtherExecute | UnixFileMode.GroupExecute | UnixFileMode.UserExecute)) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
