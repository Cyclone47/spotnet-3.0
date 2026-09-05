using System;
using System.IO;

namespace Spotnet.Mac.PostProcessing;

/// <summary>
/// Moves a directory tree into another, overwriting. A port of
/// AppHelper.MoveFilesRecursively, which the Windows post-process uses both to lift
/// the staged unpack output up and to move a finished download to its final home.
/// </summary>
public static class FileMover
{
    public static void MoveRecursively(string sourceDir, string targetDir, Action<string> log,
                                       params string[] directoriesToSkip)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string target = Path.Combine(targetDir, Path.GetFileName(file));
            try
            {
                File.Move(file, target, overwrite: true);
            }
            catch (Exception ex)
            {
                log("Kon " + Path.GetFileName(file) + " niet verplaatsen: " + ex.Message);
            }
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string name = Path.GetFileName(dir);
            if (Array.Exists(directoriesToSkip, s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            MoveRecursively(dir, Path.Combine(targetDir, name), log, directoriesToSkip);

            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch (Exception ex)
            {
                log("Kon map " + name + " niet verwijderen: " + ex.Message);
            }
        }
    }
}
