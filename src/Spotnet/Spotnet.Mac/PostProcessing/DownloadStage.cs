namespace Spotnet.Mac.PostProcessing;

/// <summary>
/// The stage a download row is in. A port of Spotnet.Downloader.DownloadStatus from
/// the Windows client, trimmed to the states the macOS client can actually reach
/// (no NzbGet-external states, no Totals row).
/// </summary>
public enum DownloadStage
{
    Unknown,
    NzbSaved,
    Queued,
    Downloading,
    Pausing,
    Paused,

    // ── post-processing, in the order PostProcessCoordinator walks them ────────
    /// <summary>Joining <c>name.ext.001/.002/…</c> split sets back together.</summary>
    Verifying,
    /// <summary>par2 quick check: file MD5s against the par2 FileDesc packets.</summary>
    Checking,
    /// <summary>Fetching extra par2 recovery blocks because a repair needs them.</summary>
    Par2PieceDownloading,
    Repairing,
    Unpacking,
    Moving,

    /// <summary>The archive is encrypted and no (or a wrong) password is set.</summary>
    WrongPassword,

    Success,
    /// <summary>Finished, but a post-process step reported a problem.</summary>
    Warning,
    Failure,
    Cancelled
}

/// <summary>
/// The Dutch status labels, taken verbatim from the Windows client's
/// Spotnet.Properties.Words.nl.resx so both clients read identically.
/// </summary>
public static class DownloadStageText
{
    public static string Label(DownloadStage stage) => stage switch
    {
        DownloadStage.NzbSaved             => "NZB opgeslagen",
        DownloadStage.Queued               => "Wachten",           // StatQueued
        DownloadStage.Downloading          => "Downloaden",        // StatDownloading
        DownloadStage.Pausing              => "Pauzeren",          // Pausing
        DownloadStage.Paused               => "Gepauzeerd",        // StatPaused
        DownloadStage.Verifying            => "Verifiëren",        // StatVerifying
        DownloadStage.Checking             => "Controleren",       // StatQuickCheck
        DownloadStage.Par2PieceDownloading => "Par2 downloaden",   // StatPar2Downloading
        DownloadStage.Repairing            => "Repareren",         // StatRepairing
        DownloadStage.Unpacking            => "Uitpakken",         // StatExtracting
        DownloadStage.Moving               => "Verplaatsen",       // StatMoving
        DownloadStage.WrongPassword        => "Wachtwoord?",       // StatWrongUnpackPassword
        DownloadStage.Success              => "Compleet",          // StatCompleted
        DownloadStage.Warning              => "Waarschuwing",      // Warning
        DownloadStage.Failure              => "Mislukt",           // StatFailed
        DownloadStage.Cancelled            => "Geannuleerd",
        _                                  => "Onbekend"           // Unknown
    };

    /// <summary>True while a post-process step owns the row.</summary>
    public static bool IsPostProcessing(DownloadStage stage) => stage
        is DownloadStage.Verifying
        or DownloadStage.Checking
        or DownloadStage.Par2PieceDownloading
        or DownloadStage.Repairing
        or DownloadStage.Unpacking
        or DownloadStage.Moving;

    /// <summary>True once the row will not change on its own any more.</summary>
    public static bool IsTerminal(DownloadStage stage) => stage
        is DownloadStage.Success
        or DownloadStage.Warning
        or DownloadStage.Failure
        or DownloadStage.Cancelled
        or DownloadStage.NzbSaved;
}
