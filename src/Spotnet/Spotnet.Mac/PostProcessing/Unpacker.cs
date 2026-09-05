using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Spotnet.Mac.PostProcessing;

/// <summary>Outcome of the unpack step.</summary>
public enum UnpackResult
{
    /// <summary>There was nothing to unpack.</summary>
    NothingToDo,
    Unpacked,
    /// <summary>The archive is encrypted and the password is missing or wrong.</summary>
    PasswordRequired,
    /// <summary>The archive itself is damaged — par2 could not put it right.</summary>
    Corrupt,
    Failed
}

/// <summary>
/// Unpacks a finished download, following the order the Windows client's Unpack.cs
/// uses — rar first, then zip, then 7z — but doing the work in managed code rather
/// than shelling out to bundled UnRAR.exe and 7za.exe.
///
/// Everything lands in a <c>__unpack</c> staging directory first; only when a set
/// comes out clean are its volumes deleted and the staged files moved up, so a
/// failed unpack never eats the download. If the user happens to have unrar or
/// 7-Zip on the machine those are tried as a fallback for archives the managed
/// extractor cannot read, but nothing needs to be installed for the step to work.
/// </summary>
public sealed class Unpacker
{
    private readonly string _workingDir;
    private readonly PostProcessToolset _tools;
    private readonly Action<string> _log;
    private readonly ManagedArchiveExtractor _managed;
    private readonly List<string> _volumesConsumed = new();

    /// <summary>Reports 0-100 while an archive is being written out.</summary>
    public event Action<double>? ProgressChanged;

    public string UnpackTargetDir => Path.Combine(_workingDir, "__unpack");

    public Unpacker(string workingDir, PostProcessToolset tools, Action<string> log)
    {
        _workingDir = workingDir;
        _tools = tools;
        _log = log;
        _managed = new ManagedArchiveExtractor(log);
    }

    /// <summary>
    /// Runs the whole unpack sequence. <paramref name="password"/> may be empty; the
    /// header probe decides whether that is a problem before any work is done.
    /// </summary>
    public async Task<UnpackResult> RunAsync(string password, CancellationToken ct = default)
    {
        _volumesConsumed.Clear();
        password ??= "";

        List<string> sets = ArchiveSets();
        if (sets.Count == 0)
        {
            _log("Geen archieven om uit te pakken");
            return UnpackResult.NothingToDo;
        }

        // Look before leaping: an encrypted set with no password can only fail, and
        // on a 40 GB set the doomed attempt costs minutes.
        if (password.Length == 0)
        {
            ArchiveEncryption encryption = ArchivePasswordProbe.InspectDirectory(_workingDir);
            if (encryption is ArchiveEncryption.Encrypted or ArchiveEncryption.EncryptedHeaders)
            {
                _log(encryption == ArchiveEncryption.EncryptedHeaders
                    ? "Archief heeft versleutelde headers: wachtwoord vereist"
                    : "Archief is met een wachtwoord beveiligd: wachtwoord vereist");
                return UnpackResult.PasswordRequired;
            }
        }

        var result = UnpackResult.NothingToDo;

        foreach (string set in sets)
        {
            ct.ThrowIfCancellationRequested();
            _log("Uitpakken van " + set);

            UnpackResult one = await UnpackOneAsync(set, password, ct).ConfigureAwait(false);
            switch (one)
            {
                case UnpackResult.PasswordRequired:
                    return one;                       // staging is cleaned by the caller's retry
                case UnpackResult.Corrupt:
                    return Finish(UnpackResult.Corrupt);
                case UnpackResult.Failed:
                    return Finish(UnpackResult.Failed);
                case UnpackResult.Unpacked:
                    result = UnpackResult.Unpacked;
                    break;
            }
        }

        return Finish(result);
    }

