using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Spotnet.Mac.PostProcessing;

/// <summary>
/// Recognises the archive naming conventions Usenet posters use. A port of
/// Spotnet.Helpers.ArchiveHelper plus the naming rules that live inline in
/// Unpack.cs and PostProcessCoordinator.cs on Windows.
/// </summary>
public static class ArchiveNaming
{
    /// <summary>The Windows regex, verbatim: .rar, .r00-.r99/.z00-.z99, and .001-style parts.</summary>
    private static readonly Regex RarLike = new(@"\.rar$|\.[rz]\d\d$|\.\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ZipLike = new(@"\.zip$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SevenZipLike = new(@"\.7z$|\.7z\.\d{3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Par2Like = new(@"\.par2$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A .partNN.rar volume; group 1 is the set's base name.</summary>
    private static readonly Regex RarPartVolume = new(@"^(.+)\.part\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The "name.ext.001" split-file pattern PostProcessCoordinator joins.</summary>
    internal static readonly Regex SplitPart = new(@"^(.*\.[a-zA-Z0-9]{3})\.(\d{3})$", RegexOptions.Compiled);

    public static bool IsRarFile(string path)
    {
        path = (path ?? "").Trim();
        return path.Length != 0 && RarLike.IsMatch(path);
    }

    public static bool IsZipFile(string path) => ZipLike.IsMatch((path ?? "").Trim());

    public static bool IsSevenZipFile(string path) => SevenZipLike.IsMatch((path ?? "").Trim());

    public static bool IsPar2File(string path) => Par2Like.IsMatch((path ?? "").Trim());

    /// <summary>Anything the unpack step might hand to a tool.</summary>
    public static bool IsArchive(string path) =>
        !IsPar2File(path) && (IsRarFile(path) || IsZipFile(path) || IsSevenZipFile(path));

    /// <summary>
    /// The first volume of a rar set: <c>name.rar</c>, <c>name.part01.rar</c> or
    /// <c>name.part001.rar</c>. Handing a tool anything else re-extracts mid-stream.
    /// </summary>
    public static bool IsFirstRarVolume(string fileName)
    {
        if (!fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)) return false;

        string withoutExt = Path.GetFileNameWithoutExtension(fileName);
        Match part = RarPartVolume.Match(withoutExt);
        if (!part.Success) return true;   // plain name.rar

        string digits = withoutExt[(part.Groups[1].Value.Length + ".part".Length)..];
        return long.TryParse(digits, out long n) && n == 1;
    }

    /// <summary>
    /// The set a rar volume belongs to, so a failed archive can be skipped as a whole.
    /// Windows calls this GetBodyOfMultipartArchive.
    /// </summary>
    public static string? MultipartBase(string path)
    {
        path = (path ?? "").Trim();
        if (!IsRarFile(path)) return null;

        string withoutExt = Path.GetFileNameWithoutExtension(path);
        if (withoutExt.Length == 0) return null;

        Match m = RarPartVolume.Match(withoutExt);
        return m.Success ? m.Groups[1].Value : withoutExt;
    }

    /// <summary>
    /// The kind of volume naming a file uses. Two files belong to the same volume
    /// chain only when they share both a base name and a family: a release can ship
    /// <c>show.rar</c> alongside an unrelated <c>show.001</c>, and deleting the
    /// second because the first was extracted would eat an un-extracted file.
    /// </summary>
    public enum VolumeFamily
    {
        None,
        /// <summary>
        /// name.rar, name.partNN.rar and the old-style name.r00 … name.z99 that sit
        /// next to a name.rar — one chain, two spellings.
        /// </summary>
        Rar,
        /// <summary>name.001, name.002 — a split payload, not a rar volume set.</summary>
        Numbered,
        Zip,
        SevenZip
    }

    public static VolumeFamily FamilyOf(string fileName)
    {
        fileName = (fileName ?? "").Trim();
        if (fileName.Length == 0) return VolumeFamily.None;
        if (IsPar2File(fileName)) return VolumeFamily.None;
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return VolumeFamily.Zip;
        if (fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return VolumeFamily.SevenZip;
        if (Regex.IsMatch(fileName, @"\.7z\.\d{3}$", RegexOptions.IgnoreCase)) return VolumeFamily.SevenZip;
        if (fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)) return VolumeFamily.Rar;
        if (Regex.IsMatch(fileName, @"\.[rz]\d\d$", RegexOptions.IgnoreCase)) return VolumeFamily.Rar;
        if (Regex.IsMatch(fileName, @"\.\d+$")) return VolumeFamily.Numbered;
        return VolumeFamily.None;
    }

    /// <summary>True when the first bytes are a RAR4 or RAR5 signature.</summary>
    public static bool HasRarSignature(string path)
    {
        // The Windows client sniffs this to catch archives posted as name.001, name.002
        // with no .rar extension in sight.
        try
        {
            if (!File.Exists(path)) return false;
            using FileStream fs = File.OpenRead(path);
            var buffer = new byte[8];
            if (fs.Read(buffer, 0, 8) < 7) return false;

            ReadOnlySpan<byte> rar4 = stackalloc byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
            ReadOnlySpan<byte> rar5 = stackalloc byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
            return buffer.AsSpan(0, 7).SequenceEqual(rar4) || buffer.AsSpan(0, 8).SequenceEqual(rar5);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
