using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Community;

/// <summary>
/// The newsgroups this community publishes to. These are the authority: they are pushed
/// into <see cref="Spotnet.Properties.Settings"/> on load, because the roughly two dozen
/// call sites that need a group name all read them from there.
/// </summary>
public class CommunityNewsgroups
{
    public string Spots { get; set; } = "free.pt";
    public string Comments { get; set; } = "free.usenet";
    public string Reports { get; set; } = "free.willey";
    public string Nzb { get; set; } = "alt.binaries.ftd";
}

/// <summary>
/// Where the curated trust lists come from. Spots, comments and spam reports themselves
/// travel over Usenet; only this curation is served centrally.
/// </summary>
public class CommunityModeration
{
    public bool Enabled { get; set; } = true;
    public int UpdateIntervalMinutes { get; set; } = 120;

    public string WhitelistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/whitelist.csv";
    public string BlacklistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/blacklist.csv";
    public string SpotWhitelistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/spot_whitelist.csv";
    public string SpotBlacklistUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/lists.new/spot_blacklist.csv";

    /// <summary>Optional XML of moderator keys; empty means the local keys.xml is used as-is.</summary>
    public string ModeratorKeysUrl { get; set; } = "";

    /// <summary>
    /// Wanneer ingeschakeld, worden ook de actieve Spotnet Classic (spotlist.store XML)
    /// lijsten opgehaald voor vertrouwde spotters en blacklist.
    /// </summary>
    public bool UseClassicLists { get; set; } = false;

    public string ClassicWhitelistUrl { get; set; } = "https://spotlist.store/spotnet/whitelist.xml";
    public string ClassicBlacklistUrl { get; set; } = "https://spotlist.store/spotnet/blacklist.xml";

    /// <summary>
    /// RSA public key, in the same XML form as the update key, against which a list's
    /// detached "&lt;list&gt;.sig" is checked. Empty means the lists are taken unsigned,
    /// which is what the current server serves.
    /// </summary>
    public string SignaturePublicKeyXml { get; set; } = "";

    /// <summary>
    /// When true, a list that fails verification is discarded rather than used. Off by
    /// default so that turning signing on is a deliberate act and never silently drops
    /// the lists of a community that does not sign them yet.
    /// </summary>
    public bool RequireSignedLists { get; set; } = false;
}

/// <summary>Community-run web endpoints the client talks to.</summary>
public class CommunityServices
{
    public string ResponseSiteUrl { get; set; } = "https://spotcloud.spotnet.wf/spotnet/response/";
    public string LogUploadUrl { get; set; } = "http://spotcloud.spotnet.wf/upload/";
    public string UpgradeFailuresUrl { get; set; } = "https://spotcloud.spotnet.wf/spotnet/upgrade.failures/";
    public string PromoFolderUrl { get; set; } = "http://spotcloud.spotnet.wf/spotnet/promo/";
}

/// <summary>
/// Diensten van derden waar de client optioneel gebruik van maakt. Alles hier is standaard
/// leeg: een integratie doet pas iets zodra de gebruiker zijn eigen adres of sleutel invult.
/// Zo draagt de repository geen sleutels meer met zich mee en wacht de client niet op een
/// server waar hij geen recht op heeft.
/// </summary>
public class CommunityIntegrations
{
    /// <summary>Newznab-index voor de niet-Usenet spotbron. Leeg = uit.</summary>
    public string NewznabBaseUrl { get; set; } = "";

    public string NewznabApiKey { get; set; } = "";

    /// <summary>OMDb-sleutel voor het filminfoblok in de spotthema's. Leeg = blok verbergen.</summary>
    public string OmdbApiKey { get; set; } = "";

    /// <summary>Newznab wordt alleen bevraagd als zowel de URL als de sleutel ingevuld zijn.</summary>
    public bool IsNewznabConfigured =>
        !string.IsNullOrWhiteSpace(NewznabBaseUrl) && !string.IsNullOrWhiteSpace(NewznabApiKey);

    public bool IsOmdbConfigured => !string.IsNullOrWhiteSpace(OmdbApiKey);
}

