using System;
using System.Text;

namespace Spotnet.Mac.Models;

/// <summary>
/// Derives the short poster id Windows shows next to a spot's sender —
/// "Paaldanser (5I54zQ)". Ported from AppHelper.MakeUnique: CRC32 of the poster's
/// decoded RSA modulus, little-endian, base64, stripped of non-alphanumerics.
/// </summary>
public static class PosterIdentity
{
    /// <summary>
    /// Undoes Spotnet's URL-safe base64 substitutions and restores the padding.
    /// SpotHelper.UnSpecialString + SpotHelper.FixPadding.
    /// </summary>
    public static string Unescape(string value)
    {
        string plain = value.Replace("-s", "/", StringComparison.Ordinal)
                            .Replace("-p", "+", StringComparison.Ordinal);
        return (plain.Length % 4) switch
        {
            1 => plain + "===",
            2 => plain + "==",
            3 => plain + "=",
            _ => plain
        };
    }

    /// <summary>
    /// Pulls the poster's modulus straight out of a raw From header, so a spot stored
    /// before the parser recorded it separately still resolves to the right id.
    /// Between "&lt;" and "@" sits "&lt;modulus&gt;.&lt;signature&gt;".
    /// </summary>
    public static string ModulusFromSender(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender)) return "";

        int open = sender.IndexOf('<', StringComparison.Ordinal);
        int at = sender.IndexOf('@', StringComparison.Ordinal);
        if (open < 0 || at <= open) return "";

        string credentials = sender[(open + 1)..at];
        if (credentials.Length <= 50) return "";

        int dot = credentials.IndexOf('.', StringComparison.Ordinal);
        return Unescape(dot < 0 ? credentials : credentials[..dot]);
    }

    /// <summary>
    /// The short id, or "Onbekend" when the modulus is missing or unreadable.
    /// </summary>
    public static string MakeUnique(string? modulus)
    {
        if (string.IsNullOrWhiteSpace(modulus) || modulus.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return "Onbekend";
        }

        try
        {
            byte[] key = Convert.FromBase64String(modulus);
            byte[] crc = BitConverter.GetBytes(Crc32(key));
            var text = new StringBuilder(8);
            foreach (char c in Convert.ToBase64String(crc))
            {
                if (char.IsLetterOrDigit(c)) text.Append(c);
            }
            return text.ToString();
        }
        catch (FormatException)
        {
            return "Onbekend";
        }
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }
            table[i] = value;
        }
        return table;
    }

    /// <summary>Standard CRC-32, returned as the signed int Windows feeds to BitConverter.</summary>
    private static int Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return unchecked((int)~crc);
    }
}
