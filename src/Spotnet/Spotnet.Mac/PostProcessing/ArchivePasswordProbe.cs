using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;

namespace Spotnet.Mac.PostProcessing;

/// <summary>What an archive's own headers say about encryption.</summary>
public enum ArchiveEncryption
{
    /// <summary>Readable headers, no encrypted entries.</summary>
    None,
    /// <summary>Entries are encrypted; the file list is still readable.</summary>
    Encrypted,
    /// <summary>The header itself is encrypted; even listing needs the password.</summary>
    EncryptedHeaders,
    /// <summary>Not an archive we can inspect, or truncated.</summary>
    Unknown
}

/// <summary>
/// Reads the archive headers to decide up front whether a password is needed.
///
/// The Windows client never does this: it finds out reactively, by watching UnRAR
/// exit with code 11 or print "wrong password", and only then flips the row to
/// "Wachtwoord?". That still works here as a fallback, but on a 40 GB set an unpack
/// attempt that is certain to fail costs minutes, so we look at the headers first.
/// RAR4, RAR5 and ZIP all advertise encryption in the clear; 7z with encrypted
/// headers does not, which is why <see cref="ArchiveEncryption.Unknown"/> exists.
/// </summary>
public static class ArchivePasswordProbe
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly byte[] Rar4Signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
    private static readonly byte[] Rar5Signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };

    private const int Rar4MainHeader = 0x73;
    private const int Rar4FileHeader = 0x74;
    private const int Rar4FlagPassword = 0x0004;   // LHD_PASSWORD
    private const int Rar4FlagEncryptedHeaders = 0x0080; // MHD_PASSWORD
    private const int Rar4FlagAddSize = 0x8000;

    private const int Rar5TypeCrypt = 4;
    private const int Rar5TypeFile = 2;
    private const int Rar5HeaderFlagExtra = 0x0001;
    private const int Rar5HeaderFlagData = 0x0002;
    private const int Rar5FileFlagUnknownSize = 0x0008;
    private const int Rar5FileFlagCrc = 0x0004;
    private const int Rar5FileFlagTime = 0x0002;
    private const int Rar5ExtraCrypt = 1;          // FHEXTRA_CRYPT

    /// <summary>
    /// Inspects every archive in <paramref name="directory"/> and returns the strongest
    /// verdict found. A directory with no archives at all reports
    /// <see cref="ArchiveEncryption.None"/>.
    /// </summary>
    public static ArchiveEncryption InspectDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return ArchiveEncryption.None;

        var verdict = ArchiveEncryption.None;
        foreach (string path in EnumerateArchives(directory))
        {
            ArchiveEncryption one = Inspect(path);
            if (one == ArchiveEncryption.EncryptedHeaders) return one;
            if (one == ArchiveEncryption.Encrypted) verdict = one;
            else if (one == ArchiveEncryption.Unknown && verdict == ArchiveEncryption.None) verdict = one;
        }
        return verdict;
    }

    /// <summary>The archive files in a download directory, first volumes first.</summary>
    public static IEnumerable<string> EnumerateArchives(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(p => ArchiveNaming.IsArchive(Path.GetFileName(p)))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads one archive's headers.</summary>
    public static ArchiveEncryption Inspect(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var signature = new byte[8];
            int read = fs.Read(signature, 0, 8);
            if (read < 4) return ArchiveEncryption.Unknown;

            if (read >= 8 && signature.AsSpan(0, 8).SequenceEqual(Rar5Signature))
                return InspectRar5(fs);

            if (read >= 7 && signature.AsSpan(0, 7).SequenceEqual(Rar4Signature))
            {
                fs.Position = 7;
                return InspectRar4(fs);
            }

            if (signature[0] == 'P' && signature[1] == 'K')
            {
                fs.Position = 0;
                return InspectZip(fs);
            }

            // 7z: "7z\xBC\xAF\x27\x1C". Encrypted headers are indistinguishable from a
            // plain encoded header without trying, so let the unpack step decide.
            if (signature[0] == '7' && signature[1] == 'z') return ArchiveEncryption.Unknown;

            return ArchiveEncryption.Unknown;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not inspect {0}", path);
            return ArchiveEncryption.Unknown;
        }
    }

    // ── RAR 4 ─────────────────────────────────────────────────────────────────

    private static ArchiveEncryption InspectRar4(Stream fs)
    {
        var header = new byte[11];
        var verdict = ArchiveEncryption.None;

        while (true)
        {
            long blockStart = fs.Position;
            if (!ReadExactly(fs, header, 7)) return verdict;

            int type = header[2];
            int flags = header[3] | (header[4] << 8);
            int headSize = header[5] | (header[6] << 8);
            if (headSize < 7) return verdict;

            long addSize = 0;
            if ((flags & Rar4FlagAddSize) != 0)
            {
                if (!ReadExactly(fs, header, 4)) return verdict;
                addSize = (uint)(header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            }

            if (type == Rar4MainHeader && (flags & Rar4FlagEncryptedHeaders) != 0)
                return ArchiveEncryption.EncryptedHeaders;

            if (type == Rar4FileHeader && (flags & Rar4FlagPassword) != 0)
                verdict = ArchiveEncryption.Encrypted;

            long next = blockStart + headSize + addSize;
            if (next <= blockStart || next >= fs.Length) return verdict;
            fs.Position = next;
        }
    }

    // ── RAR 5 ─────────────────────────────────────────────────────────────────

    private static ArchiveEncryption InspectRar5(Stream fs)
    {
        var verdict = ArchiveEncryption.None;

        while (fs.Position < fs.Length)
        {
            var crc = new byte[4];
            if (!ReadExactly(fs, crc, 4)) return verdict;

            long sizeFieldStart = fs.Position;
            if (!TryReadVInt(fs, out ulong headerSize) || headerSize == 0) return verdict;
            long headerStart = fs.Position;
            long headerEnd = headerStart + (long)headerSize;

            if (!TryReadVInt(fs, out ulong headerType)) return verdict;
            if (!TryReadVInt(fs, out ulong headerFlags)) return verdict;

            ulong extraSize = 0;
            ulong dataSize = 0;
            if ((headerFlags & Rar5HeaderFlagExtra) != 0 && !TryReadVInt(fs, out extraSize)) return verdict;
            if ((headerFlags & Rar5HeaderFlagData) != 0 && !TryReadVInt(fs, out dataSize)) return verdict;

            if (headerType == Rar5TypeCrypt) return ArchiveEncryption.EncryptedHeaders;

            if (headerType == Rar5TypeFile && extraSize > 0 &&
                FileHeaderHasCryptRecord(fs, headerEnd, (long)extraSize))
            {
                verdict = ArchiveEncryption.Encrypted;
            }

            long next = headerEnd + (long)dataSize;
            if (next <= sizeFieldStart || next >= fs.Length) return verdict;
            fs.Position = next;
        }

        return verdict;
    }

    /// <summary>
    /// Walks the extra area of a RAR5 file header looking for an FHEXTRA_CRYPT record.
    /// The extra area sits at the end of the header, so we skip the fixed fields first.
    /// </summary>
    private static bool FileHeaderHasCryptRecord(Stream fs, long headerEnd, long extraSize)
    {
        try
        {
            if (!TryReadVInt(fs, out ulong fileFlags)) return false;
            if (!TryReadVInt(fs, out _)) return false;                     // unpacked size
            if (!TryReadVInt(fs, out _)) return false;                     // attributes
            if ((fileFlags & Rar5FileFlagTime) != 0 && !Skip(fs, 4)) return false;
            if ((fileFlags & Rar5FileFlagCrc) != 0 && !Skip(fs, 4)) return false;
            _ = fileFlags & Rar5FileFlagUnknownSize;
            if (!TryReadVInt(fs, out _)) return false;                     // compression info
            if (!TryReadVInt(fs, out _)) return false;                     // host os
            if (!TryReadVInt(fs, out ulong nameLength)) return false;
            if (!Skip(fs, (long)nameLength)) return false;

            long extraStart = headerEnd - extraSize;
            if (fs.Position > extraStart) return false;
            fs.Position = extraStart;

            while (fs.Position < headerEnd)
            {
                if (!TryReadVInt(fs, out ulong recordSize) || recordSize == 0) return false;
                long recordEnd = fs.Position + (long)recordSize;
                if (!TryReadVInt(fs, out ulong recordType)) return false;
                if (recordType == Rar5ExtraCrypt) return true;
                if (recordEnd <= fs.Position || recordEnd > headerEnd) return false;
                fs.Position = recordEnd;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ── ZIP ───────────────────────────────────────────────────────────────────

    private static ArchiveEncryption InspectZip(Stream fs)
    {
        // Walk the local file headers; bit 0 of the general purpose flag means the
        // entry is encrypted, bit 13 means the central directory is too.
        var header = new byte[30];
        var verdict = ArchiveEncryption.None;

        while (ReadExactly(fs, header, 30))
        {
            uint signature = (uint)(header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            if (signature != 0x04034b50) return verdict;

            int flags = header[6] | (header[7] << 8);
            long compressedSize = (uint)(header[18] | (header[19] << 8) | (header[20] << 16) | (header[21] << 24));
            int nameLength = header[26] | (header[27] << 8);
            int extraLength = header[28] | (header[29] << 8);

            if ((flags & 0x2000) != 0) return ArchiveEncryption.EncryptedHeaders;
            if ((flags & 0x0001) != 0) verdict = ArchiveEncryption.Encrypted;

            long next = fs.Position + nameLength + extraLength + compressedSize;
            if (next <= fs.Position || next >= fs.Length) return verdict;
            fs.Position = next;
        }

        return verdict;
    }

    // ── primitives ────────────────────────────────────────────────────────────

    private static bool ReadExactly(Stream s, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buffer, read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    private static bool Skip(Stream s, long count)
    {
        if (count < 0 || s.Position + count > s.Length) return false;
        s.Position += count;
        return true;
    }

    /// <summary>RAR5 variable length integer: 7 bits per byte, high bit continues.</summary>
    private static bool TryReadVInt(Stream s, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            int b = s.ReadByte();
            if (b < 0) return false;
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }
}