    /// <summary>
    /// The first volume of every archive set in the directory, in the order Windows
    /// processes them: rar, then zip, then 7z.
    /// </summary>
    public List<string> ArchiveSets()
    {
        if (!Directory.Exists(_workingDir)) return new List<string>();

        List<string> names = Directory.GetFiles(_workingDir, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sets = new List<string>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddSet(string name)
        {
            string key = (ArchiveNaming.MultipartBase(name) ?? name) + "|" + ArchiveNaming.FamilyOf(name);
            if (handled.Add(key)) sets.Add(name);
        }

        foreach (string name in names.Where(ArchiveNaming.IsFirstRarVolume)) AddSet(name);

        // "Non standart rar archives" in the Windows client's words: posted as .001
        // with no .rar in sight, recognisable only by the signature.
        foreach (string name in names)
        {
            if (!Regex.IsMatch(name, @"\.\d+$")) continue;
            if (ArchiveNaming.IsSevenZipFile(name)) continue;
            if (!ArchiveNaming.HasRarSignature(Path.Combine(_workingDir, name))) continue;
            string key = (ArchiveNaming.MultipartBase(name) ?? name) + "|" + ArchiveNaming.VolumeFamily.Rar;
            if (handled.Contains(key)) continue;
            _log("Niet-standaard rar-archief gevonden: " + name);
            AddSet(name);
        }

        foreach (string name in names.Where(ArchiveNaming.IsZipFile)) AddSet(name);

        foreach (string name in names.Where(ArchiveNaming.IsSevenZipFile))
        {
            // For a split 7z only the .001 opens the set.
            if (Regex.IsMatch(name, @"\.7z\.\d{3}$", RegexOptions.IgnoreCase) &&
                !name.EndsWith(".001", StringComparison.OrdinalIgnoreCase)) continue;
            AddSet(name);
        }

        return sets;
    }

    // ── one set ───────────────────────────────────────────────────────────────

    private async Task<UnpackResult> UnpackOneAsync(string archive, string password, CancellationToken ct)
    {
        string path = Path.Combine(_workingDir, archive);

        ExtractResult managed = await Task.Run(
            () => _managed.Extract(path, UnpackTargetDir, password, p => ProgressChanged?.Invoke(p), ct),
            ct).ConfigureAwait(false);

        switch (managed.Outcome)
        {
            case ExtractOutcome.Extracted:
                _volumesConsumed.AddRange(managed.VolumesConsumed);
                return UnpackResult.Unpacked;

            case ExtractOutcome.PasswordRequired:
            case ExtractOutcome.WrongPassword:
                return UnpackResult.PasswordRequired;

            case ExtractOutcome.Corrupt:
                // A damaged archive is a repair problem, not an unpacker problem;
                // an external tool would fail on the same bytes.
                return UnpackResult.Corrupt;
        }

        // Unsupported or an unexpected failure: give an installed unrar/7-Zip a turn
        // if the machine happens to have one. Nothing depends on it being there.
        if (_tools.HasAnyUnpacker)
        {
            _log("Ingebouwde uitpakker kwam er niet uit, extern hulpprogramma proberen");
            ExternalUnpackResult external = await _tools
                .TryExternalExtractAsync(path, UnpackTargetDir, password, _log, ct)
                .ConfigureAwait(false);

            if (external.PasswordProblem) return UnpackResult.PasswordRequired;
            if (external.Succeeded)
            {
                _volumesConsumed.AddRange(managed.VolumesConsumed);
                return UnpackResult.Unpacked;
            }
        }

        return UnpackResult.Failed;
    }

    // ── clean up ──────────────────────────────────────────────────────────────

    /// <summary>Deletes the archives that were unpacked and moves the staged files up.</summary>
    private UnpackResult Finish(UnpackResult result)
    {
        try
        {
            if (result == UnpackResult.Unpacked && _volumesConsumed.Count > 0)
            {
                RemoveArchiveFiles();
                if (Directory.Exists(UnpackTargetDir))
                    FileMover.MoveRecursively(UnpackTargetDir, _workingDir, _log);
            }

            if (Directory.Exists(UnpackTargetDir)) Directory.Delete(UnpackTargetDir, recursive: true);
        }
        catch (IOException ex)
        {
            _log("Opruimen na uitpakken mislukt: " + ex.Message);
        }
        return result;
    }

    /// <summary>
    /// Removes the volumes that were actually consumed. The managed extractor
    /// reports the whole volume set it opened, so unlike the shell-out path there is
    /// no need to guess at siblings — and an unrelated file that merely shares a base
    /// name is never touched.
    /// </summary>
    private void RemoveArchiveFiles()
    {
        var consumed = new HashSet<string>(_volumesConsumed, StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.GetFiles(_workingDir, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (!consumed.Contains(name)) continue;

            try
            {
                File.Delete(path);
                _log("Archief verwijderd: " + name);
            }
            catch (Exception ex)
            {
                _log("Kon archief niet verwijderen: " + name + " (" + ex.Message + ")");
            }
        }
    }
}
