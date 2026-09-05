using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Spotnet.Mac.PostProcessing;

/// <summary>One file in the recovery set, with its per-slice checksums.</summary>
public sealed class Par2File
{
    public required byte[] Id { get; init; }
    public required string Name { get; init; }
    public required long Length { get; init; }
    /// <summary>MD5 of the whole file, hex, lower case.</summary>
    public required string Md5 { get; init; }
    /// <summary>MD5 of the first 16 KB, zero-padded, hex, lower case.</summary>
    public required string Md5Of16k { get; init; }

    /// <summary>Per-slice MD5s, from the IFSC packet. Empty when none was found.</summary>
    public List<byte[]> SliceMd5 { get; } = new();
    /// <summary>Per-slice CRC32s, from the IFSC packet.</summary>
    public List<uint> SliceCrc32 { get; } = new();

    /// <summary>Index of this file's first slice in the global slice numbering.</summary>
    public int FirstSliceIndex { get; set; }

    public int SliceCount(long sliceSize) => (int)((Length + sliceSize - 1) / sliceSize);
}

/// <summary>One recovery slice: where the coded data lives and the exponent behind it.</summary>
public sealed record Par2RecoverySlice(uint Exponent, string SourcePath, long Offset, int Length);

/// <summary>
/// A parsed par2 recovery set: geometry from the Main packet, the file
/// descriptions, the per-slice checksums, and where every recovery slice sits on
/// disk. Recovery slices are referenced by offset rather than loaded, because a
/// full set routinely runs to tens of gigabytes.
///
/// Packet layouts follow the Par2 v2 specification:
///   Main      slice size (8), recovery file count (4), recovery-set file IDs
///             (16 each), then the non-recovery-set file IDs
///   FileDesc  file id (16), MD5 (16), MD5-16k (16), length (8), name
///   IFSC      file id (16), then MD5 (16) + CRC32 (4) per slice
///   RecvSlice exponent (4), then one slice of coded data
/// </summary>
public sealed class Par2RecoverySet
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PAR2\0PKT");
    private const string TypeMain = "PAR 2.0\0Main\0\0\0\0";
    private const string TypeFileDesc = "PAR 2.0\0FileDesc";
    private const string TypeIfsc = "PAR 2.0\0IFSC\0\0\0\0";
    private const string TypeRecvSlice = "PAR 2.0\0RecvSlic";

    public long SliceSize { get; private set; }
    public List<Par2File> Files { get; } = new();
    public List<Par2RecoverySlice> RecoverySlices { get; } = new();

    /// <summary>Total number of input slices across the whole recovery set.</summary>
    public int TotalSlices { get; private set; }

    /// <summary>Recovery slices available, counted once per distinct exponent.</summary>
    public int AvailableRecoveryBlocks => RecoverySlices.Select(s => s.Exponent).Distinct().Count();

    /// <summary>True when there is enough here to verify slice by slice.</summary>
    public bool IsUsable => SliceSize > 0 && Files.Count > 0 && Files.All(f => f.SliceMd5.Count > 0);

    /// <summary>
    /// Reads every par2 file in <paramref name="directory"/> into one set. par2
    /// deliberately repeats the critical packets across volumes, so the first valid
    /// copy of each wins and a damaged volume costs nothing.
    /// </summary>
    public static Par2RecoverySet? Load(string directory, Action<string>? log = null)
    {
        if (!Directory.Exists(directory)) return null;

        string[] par2Files = Directory
            .GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(p => ArchiveNaming.IsPar2File(Path.GetFileName(p)))
            .OrderBy(p => new FileInfo(p).Length)          // the small master first
            .ToArray();

        if (par2Files.Length == 0) return null;

        var set = new Par2RecoverySet();
        var fileById = new Dictionary<string, Par2File>(StringComparer.Ordinal);
        var recoverySetOrder = new List<string>();

        foreach (string path in par2Files)
        {
            try
            {
                set.ReadPackets(path, fileById, recoverySetOrder);
            }
            catch (Exception ex)
            {
                log?.Invoke("par2-bestand " + Path.GetFileName(path) + " deels onleesbaar: " + ex.Message);
                Log.Debug(ex, "par2 parse error in {0}", path);
            }
        }

        if (set.SliceSize <= 0 || recoverySetOrder.Count == 0) return null;

        // Slice numbering follows the order the Main packet lists the file IDs in.
        int next = 0;
        foreach (string id in recoverySetOrder)
        {
            if (!fileById.TryGetValue(id, out Par2File? file)) continue;
            file.FirstSliceIndex = next;
            next += file.SliceCount(set.SliceSize);
            set.Files.Add(file);
        }
        set.TotalSlices = next;

        return set;
    }

    private void ReadPackets(string path, Dictionary<string, Par2File> fileById, List<string> recoverySetOrder)
    {
        using FileStream fs = File.OpenRead(path);
        var header = new byte[64];

        while (ReadExactly(fs, header, 64))
        {
            if (!header.AsSpan(0, 8).SequenceEqual(Magic))
            {
                if (!Resynchronise(fs)) return;
                continue;
            }

            long packetLength = BitConverter.ToInt64(header, 8);
            if (packetLength < 64 || packetLength % 4 != 0 || packetLength > 256L * 1024 * 1024) return;

            string type = Encoding.ASCII.GetString(header, 48, 16);
            int bodyLength = (int)(packetLength - 64);
            long bodyStart = fs.Position;

            // A recovery slice can be megabytes; note where it is and skip past it.
            if (type == TypeRecvSlice)
            {
                var exponentBytes = new byte[4];
                if (!ReadExactly(fs, exponentBytes, 4)) return;
                uint exponent = BitConverter.ToUInt32(exponentBytes, 0);
                int sliceLength = bodyLength - 4;
                if (sliceLength > 0)
                    RecoverySlices.Add(new Par2RecoverySlice(exponent, path, bodyStart + 4, sliceLength));
                fs.Position = bodyStart + bodyLength;
                continue;
            }

            var body = new byte[bodyLength];
            if (!ReadExactly(fs, body, bodyLength)) return;
            if (!PacketHashMatches(header, body)) continue;

            switch (type)
            {
                case TypeMain when SliceSize == 0:
                    ReadMain(body, recoverySetOrder);
                    break;
                case TypeFileDesc:
                    ReadFileDesc(body, fileById);
                    break;
                case TypeIfsc:
                    ReadIfsc(body, fileById);
                    break;
            }
        }
    }

    private void ReadMain(byte[] body, List<string> recoverySetOrder)
    {
        if (body.Length < 12) return;
        long sliceSize = BitConverter.ToInt64(body, 0);
        int recoveryFileCount = BitConverter.ToInt32(body, 8);
        if (sliceSize <= 0 || sliceSize % 4 != 0 || recoveryFileCount <= 0) return;

        SliceSize = sliceSize;
        int offset = 12;
        for (int i = 0; i < recoveryFileCount && offset + 16 <= body.Length; i++, offset += 16)
        {
            string id = Convert.ToHexString(body, offset, 16);
            if (!recoverySetOrder.Contains(id)) recoverySetOrder.Add(id);
        }
    }

    private static void ReadFileDesc(byte[] body, Dictionary<string, Par2File> fileById)
    {
        if (body.Length < 56) return;
        string id = Convert.ToHexString(body, 0, 16);
        if (fileById.ContainsKey(id)) return;

        long length = BitConverter.ToInt64(body, 48);
        string name = ReadName(body, 56);
        if (name.Length == 0 || length < 0) return;

        fileById[id] = new Par2File
        {
            Id = body.AsSpan(0, 16).ToArray(),
            Name = name,
            Length = length,
            Md5 = Convert.ToHexString(body, 16, 16).ToLowerInvariant(),
            Md5Of16k = Convert.ToHexString(body, 32, 16).ToLowerInvariant()
        };
    }

    private static void ReadIfsc(byte[] body, Dictionary<string, Par2File> fileById)
    {
        if (body.Length < 16) return;
        string id = Convert.ToHexString(body, 0, 16);
        if (!fileById.TryGetValue(id, out Par2File? file) || file.SliceMd5.Count > 0) return;

        for (int offset = 16; offset + 20 <= body.Length; offset += 20)
        {
            file.SliceMd5.Add(body.AsSpan(offset, 16).ToArray());
            file.SliceCrc32.Add(BitConverter.ToUInt32(body, offset + 16));
        }
    }

    // ── primitives ────────────────────────────────────────────────────────────

    private static string ReadName(byte[] body, int offset)
    {
        int end = body.Length;
        while (end > offset && body[end - 1] == 0) end--;
        return Encoding.UTF8.GetString(body, offset, end - offset).Trim();
    }

    private static bool PacketHashMatches(byte[] header, byte[] body)
    {
        using var md5 = MD5.Create();
        md5.TransformBlock(header, 32, 32, null, 0);
        md5.TransformFinalBlock(body, 0, body.Length);
        return Convert.ToHexString(md5.Hash!)
            .Equals(Convert.ToHexString(header, 16, 16), StringComparison.OrdinalIgnoreCase);
    }

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

    private static bool Resynchronise(Stream s)
    {
        int matched = 0;
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            matched = b == Magic[matched] ? matched + 1 : (b == Magic[0] ? 1 : 0);
            if (matched == Magic.Length)
            {
                s.Seek(-Magic.Length, SeekOrigin.Current);
                return true;
            }
        }
        return false;
    }
}
