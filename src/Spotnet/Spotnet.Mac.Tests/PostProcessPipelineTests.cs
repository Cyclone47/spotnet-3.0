using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Spotnet.Mac.PostProcessing;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Drives the unpack stage and the whole coordinator against real archives and real
/// par2 data, through the same managed code path the app uses at runtime. Nothing
/// here shells out, and nothing needs par2, unrar or 7-Zip on the machine — which is
/// the point: the app ships its own.
/// </summary>
public sealed class PostProcessPipelineTests : IDisposable
{
    private readonly string _work;

    public PostProcessPipelineTests()
    {
        _work = Path.Combine(Path.GetTempPath(), "spotnet-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* temp dir */ }
    }

    private static void Ignore(string _) { }
    private PostProcessToolset Tools() => new(Path.Combine(_work, "no-tools-here"));
    private string P(string name) => Path.Combine(_work, name);

    /// <summary>Writes a real zip holding the given entries.</summary>
    private void WriteZip(string zipName, params (string Name, byte[] Data)[] entries)
    {
        using var fs = new FileStream(P(zipName), FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach ((string name, byte[] data) in entries)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using Stream s = entry.Open();
            s.Write(data, 0, data.Length);
        }
    }

    private static byte[] Payload(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static string Md5(byte[] data) => Convert.ToHexString(MD5.HashData(data));

    // ── unpacking ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_zip_is_unpacked_and_the_archive_removed()
    {
        byte[] payload = Payload(50_000, 1);
        WriteZip("release.zip", ("film.mkv", payload));

        var unpacker = new Unpacker(_work, Tools(), Ignore);
        UnpackResult result = await unpacker.RunAsync("");

        Assert.Equal(UnpackResult.Unpacked, result);
        Assert.True(File.Exists(P("film.mkv")));
        Assert.Equal(Md5(payload), Md5(File.ReadAllBytes(P("film.mkv"))));
        Assert.False(File.Exists(P("release.zip")));
        Assert.False(Directory.Exists(unpacker.UnpackTargetDir));
    }

    [Fact]
    public async Task Nested_paths_inside_an_archive_are_preserved()
    {
        WriteZip("release.zip",
            ("Some.Release/film.mkv", Payload(1000, 2)),
            ("Some.Release/Subs/nl.srt", Payload(200, 3)));

        Assert.Equal(UnpackResult.Unpacked, await new Unpacker(_work, Tools(), Ignore).RunAsync(""));

        Assert.True(File.Exists(P(Path.Combine("Some.Release", "film.mkv"))));
        Assert.True(File.Exists(P(Path.Combine("Some.Release", "Subs", "nl.srt"))));
    }

    [Fact]
    public async Task An_entry_that_tries_to_escape_the_download_folder_is_refused()
    {
        // Usenet archives are untrusted input; a "../" entry must not write outside.
        WriteZip("evil.zip", ("../../escaped.txt", Payload(50, 4)));

        await new Unpacker(_work, Tools(), Ignore).RunAsync("");

        string outside = Path.Combine(Directory.GetParent(_work)!.FullName, "escaped.txt");
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task A_directory_with_no_archives_has_nothing_to_unpack()
    {
        File.WriteAllBytes(P("film.mkv"), Payload(100, 5));

        Assert.Equal(UnpackResult.NothingToDo, await new Unpacker(_work, Tools(), Ignore).RunAsync(""));
        Assert.True(File.Exists(P("film.mkv")));
    }

    [Fact]
    public async Task A_damaged_archive_is_reported_and_left_on_disk()
    {
        File.WriteAllText(P("broken.zip"), "this is not a zip at all");

        UnpackResult result = await new Unpacker(_work, Tools(), Ignore).RunAsync("");

        Assert.True(result is UnpackResult.Corrupt or UnpackResult.Failed);
        Assert.True(File.Exists(P("broken.zip")));
    }

    [Fact]
    public async Task A_truncated_archive_is_damaged_not_password_protected()
    {
        // Regression: listing the entries of a damaged archive throws, and that was
        // being read as "encrypted" — so a corrupt download told the user it needed
        // a password that never existed. Seen on a real RAR5 download whose headers
        // plainly carried no crypt record.
        WriteZip("film.zip", ("film.mkv", Payload(80_000, 20)));
        byte[] whole = File.ReadAllBytes(P("film.zip"));
        File.WriteAllBytes(P("film.zip"), whole[..(whole.Length / 3)]);

        UnpackResult result = await new Unpacker(_work, Tools(), Ignore).RunAsync("");

        Assert.NotEqual(UnpackResult.PasswordRequired, result);
        Assert.True(result is UnpackResult.Corrupt or UnpackResult.Failed);
    }

    [Fact]
    public async Task A_damaged_archive_says_so_rather_than_asking_for_a_password()
    {
        WriteZip("film.zip", ("film.mkv", Payload(80_000, 21)));
        byte[] whole = File.ReadAllBytes(P("film.zip"));
        File.WriteAllBytes(P("film.zip"), whole[..(whole.Length / 3)]);

        PostProcessOutcome outcome =
            await new PostProcessCoordinator(_work, Tools(), logSink: Ignore).RunAsync("");

        Assert.Equal(PostProcessOutcome.ArchiveDamagedNoPar2, outcome);
        Assert.NotEqual(PostProcessOutcome.PasswordRequired, outcome);
    }

    [Fact]
    public async Task A_damaged_archive_with_par2_that_cannot_fix_it_is_reported_separately()
    {
        WriteZip("film.zip", ("film.mkv", Payload(60_000, 22)));
        byte[] whole = File.ReadAllBytes(P("film.zip"));
        Par2Fixture.Write(P("film.par2"), 4000,
            new[] { new Par2Fixture.InputFile("film.zip", whole) }, recoveryBlocks: 1);

        // Damage far more than one recovery block can carry.
        File.WriteAllBytes(P("film.zip"), whole[..(whole.Length / 3)]);

        PostProcessOutcome outcome =
            await new PostProcessCoordinator(_work, Tools(), logSink: Ignore).RunAsync("");

        Assert.Equal(PostProcessOutcome.ArchiveDamaged, outcome);
    }

    [Fact]
    public async Task An_encrypted_set_with_no_password_stops_before_any_work()
    {
        // A synthetic RAR4 header flagged encrypted: the probe decides up front,
        // which is the whole reason for doing it before the extractor runs.
        File.WriteAllBytes(P("secret.rar"), SyntheticEncryptedRar());

        Assert.Equal(UnpackResult.PasswordRequired, await new Unpacker(_work, Tools(), Ignore).RunAsync(""));
        Assert.True(File.Exists(P("secret.rar")));
    }

    [Fact]
    public async Task Several_archives_in_one_download_are_all_unpacked()
    {
        WriteZip("one.zip", ("a.txt", Payload(100, 6)));
        WriteZip("two.zip", ("b.txt", Payload(100, 7)));

        Assert.Equal(UnpackResult.Unpacked, await new Unpacker(_work, Tools(), Ignore).RunAsync(""));

        Assert.True(File.Exists(P("a.txt")));
        Assert.True(File.Exists(P("b.txt")));
        Assert.Empty(Directory.GetFiles(_work, "*.zip"));
    }

    [Fact]
    public async Task An_unrelated_split_file_survives_the_archive_cleanup()
    {
        // show.zip and show.001 share a base name but are not one set.
        WriteZip("show.zip", ("show.mkv", Payload(500, 8)));
        File.WriteAllBytes(P("show.001"), Payload(300, 9));

        Assert.Equal(UnpackResult.Unpacked, await new Unpacker(_work, Tools(), Ignore).RunAsync(""));

        Assert.False(File.Exists(P("show.zip")));
        Assert.True(File.Exists(P("show.001")));
    }

    // ── the whole pipeline ────────────────────────────────────────────────────

    [Fact]
    public async Task The_pipeline_walks_verify_check_repair_unpack_move_in_that_order()
    {
        // A split payload, a par2 set covering a damaged archive, and that archive.
        File.WriteAllBytes(P("extra.bin.001"), Enumerable.Repeat((byte)1, 100).ToArray());
        File.WriteAllBytes(P("extra.bin.002"), Enumerable.Repeat((byte)2, 40).ToArray());

        WriteZip("film.zip", ("film.mkv", Payload(30_000, 10)));
        byte[] zipBytes = File.ReadAllBytes(P("film.zip"));

        Par2Fixture.Write(P("film.par2"), 4000,
            new[] { new Par2Fixture.InputFile("film.zip", zipBytes) }, recoveryBlocks: 3);

        // Damage one slice so the repair stage has real work to do.
        byte[] damaged = (byte[])zipBytes.Clone();
        for (int i = 4000; i < 8000; i++) damaged[i] ^= 0x5A;
        File.WriteAllBytes(P("film.zip"), damaged);

        var stages = new List<DownloadStage>();
        var progress = new Progress<PostProcessProgress>(p =>
        {
            lock (stages) { if (stages.Count == 0 || stages[^1] != p.Stage) stages.Add(p.Stage); }
        });

        PostProcessOutcome outcome =
            await new PostProcessCoordinator(_work, Tools(), progress, Ignore).RunAsync("");

        Assert.Equal(PostProcessOutcome.Success, outcome);

        Assert.Equal(
            new[]
            {
                DownloadStage.Verifying,
                DownloadStage.Checking,
                DownloadStage.Unpacking,
                DownloadStage.Moving,
                DownloadStage.Success
            },
            stages);

        // Split parts joined, archive repaired then unpacked, par2 files cleaned up.
        Assert.True(File.Exists(P("extra.bin")));
        Assert.Equal(140, new FileInfo(P("extra.bin")).Length);
        Assert.True(File.Exists(P("film.mkv")));
        Assert.Empty(Directory.GetFiles(_work, "*.zip"));
        Assert.Empty(Directory.GetFiles(_work).Where(p => ArchiveNaming.IsPar2File(Path.GetFileName(p))));
    }

    [Fact]
    public async Task A_download_par2_says_is_beyond_repair_still_ends_as_a_warning()
    {
        WriteZip("film.zip", ("film.mkv", Payload(30_000, 11)));
        byte[] zipBytes = File.ReadAllBytes(P("film.zip"));

        Par2Fixture.Write(P("film.par2"), 4000,
            new[] { new Par2Fixture.InputFile("film.zip", zipBytes) }, recoveryBlocks: 1);

        byte[] damaged = (byte[])zipBytes.Clone();
        for (int i = 0; i < 12_000; i++) damaged[i] ^= 0x11;      // 3 slices, 1 block
        File.WriteAllBytes(P("film.zip"), damaged);

        PostProcessOutcome outcome =
            await new PostProcessCoordinator(_work, Tools(), logSink: Ignore).RunAsync("");

        Assert.Equal(PostProcessOutcome.Warning, outcome);
        Assert.True(File.Exists(P("film.zip")));                  // nothing was thrown away
    }

    [Fact]
    public async Task A_clean_download_passes_straight_through_to_success()
    {
        WriteZip("film.zip", ("film.mkv", Payload(20_000, 12)));
        byte[] zipBytes = File.ReadAllBytes(P("film.zip"));
        Par2Fixture.Write(P("film.par2"), 4000,
            new[] { new Par2Fixture.InputFile("film.zip", zipBytes) }, recoveryBlocks: 2);

        var stages = new List<DownloadStage>();
        var progress = new Progress<PostProcessProgress>(p => { lock (stages) stages.Add(p.Stage); });

        Assert.Equal(PostProcessOutcome.Success,
            await new PostProcessCoordinator(_work, Tools(), progress, Ignore).RunAsync(""));

        Assert.DoesNotContain(DownloadStage.Repairing, stages);
        Assert.True(File.Exists(P("film.mkv")));
    }

    [Fact]
    public async Task A_password_protected_set_stops_the_pipeline_and_keeps_the_archive()
    {
        File.WriteAllBytes(P("secret.rar"), SyntheticEncryptedRar());

        PostProcessOutcome outcome =
            await new PostProcessCoordinator(_work, Tools(), logSink: Ignore).RunAsync("");

        Assert.Equal(PostProcessOutcome.PasswordRequired, outcome);
        Assert.True(File.Exists(P("secret.rar")));
    }

    [Fact]
    public async Task Par2_files_survive_when_the_setting_says_to_keep_them()
    {
        WriteZip("film.zip", ("film.mkv", Payload(10_000, 13)));
        byte[] zipBytes = File.ReadAllBytes(P("film.zip"));
        Par2Fixture.Write(P("film.par2"), 4000,
            new[] { new Par2Fixture.InputFile("film.zip", zipBytes) }, recoveryBlocks: 1);

        var coordinator = new PostProcessCoordinator(_work, Tools(), logSink: Ignore)
        {
            RemovePar2Files = false
        };

        Assert.Equal(PostProcessOutcome.Success, await coordinator.RunAsync(""));
        Assert.True(File.Exists(P("film.par2")));
    }

    [Fact]
    public async Task A_download_with_no_par2_at_all_is_still_unpacked()
    {
        WriteZip("film.zip", ("film.mkv", Payload(5_000, 14)));

        Assert.Equal(PostProcessOutcome.Success,
            await new PostProcessCoordinator(_work, Tools(), logSink: Ignore).RunAsync(""));

        Assert.True(File.Exists(P("film.mkv")));
    }

    // ── fixtures ──────────────────────────────────────────────────────────────

    /// <summary>A minimal RAR4 archive whose single file header carries the password flag.</summary>
    private static byte[] SyntheticEncryptedRar()
    {
        var bytes = new List<byte> { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

        bytes.AddRange(new byte[] { 0x00, 0x00, 0x73 });      // main header
        bytes.AddRange(Le16(0x0000));
        bytes.AddRange(Le16(13));
        bytes.AddRange(new byte[6]);

        byte[] name = System.Text.Encoding.ASCII.GetBytes("film.mkv");
        int headSize = 32 + name.Length;
        const int packSize = 8;

        bytes.AddRange(new byte[] { 0x00, 0x00, 0x74 });      // file header
        bytes.AddRange(Le16(0x8000 | 0x0004));               // ADD_SIZE | password
        bytes.AddRange(Le16(headSize));
        bytes.AddRange(Le32(packSize));
        bytes.AddRange(Le32(packSize));
        bytes.Add(0x02);
        bytes.AddRange(Le32(0));
        bytes.AddRange(Le32(0));
        bytes.Add(20);
        bytes.Add(0x30);
        bytes.AddRange(Le16(name.Length));
        bytes.AddRange(Le32(0x20));
        bytes.AddRange(name);
        bytes.AddRange(new byte[packSize]);
        return bytes.ToArray();
    }

    private static byte[] Le16(int v) => new[] { (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF) };
    private static byte[] Le32(long v) => BitConverter.GetBytes((uint)v);
}
