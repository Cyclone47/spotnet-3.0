using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Mac.PostProcessing;

/// <summary>What the pipeline decided about a finished download.</summary>
public enum PostProcessOutcome
{
    /// <summary>Everything verified, repaired and unpacked cleanly.</summary>
    Success,
    /// <summary>Finished, but a step reported a problem. Files are left in place.</summary>
    Warning,
    /// <summary>The archive itself is damaged and could not be repaired.</summary>
    ArchiveDamaged,
    /// <summary>Damaged and there was no par2 data to repair it with.</summary>
    ArchiveDamagedNoPar2,
    /// <summary>Stopped because the archive needs a password.</summary>
    PasswordRequired,
    Failed,
    Cancelled
}

/// <summary>Progress report for the download row.</summary>
public sealed record PostProcessProgress(DownloadStage Stage, double Percent, string? Detail = null);

/// <summary>
/// Runs everything Spotnet does after the last segment lands, in the same order as
/// the Windows client's PostProcessCoordinator.Run():
///
///   1. join <c>name.ext.001</c> split sets            (Verifiëren)
///   2. par2 quick check, then repair if it fails      (Controleren / Repareren)
///   3. unrar, then 7-Zip for zip/7z                   (Uitpakken)
///   4. delete the par2 files, lift the staged output  (Verplaatsen)
///
/// Two things differ from Windows and are deliberate. Windows downloads into an
/// "incomplete" directory and moves the result to a "complete" one at the end; the
/// macOS client already downloads into the directory the user picked, so the move
/// step only lifts the <c>__unpack</c> staging directory. And the password check runs
/// before the unpack instead of only reacting to unrar's exit code — see
/// <see cref="ArchivePasswordProbe"/>.
/// </summary>
public sealed class PostProcessCoordinator
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _workingDir;
    private readonly PostProcessToolset _tools;
    private readonly IProgress<PostProcessProgress>? _progress;
    private readonly Action<string>? _logSink;

    /// <summary>Mirrors Settings.RemovePar2FilesAfterDownload.</summary>
    public bool RemovePar2Files { get; init; } = true;

    /// <summary>
    /// Called when a repair is short of blocks, so the caller can fetch extra par2
    /// volumes from Usenet the way SpotnetDownloaderItemViewModel.DownloadParPieces
    /// does. Return true when new blocks landed and the repair should be retried.
    /// </summary>
    public Func<int, CancellationToken, Task<bool>>? FetchExtraPar2Blocks { get; init; }

    public PostProcessCoordinator(
        string workingDir,
        PostProcessToolset tools,
        IProgress<PostProcessProgress>? progress = null,
        Action<string>? logSink = null)
    {
        _workingDir = workingDir;
        _tools = tools;
        _progress = progress;
        _logSink = logSink;
    }

    public async Task<PostProcessOutcome> RunAsync(string unpackPassword, CancellationToken ct = default)
    {
        if (!Directory.Exists(_workingDir))
        {
            WriteLog("Downloadmap bestaat niet: " + _workingDir);
            return PostProcessOutcome.Failed;
        }

        WriteLog("Start nabewerking in " + _workingDir);
        var outcome = PostProcessOutcome.Success;

        try
        {
            // 1 ── split files ────────────────────────────────────────────────
            Report(DownloadStage.Verifying, -1);
            List<string> joined = SplitFileJoiner.JoinAll(_workingDir, WriteLog, ct);
            if (joined.Count > 0) WriteLog($"{joined.Count} gesplitst(e) bestand(en) samengevoegd");

            // 2 ── par2 verify and repair ─────────────────────────────────────
            var par2 = new Par2Repair(_workingDir, WriteLog);
            bool hasPar2 = par2.HasPar2Files();

            if (hasPar2)
            {
                // Verification is the quick check and the repair check in one pass:
                // slice CRC32s reject a damaged download about as fast as reading it.
                Report(DownloadStage.Checking, 0);
                par2.ProgressChanged += pct => Report(CurrentPar2Stage, pct);

                (bool repaired, Par2Result result) = await par2.RunAsync(
                    tryFetchMoreBlocks: async (blocksShort, token) =>
                    {
                        if (FetchExtraPar2Blocks == null) return false;
                        Report(DownloadStage.Par2PieceDownloading, -1);
                        bool got = await FetchExtraPar2Blocks(blocksShort, token).ConfigureAwait(false);
                        Report(DownloadStage.Repairing, 0);
                        return got;
                    },
                    ct: ct).ConfigureAwait(false);

                if (result == Par2Result.Repaired) CurrentPar2Stage = DownloadStage.Repairing;

                if (!repaired)
                {
                    // Windows carries on regardless and lets the unpack decide; so do
                    // we, because a stale par2 set next to a good download is common.
                    WriteLog(result == Par2Result.CannotRepair
                        ? "Reparatie onmogelijk, uitpakken wordt alsnog geprobeerd"
                        : "Reparatie leverde geen geldig resultaat, uitpakken wordt alsnog geprobeerd");
                    outcome = PostProcessOutcome.Warning;
                }
            }
            else
            {
                WriteLog("Geen par2-set aanwezig");
            }

            // 3 ── unpack ─────────────────────────────────────────────────────
            Report(DownloadStage.Unpacking, 0);
            var unpacker = new Unpacker(_workingDir, _tools, WriteLog);
            unpacker.ProgressChanged += pct => Report(DownloadStage.Unpacking, pct);

            UnpackResult unpack = await unpacker.RunAsync(unpackPassword ?? "", ct).ConfigureAwait(false);
            switch (unpack)
            {
                case UnpackResult.PasswordRequired:
                    WriteLog("Nabewerking wacht op een wachtwoord");
                    return PostProcessOutcome.PasswordRequired;
                case UnpackResult.Corrupt:
                    // Say which of the two it is: without par2 there was never any
                    // way back, which is a different conversation from a repair that
                    // ran and still came up short.
                    WriteLog(hasPar2
                        ? "Archief is beschadigd en kon met de par2-set niet worden hersteld"
                        : "Archief is beschadigd en er is geen par2-set om het mee te herstellen");
                    return hasPar2
                        ? PostProcessOutcome.ArchiveDamaged
                        : PostProcessOutcome.ArchiveDamagedNoPar2;
                case UnpackResult.Failed:
                    WriteLog("Uitpakken mislukt, bestanden blijven staan");
                    outcome = PostProcessOutcome.Warning;
                    break;
            }

            // 4 ── tidy up ────────────────────────────────────────────────────
            Report(DownloadStage.Moving, -1);
            if (RemovePar2Files && hasPar2) par2.RemovePar2Files();

            Report(outcome == PostProcessOutcome.Success ? DownloadStage.Success : DownloadStage.Warning, 100);
            WriteLog("Nabewerking klaar: " + (outcome == PostProcessOutcome.Success ? "succesvol" : "met problemen"));
            return outcome;
        }
        catch (OperationCanceledException)
        {
            WriteLog("Nabewerking geannuleerd");
            return PostProcessOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Post-process failed in {0}", _workingDir);
            WriteLog("Nabewerking mislukt: " + ex.Message);
            return PostProcessOutcome.Failed;
        }
    }

    /// <summary>Whether par2 progress currently means checking or repairing.</summary>
    private DownloadStage CurrentPar2Stage { get; set; } = DownloadStage.Checking;

    private void Report(DownloadStage stage, double percent, string? detail = null) =>
        _progress?.Report(new PostProcessProgress(stage, percent, detail));

    private void WriteLog(string message)
    {
        Log.Info("[postprocess] {0}", message);
        _logSink?.Invoke(message);
    }
}
