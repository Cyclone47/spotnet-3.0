using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

/// <summary>
/// Handles filing complaints / spam reports about spots (Spam/Malware/Fake).
/// Posts an NNTP REPORT message to the ReportGroup (default free.willey),
/// updates the local trust model (blacklist) if requested, and stores the report
/// in the local SQLite spamreports and spamgroup tables.
/// Matches Windows Spots.CreatReport and ComplainToTheSpot.
/// </summary>
public sealed class ComplaintService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;
    private readonly SpotDatabaseService _dbService;
    private readonly UserPreferencesService _prefsService;
    private readonly TrustService _trustService;
    private readonly IUserKeyService _userKeyService;
    private readonly UsenetConnection _connection;

    public ComplaintService(
        IAppPaths appPaths,
        ISecretStore secretStore,
        SpotDatabaseService dbService,
        UserPreferencesService prefsService,
        TrustService trustService,
        IUserKeyService? userKeyService = null)
    {
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        _prefsService = prefsService ?? throw new ArgumentNullException(nameof(prefsService));
        _trustService = trustService ?? throw new ArgumentNullException(nameof(trustService));
        _userKeyService = userKeyService ?? new UserKeyService(dbService);
        _connection = new UsenetConnection(appPaths, secretStore, prefsService);
    }

    /// <summary>
    /// Validates the complaint description. Returns (valid, errorMessage).
    /// Matches Windows Spots.CreatReport and Words.nl.resx.
    /// </summary>
    public static (bool valid, string? error) ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return (false, "Vul een beschrijving in.");
        }

        string trimmed = description.Trim();
        if (trimmed.Length < 3)
        {
            return (false, "Beschrijving is te kort.");
        }

        if (trimmed.Length > 900)
        {
            return (false, "Beschrijving is te lang.");
        }

        return (true, null);
    }

    /// <summary>
    /// Constructs the REPORT NNTP headers matching Windows Spots.CreatReport.
    /// Useful for testing and inspection.
    /// </summary>
    public static (string subject, string from, string references, string extraHeaders) BuildReportHeaders(
        SpotItem spot,
        string nick,
        RSA rsa)
    {
        string cleanTargetMsgId = spot.MsgId.Trim('<', '>');
        string cleanTitle = spot.Subject.Replace("\r\n", " ").Replace("\n", " ").Trim();
        string subject = $"REPORT <{cleanTargetMsgId}> - {cleanTitle}";
        string references = $"<{cleanTargetMsgId}>";

        // User RSA public key XML and modulus
        var rsaParams = rsa.ExportParameters(false);
        string modulusBase64 = Convert.ToBase64String(rsaParams.Modulus!);
        string modulusEscaped = PosterIdentity.Escape(modulusBase64);
        string pubKeyXml = rsa.ToXmlString(false).Replace("\t", "").Replace("\r\n", "").Replace("\n", "");

        // 1. Signature on target message ID
        byte[] targetBytes = Encoding.UTF8.GetBytes(cleanTargetMsgId);
        byte[] sigBytes = rsa.SignData(targetBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        string userSig = Convert.ToBase64String(sigBytes);

        // 2. Sender field (From): {cleanNick} <{hash10}.{modulusEscaped}.{hashSigEscaped}@spot.net>
        string cleanNick = CleanNickName(nick);
        string hashInput = cleanNick + subject + cleanTargetMsgId;
        byte[] md5 = MD5.HashData(Encoding.Latin1.GetBytes(hashInput));
        string hash10 = BitConverter.ToString(md5).Replace("-", "").ToLowerInvariant()[..10];

        byte[] hashSigBytes = rsa.SignData(Encoding.Latin1.GetBytes(hash10), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        string hashSigEscaped = PosterIdentity.Escape(Convert.ToBase64String(hashSigBytes));

        string from = $"{cleanNick} <{hash10}.{modulusEscaped}.{hashSigEscaped}@spot.net>";
        string extraHeaders = $"X-No-Archive: yes\r\nX-User-Signature: {userSig}\r\nX-User-Key: {pubKeyXml}";

        return (subject, from, references, extraHeaders);
    }

    public static string CleanNickName(string? nick)
    {
        if (string.IsNullOrWhiteSpace(nick)) return "Spotter";
        var sb = new StringBuilder();
        foreach (char c in nick)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        string res = sb.ToString();
        return res.Length >= 3 ? res : "Spotter";
    }

    /// <summary>
    /// Submits a complaint about a spot.
    /// </summary>
    public async Task<(bool success, string message)> SubmitComplaintAsync(
        SpotItem spot,
        string reason,
        bool addToBlacklist,
        string? senderNickname = null,
        CancellationToken cancellationToken = default)
    {
        var (valid, error) = ValidateDescription(reason);
        if (!valid)
        {
            return (false, error!);
        }

        try
        {
            // 1. Check upload server configuration
            if (_connection.LoadServerConfig(ServerRole.Upload) == null)
            {
                return (false, "Geen Usenet server geconfigureerd in Instellingen.");
            }

            // 2. Add to blacklist if requested
            if (addToBlacklist)
            {
                if (!string.IsNullOrWhiteSpace(spot.Modulus))
                {
                    _trustService.AddBlack(spot.SenderName, spot.Modulus);
                }
                if (!string.IsNullOrWhiteSpace(spot.MsgId))
                {
                    _trustService.AddSpotBlack(spot.MsgId);
                }
                await _trustService.SyncToDatabaseAsync(_dbService);
            }

            // 3. Obtain user RSA key and generate REPORT headers
            using var rsa = await _userKeyService.GetOrCreateUserRsaKeyAsync();
            string nick = !string.IsNullOrWhiteSpace(senderNickname)
                ? senderNickname.Trim()
                : (!string.IsNullOrWhiteSpace(_prefsService.Current.Nickname) ? _prefsService.Current.Nickname.Trim() : "Spotter");

            var (subject, from, references, extraHeaders) = BuildReportHeaders(spot, nick, rsa);

            // 4. Open connection to upload server and post
            using var client = await _connection.OpenAsync(ServerRole.Upload, cancellationToken);
            if (client == null)
            {
                return (false, "Geen Usenet server geconfigureerd in Instellingen.");
            }

            string reportGroup = !string.IsNullOrWhiteSpace(_prefsService.Current.ReportGroup)
                ? _prefsService.Current.ReportGroup
                : "free.willey";

            var (postSuccess, postMsg) = await client.PostArticleAsync(
                reportGroup,
                subject,
                from,
                references,
                extraHeaders,
                reason.Trim(),
                cancellationToken
            );

            if (!postSuccess)
            {
                return (false, postMsg);
            }

            // 5. Save report locally to SQLite and increment spot spam count
            string userModulus = await _userKeyService.GetUserModulusBase64Async();
            string cleanTargetMsgId = spot.MsgId.Trim('<', '>');
            var localReport = new SpamReportItem
            {
                RowId = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                MsgId = cleanTargetMsgId,
                Modulus = userModulus,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ReportMsgId = $"{Guid.NewGuid():N}@spot.net",
                Sender = nick
            };

            await _dbService.InsertSpamReportsAsync(new[] { localReport });
            spot.NumberOfSpamReports += 1;

            return (true, "Je melding is verzonden, bedankt voor de moeite.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fout bij verzenden van melding: {0}", ex.Message);
            return (false, $"Fout: {ex.Message}");
        }
    }
}
