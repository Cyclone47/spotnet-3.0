using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Spotnet.Mac.PostProcessing;

/// <summary>How one archive set came out.</summary>
public enum ExtractOutcome
{
    Extracted,
    /// <summary>Encrypted and no password was supplied.</summary>
    PasswordRequired,
    /// <summary>A password was supplied and it did not work.</summary>
    WrongPassword,
    /// <summary>A format or feature this extractor cannot handle. Worth a fallback.</summary>
    Unsupported,
    /// <summary>The archive is damaged — a repair should have happened first.</summary>
    Corrupt,
    Failed
}

public sealed record ExtractResult(ExtractOutcome Outcome, List<string> VolumesConsumed, string? Message = null);

/// <summary>
/// Unpacks rar, zip, 7z and tar entirely in managed code, via SharpCompress.
///
/// The Windows client shells out to bundled UnRAR.exe and 7za.exe. Those binaries
/// cannot be shipped the same way on macOS, and requiring a Homebrew install before
/// the app works is not acceptable, so the unpacker is part of the app instead: a
/// NuGet reference is compiled in and travels with the bundle on both Apple Silicon
/// and Intel, with nothing for the user to install.
/// </summary>
public sealed class ManagedArchiveExtractor
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Action<string> _log;

    public ManagedArchiveExtractor(Action<string> log) => _log = log;

    /// <summary>
    /// Extracts <paramref name="archivePath"/> — and, for a multi-volume set, its
    /// sibling volumes — into <paramref name="targetDir"/>.
    /// </summary>
    public ExtractResult Extract(
        string archivePath,
        string targetDir,
        string password,
        Action<double>? progress = null,
        CancellationToken ct = default)
    {
        var consumed = new List<string>();
        string name = Path.GetFileName(archivePath);

        try
        {
            List<FileInfo> volumes = VolumesOf(archivePath);
            consumed.AddRange(volumes.Select(v => v.Name));

            var options = new ReaderOptions
            {
                Password = password.Length == 0 ? null : password,
                LeaveStreamOpen = false,
                // We verify with par2 ourselves; a set missing a trailing volume should
                // still yield the entries it does have rather than throwing up front.
                DisableCheckIncomplete = true
            };

            using IArchive archive = OpenArchive(volumes, options);

            // Materialise the entry list once, up front. SharpCompress throws while
            // reading a damaged archive's headers — but only on the first pass; a
            // second enumeration quietly yields nothing, which would look like an
            // empty archive rather than a broken one.
            List<IArchiveEntry> entries = archive.Entries.ToList();

            if (IsEncrypted(archive, entries) && password.Length == 0)
            {
                _log(name + " is met een wachtwoord beveiligd");
                return new ExtractResult(ExtractOutcome.PasswordRequired, consumed);
            }

            Directory.CreateDirectory(targetDir);

            long totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(e.Size, 0));
            long doneBytes = 0;
            int files = 0;

            foreach (IArchiveEntry entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;

                string? key = entry.Key;
                if (string.IsNullOrEmpty(key)) continue;

                string destination = SafeDestination(targetDir, key);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                using (Stream source = entry.OpenEntryStream())
                using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        target.Write(buffer, 0, read);
                        doneBytes += read;
                        if (totalBytes > 0) progress?.Invoke(Math.Clamp(doneBytes * 100.0 / totalBytes, 0, 100));
                    }
                }

                files++;
                _log("Uitgepakt: " + key);
            }

            if (files == 0)
            {
                // An archive in a Usenet download that yields nothing is damaged, not
                // unsupported — handing the same bytes to an external tool will not
                // help. (A genuinely empty archive lands here too, and reporting it as
                // damaged is the right call for a download that produced no files.)
                _log(name + " bevat geen uitpakbare bestanden");
                return new ExtractResult(ExtractOutcome.Corrupt, consumed, "geen bestanden in het archief");
            }

            return new ExtractResult(ExtractOutcome.Extracted, consumed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsCryptoFailure(ex))
        {
            // SharpCompress surfaces a bad AES key this way.
            _log("Wachtwoord onjuist voor " + name + ": " + ex.Message);
            return new ExtractResult(
                password.Length == 0 ? ExtractOutcome.PasswordRequired : ExtractOutcome.WrongPassword, consumed);
        }
        catch (Exception ex) when (IsPasswordProblem(ex))
        {
            _log("Wachtwoordprobleem bij " + name + ": " + ex.Message);
            return new ExtractResult(
                password.Length == 0 ? ExtractOutcome.PasswordRequired : ExtractOutcome.WrongPassword, consumed);
        }
        catch (InvalidFormatException ex)
        {
            if (password.Length > 0 && ArchivePasswordProbe.Inspect(archivePath) is ArchiveEncryption.Encrypted or ArchiveEncryption.EncryptedHeaders)
            {
                _log("Wachtwoord onjuist voor " + name + ": " + ex.Message);
                return new ExtractResult(ExtractOutcome.WrongPassword, consumed);
            }
            // "Unknown Rar Header" and friends: the bytes on disk are not a valid
            // archive. Almost always a download that par2 could not put right.
            _log("Archief " + name + " is beschadigd of niet leesbaar: " + ex.Message);
            return new ExtractResult(ExtractOutcome.Corrupt, consumed, ex.Message);
        }
        catch (ArchiveException ex)
        {
            if (password.Length > 0 && ArchivePasswordProbe.Inspect(archivePath) is ArchiveEncryption.Encrypted or ArchiveEncryption.EncryptedHeaders)
            {
                _log("Wachtwoord onjuist voor " + name + ": " + ex.Message);
                return new ExtractResult(ExtractOutcome.WrongPassword, consumed);
            }
            // SharpCompress raises this for malformed data — "Failed to locate the
            // Zip Header", "Cannot determine compressed stream type" and friends.
            // That is damage, not an unsupported feature, and an external unpacker
            // would choke on exactly the same bytes.
            _log("Archief " + name + " is beschadigd of onleesbaar: " + ex.Message);
            return new ExtractResult(ExtractOutcome.Corrupt, consumed, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            // A feature we lack rather than broken bytes: worth a fallback if the
            // machine happens to have unrar or 7-Zip.
            _log("Archiefvorm van " + name + " wordt niet ondersteund: " + ex.Message);
            return new ExtractResult(ExtractOutcome.Unsupported, consumed, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Managed extract failed for {0}", archivePath);
            _log("Uitpakken van " + name + " mislukt: " + ex.Message);
            return new ExtractResult(ExtractOutcome.Failed, consumed, ex.Message);
        }
    }

    /// <summary>Reads the entry list without extracting, to report encryption.</summary>
    public ArchiveEncryption Inspect(string archivePath, string password = "")
    {
        try
        {
            var options = new ReaderOptions
            {
                Password = password.Length == 0 ? null : password,
                DisableCheckIncomplete = true
            };
            using IArchive archive = OpenArchive(VolumesOf(archivePath), options);
            List<IArchiveEntry> entries = archive.Entries.ToList();
            return IsEncrypted(archive, entries) ? ArchiveEncryption.Encrypted : ArchiveEncryption.None;
        }
        catch (Exception ex) when (IsCryptoFailure(ex) || IsPasswordProblem(ex))
        {
            return ArchiveEncryption.EncryptedHeaders;
        }
        catch (Exception)
        {
            return ArchiveEncryption.Unknown;
        }
    }

    // ── internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The volumes belonging to the set that starts at <paramref name="archivePath"/>.
    /// SharpCompress knows the rar naming conventions, so we let it decide rather
    /// than re-deriving .partNN/.rNN rules here.
    /// </summary>
    private static List<FileInfo> VolumesOf(string archivePath)
    {
        var first = new FileInfo(archivePath);
        if (!RarArchive.IsRarFile(first)) return new List<FileInfo> { first };

        try
        {
            List<FileInfo> parts = ArchiveFactory.GetFileParts(first).ToList();
            return parts.Count > 0 ? parts : new List<FileInfo> { first };
        }
        catch (Exception)
        {
            return new List<FileInfo> { first };
        }
    }

    private static IArchive OpenArchive(List<FileInfo> volumes, ReaderOptions options) =>
        volumes.Count > 1
            ? ArchiveFactory.OpenArchive(volumes, options)
            : ArchiveFactory.OpenArchive(volumes[0], options);

    /// <summary>
    /// Whether the archive says its entries are encrypted.
    ///
    /// Failing to read the entry list is deliberately NOT treated as "encrypted".
    /// An earlier version did, on the theory that listing can itself need a
    /// password — but a merely damaged archive throws here too, and reporting
    /// "Wachtwoord vereist" for a corrupt download sends the user hunting for a
    /// password that does not exist. A genuinely encrypted-header archive still
    /// gets caught: either the header probe sees the RAR5 crypt record up front, or
    /// the crypto exception from the caller's try/catch classifies it.
    /// </summary>
    private static bool IsEncrypted(IArchive archive, List<IArchiveEntry> entries)
    {
        if (archive is RarArchive { IsEncrypted: true }) return true;
        return entries.Any(e => e.IsEncrypted);
    }

    private static bool IsCryptoFailure(Exception ex) =>
        ex is SharpCompress.Common.CryptographicException
           or System.Security.Cryptography.CryptographicException;

    /// <summary>
    /// Only a message that actually names a password or encryption counts. An
    /// earlier version also treated "Cannot determine compressed stream type" as a
    /// password problem — 7-Zip with encrypted headers can surface that way — but so
    /// does any file that simply is not an archive, and telling someone their
    /// corrupt download needs a password sends them hunting for one that does not
    /// exist. Encrypted archives are caught up front by
    /// <see cref="ArchivePasswordProbe"/> instead.
    /// </summary>
    private static bool IsPasswordProblem(Exception ex)
    {
        string m = ex.Message;
        return m.Contains("password", StringComparison.OrdinalIgnoreCase)
            || m.Contains("encrypted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps an entry name onto a path inside the target directory, refusing to let
    /// an archive write outside it. Usenet archives are untrusted input and "../"
    /// entries are a real attack, not a hypothetical one.
    /// </summary>
    private static string SafeDestination(string targetDir, string entryKey)
    {
        string cleaned = entryKey.Replace('\\', '/');
        var safeParts = new List<string>();
        foreach (string part in cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "." || part == "..") continue;
            safeParts.Add(string.Join("_", part.Split(Path.GetInvalidFileNameChars())));
        }
        if (safeParts.Count == 0) safeParts.Add("bestand");

        string full = Path.GetFullPath(Path.Combine(new[] { targetDir }.Concat(safeParts).ToArray()));
        string root = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.Ordinal))
            throw new IOException("Archiefingang wijst buiten de doelmap: " + entryKey);

        return full;
    }
}
