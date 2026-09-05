using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Spotnet.Mac.PostProcessing;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Builds real, checksum-valid par2 files — Main, FileDesc, IFSC and RecvSlice
/// packets — so the verifier and the repairer can be tested end to end without
/// shipping fixture blobs or needing par2 installed.
///
/// The recovery slices are generated with a deliberately naive Reed-Solomon
/// encoder written straight from the spec: a plain triple loop over GF(2^16) with
/// its own field tables. It shares no code with <see cref="Galois16"/> or
/// <see cref="Par2Repairer"/>, so a repair that succeeds against these fixtures has
/// been checked against an independent implementation of the same maths, not
/// against itself.
/// </summary>
internal static class Par2Fixture
{
    private const string MagicText = "PAR2\0PKT";

    /// <summary>One file to protect: its name and its bytes.</summary>
    internal sealed record InputFile(string Name, byte[] Data);

    /// <summary>
    /// Writes <paramref name="par2Path"/> describing <paramref name="files"/>, with
    /// <paramref name="recoveryBlocks"/> recovery slices (exponents 0..n-1).
    /// </summary>
    public static void Write(string par2Path, long sliceSize, IReadOnlyList<InputFile> files, int recoveryBlocks)
    {
        var setId = new byte[16];
        for (int i = 0; i < 16; i++) setId[i] = (byte)(i * 7 + 1);

        // File IDs decide slice order, so keep them in the order given.
        var ids = new List<byte[]>();
        for (int i = 0; i < files.Count; i++)
        {
            var id = new byte[16];
            id[0] = (byte)(i + 1);
            ids.Add(id);
        }

        var packets = new List<byte[]>();

        // ── Main ──────────────────────────────────────────────────────────────
        var main = new List<byte>();
        main.AddRange(BitConverter.GetBytes(sliceSize));
        main.AddRange(BitConverter.GetBytes(files.Count));
        foreach (byte[] id in ids) main.AddRange(id);
        packets.Add(Packet(setId, "PAR 2.0\0Main\0\0\0\0", main.ToArray()));

        // ── FileDesc and IFSC ─────────────────────────────────────────────────
        for (int f = 0; f < files.Count; f++)
        {
            InputFile file = files[f];
            byte[] name = Encoding.ASCII.GetBytes(file.Name);
            int padded = (name.Length + 3) / 4 * 4;

            var desc = new List<byte>();
            desc.AddRange(ids[f]);
            desc.AddRange(MD5.HashData(file.Data));
            desc.AddRange(MD5.HashData(First16k(file.Data)));
            desc.AddRange(BitConverter.GetBytes((long)file.Data.Length));
            desc.AddRange(name);
            desc.AddRange(new byte[padded - name.Length]);
            packets.Add(Packet(setId, "PAR 2.0\0FileDesc", desc.ToArray()));

            var ifsc = new List<byte>();
            ifsc.AddRange(ids[f]);
            foreach (byte[] slice in Slices(file.Data, sliceSize))
            {
                ifsc.AddRange(MD5.HashData(slice));
                ifsc.AddRange(BitConverter.GetBytes(Crc32.Compute(slice)));
            }
            packets.Add(Packet(setId, "PAR 2.0\0IFSC\0\0\0\0", ifsc.ToArray()));
        }

        // ── RecvSlice ─────────────────────────────────────────────────────────
        List<byte[]> allSlices = files.SelectMany(f => Slices(f.Data, sliceSize)).ToList();
        for (uint exponent = 0; exponent < recoveryBlocks; exponent++)
        {
            byte[] coded = EncodeRecoverySlice(allSlices, exponent, (int)sliceSize);
            var body = new List<byte>();
            body.AddRange(BitConverter.GetBytes(exponent));
            body.AddRange(coded);
            packets.Add(Packet(setId, "PAR 2.0\0RecvSlic", body.ToArray()));
        }

        File.WriteAllBytes(par2Path, packets.SelectMany(p => p).ToArray());
    }