/// <summary>
/// Every reference that ties this client to a particular Spotnet community, in one file.
/// The shipped defaults point at the infrastructure the original team runs, so an
/// untouched install behaves exactly as it did when these values were compiled in.
/// </summary>
public class CommunityConfig
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object Lock = new object();
    private static CommunityConfig _current;

    internal const string FileName = "community_config.json";

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Name { get; set; } = "Spotnet NL";
    public CommunityNewsgroups Newsgroups { get; set; } = new CommunityNewsgroups();
    public CommunityModeration Moderation { get; set; } = new CommunityModeration();
    public CommunityServices Services { get; set; } = new CommunityServices();
    public CommunityIntegrations Integrations { get; set; } = new CommunityIntegrations();

    /// <summary>
    /// Leespad voor configuratiebestanden van voor de verhuizing naar <see cref="Integrations"/>.
    /// Wordt nooit weggeschreven; <see cref="Deserialize"/> neemt de waarden over.
    /// </summary>
    [JsonPropertyName("Indexer")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyIndexerSection LegacyIndexer { get; set; }

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(AppHelper.SettingsFolder, FileName);

    /// <summary>
    /// The configuration in force. Read through this everywhere; it is loaded once and
    /// replaced wholesale by <see cref="Replace"/> when the user saves.
    /// </summary>
    public static CommunityConfig Current
    {
        get
        {
            if (_current != null)
            {
                return _current;
            }

            lock (Lock)
            {
                return _current ??= Load();
            }
        }
    }

    /// <summary>Reads the file, falling back to the shipped defaults if it is missing or unreadable.</summary>
    public static CommunityConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                CommunityConfig config = Deserialize(File.ReadAllText(ConfigPath));
                if (config != null)
                {
                    return config;
                }

                Log.Warn("{0} could not be parsed; falling back to the built-in community defaults.", FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to read {0}: {1}", FileName, ex.Message);
        }

        return new CommunityConfig();
    }

    /// <summary>
    /// Parses configuration JSON. Missing sections keep their defaults, so a file that
    /// only overrides one URL stays valid across versions that add new sections.
    /// </summary>
    public static CommunityConfig Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        CommunityConfig config = JsonSerializer.Deserialize<CommunityConfig>(json, ReadOptions);
        if (config == null)
        {
            return null;
        }

        config.Newsgroups ??= new CommunityNewsgroups();
        config.Moderation ??= new CommunityModeration();
        config.Services ??= new CommunityServices();
        config.Integrations ??= new CommunityIntegrations();
        config.MigrateLegacyIndexer();
        return config;
    }

    /// <summary>
    /// Neemt een oude "Indexer"-sectie over in <see cref="Integrations"/>. De dode
    /// standaardwaarden uit de 2.0-reconstructie (een IP zonder TLS en een sleutel die in
    /// de broncode stond) worden daarbij bewust niet overgenomen: die server is onbereikbaar
    /// en de sleutel is publiek geworden.
    /// </summary>
    private void MigrateLegacyIndexer()
    {
        LegacyIndexerSection legacy = LegacyIndexer;
        LegacyIndexer = null;
        if (legacy == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Integrations.NewznabBaseUrl)
            && !IsRetiredIndexerDefault(legacy.NewznabBaseUrl, legacy.NewznabApiKey))
        {
            Integrations.NewznabBaseUrl = legacy.NewznabBaseUrl ?? "";
            Integrations.NewznabApiKey = legacy.NewznabApiKey ?? "";
        }
    }

    private static bool IsRetiredIndexerDefault(string url, string key)
    {
        return string.Equals(url, RetiredIndexerUrl, StringComparison.OrdinalIgnoreCase)
            || IsRetiredIndexerKey(key);
    }

    /// <summary>
    /// De ingetrokken sleutel staat hier als SHA-256 en niet als tekst. Hij moet herkend
    /// worden om hem uit oude configuratiebestanden te kunnen weren, maar hij hoeft daarvoor
    /// niet leesbaar in de repository te staan - hij is publiek geworden en geldt als
    /// gecompromitteerd.
    /// </summary>
    private static bool IsRetiredIndexerKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        byte[] digest = SHA256.HashData(Encoding.ASCII.GetBytes(key.Trim()));
        return string.Equals(Convert.ToHexString(digest), RetiredIndexerKeySha256, StringComparison.OrdinalIgnoreCase);
    }

    private const string RetiredIndexerUrl = "http://51.15.59.166";

    private const string RetiredIndexerKeySha256 =
        "BFD2F124B8DE62DF6D2957E2EE634BF82A6E17D57E40A18EFB578D090E72D9CA";

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, WriteOptions);
    }

    public bool Save()
    {
        lock (Lock)
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(ConfigPath, Serialize());
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, showToClient: true);
                return false;
            }
        }
    }

    /// <summary>Installs <paramref name="config"/> as the configuration in force.</summary>
    public static void Replace(CommunityConfig config)
    {
        if (config == null)
        {
            return;
        }

        lock (Lock)
        {
            _current = config;
        }
    }

    /// <summary>Drops the cached instance so the next read comes off disk again.</summary>
    public static void Invalidate()
    {
        lock (Lock)
        {
            _current = null;
        }
    }

    /// <summary>
    /// Copies the newsgroup names into the application settings, which is where the rest
    /// of the client reads them. Called once at startup and again whenever the user saves.
    /// </summary>
    public void ApplyNewsgroupsToSettings()
    {
        Properties.Settings settings = Properties.Settings.Default;
        bool changed = false;

        changed |= Assign(Newsgroups.Spots, settings.HeaderGroup, v => settings.HeaderGroup = v);
        changed |= Assign(Newsgroups.Comments, settings.ReplyGroup, v => settings.ReplyGroup = v);
        changed |= Assign(Newsgroups.Reports, settings.ReportGroup, v => settings.ReportGroup = v);
        changed |= Assign(Newsgroups.Nzb, settings.NZBGroup, v => settings.NZBGroup = v);

        if (changed)
        {
            settings.Save();
        }
    }

    private static bool Assign(string wanted, string current, Action<string> set)
    {
        if (string.IsNullOrWhiteSpace(wanted) || wanted == current)
        {
            return false;
        }

        set(wanted.Trim());
        return true;
    }

    /// <summary>
    /// Seeds the newsgroup section from whatever the user already had configured, so an
    /// existing install that changed a group by hand keeps it when the file is created.
    /// </summary>
    public void CaptureNewsgroupsFromSettings()
    {
        Properties.Settings settings = Properties.Settings.Default;
        if (!string.IsNullOrWhiteSpace(settings.HeaderGroup)) Newsgroups.Spots = settings.HeaderGroup;
        if (!string.IsNullOrWhiteSpace(settings.ReplyGroup)) Newsgroups.Comments = settings.ReplyGroup;
        if (!string.IsNullOrWhiteSpace(settings.ReportGroup)) Newsgroups.Reports = settings.ReportGroup;
        if (!string.IsNullOrWhiteSpace(settings.NZBGroup)) Newsgroups.Nzb = settings.NZBGroup;
    }

    /// <summary>
    /// Runs at startup: creates the file on first run, seeding it from the settings that
    /// are already in place, then makes its newsgroups the ones the client uses.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                CommunityConfig seeded = new CommunityConfig();
                seeded.CaptureNewsgroupsFromSettings();
                seeded.MigrateLegacyListOverrides();
                seeded.Save();
                Replace(seeded);
            }

            Current.ApplyNewsgroupsToSettings();
        }
        catch (Exception ex)
        {
            Log.Warn("Community configuration could not be initialised: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Carries over the per-user list overrides that used to live in the application
    /// settings. They are only consulted here, when the file is first written.
    /// </summary>
    private void MigrateLegacyListOverrides()
    {
        Properties.Settings settings = Properties.Settings.Default;
        if (!string.IsNullOrWhiteSpace(settings.WhitelistURL)) Moderation.WhitelistUrl = settings.WhitelistURL;
        if (!string.IsNullOrWhiteSpace(settings.BlacklistURL)) Moderation.BlacklistUrl = settings.BlacklistURL;
        if (!string.IsNullOrWhiteSpace(settings.SpotWhitelistURL)) Moderation.SpotWhitelistUrl = settings.SpotWhitelistURL;
        if (!string.IsNullOrWhiteSpace(settings.SpotBlacklistURL)) Moderation.SpotBlacklistUrl = settings.SpotBlacklistURL;
        if (!string.IsNullOrWhiteSpace(settings.KeysURL)) Moderation.ModeratorKeysUrl = settings.KeysURL;

        int interval = Convert.ToInt32(settings.ExternalListsUpdateInterval);
        if (interval > 0)
        {
            Moderation.UpdateIntervalMinutes = interval;
        }

        Moderation.Enabled = settings.DownloadExternalLists;
    }

    /// <summary>Human-readable problems with this configuration; empty means it is usable.</summary>
    public IList<string> Validate()
    {
        List<string> errors = new List<string>();

        RequireGroup(errors, Newsgroups.Spots, "Spots-newsgroup");
        RequireGroup(errors, Newsgroups.Comments, "Reacties-newsgroup");
        RequireGroup(errors, Newsgroups.Reports, "Klachten-newsgroup");
        RequireGroup(errors, Newsgroups.Nzb, "NZB-newsgroup");

        RequireUrl(errors, Moderation.WhitelistUrl, "Whitelist-URL", required: Moderation.Enabled);
        RequireUrl(errors, Moderation.BlacklistUrl, "Blacklist-URL", required: Moderation.Enabled);
        RequireUrl(errors, Moderation.SpotWhitelistUrl, "Spot-whitelist-URL", required: Moderation.Enabled);
        RequireUrl(errors, Moderation.SpotBlacklistUrl, "Spot-blacklist-URL", required: Moderation.Enabled);
        if (Moderation.Enabled && Moderation.UseClassicLists)
        {
            RequireUrl(errors, Moderation.ClassicWhitelistUrl, "Classic-whitelist-URL", required: true);
            RequireUrl(errors, Moderation.ClassicBlacklistUrl, "Classic-blacklist-URL", required: true);
        }
        RequireUrl(errors, Moderation.ModeratorKeysUrl, "Moderatorsleutels-URL", required: false);

        RequireUrl(errors, Services.ResponseSiteUrl, "Feedbacksite-URL", required: false);
        RequireUrl(errors, Services.LogUploadUrl, "Log-upload-URL", required: false);
        RequireUrl(errors, Services.UpgradeFailuresUrl, "Update-foutmelding-URL", required: false);
        RequireUrl(errors, Services.PromoFolderUrl, "Promo-map-URL", required: false);
        RequireUrl(errors, Integrations.NewznabBaseUrl, "Newznab-URL", required: false);

        if (Moderation.UpdateIntervalMinutes < 0 || Moderation.UpdateIntervalMinutes > 10080)
        {
            errors.Add("Het bijwerkinterval moet tussen 0 en 10080 minuten liggen.");
        }

        if (Moderation.RequireSignedLists && string.IsNullOrWhiteSpace(Moderation.SignaturePublicKeyXml))
        {
            errors.Add("Ondertekende lijsten verplicht stellen kan alleen met een ingevulde publieke sleutel.");
        }

        return errors;
    }

    private static void RequireGroup(ICollection<string> errors, string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(label + " mag niet leeg zijn.");
            return;
        }

        // A newsgroup name is dot-separated words; anything with whitespace or a slash is
        // almost certainly a URL pasted into the wrong field.
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || c == '/' || c == '\\' || c == ':')
            {
                errors.Add(label + " is geen geldige newsgroup-naam: " + value);
                return;
            }
        }
    }

    private static void RequireUrl(ICollection<string> errors, string value, string label, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add(label + " mag niet leeg zijn.");
            }

            return;
        }

        string trimmed = value.Trim();
        string candidate = trimmed.Contains("://") ? trimmed : "http://" + trimmed;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(label + " is geen geldige http(s)-URL: " + value);
        }
    }
}

/// <summary>
/// De vorm van de oude "Indexer"-sectie, uitsluitend om bestaande
/// <c>community_config.json</c>-bestanden te kunnen lezen.
/// </summary>
public class LegacyIndexerSection
{
    public string NewznabBaseUrl { get; set; }
    public string NewznabApiKey { get; set; }
}
