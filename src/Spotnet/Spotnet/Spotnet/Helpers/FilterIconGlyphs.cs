using System;
using System.Collections.Generic;
using System.IO;

namespace Spotnet.Helpers;

/// <summary>
/// Maps the bitmap filter icons the built-in filter sets ship with onto FontAwesome
/// glyphs, for the Modern styles.
/// </summary>
/// <remarks>
/// The filter sets name their icons as file paths - <c>Image="\Images\video2.ico"</c> -
/// and those files come from at least three unrelated icon sets (KDE Crystal Clear, a
/// handful of loose .ico files, assorted PNGs), which is why the filter tree never looked
/// like one app. Rather than rewrite every filter set, Modern keys off the file name and
/// draws the equivalent glyph from Resources/fontawesome-webfont.ttf - the same font the
/// toolbar, tab bar and status bar already use.
///
/// A file with no entry here falls back to its bitmap, so a filter set with custom icons
/// keeps working and simply keeps its own artwork.
///
/// Glyphs are written as escapes on purpose: these code points live in the private use
/// area, and a literal in the source is one mis-detected encoding away from turning into
/// mojibake - which is exactly what happened to the search box icons.
/// </remarks>
internal static class FilterIconGlyphs
{
    /// <summary>folder-o, for a group whose own icon is unknown and that is collapsed.</summary>
    public const string FolderClosed = "\uF114";

    /// <summary>folder-open-o, the same group once unfolded.</summary>
    public const string FolderOpen = "\uF115";

    private const string Film = "\uF008";
    private const string Television = "\uF26C";
    private const string Book = "\uF02D";
    private const string Music = "\uF001";
    private const string Gamepad = "\uF11B";
    private const string Desktop = "\uF108";
    private const string Heart = "\uF004";
    private const string Disc = "\uF192";        // dot-circle-o
    private const string VideoFile = "\uF1C8";   // file-video-o
    private const string VideoCamera = "\uF03D";
    private const string Mobile = "\uF10B";
    private const string GenreList = "\uF0CA";   // list-ul

    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- the simple set, and the icons Filters.cs assigns in code ---
            ["favorites2.ico"] = "\uF005",       // star
            ["fav24.ico"] = "\uF005",            // star
            ["new2.ico"] = "\uF0F3",             // bell
            ["today.ico"] = "\uF017",            // clock-o
            ["video2.ico"] = Film,
            ["series2.ico"] = Television,
            ["books2.ico"] = Book,
            ["audio2.ico"] = Music,
            ["games2.ico"] = Gamepad,
            ["applications2.ico"] = Desktop,
            ["x2.ico"] = Heart,
            ["tag2.ico"] = "\uF02B",             // tag
            ["people2.ico"] = "\uF007",          // user
            ["custom2.ico"] = "\uF0B0",          // filter

            // --- top level of the advanced sets ---
            ["vandaag.png"] = "\uF017",          // clock-o
            ["overzicht.png"] = "\uF03A",        // list
            ["48px-crystal_clear_app_camera.png"] = Film,
            ["48px-crystal_clear_app_aktion.png"] = Television,
            ["48px-crystal_clear_device_pda_blue.png"] = Book,
            ["48px-crystal_clear_app_mp3.png"] = Music,
            ["48px-crystal_clear_device_joystick.png"] = Gamepad,
            ["48px-crystal_clear_app_demo.png"] = Desktop,
            ["sex-male-female.png"] = Heart,
            ["red.png"] = Film,
            ["blue.png"] = GenreList,
            ["green.png"] = Television,

            // --- video formats ---
            ["48px-crystal_clear_mimetype_source_moc.png"] = "\uF1B2",   // cube, 3D
            ["48px-crystal_clear_device_cdwriter_unmount.png"] = Disc,   // Bluray
            ["48px-crystal_clear_device_dvd_unmount.png"] = Disc,
            ["48px-crystal_clear_device_dvd_mount.png"] = Disc,
            ["48px-crystal_clear_device_dvd_mount_2.png"] = Disc,
            ["48px-crystal_clear_device_cdrom_mount.png"] = Disc,
            ["48px-crystal_clear_mimetype_soffice.png"] = VideoFile,     // DivX
            ["48px-crystal_clear_mimetype_dvi.png"] = VideoFile,         // MPG/WMV
            ["48px-crystal_clear_mimetype_source_py.png"] = VideoFile,   // WMV
            ["48px-crystal_clear_mimetype_cdimage.png"] = VideoCamera,   // HD

