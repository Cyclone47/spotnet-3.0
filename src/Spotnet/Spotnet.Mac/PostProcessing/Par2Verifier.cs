using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Spotnet.Mac.PostProcessing;

/// <summary>What verification found for one file.</summary>
public sealed class Par2FileStatus
{
    public required Par2File File { get; init; }
    /// <summary>False when the file is not on disk at all.</summary>
    public bool Exists { get; set; }
    /// <summary>True when every slice checked out and the length is right.</summary>
    public bool Complete { get; set; }
    /// <summary>Local slice indices that failed or are past the end of the file.</summary>
    public List<int> DamagedSlices { get; } = new();
    public int SliceCount { get; set; }
}

/// <summary>The whole recovery set's condition.</summary>
public sealed class Par2VerifyResult
{
    public required Par2RecoverySet Set { get; init; }
    public List<Par2FileStatus> Files { get; } = new();

    /// <summary>Global slice indices that are missing or damaged.</summary>
    public List<int> MissingSliceIndices { get; } = new();

    public bool AllFilesComplete => Files.All(f => f.Complete);
    public int MissingSliceCount => MissingSliceIndices.Count;

    /// <summary>How many more recovery blocks a repair would need. Zero when repairable.</summary>
    public int BlocksShort => Math.Max(0, MissingSliceCount - Set.AvailableRecoveryBlocks);
    public bool CanRepair => !AllFilesComplete && BlocksShort == 0;
}

/// <summary>
/// Verifies a download against its par2 set, slice by slice.
///
/// The Windows client shells out to phpar2 for this. Doing it in managed code is
/// not just about removing the dependency: it is plain CRC32 and MD5 over
/// fixed-size slices, so there is no reason to spawn a process for it. CRC32 comes
/// first because it rejects a damaged slice roughly an order of magnitude faster
/// than MD5 does, and MD5 only has to confirm the survivors.
/// </summary>
public sealed class Par2Verifier
{
    private readonly string _workingDir;
    private readonly Par2RecoverySet _set;
    private readonly Action<string> _log;

    /// <summary>Reports 0-100 across the whole set.</summary>
    public event Action<double>? ProgressChanged;

    public Par2Verifier(string workingDir, Par2RecoverySet set, Action<string> log)
    {
        _workingDir = workingDir;
        _set = set;
        _log = log;
    }

    public Par2VerifyResult Verify(CancellationToken ct = default)
    {
        var result = new Par2VerifyResult { Set = _set };

        long totalSlices = Math.Max(1, _set.TotalSlices);
        long slicesDone = 0;

        foreach (Par2File file in _set.Files)
        {
            ct.ThrowIfCancellationRequested();

            int sliceCount = file.SliceCount(_set.SliceSize);
            var status = new Par2FileStatus { File = file, SliceCount = sliceCount };
            string path = Path.Combine(_workingDir, file.Name);

            if (!File.Exists(path))
            {
                _log("Ontbreekt: " + file.Name);
                status.Exists = false;
                for (int i = 0; i < sliceCount; i++)
                {
                    status.DamagedSlices.Add(i);
                    result.MissingSliceIndices.Add(file.FirstSliceIndex + i);
                }
                result.Files.Add(status);
                slicesDone += sliceCount;
                ProgressChanged?.Invoke(slicesDone * 100.0 / totalSlices);
                continue;
            }

            status.Exists = true;
            VerifySlices(path, file, status, () =>
            {
                slicesDone++;
                ProgressChanged?.Invoke(Math.Min(100, slicesDone * 100.0 / totalSlices));
            }, ct);

            foreach (int local in status.DamagedSlices)
                result.MissingSliceIndices.Add(file.FirstSliceIndex + local);

            status.Complete = status.DamagedSlices.Count == 0;
            _log(status.Complete
                ? "OK: " + file.Name
                : $"Beschadigd: {file.Name} ({status.DamagedSlices.Count}/{sliceCount} blokken)");

            result.Files.Add(status);
        }

        return result;
    }

    private void VerifySlices(string path, Par2File file, Par2FileStatus status,
                              Action onSliceDone, CancellationToken ct)
    {
        int sliceSize = checked((int)_set.SliceSize);
        var buffer = new byte[sliceSize];

        using FileStream fs = File.OpenRead(path);
        for (int index = 0; index < status.SliceCount; index++)
        {
            ct.ThrowIfCancellationRequested();

            long offset = (long)index * sliceSize;
            int read = 0;
            if (offset < fs.Length)
            {
                fs.Position = offset;
                while (read < sliceSize)
                {
                    int n = fs.Read(buffer, read, sliceSize - read);
                    if (n == 0) break;
                    read += n;
                }
            }

            // par2 hashes a slice zero-padded to the full slice size.
            if (read < sliceSize) Array.Clear(buffer, read, sliceSize - read);

            bool ok = index < file.SliceCrc32.Count
                      && Crc32.Compute(buffer) == file.SliceCrc32[index]
                      && index < file.SliceMd5.Count
                      && MD5.HashData(buffer).AsSpan().SequenceEqual(file.SliceMd5[index]);

            if (!ok) status.DamagedSlices.Add(index);
            onSliceDone();
        }

        // A file that is the wrong length is not whole even if every slice hashed:
        // trailing junk past the last slice would go unnoticed otherwise.
        if (fs.Length != file.Length && status.DamagedSlices.Count == 0)
        {
            _log($"Lengte klopt niet voor {file.Name}: {fs.Length} in plaats van {file.Length}");
            status.DamagedSlices.Add(status.SliceCount - 1);
        }
    }
}
