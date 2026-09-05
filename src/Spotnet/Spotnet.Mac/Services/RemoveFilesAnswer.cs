using System;

namespace Spotnet.Mac.Services;

/// <summary>
/// What the user answered when asked whether removing a download row should also
/// delete the files from disk. Port of Windows' RemoveFilesFromTheDiskDialogAnswerEnum.
/// </summary>
public enum RemoveFilesAnswer
{
    Cancel = 0,
    Yes = 1,
    No = 2
}

/// <summary>
/// Decides, for a row removal, whether the files on disk go with it — port of
/// DownloaderItems.RunRemoveFilesFromTheDiskDialog from the Windows client:
///
///   preference 1  → always delete, no question
///   preference 0  → always keep, no question
///   preference -1 → ask, and remember the answer with the checkbox when the user
///                   chooses "sla mijn antwoord op"; only asked when there is
///                   actually something on disk to remove.
///
/// The dialog itself stays in the view layer; this class holds the decision logic so
/// it is testable.
/// </summary>
public static class RemoveFilesDecision
{
    /// <summary>
    /// Which question to ask before the row disappears. Returns null when no question
    /// may be asked (a stored answer decides), or the default answer to preselect.
    /// </summary>
    /// <param name="filesOnDisk">Whether the download actually has files on disk.</param>
    public static RemoveFilesAnswer? QuestionOrDefault(int preference, bool filesOnDisk)
    {
        return preference switch
        {
            1 => RemoveFilesAnswer.Yes,      // SettingsForDownload combo row 0: always delete
            0 => RemoveFilesAnswer.No,       // combo row 1: always keep
            _ => filesOnDisk ? null : RemoveFilesAnswer.No   // -1: ask, but only with files
        };
    }

    /// <summary>
    /// Applies a user answer when the "sla mijn antwoord op" checkbox was ticked: the
    /// preference stops asking, exactly like RemoveFilesFromTheDiskDialog's buttons.
    /// Returns the preference to store.
    /// </summary>
    public static int Remember(int preference, RemoveFilesAnswer answer)
    {
        return answer switch
        {
            RemoveFilesAnswer.Yes => 1,
            RemoveFilesAnswer.No => 0,
            _ => preference
        };
    }

    /// <summary>Whether the answer means "delete the files from disk".</summary>
    public static bool DeletesFiles(RemoveFilesAnswer answer) => answer == RemoveFilesAnswer.Yes;
}
