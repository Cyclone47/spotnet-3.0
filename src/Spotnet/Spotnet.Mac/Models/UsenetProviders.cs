using System.Collections.Generic;
using System.Linq;

namespace Spotnet.Mac.Models;

/// <summary>
/// The Usenet providers offered in the connect and settings dialogs.
/// </summary>
public static class UsenetProviders
{
    public const string Netherlands = "NL";
    public const string International = "INT";
    public const string Manual = "MANUAL";

    private const int Nntps = 563;
    private const int NntpsAlternative = 443;

    /// <summary>
    /// The list actually offered by the dialog: the published catalogue when one has been fetched
    /// and fully validated, otherwise <see cref="BuiltIn"/>.
    /// </summary>
    public static IReadOnlyList<ProviderItem> All => ProviderCatalogueSource.Current;

    /// <summary>The catalogue compiled into this build, used whenever no valid published one exists.</summary>
    public static IReadOnlyList<ProviderItem> BuiltIn { get; } = Build();

    /// <summary>The "Other..." row. Client-owned: a published catalogue never supplies it.</summary>
    public static ProviderItem ManualEntry() =>
        new ProviderItem { Name = "Andere provider...", Group = Manual, IsManual = true, HeadersPort = Nntps };

    private static List<ProviderItem> Build()
    {
        var providers = new List<ProviderItem>
        {
            Dutch("5 Euro Usenet", "reader.5eurousenet.com", Nntps),
            Dutch("Bulknews", "news.bulknews.eu", Nntps),
            Dutch("Eweka", "newsreader1.eweka.nl", NntpsAlternative,
                upload: "upload.eweka.nl", headers: "textnews.eweka.nl"),
            Dutch("Extreme Usenet", "reader.extremeusenet.nl", NntpsAlternative),
            Dutch("Hitnews", "news.hitnews.com", Nntps),
            Dutch("NewsXS", "reader2.newsxs.nl", NntpsAlternative),
            Dutch("Pure Usenet", "news.pureusenet.nl", NntpsAlternative),
            Dutch("SnelNL", "reader.snelnl.com", Nntps),
            Dutch("Sunny Usenet", "news.sunnyusenet.com", NntpsAlternative),
            Dutch("Tele2", "tele2news.tweaknews.nl", Nntps),
            Dutch("Tweaknews", "news.tweaknews.eu", Nntps),
            Dutch("Usenet.Farm", "news.usenet.farm", Nntps),
            Dutch("XLned", "news.xlned.com", Nntps),
            Dutch("XSnews", "reader.xsnews.nl", NntpsAlternative, upload: "upload.xsnews.nl"),

            Global("Astraweb", "ssl-eu.astraweb.com", Nntps),
            Global("Cheapnews", "news.cheapnews.eu", Nntps),
            Global("Easynews", "news.easynews.com", Nntps),
            Global("Frugal Usenet", "news.frugalusenet.com", Nntps),
            Global("Giganews", "news.giganews.com", Nntps),
            Global("NewsDemon", "news.newsdemon.com", Nntps),
            Global("Newsgroup Direct", "news.newsgroupdirect.com", Nntps),
            Global("Newshosting", "news.newshosting.com", Nntps),
            Global("Supernews", "news.supernews.com", Nntps),
            Global("UsenetServer", "news.usenetserver.com", Nntps)
        };
        providers.Add(ManualEntry());
        return providers;
    }

    private static ProviderItem Dutch(string name, string download, int port, string? upload = null, string? headers = null) =>
        Create(name, Netherlands, download, port, upload, headers);

    private static ProviderItem Global(string name, string download, int port, string? upload = null, string? headers = null) =>
        Create(name, International, download, port, upload, headers);

    private static ProviderItem Create(string name, string group, string download, int port, string? upload, string? headers) =>
        new ProviderItem
        {
            Name = name,
            Group = group,
            Download = download,
            Upload = upload ?? download,
            Headers = headers ?? download,
            DownloadPort = port,
            UploadPort = port,
            HeadersPort = port
        };

    /// <summary>The entry whose header server matches, so an imported servers.xml selects the right row.</summary>
    public static ProviderItem? Match(IEnumerable<ProviderItem> providers, string headerServer)
    {
        string host = (headerServer ?? string.Empty).Trim();
        if (host.Length == 0) return null;
        if (host.EndsWith(".snelnl.com", System.StringComparison.OrdinalIgnoreCase))
            return providers.FirstOrDefault(p => p.Name == "SnelNL");
        return providers.FirstOrDefault(p =>
            !p.IsManual && string.Equals(p.Headers, host, System.StringComparison.OrdinalIgnoreCase));
    }
}