            // --- film genres ---
            ["48px-crystal_clear_app_aim3.png"] = "\uF0E7",        // bolt, Action
            ["48px-crystal_clear_app_access.png"] = "\uF14E",      // compass, Adventure
            ["48px-crystal_clear_app_proxy.png"] = "\uF130",       // microphone, Cabaret
            ["48px-crystal_clear_app_amor.png"] = "\uF118",        // smile-o, Comedy
            ["48px-crystal_clear_app_staroffice.png"] = VideoCamera,  // Documentary
            ["48px-crystal_clear_app_ksame.png"] = "\uF075",       // comment, Drama
            ["48px-crystal_clear_app_web.png"] = "\uF071",         // warning, Horror
            ["48px-crystal_clear_app_kbattleship.png"] = "\uF1E2", // bomb, War
            ["48px-crystal_clear_app_katomic.png"] = "\uF135",     // rocket, Sci-Fi
            ["48px-crystal_clear_app_gadu.png"] = "\uF1AE",        // child, Kids
            ["48px-crystal_clear_app_ksplash.png"] = "\uF06B",     // gift, Christmas
            ["48px-crystal_clear_app_clicknrun.png"] = "\uF1E3",   // futbol-o, Sport
            ["48px-crystal_clear_app_pysol.png"] = "\uF21B",       // user-secret, Thriller

            // --- books ---
            ["books.png"] = Book,
            ["48px-crystal_clear_device_pda.png"] = Book,
            ["48px-crystal_clear_boeken_nl.png"] = Book,
            ["48px-crystal_clear_action_editcut.png"] = "\uF0C4",  // scissors, Cuttingsheets
            ["48px-crystal_clear_app_wine.png"] = "\uF1EA",        // newspaper-o, Magazines
            ["48px-crystal_clear_app_krita.png"] = "\uF1FC",       // paint-brush, Strips

            // --- music ---
            ["music.png"] = Music,
            ["musicgenres.png"] = GenreList,
            ["48px-crystal_clear_compressed.png"] = "\uF1C6",      // file-archive-o
            ["48px-crystal_clear_discografie.png"] = "\uF0CB",     // list-ol
            ["48px-crystal_clear_lossless.png"] = "\uF1C7",        // file-audio-o
            ["48px-crystal_clear_luisterboek.png"] = "\uF025",     // headphones, Audiobooks

            // --- games and platforms ---
            ["games.png"] = Gamepad,
            ["mario.ico"] = Gamepad,
            ["psp.png"] = Gamepad,
            ["xbox.png"] = Gamepad,
            ["gameboy.png"] = Gamepad,
            ["wii.png"] = Gamepad,
            ["48px-crystal_clear_app_kcmdevices.png"] = Gamepad,   // GameCube
            ["48px-crystal_clear_app_klaptop.png"] = Gamepad,      // 3DS / NDS
            ["aircraft.png"] = "\uF072",                           // plane, Flightsims

            ["os_windows.png"] = "\uF17A",
            ["os_apple.png"] = "\uF179",
            ["os_linux.png"] = "\uF17C",
            ["os_android.png"] = "\uF17B",
            ["android-tablet.png"] = "\uF10A",                     // tablet
            ["blackberry.png"] = Mobile,
            ["symbian.png"] = Mobile,

            // --- software and the rest ---
            ["applications.png"] = Desktop,
            ["48px-crystal_clear_filesystem_chardevice.png"] = "\uF124", // location-arrow
            ["48px-crystal_clear_spotnet.png"] = "\uF0C2",               // cloud
            ["erotic.png"] = Heart,
            ["sex-male.png"] = "\uF222",                                 // mars
            ["sex-female.png"] = "\uF221",                               // venus
        };

    /// <summary>
    /// The glyph for a filter's icon reference, or null when the icon is one this table
    /// does not know and the caller should fall back to drawing the bitmap.
    /// </summary>
    public static string ForIcon(string imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            return null;
        }

        string fileName;
        try
        {
            fileName = Path.GetFileName(imageReference.Trim().Replace('\\', '/'));
        }
        catch (ArgumentException)
        {
            return null;
        }

        return Map.TryGetValue(fileName, out string glyph) ? glyph : null;
    }

    /// <summary>Every glyph this table can produce, for verification and preview.</summary>
    public static IEnumerable<KeyValuePair<string, string>> All => Map;
}
