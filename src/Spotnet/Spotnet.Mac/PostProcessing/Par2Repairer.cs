using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;

namespace Spotnet.Mac.PostProcessing;

/// <summary>How a repair attempt ended.</summary>
public enum Par2RepairOutcome
{
    NoRepairNeeded,
    Repaired,
    /// <summary>Not enough recovery blocks for the damage found.</summary>
    NotEnoughBlocks,
    /// <summary>The repair ran but the result still does not verify — nothing is trusted.</summary>
    RepairDidNotVerify,
    /// <summary>No usable par2 data at all.</summary>
    NoPar2Data,
    Failed
}

/// <summary>
/// Reconstructs damaged or missing slices from par2 recovery data — the job the
/// Windows client hands to phpar2.exe, done in managed code so nothing has to be
/// installed alongside the app.
///
/// par2's code is Reed-Solomon over GF(2^16). Recovery slice <c>j</c>, made with
/// exponent <c>e_j</c>, is <c>Σ_i base_i^e_j · s_i</c> over every input slice
/// <c>s_i</c>. Slices we still have are known, so moving them to the other side
/// leaves a square system in the missing ones; inverting it once and applying it to
/// the data recovers them.
///
/// Two properties matter for safety. Only slices that already failed verification
/// are ever written, so a bad repair cannot damage good data. And the whole set is
/// re-verified afterwards — if the result does not check out, the outcome is
/// <see cref="Par2RepairOutcome.RepairDidNotVerify"/> rather than a quiet success.
/// </summary>
public sealed class Par2Repairer
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Slice bytes held in memory per recovery row while solving.</summary>
    private const int ChunkSize = 1 << 20;   // 1 MiB, always even

    private readonly string _workingDir;
    private readonly Par2RecoverySet _set;
    private readonly Action<string> _log;

    /// <summary>Reports 0-100 across the repair.</summary>
    public event Action<double>? ProgressChanged;

    public Par2Repairer(string workingDir, Par2RecoverySet set, Action<string> log)
    {
        _workingDir = workingDir;
        _set = set;
        _log = log;
    }

    public Par2RepairOutcome Repair(Par2VerifyResult verify, CancellationToken ct = default)
    {
        if (verify.AllFilesComplete) return Par2RepairOutcome.NoRepairNeeded;
        if (!_set.IsUsable) return Par2RepairOutcome.NoPar2Data;

        List<int> missing = verify.MissingSliceIndices.Distinct().OrderBy(i => i).ToList();
        List<uint> exponents = _set.RecoverySlices
            .Select(s => s.Exponent).Distinct().OrderBy(e => e).ToList();

        if (missing.Count > exponents.Count)
        {
            _log($"Reparatie onmogelijk: {missing.Count} blokken beschadigd, {exponents.Count} herstelblokken beschikbaar " +
                 $"({missing.Count - exponents.Count} tekort)");
            return Par2RepairOutcome.NotEnoughBlocks;
        }

        _log($"Herstellen van {missing.Count} blok(ken) met {missing.Count} van {exponents.Count} herstelblokken");

        try
        {
            ushort[,] inverse = BuildInverseMatrix(missing, exponents.Take(missing.Count).ToList());
            ApplyRepair(missing, exponents.Take(missing.Count).ToList(), inverse, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "par2 repair failed in {0}", _workingDir);
            _log("Reparatie mislukt: " + ex.Message);
            return Par2RepairOutcome.Failed;
        }

        // Never take our own word for it.
        _log("Reparatie klaar, resultaat controleren");
        Par2VerifyResult after = new Par2Verifier(_workingDir, _set, _ => { }).Verify(ct);
        if (after.AllFilesComplete)
        {
            _log("Reparatie geslaagd en geverifieerd");
            return Par2RepairOutcome.Repaired;
        }

        _log($"Na reparatie zijn nog {after.MissingSliceCount} blokken onjuist — resultaat niet vertrouwd");
        return Par2RepairOutcome.RepairDidNotVerify;
    }

    // ── the linear algebra ────────────────────────────────────────────────────

    /// <summary>
    /// Builds and inverts the square matrix relating the chosen recovery rows to the
    /// missing input slices: A[j,k] = base(missing_k) ^ exponent_j.
    /// </summary>
    private static ushort[,] BuildInverseMatrix(List<int> missing, List<uint> exponents)
    {
        int n = missing.Count;
        ushort[] bases = Galois16.BaseConstants(missing.Max() + 1);

        var a = new ushort[n, n];
        for (int j = 0; j < n; j++)
            for (int k = 0; k < n; k++)
                a[j, k] = Galois16.Pow(bases[missing[k]], exponents[j]);

        return Invert(a, n);
    }

    /// <summary>Gauss-Jordan elimination over GF(2^16).</summary>
    private static ushort[,] Invert(ushort[,] a, int n)
    {
        var inv = new ushort[n, n];
        for (int i = 0; i < n; i++) inv[i, i] = 1;

        for (int col = 0; col < n; col++)
        {
            int pivot = -1;
            for (int row = col; row < n; row++)
                if (a[row, col] != 0) { pivot = row; break; }

            if (pivot < 0)
                throw new InvalidOperationException("Reed-Solomon-matrix is singulier; reparatie onmogelijk");

            if (pivot != col) { SwapRows(a, pivot, col, n); SwapRows(inv, pivot, col, n); }

            ushort scale = Galois16.Reciprocal(a[col, col]);
            for (int k = 0; k < n; k++)
            {
                a[col, k] = Galois16.Multiply(a[col, k], scale);
                inv[col, k] = Galois16.Multiply(inv[col, k], scale);
            }

            for (int row = 0; row < n; row++)
            {
                if (row == col) continue;
                ushort factor = a[row, col];
                if (factor == 0) continue;
                for (int k = 0; k < n; k++)
                {
                    a[row, k] ^= Galois16.Multiply(a[col, k], factor);
                    inv[row, k] ^= Galois16.Multiply(inv[col, k], factor);
                }
            }
        }

        return inv;
    }

    private static void SwapRows(ushort[,] m, int r1, int r2, int n)
    {
        for (int k = 0; k < n; k++) (m[r1, k], m[r2, k]) = (m[r2, k], m[r1, k]);
    }

    // ── the data pass ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the slices a chunk at a time so memory stays bounded no matter how big
    /// the set is: for each chunk, subtract the surviving slices from the recovery
    /// rows, apply the inverse, and write the reconstructed bytes back.
    /// </summary>
    private void ApplyRepair(List<int> missing, List<uint> exponents, ushort[,] inverse, CancellationToken ct)
    {
        int n = missing.Count;
        int sliceSize = checked((int)_set.SliceSize);
        ushort[] bases = Galois16.BaseConstants(_set.TotalSlices);

        var missingSet = new HashSet<int>(missing);
        List<Par2RecoverySlice> rows = exponents
            .Select(e => _set.RecoverySlices.First(s => s.Exponent == e))
            .ToList();

        EnsureTargetFilesExist();

        var rhs = new byte[n][];
        for (int j = 0; j < n; j++) rhs[j] = new byte[ChunkSize];
        var solved = new byte[n][];
        for (int k = 0; k < n; k++) solved[k] = new byte[ChunkSize];
        var sliceBuffer = new byte[ChunkSize];

        int chunks = (sliceSize + ChunkSize - 1) / ChunkSize;
        long unitsTotal = (long)chunks * Math.Max(1, _set.TotalSlices);
        long unitsDone = 0;

        for (int chunk = 0; chunk < chunks; chunk++)
        {
            ct.ThrowIfCancellationRequested();

            int offset = chunk * ChunkSize;
            int length = Math.Min(ChunkSize, sliceSize - offset);

            // Start each row from its recovery data.
            for (int j = 0; j < n; j++)
            {
                Array.Clear(rhs[j], 0, ChunkSize);
                ReadAt(rows[j].SourcePath, rows[j].Offset + offset, rhs[j], length);
            }

            // Subtract every surviving input slice.
            foreach (Par2File file in _set.Files)
            {
                string path = Path.Combine(_workingDir, file.Name);
                int sliceCount = file.SliceCount(_set.SliceSize);
                using FileStream fs = File.OpenRead(path);

                for (int local = 0; local < sliceCount; local++)
                {
                    ct.ThrowIfCancellationRequested();
                    int global = file.FirstSliceIndex + local;
                    unitsDone++;

                    if (missingSet.Contains(global)) continue;

                    long position = (long)local * sliceSize + offset;
                    Array.Clear(sliceBuffer, 0, length);
                    ReadAt(fs, position, sliceBuffer, length);

                    for (int j = 0; j < n; j++)
                    {
                        ushort factor = Galois16.Pow(bases[global], exponents[j]);
                        MultiplyAdd(factor, sliceBuffer, rhs[j], length);
                    }
                }

                ProgressChanged?.Invoke(Math.Clamp(unitsDone * 100.0 / unitsTotal, 0, 100));
            }

            // x = A⁻¹ · rhs, then write the reconstructed slices back.
            for (int k = 0; k < n; k++)
            {
                Array.Clear(solved[k], 0, length);
                for (int j = 0; j < n; j++)
                {
                    ushort factor = inverse[k, j];
                    if (factor != 0) MultiplyAdd(factor, rhs[j], solved[k], length);
                }
            }

            for (int k = 0; k < n; k++) WriteSlice(missing[k], offset, solved[k], length, sliceSize);
        }
    }

    /// <summary>dst ^= factor · src, over 16-bit little-endian words.</summary>
    private static void MultiplyAdd(ushort factor, byte[] src, byte[] dst, int length)
    {
        if (factor == 0) return;

        if (factor == 1)
        {
            for (int i = 0; i < length; i++) dst[i] ^= src[i];
            return;
        }

        // Two 256-entry tables turn each word into two lookups and an XOR, which is
        // what keeps a multi-gigabyte repair down to minutes rather than hours.
        Span<ushort> low = stackalloc ushort[256];
        Span<ushort> high = stackalloc ushort[256];
        for (int b = 0; b < 256; b++)
        {
            low[b] = Galois16.Multiply(factor, (ushort)b);
            high[b] = Galois16.Multiply(factor, (ushort)(b << 8));
        }

        int words = length / 2;
        for (int i = 0; i < words; i++)
        {
            int p = i * 2;
            ushort w = (ushort)(src[p] | (src[p + 1] << 8));
            if (w == 0) continue;
            ushort product = (ushort)(low[w & 0xFF] ^ high[w >> 8]);
            dst[p] ^= (byte)(product & 0xFF);
            dst[p + 1] ^= (byte)(product >> 8);
        }

        // A slice size is always a multiple of 4, so an odd tail only ever appears on
        // the final short chunk; treat it as a low byte.
        if ((length & 1) != 0)
        {
            int p = length - 1;
            ushort product = low[src[p]];
            dst[p] ^= (byte)(product & 0xFF);
        }
    }

    private void EnsureTargetFilesExist()
    {
        foreach (Par2File file in _set.Files)
        {
            string path = Path.Combine(_workingDir, file.Name);
            if (!File.Exists(path))
            {
                _log("Ontbrekend bestand wordt opnieuw opgebouwd: " + file.Name);
                using FileStream fs = File.Create(path);
                fs.SetLength(file.Length);
            }
            else if (new FileInfo(path).Length < file.Length)
            {
                using FileStream fs = File.Open(path, FileMode.Open, FileAccess.Write);
                fs.SetLength(file.Length);
            }
        }
    }

    /// <summary>Writes a reconstructed chunk into whichever file owns that global slice.</summary>
    private void WriteSlice(int globalIndex, int offset, byte[] data, int length, int sliceSize)
    {
        foreach (Par2File file in _set.Files)
        {
            int sliceCount = file.SliceCount(_set.SliceSize);
            if (globalIndex < file.FirstSliceIndex || globalIndex >= file.FirstSliceIndex + sliceCount) continue;

            int local = globalIndex - file.FirstSliceIndex;
            long position = (long)local * sliceSize + offset;

            // The final slice of a file is zero-padded in par2 but must not extend
            // the file itself, so clip the write at the recorded length.
            int writable = (int)Math.Min(length, Math.Max(0, file.Length - position));
            if (writable <= 0) return;

            string path = Path.Combine(_workingDir, file.Name);
            using FileStream fs = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            fs.Position = position;
            fs.Write(data, 0, writable);
            return;
        }
    }

    private static void ReadAt(string path, long offset, byte[] buffer, int length)
    {
        using FileStream fs = File.OpenRead(path);
        ReadAt(fs, offset, buffer, length);
    }

    private static void ReadAt(FileStream fs, long offset, byte[] buffer, int length)
    {
        if (offset >= fs.Length) return;
        fs.Position = offset;
        int read = 0;
        while (read < length)
        {
            int n = fs.Read(buffer, read, length - read);
            if (n == 0) break;
            read += n;
        }
    }
}
