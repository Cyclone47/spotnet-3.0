using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Spotnet.Mac.PostProcessing;

/// <summary>
/// Joins <c>name.ext.001</c>, <c>name.ext.002</c>, … back into <c>name.ext</c>.
///
/// A port of PostProcessCoordinator.ProcessSplittedFiles/GetSplittedBases from the
/// Windows client, including its safety rule: a set is only joined when every part
/// is the same size except exactly one short final part, and the numbering runs
/// 1..n with no gaps. Anything else is a rar set or a partial download, and
/// concatenating it would produce garbage.
/// </summary>
public static class SplitFileJoiner
{
    /// <summary>Joins every complete split set in the directory. Returns the names written.</summary>
    public static List<string> JoinAll(string directory, Action<string> log, CancellationToken ct = default)
    {
        var joined = new List<string>();
        if (!Directory.Exists(directory)) return joined;

        List<string> names = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

        foreach (string baseName in FindJoinableBases(directory, names))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(directory, baseName))) continue;

            List<string> parts = names
                .Where(n => n.StartsWith(baseName, StringComparison.Ordinal) && ArchiveNaming.SplitPart.IsMatch(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (Join(directory, baseName, parts, log)) joined.Add(baseName);
        }

        return joined;
    }

    /// <summary>
    /// The base names whose split set is complete. Exposed for the tests, which build
    /// synthetic part sets rather than downloading one.
    /// </summary>
    public static List<string> FindJoinableBases(string directory, IReadOnlyList<string> names)
    {
        var result = new List<string>();
        var firstPart = new Regex(@"^(.*\.[a-zA-Z0-9]{3})\.001$", RegexOptions.Compiled);

        foreach (string name in names)
        {
            Match m = firstPart.Match(name);
            if (!m.Success) continue;

            string baseName = m.Groups[1].Value;
            long firstLength;
            try
            {
                firstLength = new FileInfo(Path.Combine(directory, baseName + ".001")).Length;
            }
            catch (Exception)
            {
                continue;
            }

            var numbers = new List<int>();
            int shortPart = -1;
            bool usable = true;

            foreach (string candidate in names)
            {
                if (!candidate.StartsWith(baseName, StringComparison.Ordinal)) continue;
                Match part = ArchiveNaming.SplitPart.Match(candidate);
                if (!part.Success) continue;

                int number = int.Parse(part.Groups[2].Value);
                long length = new FileInfo(Path.Combine(directory, candidate)).Length;

                if (length != firstLength)
                {
                    // Only one part may differ, and only by being shorter: the last one.
                    if (shortPart != -1 || length > firstLength) { usable = false; break; }
                    shortPart = number;
                }
                numbers.Add(number);
            }

            if (!usable || numbers.Count == 0 || shortPart == -1) continue;

            numbers.Sort();
            for (int i = 1; i <= shortPart; i++)
            {
                if (numbers.Count < i || numbers[i - 1] != i) { usable = false; break; }
            }

            if (usable) result.Add(baseName);
        }

        return result;
    }

    private static bool Join(string directory, string baseName, List<string> parts, Action<string> log)
    {
        string targetPath = Path.Combine(directory, baseName);
        try
        {
            using (FileStream target = File.Create(targetPath))
            {
                foreach (string part in parts)
                {
                    using FileStream source = File.OpenRead(Path.Combine(directory, part));
                    source.CopyTo(target);
                    log("Deel " + part + " toegevoegd aan " + baseName);
                }
            }

            foreach (string part in parts)
            {
                try
                {
                    File.Delete(Path.Combine(directory, part));
                }
                catch (Exception ex)
                {
                    log("Kon deel " + part + " niet verwijderen: " + ex.Message);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            log("Samenvoegen van gesplitste bestanden mislukt: " + ex.Message);
            return false;
        }
    }
}
