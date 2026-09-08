using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Mac.Models;

namespace Spotnet.Mac.Media;

/// <summary>
/// Zoekt afspeelbare bestanden bij een download — de port van Windows'
/// <c>Spotnet.Model.Player</c>: dezelfde 54 ondersteunde extensies, dezelfde
/// uitzonderingen (<c>.IFO</c> eruit, <c>__unpack</c>-mappen overgeslagen,
/// drievoudige herhaling bij leesfouten) en hetzelfde wachten tot er een
/// bestand > 0 bytes is, zodat een voorbeeld al tijdens het downloaden start.
/// </summary>
public static class PlayerPlaylistService
{
    /// <summary>Exact de extensielijst van Windows' Player._extentionsSupported.</summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ASX", ".DTS", ".GXF", ".M2V", ".M3U", ".M4V", ".MPEG1", ".MPEG2", ".MTS", ".MXF",
        ".OGM", ".PLS", ".A52", ".AAC", ".B4S", ".CUE", ".DIVX", ".DV", ".FLV", ".M1V",
        ".M2TS", ".MKV", ".MOV", ".MPEG4", ".OMA", ".SPX", ".TS", ".VLC", ".VOB", ".XSPF",
        ".DAT", ".BIN", ".IFO", ".3G2", ".AVI", ".MPEG", ".MPG", ".FLAC", ".M4A", ".MP1",
        ".OGG", ".WAV", ".XM", ".3GP", ".WMV", ".AC3", ".ASF", ".MOD", ".MP2", ".MP3",
        ".MP4", ".WMA", ".MKA", ".M4P"
    };

    /// <summary>Directory's waarvan bestanden overgeslagen worden (zoals Windows).</summary>
    private const string IgnoredDirectoryMarker = "__unpack";

    /// <summary>Is deze extensie in de playlist opgenomen? (.IFO-listing uit, zoals Windows.)</summary>
    public static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension) || extension.Equals(".IFO", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// Alle afspeelbare bestanden onder de downloadmap, recursief en op pad
    /// gesorteerd — de Mac heeft één downloadmap waar Windows er twee heeft
    /// (complete + incomplete), dus één scan volstaat.
    /// </summary>
    public static IReadOnlyList<string> GetFilesToPlay(DownloadItem item)
    {
        return GetFilesToPlay(item.DownloadDir);
    }

    public static IReadOnlyList<string> GetFilesToPlay(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        List<string> files = new();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).ToList();
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }

        return files
            .Where(IsSupportedFile)
            .Where(f => !f.Contains(IgnoredDirectoryMarker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Wacht tot er een afspeelbaar bestand met inhoud (> 0 bytes) is — de port
    /// van Windows' <c>Player.WaitForFilesToPlay</c>, zodat het voorbeeld start
    /// zodra de eerste data binnenstroomt. Geeft false bij een time-out.
    /// </summary>
    public static async Task<bool> WaitForPlayableFileAsync(
        DownloadItem item, TimeSpan timeout, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            foreach (var file in GetFilesToPlay(item))
            {
                try
                {
                    if (new FileInfo(file).Length > 0)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // Bestand wordt nog geschreven; volgende ronde opnieuw proberen.
                }
            }

            if (item.IsCompleted || DateTime.UtcNow >= deadline || cancel.IsCancellationRequested)
            {
                return false;
            }

            await Task.Delay(1000, cancel).ConfigureAwait(false);
        }
    }
}
