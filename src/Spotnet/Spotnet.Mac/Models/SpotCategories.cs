using System;
using System.Globalization;

namespace Spotnet.Mac.Models;

/// <summary>
/// Spotnet's category tables, ported from the Windows client's AppHelper
/// (TranslateCat / TranslateCatShort / TranslateInfo / ExtCatToString) with the Dutch
/// strings taken from Spotnet.Properties.Categories.nl.
///
/// Two columns depend on these: "Formaat" is the a-subcategory in its short form
/// (x264, MP3, WAV, Win, …), and "Genre" is the first named genre subcategory
/// (Televisie, Waargebeurd, Komedie, Systeem, …).
/// </summary>
public static class SpotCategories
{
    /// <summary>
    /// The "Formaat" column, from the stored <c>subcat</c> value (category*100 + the
    /// a-subcategory). Windows splits the digits exactly like this — first digit as the
    /// category, the rest as the code — so a spot with no a-subcategory (code 100) is
    /// read against the next category's table. That quirk is kept deliberately: it is
    /// what the Windows column shows.
    /// Mirrors SpotRowViewModel.Formaat.
    /// </summary>
    public static string FormatFromSubcat(int subCat)
    {
        if (subCat < 10) return "";

        string text = subCat.ToString(CultureInfo.InvariantCulture);
        int category = int.Parse(text.AsSpan(0, 1), CultureInfo.InvariantCulture);
        if (!int.TryParse(text.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
        {
            return "";
        }
        return FormatShort(category, code);
    }

    /// <summary>
    /// The "Formaat" column: the short label for a category's a-subcategory.
    /// Mirrors AppHelper.TranslateCatShort.
    /// </summary>
    public static string FormatShort(int category, int subCat) => category switch
    {
        2 => subCat switch
        {
            0 => "MP3", 1 => "WMA", 2 => "WAV", 3 => "OGG", 4 => "EAC",
            5 => "DTS", 6 => "AAC", 7 => "APE", 8 => "FLAC", _ => ""
        },
        3 => subCat switch
        {
            0 => "Win", 1 => "Mac", 2 => "Linux", 3 => "PSX", 4 => "PS2",
            5 => "PSP", 6 => "XBox", 7 => "360", 8 => "GBA", 9 => "GC",
            10 => "NDS", 11 => "Wii", 12 => "PS3", 13 => "WP7", 14 => "iOs",
            15 => "Android", _ => ""
        },
        4 => subCat switch
        {
            0 => "Win", 1 => "Mac", 2 => "Linux", 3 => "OS2", 4 => "WP7",
            5 => "Navi", 6 => "iOs", 7 => "Android", _ => ""
        },
        _ => subCat switch
        {
            0 => "DivX", 1 => "WMV", 2 => "MPG", 3 => "DVD5", 4 => "HD",
            5 => "ePub", 6 => "Bluray", 7 => "HD", 8 => "HD", 9 => "x264",
            10 => "DVD9", 11 => "PDF", 12 => "Bitmap", 13 => "Vector", _ => ""
        }
    };

    /// <summary>
    /// The "Genre" column. <paramref name="extCat"/> is category*100 + the genre code
    /// picked by <see cref="PickGenreCode"/>; codes above 98 mean "no genre".
    /// Mirrors AppHelper.ExtCatToString.
    /// </summary>
    public static string GenreFromExtCat(int extCat)
    {
        if (extCat < 100) return "";

        string text = extCat.ToString(CultureInfo.InvariantCulture);
        int category = int.Parse(text.AsSpan(0, 1), CultureInfo.InvariantCulture);
        int code = int.Parse(text.AsSpan(1), CultureInfo.InvariantCulture);
        if (code > 98) return "";

        return Translate(category, GenreLetter(category), code);
    }

    /// <summary>
    /// The "Genre" column, straight from the <c>cats</c> column. Equivalent to what
    /// Windows shows: it stores category*100 + <see cref="PickGenreCode"/> in
    /// <c>extcat</c> and names that back with the same tables, and the code is always
    /// 0..99 so the round-trip through extcat cannot change the answer. Reading
    /// <c>cats</c> directly means rows written before a parser fix still render right.
    /// </summary>
    public static string GenreFromCats(int category, string cats)
    {
        byte code = PickGenreCode(category, cats);
        return code > 98 ? "" : Translate(category, GenreLetter(category), code);
    }

    /// <summary>
    /// Which subcategory letter carries the genre for a category: games use "c",
    /// software "b", everything else "d".
    /// </summary>
    private static char GenreLetter(int category) => category switch { 3 => 'c', 4 => 'b', _ => 'd' };

    /// <summary>
    /// Walks the cats column ("1 1a9 1b3 1d11 1z0") and returns the first genre code
    /// that actually has a name, or 99 when there is none. Mirrors AppHelper.TranslateInfo.
    /// </summary>
    public static byte PickGenreCode(int category, string cats)
    {
        if (string.IsNullOrEmpty(cats)) return 99;

        char letter = GenreLetter(category);
        for (int i = cats.IndexOf(letter); i >= 0; i = cats.IndexOf(letter, i + 1))
        {
            int digitsStart = i + 1;
            int digitsEnd = digitsStart;
            while (digitsEnd < cats.Length && char.IsDigit(cats[digitsEnd])) digitsEnd++;
            if (digitsEnd == digitsStart) continue;

            if (!byte.TryParse(cats.AsSpan(digitsStart, digitsEnd - digitsStart), out byte code)) continue;
            if (code > 100) continue;

            // Erotica: the broad orientation codes give way to a more specific genre
            // later in the string, if there is one.
            if (category == 9 && (code is >= 23 and <= 26 or >= 72 and <= 75)
                && cats.IndexOf(letter, i + 1) > -1)
            {
                continue;
            }

            if (Translate(category, letter, code).Length > 0) return code;
        }

        return 99;
    }

    /// <summary>
    /// Names one subcategory. This is AppHelper.TranslateCat with strict=false.
    /// </summary>
    public static string Translate(int category, char letter, int n) => category switch
    {
        2 => letter switch
        {
            'a' => MusicFormat(n), 'b' => MusicSource(n), 'c' => MusicBitrate(n),
            'd' => MusicGenre(n),  'z' => MusicKind(n),   _ => ""
        },
        3 => letter switch
        {
            'a' => GamePlatform(n), 'b' => GameFormat(n), 'c' => GameGenre(n),
            'z' => Kind(n),         _ => ""
        },
        4 => letter switch
        {
            'a' => SoftwarePlatform(n), 'b' => SoftwareGenre(n), 'z' => Kind(n), _ => ""
        },
        _ => letter switch
        {
            'a' => VideoFormat(n), 'b' => VideoSource(n), 'c' => VideoLanguage(category, n),
            'd' => VideoGenre(n),  'z' => Kind(n),        _ => ""
        }
    };

    /// <summary>
    /// The label Windows puts in front of a subcategory in the spot detail panel
    /// (AppHelper.TranslateCatDesc): Formaat, Bron, Bitrate, Taal, Genre, Platform,
    /// Categorie.
    /// </summary>
    public static string DescribeLetter(int category, char letter) => category switch
    {
        2 => letter switch { 'a' => "Formaat", 'b' => "Bron", 'c' => "Bitrate", 'd' => "Genre", 'z' => "Categorie", _ => "" },
        3 => letter switch { 'a' => "Platform", 'b' => "Formaat", 'c' => "Genre", 'z' => "Categorie", _ => "" },
        4 => letter switch { 'a' => "Platform", 'b' => "Genre", 'z' => "Categorie", _ => "" },
        _ => letter switch { 'a' => "Formaat", 'b' => "Bron", 'c' => "Taal", 'd' => "Genre", 'z' => "Categorie", _ => "" }
    };

    private static string MusicFormat(int n) => n switch
    {
        0 => "MP3", 1 => "WMA", 2 => "WAV", 3 => "OGG", 4 => "EAC",
        5 => "DTS", 6 => "AAC", 7 => "APE", 8 => "FLAC", _ => ""
    };

    private static string MusicSource(int n) => n switch
    {
        0 => "CD", 1 => "Radio", 2 => "Compilatie", 3 => "DVD",
        5 => "Vinyl", 6 => "Stream", _ => ""
    };

    private static string MusicBitrate(int n) => n switch
    {
        0 => "Variabel", 1 => "< 96kbit", 2 => "96kbit", 3 => "128kbit",
        4 => "160kbit", 5 => "192kbit", 6 => "256kbit", 7 => "320kbit",
        8 => "Lossless", _ => ""
    };

    private static string MusicKind(int n) => n switch
    {
        0 => "Album", 1 => "Liveset", 2 => "Podcast", 3 => "Luisterboek", _ => ""
    };

    private static string Kind(int n) => n switch
    {
        0 => "Film", 1 => "Serie", 2 => "Boek", 3 => "Erotiek", 4 => "Afbeeldingen", _ => ""
    };

    private static string VideoFormat(int n) => n switch
    {
        0 => "DivX", 1 => "WMV", 2 => "MPG", 3 => "DVD5", 4 => "HD Overig",
        5 => "ePub", 6 => "Bluray", 7 => "HD-DVD", 8 => "WMV HD", 9 => "x264",
        10 => "DVD9", 11 => "PDF", 12 => "Bitmap", 13 => "Vector", _ => ""
    };

    private static string VideoSource(int n) => n switch
    {
        0 => "Cam", 1 => "(S)VCD", 2 => "Promo", 3 => "Retail", 4 => "TV",
        6 => "Satellite", 7 => "R5", 8 => "Telecine", 9 => "Telesync",
        10 => "Scan", _ => ""
    };

    /// <summary>Category 5 words its language codes as "geschreven" rather than "gesproken".</summary>
    private static string VideoLanguage(int category, int n) => n switch
    {
        0 => "Geen ondertitels",
        1 => "Nederlands ondertiteld (extern)",
        2 => category == 5 ? "Nederlands geschreven" : "Nederlands ondertiteld (ingebakken)",
        3 => "Engels ondertiteld (extern)",
        4 => category == 5 ? "Engels geschreven" : "Engels ondertiteld (ingebakken)",
        6 => "Nederlands ondertiteld (instelbaar)",
        7 => "Engels ondertiteld (instelbaar)",
        10 => "Engels gesproken",
        11 => "Nederlands gesproken",
        12 => category == 5 ? "Duits geschreven" : "Duits gesproken",
        13 => category == 5 ? "Frans geschreven" : "Frans gesproken",
        14 => category == 5 ? "Spaans geschreven" : "Spaans gesproken",
        _ => ""
    };

    private static string GamePlatform(int n) => n switch
    {
        0 => "Windows", 1 => "Macintosh", 2 => "Linux", 3 => "Playstation",
        4 => "Playstation 2", 5 => "PSP", 6 => "XBox", 7 => "XBox 360",
        8 => "Gameboy Advance", 9 => "Gamecube", 10 => "Nintendo DS",
        11 => "Nintendo Wii", 12 => "Playstation 3", 13 => "Windows Phone",
        14 => "iOs", 15 => "Android", 16 => "Nintendo 3DS", _ => ""
    };

    private static string GameFormat(int n) => n switch
    {
        0 => "ISO", 1 => "Rip", 2 => "Retail", 3 => "DLC", 5 => "Patch", 6 => "Crack", _ => ""
    };

    private static string SoftwarePlatform(int n) => n switch
    {
        0 => "Windows", 1 => "Macintosh", 2 => "Linux", 3 => "OS/2",
        4 => "Windows Phone", 5 => "Navi", 6 => "iOs", 7 => "Android", _ => ""
    };

    /// <summary>Video/book/erotica genres (the "d" list shared by categories 1, 5, 6 and 9).</summary>
    private static string VideoGenre(int n) => n switch
    {
        0 => "Actie",         1 => "Avontuur",       2 => "Animatie",     3 => "Cabaret",
        4 => "Komedie",       5 => "Misdaad",        6 => "Documentaire", 7 => "Drama",
        8 => "Familie",       9 => "Fantasie",      10 => "Filmhuis",    11 => "Televisie",
        12 => "Horror",      13 => "Muziek",        14 => "Musical",     15 => "Mysterie",
        16 => "Romantiek",   17 => "Science Fiction", 18 => "Sport",     19 => "Kort",
        20 => "Thriller",    21 => "Oorlog",        22 => "Western",     23 => "Hetero",
        24 => "Homo",        25 => "Lesbo",         26 => "Bi",
        28 => "Aziatisch",   29 => "Anime",         30 => "Cover",       31 => "Stripboek",
        32 => "Cartoon",     33 => "Jeugd",         34 => "Zakelijk",    35 => "Computer",
        36 => "Hobby",       37 => "Koken",         38 => "Knutselen",   39 => "Handwerk",
        40 => "Gezondheid",  41 => "Historie",      42 => "Psychologie", 43 => "Dagblad",
        44 => "Tijdschrift", 45 => "Wetenschap",    46 => "Vrouw",       47 => "Religie",
        48 => "Roman",       49 => "Biografie",     50 => "Detective",   51 => "Dieren",
        53 => "Reizen",      54 => "Waargebeurd",   55 => "Non-fictie",
        57 => "Poezie",      58 => "Sprookje",
        72 => "Bi",          73 => "Lesbo",         74 => "Homo",        75 => "Hetero",
        76 => "Amateur",     77 => "Groep",         78 => "POV",         79 => "Solo",
        80 => "Jong",        81 => "Soft",          82 => "Fetisj",      83 => "Oud",
        84 => "BBW",         85 => "SM",            86 => "Hard",        87 => "Donker",
        88 => "Hentai",      89 => "Buiten",
        _ => ""
    };

    /// <summary>Music genres (the "d" list of category 2).</summary>
    private static string MusicGenre(int n) => n switch
    {
        0 => "Blues",      1 => "Compilatie", 2 => "Cabaret",    3 => "Dance",
        4 => "Diversen",   5 => "Hardstyle",  6 => "Wereld",     7 => "Jazz",
        8 => "Jeugd",      9 => "Klassiek",  10 => "Kleinkunst", 11 => "Hollands",
        12 => "New Age",  13 => "Pop",       14 => "RnB",       15 => "Hiphop",
        16 => "Reggae",   17 => "Religieus", 18 => "Rock",      19 => "Soundtrack",
        21 => "Hardstyle", 22 => "Aziatisch", 23 => "Disco",    24 => "Classics",
        25 => "Metal",    26 => "Country",   27 => "Dubstep",   28 => "Nederhop",
        29 => "DnB",      30 => "Electro",   31 => "Folk",      32 => "Soul",
        33 => "Trance",   34 => "Balkan",    35 => "Techno",    36 => "Ambient",
        37 => "Latin",    38 => "Live",
        _ => ""
    };

    /// <summary>Game genres (the "c" list of category 3).</summary>
    private static string GameGenre(int n) => n switch
    {
        0 => "Actie",     1 => "Avontuur",  2 => "Strategie", 3 => "Rollenspel",
        4 => "Simulatie", 5 => "Race",      6 => "Vliegen",   7 => "Shooter",
        8 => "Platform",  9 => "Sport",    10 => "Jeugd",    11 => "Puzzel",
        13 => "Bordspel", 14 => "Kaart",   15 => "Educatie", 16 => "Muziek",
        17 => "Party",
        _ => ""
    };

    /// <summary>Software genres (the "b" list of category 4).</summary>
    private static string SoftwareGenre(int n) => n switch
    {
        0 => "Audio",              1 => "Video",             2 => "Grafisch",
        3 => "CD/DVD Tools",       4 => "Media Players",     5 => "Rippers & Encoders",
        6 => "Plugins",            7 => "Database Tools",    8 => "Email Software",
        9 => "Foto",              10 => "Screensavers",     11 => "Skin Software",
        12 => "Drivers",          13 => "Browsers",         14 => "Download Managers",
        15 => "Download",         16 => "Usenet Software",  17 => "RSS Readers",
        18 => "FTP Software",     19 => "Firewalls",        20 => "Antivirus Software",
        21 => "Antispyware Software", 22 => "Optimization Software",
        23 => "Beveiliging",      24 => "Systeem",          26 => "Educatief",
        27 => "Kantoor",          28 => "Internet",         29 => "Communicatie",
        30 => "Ontwikkel",        31 => "Spotnet",
        _ => ""
    };
}
