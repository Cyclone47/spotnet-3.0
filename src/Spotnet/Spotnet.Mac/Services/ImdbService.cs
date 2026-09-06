using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Spotnet.Mac.Services;

/// <summary>
/// Eén rij in het IMDb- of iTunes-paneel: label + waarde, zoals de tabelrijen in
/// Windows' spotthema (<c>&lt;td&gt;Title&lt;/td&gt;&lt;td&gt;…&lt;/td&gt;</c>).
/// </summary>
public sealed class SpotInfoRow
{
    public SpotInfoRow(string label, string value) { Label = label; Value = value; }
    public string Label { get; }
    public string Value { get; }

    /// <summary>De rij is een klikbare link (waarde = URL), zoals Windows' ImdbID-rij.</summary>
    public bool IsLink { get; init; }
}

/// <summary>
/// IMDb- en iTunes-informatie bij een spot — de macOS-tegenhanger van het
/// <c>loadImdb()</c>-blok in Windows' spotthema's. Voor Films en Series vraagt het
/// omdbapi.com met de opgeschoonde titel; voor Muziek bouwt het dezelfde vier
/// winkel-links (Allmusic, Amazon, Last.fm, Bol.com) en vraagt het iTunes Search API
/// naar het album (eerst NL, dan US, zoals het thema's fallback). Alle
/// titel-opschoonfuncties zijn regel voor regel overgezet uit <c>spot.htm</c>, zodat
/// dezelfde zoektermen ontstaan.
/// </summary>
public static class ImdbService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // ── Titel-opschoning (port van escapeRegExp in spot.htm) ──────────────────

    /// <summary>
    /// Bouwt de zoekterm, zoals GetCleantitle() in spot.htm: Films en Series delen
    /// toCleanString() (jaartallen verdwijnen samen met de release-markeringen),
    /// Muziek krijgt allmusic(). Het jaartal-strijsel in het JavaScript dat een
    /// "&y=..."-tekst in de titel zoekt is dode code en blijft hier dus ook weg.
    /// </summary>
    public static string GetInfoTitle(string category, string title)
    {
        if (category == "Muziek")
        {
            return MusicTitle(title);
        }
        if (category is "Films" or "Series")
        {
            return CleanTitle(title);
        }
        return title;
    }

    /// <summary>Het eerste viercijferige jaartal in de titel, zoals getyear().</summary>
    public static string ExtractYear(string title)
    {
        Match m = Regex.Match(title, @"\d{4}");
        return m.Success ? m.Value : "";
    }

    /// <summary>
    /// Port van toCleanString(): punten, onderstrepingstekens en streepjes worden
    /// spaties, aanhalingstekens verdwijnen, daarna haalt regexstr() seizoenen,
    /// jaartallen en release-markeringen weg (het jaartal gaat mee in de staart die
    /// stript) en begint elk woord met een hoofdletter.
    /// </summary>
    public static string CleanTitle(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        string s = input
            .Replace(".", " ")
            .Replace("_", " ")
            .Replace("`", "")
            .Replace("'", "")
            .Replace("-", " ");

        s = StripSeasonMarkers(s);
        s = ToProperCase(s);
        s = System.Net.WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    /// <summary>Eerste letter van elk woord een hoofdletter, zoals toProperCase().</summary>
    public static string ToProperCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return Regex.Replace(s.ToLowerInvariant(), @"^(.)|\s(.)",
            m => m.Value.ToUpperInvariant());
    }

    /// <summary>
    /// Port van regexstr(): jaartallen (19xx/20xx), seizoenen, aflaten en
    /// release-tags ("1080p", "NL-SUBS", "DVD", "web"…) knipt hij uit de titel.
    /// De groepen staan in de volgorde van het origineel; het typfoutje
    /// <c>[a-zAZ]</c> is hier wel als <c>[a-zA-Z]</c> overgezet.
    /// </summary>
    private static readonly Regex SeasonMarkersRegex = new(
        @"(\#?\*?\(?\{?\[?(19\d{2})\]?\}?\)?\*?.*)?" +              // 19xx (+ rest)
        @"(\*?\(?\{?\[?(20\d{2})\]?\}?\)?\*?\#?.*)?" +              // 20xx (+ rest)
        @"(\*?\(?\{?\[?\s?[Ss]\d{1,2}\s?[eE]\d{1,2}\s?\]?\}?\)?\*?.*)?" + // S01E02
        @"(deel\s*\d{1,2}?.*)?" +
        @"(\s*\[.*\]\s*)?" +
        @"(\s*\{.*\}\s*)?" +
        @"(seiz.*\s?\d{1,2}?.*)?" +
        @"((\(?\s?\d{3,4}[pmi]).*)?" +                                  // 1080p, 720i
        @"([a-zA-Z]*verzoek[a-zA-Z]*\s*)?" +
        @"(\(basp\)\s*)?" +
        @"(\(bbc\)\s*)?" +
        @"(\(itv\)\s*)?" +
        @"(\d{1,2}x\d{1,2})?" +
        @"(\(?\s?NL\s?\-?SUBS?\s?\)?)?" +
        @"(\(?\d{1,2}\-?\s?\.?\/?\d{1,2}\-?\s?\.?\/?\d{2,4}\)?.*)?" +
        @"(\bDVD\b.*)?" +
        @"(E\d{1,2}\s?.*)?" +
        @"(\#\s?)?" +
        @"(\d{3,4}\s?bluray.*)?" +
        @"(\bweb\b.*)?" +
        @"(\bdtv\b.*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>De regexstr()-port, als losse methode zodat de tests hem direct aanroepen.</summary>
    public static string StripSeasonMarkers(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return SeasonMarkersRegex.Replace(input, "");
    }

    /// <summary>
    /// Port van allmusic(): haalt cd-aantallen, bitrates, jaartallen en
    /// codec-markeringen uit een muziekstitel zodat de artiest overblijft.
    /// </summary>
    public static string AllMusicTitle(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        string s = Regex.Replace(input,
            @"(\(.*\))?((box)?\s?\d*\s?cd'?\s?\d*)?(\[.*\]\s?)?(\bMP3\b)?(\d{4}.*)?('?\*?#?)?(\d*\s*-*bits?)?(\d*\s*-*kbps)?(\d*\s*-*\.*\d*khz)*(@\d*)?(\{.*\}\s?)?(\s?deel\s?\d{1,2})?(?:flac|stereo|deel|week|eac|ogg|wav\s|wave\)|lossless|dts|HD tracks)?",
            "", RegexOptions.IgnoreCase);
        s = s.Replace("- s", "");
        s = s.Trim();
        s = s.Trim('-');
        s = s.Replace("&39;", "'");
        return s;
    }

    // ── URL-builders (port van Setimdb / SetYouTube / het Muziek-blok) ────────

    private static string UrlEncodeSpace(string s, string separator) =>
        Regex.Replace(s.Trim(), @"\s+", separator);

    /// <summary>
    /// De omdb-url zoals Setimdb() die bouwt: <c>?i=&amp;t=…&amp;y=&amp;plot=full&amp;r=json</c>.
    /// Het jaartal-pad (een "(1999)" of " 1999 " dat de opschoning overleeft) levert
    /// een <c>&amp;y=1999</c> vlak achter de naam op, precies zoals het origineel; in de
    /// praktijk stript CleanTitle elk jaartal en blijft &amp;y= leeg.
    /// </summary>
    public static string BuildOmdbUrl(string category, string title)
    {
        string clean = GetInfoTitle(category, title);
        string urlTitle = clean;

        // Setimdb(): "(2012)" (niet gevolgd door "p") of " 2012 " knipt de naam af
        // en geeft &y=2012 mee.
        Match yearInParens = Regex.Match(clean, @"\(\s*\d{4}\s*\)(?!p)|\s\d{4}\s");
        if (yearInParens.Success && yearInParens.Index > 0)
        {
            string partname = clean[..(yearInParens.Index - 1)].Trim();
            urlTitle = partname + "&y=" + ExtractYear(clean);
        }

        // Alleen de naam zelf escapen; de &-scheidingstekens moeten letterlijk blijven.
        string namePart = urlTitle;
        string yearPart = "";
        int yIdx = urlTitle.IndexOf("&y=", StringComparison.Ordinal);
        if (yIdx >= 0)
        {
            namePart = urlTitle[..yIdx];
            yearPart = urlTitle[yIdx..];
        }
        return "http://www.omdbapi.com/?i=&t=" + Uri.EscapeDataString(namePart)
               + yearPart + "&y=&plot=full&r=json";
    }

    /// <summary>De YouTube-zoekurl voor de trailer, zoals SetYouTube().</summary>
    public static string BuildYouTubeTrailerUrl(string category, string title)
    {
        string clean = GetInfoTitle(category, title);
        return "http://www.youtube.com/results?search_query=" +
               UrlEncodeSpace(clean, "%20") + "%20trailer";
    }

    /// <summary>
    /// De muziekzoekterm zoals de Muziek-tak van loadImdb() die bouwt: allmusic(),
    /// daarna alle streepjes eruit (<c>Music.replace(/-/g, "")</c>) en spaties
    /// samenvoegen.
    /// </summary>
    public static string MusicTitle(string title)
    {
        string music = AllMusicTitle(title).Replace("-", "");
        return Regex.Replace(music, @"\s+", " ").Trim();
    }

    /// <summary>De vier muziekwinkel-links uit het Muziek-blok: Allmusic, Amazon, Last.fm en Bol.com.</summary>
    public static IReadOnlyList<SpotInfoRow> BuildMusicLinks(string title)
    {
        string music = MusicTitle(title);
        string music1 = UrlEncodeSpace(music, "%20");
        string music2 = UrlEncodeSpace(music, "+");
        string music3 = UrlEncodeSpace(music, "%2B");

        return new List<SpotInfoRow>
        {
            new("Allmusic.com", "http://www.allmusic.com/search/all/" + music1) { IsLink = true },
            new("Amazon.com", "https://www.amazon.com/s/ref=nb_sb_noss_2?url=search-alias%3Daps&field-keywords=" + music2) { IsLink = true },
            new("Last.fm", "http://www.last.fm/search?q=" + music2) { IsLink = true },
            new("Bol.com", "http://www.bol.com/nl/s/muziek/zoekresultaten/Ntt/" + music3 + "/N/3132/Nty/1/search/true/searchType/qck/suggestedFor/" + music2 + "/originalSearchContext/media_all/originalSection/main/defaultSearchContext/media_all/sc/music_all/index.html") { IsLink = true }
        };
    }

    /// <summary>iTunes Search API naar het album, eerst in de NL-store zoals het thema.</summary>
    public static string BuildiTunesSearchUrl(string title, string country = "NL") =>
        "https://itunes.apple.com/search?term=" + UrlEncodeSpace(MusicTitle(title), "+") +
        "&country=" + country + "&media=music&entity=album";

    /// <summary>iTunes Lookup API naar de nummers van een album, zoals GetTracks().</summary>
    public static string BuildiTunesTracksUrl(long collectionId) =>
        "https://itunes.apple.com/lookup?id=" + collectionId +
        "&country=NL&entity=song&limit=200";

    // ── De lookups zelf ────────────────────────────────────────────────────────

    /// <summary>
    /// De opvrager van de omdb-gegevens, als inspringpunt voor tests zodat het paneel
    /// ook zonder netwerk te controleren is.
    /// </summary>
    internal static Func<string, string, Task<IReadOnlyList<SpotInfoRow>?>> MovieInfoFetcher { get; set; } =
        (category, title) => FetchMovieInfoAsync(category, title);

    /// <summary>
    /// IMDb-informatie voor Films en Series: één rij per veld uit het omdb-antwoord,
    /// in de volgorde van het thema. Geeft null terug als de film niet gevonden is
    /// (het thema toont dan "FOUT: Film niet gevonden").
    /// </summary>
    public static async Task<IReadOnlyList<SpotInfoRow>?> FetchMovieInfoAsync(string category, string title)
    {
        string url = BuildOmdbUrl(category, title);
        try
        {
            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            var root = json.RootElement;
            if (!root.TryGetProperty("Response", out var responseProp) ||
                !string.Equals(responseProp.GetString(), "True", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var rows = new List<SpotInfoRow>();
            string? imdbId = GetString(root, "imdbID");

            void Add(string label, string prop)
            {
                string? value = GetString(root, prop);
                if (!string.IsNullOrEmpty(value) && value != "N/A")
                {
                    rows.Add(new SpotInfoRow(label, value));
                }
            }

            Add("Title", "Title");
            Add("Year", "Year");
            Add("Released", "Released");
            Add("ImdbRating", "imdbRating");
            Add("ImdbVotes", "imdbVotes");
            Add("Awards", "Awards");
            Add("Genre", "Genre");
            Add("Rated", "Rated");
            Add("Runtime", "Runtime");
            Add("Director", "Director");
            Add("Writer", "Writer");
            Add("Actors", "Actors");
            Add("Plot", "Plot");

            if (!string.IsNullOrEmpty(imdbId))
            {
                rows.Add(new SpotInfoRow("ImdbID", "http://www.imdb.com/title/" + imdbId)
                { IsLink = true });
            }

            return rows;
        }
        catch (Exception)
        {
            // Geen netwerk of een kapot antwoord: het paneel blijft gewoon leeg, zoals
            // het JavaScript dat doet als JSONP faalt.
            return null;
        }
    }

    /// <summary>Het imdbID uit het omdb-antwoord, voor de trailer- en IMDb-links.</summary>
    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Album-informatie voor Muziek uit de iTunes Search API: artiest, album, genre,
    /// release, trackaantal en album-link. Zoekt eerst in de NL-store en valt, als het
    /// thema, terug op de US-store.
    /// </summary>
    public static async Task<MusicAlbumInfo?> FetchMusicInfoAsync(string title)
    {
        foreach (string country in new[] { "NL", "US" })
        {
            var album = await FetchiTunesAlbumAsync(BuildiTunesSearchUrl(title, country));
            if (album != null)
            {
                return album;
            }
        }
        return null;
    }

    private static async Task<MusicAlbumInfo?> FetchiTunesAlbumAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            var root = json.RootElement;
            if (!root.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var album = results[0];
            if (album.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            long collectionId = album.TryGetProperty("collectionId", out var idProp) &&
                                idProp.ValueKind == JsonValueKind.Number
                ? idProp.GetInt64()
                : 0;

            string? trackUrl = null;
            if (collectionId > 0 && album.TryGetProperty("trackCount", out var trackCount) &&
                trackCount.ValueKind == JsonValueKind.Number && trackCount.GetInt32() > 1)
            {
                trackUrl = BuildiTunesTracksUrl(collectionId);
            }

            return new MusicAlbumInfo(
                Artist: GetStringOr(album, "artistName", ""),
                Album: GetStringOr(album, "collectionName", ""),
                Genre: GetStringOr(album, "primaryGenreName", ""),
                Copyright: GetStringOr(album, "copyright", ""),
                Release: GetStringOr(album, "releaseDate", ""),
                TrackCount: album.TryGetProperty("trackCount", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0,
                AlbumUrl: GetStringOr(album, "collectionViewUrl", ""),
                ArtworkUrl: GetStringOr(album, "artworkUrl100", ""),
                TracksUrl: trackUrl,
                Tracks: collectionId > 0 ? await FetchiTunesTracksAsync(collectionId) : new List<SpotInfoRow>());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<SpotInfoRow>> FetchiTunesTracksAsync(long collectionId)
    {
        var tracks = new List<SpotInfoRow>();
        try
        {
            using var response = await Http.GetAsync(BuildiTunesTracksUrl(collectionId));
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            var root = json.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return tracks;
            }

            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("wrapperType", out var wrapper) ||
                    !string.Equals(wrapper.GetString(), "track", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? name = GetString(item, "trackName");
                if (string.IsNullOrEmpty(name)) continue;

                int number = item.TryGetProperty("trackNumber", out var numProp) &&
                             numProp.ValueKind == JsonValueKind.Number
                    ? numProp.GetInt32()
                    : 0;
                string disk = item.TryGetProperty("discNumber", out var discProp) &&
                              discProp.ValueKind == JsonValueKind.Number && discProp.GetInt32() > 1
                    ? discProp.GetInt32().ToString(CultureInfo.InvariantCulture) + "-"
                    : "";
                string prefix = number > 0 ? disk + number.ToString("00", CultureInfo.InvariantCulture) + " " : "";

                tracks.Add(new SpotInfoRow("Track", prefix + name));
            }
        }
        catch (Exception)
        {
            // Zonder tracklijst toont het paneel gewoon alleen de albumvelden.
        }
        return tracks;
    }

    private static string GetStringOr(JsonElement element, string property, string fallback) =>
        GetString(element, property) ?? fallback;
}

/// <summary>Album-informatie uit de iTunes Search API, voor het Muziek-paneel.</summary>
public sealed record MusicAlbumInfo(
    string Artist,
    string Album,
    string Genre,
    string Copyright,
    string Release,
    int TrackCount,
    string? AlbumUrl,
    string? ArtworkUrl,
    string? TracksUrl,
    IReadOnlyList<SpotInfoRow> Tracks);
