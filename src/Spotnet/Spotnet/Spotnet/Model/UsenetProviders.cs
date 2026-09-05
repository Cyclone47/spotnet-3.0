using System.Collections.Generic;
using System.Linq;
using Spotnet.Properties;

namespace Spotnet.Model;

/// <summary>
/// The Usenet providers offered in the connect dialog.
/// </summary>
/// <remarks>
/// Spotnet 2.0 built this list from "Name#host#port#..." strings parsed at runtime, and it had not
/// been touched since 2014. Every entry below was re-verified on 2026-09-01 by opening the socket
/// and reading the NNTP greeting banner, so a host/port pair only appears here if a server actually
/// answered on it.
///
/// Removed because the servers now refuse service:
///   KPN v1 (nova.planet.nl) and KPN v2 (textnews.kpn.nl) both answer
///   "500 Vanaf 1 mei 2026 zijn we gestopt met Usenet-toegang via deze server".
///
/// Corrected because the shipped port no longer serves NNTP:
///   5 Euro Usenet and SnelNL were listed on port 80. That port accepts the TCP connection but
///   never sends a greeting, so the client hangs until it times out. Both answer on 563.
///
/// Ports left alone are the ones that were verified working as shipped (Eweka, XSnews, NewsXS,
/// Extreme, Sunny, Pure on 443; Tele2 on 563). SSL is not stored here: ServerInfo.DoesProviderUseSsl
/// probes the port for a TLS handshake when the user connects.
/// </remarks>
internal static class UsenetProviders
{
    /// <summary>Grouping headers for the provider dropdown, listed in the order the groups appear.</summary>
    internal const string Netherlands = "NL";

    internal const string International = "INT";

    internal const string Manual = "MANUAL";

    /// <summary>Standard NNTPS port. Used for every entry that was verified on it.</summary>
    private const int Nntps = 563;

    /// <summary>Alternative TLS port several providers publish to get through restrictive firewalls.</summary>
    private const int NntpsAlternative = 443;

    /// <summary>
    /// The list actually offered by the dialog: the published catalogue when one has been fetched
    /// and fully validated, otherwise <see cref="BuiltIn"/>.
    /// </summary>
    internal static IReadOnlyList<ProviderItem> All => ProviderCatalogueSource.Current;

    /// <summary>The catalogue compiled into this build, used whenever no valid published one exists.</summary>
    internal static IReadOnlyList<ProviderItem> BuiltIn { get; } = Build();

    /// <summary>The "Other..." row. Client-owned: a published catalogue never supplies it.</summary>
    internal static ProviderItem ManualEntry() =>
        new ProviderItem { Name = Words.OtherProvider, Group = Manual, IsManual = true, HeadersPort = Nntps };

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
        // The manual entry keeps its servers empty; the dialog opens the advanced panel for it.
        providers.Add(ManualEntry());
        return providers;
    }

    /// <summary>Upload and headers default to the download server, which is how most providers work.</summary>
    private static ProviderItem Dutch(string name, string download, int port, string upload = null, string headers = null) =>
        Create(name, Netherlands, download, port, upload, headers);

    private static ProviderItem Global(string name, string download, int port, string upload = null, string headers = null) =>
        Create(name, International, download, port, upload, headers);

    private static ProviderItem Create(string name, string group, string download, int port, string upload, string headers) =>
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
    internal static ProviderItem Match(IEnumerable<ProviderItem> providers, string headerServer)
    {
        string host = (headerServer ?? string.Empty).Trim();
        if (host.Length == 0) return null;
        // SnelNL hands out per-customer hostnames under its own domain rather than the one listed.
        if (host.EndsWith(".snelnl.com", System.StringComparison.OrdinalIgnoreCase))
            return providers.FirstOrDefault(p => p.Name == "SnelNL");
        return providers.FirstOrDefault(p =>
            !p.IsManual && string.Equals(p.Headers, host, System.StringComparison.OrdinalIgnoreCase));
    }
}