    /// <summary>Splits a file into zero-padded slices, exactly as par2 does.</summary>
    public static List<byte[]> Slices(byte[] data, long sliceSize)
    {
        var slices = new List<byte[]>();
        int size = (int)sliceSize;
        int count = (int)((data.Length + size - 1) / size);
        for (int i = 0; i < count; i++)
        {
            var slice = new byte[size];
            int offset = i * size;
            Array.Copy(data, offset, slice, 0, Math.Min(size, data.Length - offset));
            slices.Add(slice);
        }
        return slices;
    }

    // ── an independent Reed-Solomon encoder ───────────────────────────────────

    private static ushort[]? _exp;
    private static ushort[]? _log;

    /// <summary>Field tables built here rather than reused, to keep the check honest.</summary>
    private static void EnsureTables()
    {
        if (_exp != null) return;
        var exp = new ushort[65536];
        var log = new ushort[65536];
        uint x = 1;
        for (uint i = 0; i < 65535; i++)
        {
            exp[i] = (ushort)x;
            log[x] = (ushort)i;
            x <<= 1;
            if ((x & 0x10000) != 0) x ^= 0x1100B;
        }
        exp[65535] = 0;
        log[0] = 65535;
        _exp = exp;
        _log = log;
    }

    private static ushort Mul(ushort a, ushort b)
    {
        EnsureTables();
        if (a == 0 || b == 0) return 0;
        int s = _log![a] + _log[b];
        if (s >= 65535) s -= 65535;
        return _exp![s];
    }

    private static ushort PowOf(ushort a, uint e)
    {
        ushort r = 1;
        for (uint i = 0; i < e; i++) r = Mul(r, a);
        return r;
    }

    /// <summary>The base constant for input slice <paramref name="index"/>, per the spec.</summary>
    private static ushort BaseFor(int index)
    {
        EnsureTables();
        int logbase = 0;
        for (int i = 0; i <= index; i++)
        {
            do { logbase++; } while (!Coprime(logbase));
        }
        return _exp![(int)((long)logbase % 65535)];
    }

    /// <summary>65535 = 3 · 5 · 17 · 257, so coprimality is four divisibility checks.</summary>
    private static bool Coprime(int n) => n % 3 != 0 && n % 5 != 0 && n % 17 != 0 && n % 257 != 0;

    private static byte[] EncodeRecoverySlice(List<byte[]> inputSlices, uint exponent, int sliceSize)
    {
        var result = new byte[sliceSize];
        for (int i = 0; i < inputSlices.Count; i++)
        {
            ushort factor = PowOf(BaseFor(i), exponent);
            if (factor == 0) continue;
            byte[] slice = inputSlices[i];
            for (int w = 0; w * 2 + 1 < sliceSize; w++)
            {
                int p = w * 2;
                ushort word = (ushort)(slice[p] | (slice[p + 1] << 8));
                ushort product = Mul(factor, word);
                result[p] ^= (byte)(product & 0xFF);
                result[p + 1] ^= (byte)(product >> 8);
            }
        }
        return result;
    }

    // ── packet plumbing ───────────────────────────────────────────────────────

    private static byte[] Packet(byte[] setId, string type, byte[] body)
    {
        var hashed = new List<byte>();
        hashed.AddRange(setId);
        hashed.AddRange(Encoding.ASCII.GetBytes(type));
        hashed.AddRange(body);
        byte[] hashedBytes = hashed.ToArray();

        var packet = new List<byte>();
        packet.AddRange(Encoding.ASCII.GetBytes(MagicText));
        packet.AddRange(BitConverter.GetBytes((long)(64 + hashedBytes.Length - 32)));
        packet.AddRange(MD5.HashData(hashedBytes));
        packet.AddRange(hashedBytes);
        return packet.ToArray();
    }

    private static byte[] First16k(byte[] payload)
    {
        var block = new byte[16 * 1024];
        Array.Copy(payload, block, Math.Min(payload.Length, block.Length));
        return block;
    }
}
