using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Spotnet.Mac.PostProcessing;

/// <summary>The verdict of the verify-and-repair stage.</summary>
public enum Par2Result
{
    NoRepairNeeded,
    Repaired,
    /// <summary>Damaged, and there are not enough recovery blocks to fix it.</summary>
    CannotRepair,
    /// <summary>A repair ran but its output does not verify.</summary>
    RepairDidNotVerify,
    /// <summary>No par2 files, or none that could be parsed.</summary>
    NoPar2Data
}

/// <summary>
/// The verify-and-repair stage, built on <see cref="Par2Verifier"/> and
/// <see cref="Par2Repairer"/>. Replaces the Windows client's shell-out to
/// phpar2.exe; everything happens inside the app.
/// </summary>
public sealed class Par2Repair
{
    private readonly string _workingDir;
    private readonly Action<string> _log;

    /// <summary>Reports 0-100 while verifying or repairing.</summary>
    public event Action<double>? ProgressChanged;

    /// <summary>How many recovery blocks the last attempt was short. Zero when fine.</summary>
    public int BlocksShort { get; private set; }

    /// <summary>Files the last verification could not find at all.</summary>
    public IReadOnlyList<string> FilesMissing { get; private set; } = Array.Empty<string>();

    public Par2Repair(string workingDir, Action<string> log)
    {
        _workingDir = workingDir;
        _log = log;
    }

    /// <summary>True when the directory holds any par2 file.</summary>
    public bool HasPar2Files() =>
        Directory.Exists(_workingDir) &&
        Directory.GetFiles(_workingDir, "*", SearchOption.TopDirectoryOnly)
            .Any(p => ArchiveNaming.IsPar2File(Path.GetFileName(p)));

    /// <summary>
    /// Verifies the download and, when it is damaged, repairs it.
    ///
    /// <paramref name="tryFetchMoreBlocks"/> mirrors the Windows client's
    /// DownloadParPieces: when the damage exceeds the recovery blocks on hand, the
    /// caller gets a chance to fetch more par2 volumes from Usenet and the check runs
    /// again.
    /// </summary>
    public async Task<(bool ok, Par2Result result)> RunAsync(
        Func<int, CancellationToken, Task<bool>>? tryFetchMoreBlocks = null,
        CancellationToken ct = default)
    {
        BlocksShort = 0;
        FilesMissing = Array.Empty<string>();

        if (!HasPar2Files())
        {
            _log("Geen par2-bestanden gevonden, controle overgeslagen");
            return (true, Par2Result.NoPar2Data);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            Par2RecoverySet? set = await Task.Run(() => Par2RecoverySet.Load(_workingDir, _log), ct)
                .ConfigureAwait(false);

            if (set == null || !set.IsUsable)
            {
                _log("par2-set kon niet worden gelezen");
                return (false, Par2Result.NoPar2Data);
            }

            _log($"par2-set: {set.Files.Count} bestand(en), {set.TotalSlices} blokken van {set.SliceSize:N0} bytes, " +
                 $"{set.AvailableRecoveryBlocks} herstelblokken");

            var verifier = new Par2Verifier(_workingDir, set, _log);
            verifier.ProgressChanged += p => ProgressChanged?.Invoke(p);

            Par2VerifyResult verified = await Task.Run(() => verifier.Verify(ct), ct).ConfigureAwait(false);

            FilesMissing = verified.Files.Where(f => !f.Exists).Select(f => f.File.Name).ToList();

            if (verified.AllFilesComplete)
            {
                _log("Alle bestanden zijn in orde, reparatie niet nodig");
                return (true, Par2Result.NoRepairNeeded);
            }

            BlocksShort = verified.BlocksShort;

            if (!verified.CanRepair)
            {
                _log($"Reparatie onmogelijk: {verified.BlocksShort} herstelblokken tekort");

                bool fetched = tryFetchMoreBlocks != null && attempt == 0 &&
                               await tryFetchMoreBlocks(verified.BlocksShort, ct).ConfigureAwait(false);
                if (fetched) continue;

                return (false, Par2Result.CannotRepair);
            }

            var repairer = new Par2Repairer(_workingDir, set, _log);
            repairer.ProgressChanged += p => ProgressChanged?.Invoke(p);

            Par2RepairOutcome outcome = await Task.Run(() => repairer.Repair(verified, ct), ct)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case Par2RepairOutcome.Repaired:
                    return (true, Par2Result.Repaired);
                case Par2RepairOutcome.NoRepairNeeded:
                    return (true, Par2Result.NoRepairNeeded);
                case Par2RepairOutcome.NotEnoughBlocks:
                    return (false, Par2Result.CannotRepair);
                case Par2RepairOutcome.RepairDidNotVerify:
                    return (false, Par2Result.RepairDidNotVerify);
                default:
                    return (false, Par2Result.CannotRepair);
            }
        }

        return (false, Par2Result.CannotRepair);
    }

    /// <summary>Deletes the par2 files, as Settings.RemovePar2FilesAfterDownload does.</summary>
    public void RemovePar2Files()
    {
        if (!Directory.Exists(_workingDir)) return;
        foreach (string path in Directory.GetFiles(_workingDir, "*", SearchOption.TopDirectoryOnly))
        {
            if (!ArchiveNaming.IsPar2File(Path.GetFileName(path))) continue;
            try
            {
                File.Delete(path);
                _log("par2-bestand verwijderd: " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                _log("Kon par2-bestand niet verwijderen: " + ex.Message);
            }
        }
    }
}
