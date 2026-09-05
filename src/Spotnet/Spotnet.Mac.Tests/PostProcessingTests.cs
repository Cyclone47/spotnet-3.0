using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Spotnet.Mac.Models;
using Spotnet.Mac.PostProcessing;
using Spotnet.Mac.Services;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Covers the post-download pipeline's naming, split joining and archive header
/// probing. par2 verification and repair have their own suite in Par2Tests; the
/// unpacking path is covered by PostProcessPipelineTests.
/// </summary>
public sealed class PostProcessingTests : IDisposable
{
    private readonly string _dir;

    public PostProcessingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "spotnet-pp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp dir */ }
    }

    private void Write(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(_dir, name), bytes);
    private void Write(string name, int length, byte fill = 0x41) => Write(name, Enumerable.Repeat(fill, length).ToArray());
    private static void Ignore(string _) { }

    // ── stage labels ──────────────────────────────────────────────────────────

    [Fact]
    public void Stage_labels_match_the_Windows_client_wording()
    {
        Assert.Equal("Controleren", DownloadStageText.Label(DownloadStage.Checking));
        Assert.Equal("Repareren", DownloadStageText.Label(DownloadStage.Repairing));
        Assert.Equal("Uitpakken", DownloadStageText.Label(DownloadStage.Unpacking));
        Assert.Equal("Verifiëren", DownloadStageText.Label(DownloadStage.Verifying));
        Assert.Equal("Verplaatsen", DownloadStageText.Label(DownloadStage.Moving));
        Assert.Equal("Wachtwoord?", DownloadStageText.Label(DownloadStage.WrongPassword));
        Assert.Equal("Compleet", DownloadStageText.Label(DownloadStage.Success));
        Assert.Equal("Par2 downloaden", DownloadStageText.Label(DownloadStage.Par2PieceDownloading));
    }

    [Fact]
    public void Post_processing_stages_drive_the_row_state()
    {
        var item = new DownloadItem { BytesTotal = 1000, BytesDone = 1000 };

        item.SetStage(DownloadStage.Repairing, "42%");
        Assert.Equal("Repareren — 42%", item.Status);
        Assert.True(item.IsPostProcessing);
        Assert.False(item.NeedsPassword);

        item.PostProcessPercent = 42;
        Assert.False(item.IsProgressIndeterminate);
        Assert.Equal(42, item.ProgressPercent);

        item.PostProcessPercent = -1;
        item.SetStage(DownloadStage.Verifying);
        Assert.True(item.IsProgressIndeterminate);

        item.SetStage(DownloadStage.WrongPassword, "wachtwoord vereist");
        Assert.True(item.NeedsPassword);
        Assert.False(item.IsCompleted);
        Assert.Equal("Wachtwoord? — wachtwoord vereist", item.Status);
    }

    // ── history back-compat ───────────────────────────────────────────────────

    [Theory]
    [InlineData("✓ Download voltooid", DownloadStage.Success)]
    [InlineData("Fout: geen server", DownloadStage.Failure)]
    [InlineData("Geannuleerd", DownloadStage.Cancelled)]
    [InlineData("NZB opgeslagen", DownloadStage.NzbSaved)]
    [InlineData("Downloaden... 45%", DownloadStage.Queued)]
    public void Legacy_status_strings_map_onto_stages(string legacy, DownloadStage expected)
    {
        Assert.Equal(expected, DownloadHistoryService.StageFromLegacyStatus(legacy));
    }

    [Fact]
    public void An_interrupted_download_comes_back_queued_not_half_done()
    {
        Assert.Equal(DownloadStage.Queued, DownloadHistoryService.InterruptedStage(DownloadStage.Downloading));
        Assert.Equal(DownloadStage.Queued, DownloadHistoryService.InterruptedStage(DownloadStage.Unpacking));
        Assert.Equal(DownloadStage.Success, DownloadHistoryService.InterruptedStage(DownloadStage.Success));
        // A row waiting on a password keeps waiting: the password is persisted too.
        Assert.Equal(DownloadStage.WrongPassword, DownloadHistoryService.InterruptedStage(DownloadStage.WrongPassword));
    }

    // ── split file joining ────────────────────────────────────────────────────

    [Fact]
    public void A_complete_split_set_is_joined_and_the_parts_removed()
    {
        Write("film.mkv.001", 100, 0x01);
        Write("film.mkv.002", 100, 0x02);
        Write("film.mkv.003", 40, 0x03);

        List<string> joined = SplitFileJoiner.JoinAll(_dir, Ignore);

        Assert.Equal(new[] { "film.mkv" }, joined);
        byte[] result = File.ReadAllBytes(Path.Combine(_dir, "film.mkv"));
        Assert.Equal(240, result.Length);
        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x02, result[100]);
        Assert.Equal(0x03, result[200]);
        Assert.False(File.Exists(Path.Combine(_dir, "film.mkv.001")));
    }

    [Fact]
    public void A_split_set_with_a_gap_is_left_alone()
    {
        Write("film.mkv.001", 100);
        Write("film.mkv.003", 40);

        Assert.Empty(SplitFileJoiner.JoinAll(_dir, Ignore));
        Assert.False(File.Exists(Path.Combine(_dir, "film.mkv")));
    }

    [Fact]
    public void A_still_downloading_set_with_no_short_tail_is_left_alone()
    {
        // Every part the same size means the last one has not arrived yet.
        Write("film.mkv.001", 100);
        Write("film.mkv.002", 100);

        Assert.Empty(SplitFileJoiner.JoinAll(_dir, Ignore));
    }

    // ── archive naming ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("show.rar", true)]
    [InlineData("show.r00", true)]
    [InlineData("show.z09", true)]
    [InlineData("show.001", true)]
    [InlineData("show.nfo", false)]
    [InlineData("show.par2", false)]
    public void Rar_naming_follows_the_Windows_regex(string name, bool expected)
    {
        Assert.Equal(expected, ArchiveNaming.IsRarFile(name));
    }

    [Fact]
    public void Par2_files_are_never_handed_to_the_unpacker()
    {
        Assert.False(ArchiveNaming.IsArchive("show.vol000+01.par2"));
        Assert.True(ArchiveNaming.IsArchive("show.part1.rar"));
        Assert.True(ArchiveNaming.IsArchive("show.zip"));
        Assert.True(ArchiveNaming.IsArchive("show.7z.001"));
    }

    [Theory]
    [InlineData("show.rar", true)]
    [InlineData("show.part1.rar", true)]
    [InlineData("show.part01.rar", true)]
    [InlineData("show.part001.rar", true)]
    [InlineData("show.part02.rar", false)]
    [InlineData("show.part12.rar", false)]
    [InlineData("show.r00", false)]
    public void Only_the_first_volume_of_a_rar_set_is_opened(string name, bool expected)
    {
        Assert.Equal(expected, ArchiveNaming.IsFirstRarVolume(name));
    }

    [Fact]
    public void Volumes_of_one_set_share_a_multipart_base()
    {
        Assert.Equal("show", ArchiveNaming.MultipartBase("show.part01.rar"));
        Assert.Equal("show", ArchiveNaming.MultipartBase("show.part40.rar"));
        Assert.Equal("show", ArchiveNaming.MultipartBase("show.rar"));
        Assert.Null(ArchiveNaming.MultipartBase("show.nfo"));
    }

    [Fact]
    public void The_archive_scan_picks_one_entry_per_set()
    {
        var tools = new PostProcessToolset(_dir);
        Write("show.part01.rar", 10);
        Write("show.part02.rar", 10);
        Write("show.part03.rar", 10);
        Write("other.rar", 10);
        Write("show.nfo", 10);

        var volumes = new Unpacker(_dir, tools, Ignore).ArchiveSets();

        Assert.Equal(new[] { "other.rar", "show.part01.rar" }, volumes.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void An_extensionless_part_with_a_rar_signature_is_still_unpacked()
    {
        var tools = new PostProcessToolset(_dir);
        // "Non standart rar archives" in the Windows client's words: posted as .001
        // with no .rar in sight, recognised only by the signature.
        Write("mystery.001", Rar4Signature().Concat(Enumerable.Repeat((byte)0, 20)).ToArray());
        Write("mystery.002", 20);

        var volumes = new Unpacker(_dir, tools, Ignore).ArchiveSets();

        Assert.Equal(new[] { "mystery.001" }, volumes);
    }

    // ── password detection ────────────────────────────────────────────────────

    [Fact]
    public void A_rar4_file_marked_encrypted_needs_a_password()
    {
        Write("secret.rar", Rar4(fileEncrypted: true));
        Assert.Equal(ArchiveEncryption.Encrypted, ArchivePasswordProbe.Inspect(Path.Combine(_dir, "secret.rar")));
    }

    [Fact]
    public void A_plain_rar4_file_needs_no_password()
    {
        Write("open.rar", Rar4(fileEncrypted: false));
        Assert.Equal(ArchiveEncryption.None, ArchivePasswordProbe.Inspect(Path.Combine(_dir, "open.rar")));
    }

    [Fact]
    public void A_rar4_archive_with_encrypted_headers_needs_a_password()
    {
        Write("secret.rar", Rar4(fileEncrypted: false, encryptedHeaders: true));
        Assert.Equal(ArchiveEncryption.EncryptedHeaders, ArchivePasswordProbe.Inspect(Path.Combine(_dir, "secret.rar")));
    }

    [Fact]
    public void A_rar5_archive_with_a_crypt_header_needs_a_password()
    {
        Write("secret5.rar", Rar5WithCryptHeader());
        Assert.Equal(ArchiveEncryption.EncryptedHeaders, ArchivePasswordProbe.Inspect(Path.Combine(_dir, "secret5.rar")));
    }

    [Fact]
    public void An_encrypted_zip_entry_needs_a_password()
    {
        Write("secret.zip", Zip(encrypted: true));
        Assert.Equal(ArchiveEncryption.Encrypted, ArchivePasswordProbe.Inspect(Path.Combine(_dir, "secret.zip")));

        Write("open.zip", Zip(encrypted: false));
        Assert.Equal(ArchiveEncryption.None, ArchivePasswordProbe.Inspect(Path.Combine(_dir, "open.zip")));
    }

    [Fact]
    public void A_directory_verdict_is_the_strongest_of_its_archives()
    {
        Write("open.rar", Rar4(fileEncrypted: false));
        Write("plain.txt", 10);
        Assert.Equal(ArchiveEncryption.None, ArchivePasswordProbe.InspectDirectory(_dir));

        Write("secret.rar", Rar4(fileEncrypted: true));
        Assert.Equal(ArchiveEncryption.Encrypted, ArchivePasswordProbe.InspectDirectory(_dir));
    }

    [Fact]
    public async System.Threading.Tasks.Task An_encrypted_set_with_no_password_stops_before_any_tool_runs()
    {
        // No unrar needed: the probe decides, which is the point of doing it up front.
        Write("secret.rar", Rar4(fileEncrypted: true));
        var unpacker = new Unpacker(_dir, new PostProcessToolset(_dir), Ignore);

        Assert.Equal(UnpackResult.PasswordRequired, await unpacker.RunAsync(""));
    }

    [Fact]
    public async System.Threading.Tasks.Task A_directory_with_no_archives_has_nothing_to_unpack()
    {
        Write("film.mkv", 100);
        var unpacker = new Unpacker(_dir, new PostProcessToolset(_dir), Ignore);

        Assert.Equal(UnpackResult.NothingToDo, await unpacker.RunAsync(""));
    }

    [Fact]
    public async System.Threading.Tasks.Task The_pipeline_reports_a_password_before_touching_par2_or_the_payload()
    {
        Write("secret.rar", Rar4(fileEncrypted: true));
        var stages = new List<DownloadStage>();
        var progress = new Progress<PostProcessProgress>(p => stages.Add(p.Stage));

        var outcome = await new PostProcessCoordinator(_dir, new PostProcessToolset(_dir), progress)
            .RunAsync("");

        Assert.Equal(PostProcessOutcome.PasswordRequired, outcome);
        Assert.True(File.Exists(Path.Combine(_dir, "secret.rar")));
    }

    // ── file moving ───────────────────────────────────────────────────────────

    [Fact]
    public void Staged_files_are_lifted_out_of_their_subdirectory()
    {
        string staging = Path.Combine(_dir, "__unpack", "Some.Release");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "film.mkv"), "x");

        FileMover.MoveRecursively(Path.Combine(_dir, "__unpack"), _dir, Ignore);

        Assert.True(File.Exists(Path.Combine(_dir, "Some.Release", "film.mkv")));
    }

    // ── synthesised inputs ────────────────────────────────────────────────────

    private static byte[] Rar4Signature() => new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

    /// <summary>A minimal but structurally valid RAR4 archive: signature, main header, one file header.</summary>
    private static byte[] Rar4(bool fileEncrypted, bool encryptedHeaders = false)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Rar4Signature());

        // main header, type 0x73, 13 bytes
        int mainFlags = encryptedHeaders ? 0x0080 : 0x0000;
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x73 });
        bytes.AddRange(Le16(mainFlags));
        bytes.AddRange(Le16(13));
        bytes.AddRange(new byte[6]);

        // file header, type 0x74, with ADD_SIZE and optionally the password flag
        byte[] name = Encoding.ASCII.GetBytes("film.mkv");
        int headSize = 32 + name.Length;
        const int packSize = 8;
        int fileFlags = 0x8000 | (fileEncrypted ? 0x0004 : 0x0000);

        bytes.AddRange(new byte[] { 0x00, 0x00, 0x74 });
        bytes.AddRange(Le16(fileFlags));
        bytes.AddRange(Le16(headSize));
        bytes.AddRange(Le32(packSize));      // ADD_SIZE
        bytes.AddRange(Le32(packSize));      // UNP_SIZE
        bytes.Add(0x02);                     // host OS
        bytes.AddRange(Le32(0));             // file CRC
        bytes.AddRange(Le32(0));             // ftime
        bytes.Add(20);                       // unpack version
        bytes.Add(0x30);                     // method
        bytes.AddRange(Le16(name.Length));
        bytes.AddRange(Le32(0x20));          // attributes
        bytes.AddRange(name);
        bytes.AddRange(new byte[packSize]);  // the "file data"
        return bytes.ToArray();
    }

    /// <summary>A RAR5 archive whose first block is the encryption header (type 4).</summary>
    private static byte[] Rar5WithCryptHeader()
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 });
        bytes.AddRange(new byte[] { 0, 0, 0, 0 });   // header CRC32
        bytes.Add(0x02);                             // header size vint: type + flags
        bytes.Add(0x04);                             // header type 4 = crypt
        bytes.Add(0x00);                             // header flags
        return bytes.ToArray();
    }

    /// <summary>A ZIP holding exactly one local file header.</summary>
    private static byte[] Zip(bool encrypted)
    {
        byte[] name = Encoding.ASCII.GetBytes("film.mkv");
        byte[] data = new byte[16];
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        bytes.AddRange(Le16(20));                                   // version needed
        bytes.AddRange(Le16(encrypted ? 0x0001 : 0x0000));          // general purpose flags
        bytes.AddRange(Le16(0));                                    // method: stored
        bytes.AddRange(Le16(0));                                    // time
        bytes.AddRange(Le16(0));                                    // date
        bytes.AddRange(Le32(0));                                    // crc32
        bytes.AddRange(Le32(data.Length));                          // compressed size
        bytes.AddRange(Le32(data.Length));                          // uncompressed size
        bytes.AddRange(Le16(name.Length));
        bytes.AddRange(Le16(0));                                    // extra length
        bytes.AddRange(name);
        bytes.AddRange(data);
        return bytes.ToArray();
    }

    private static byte[] Le16(int v) => new[] { (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF) };
    private static byte[] Le32(long v) => BitConverter.GetBytes((uint)v);
}
