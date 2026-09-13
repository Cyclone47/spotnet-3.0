using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Model;
using Spotnet.Platform;
using SpotnetEnc;

namespace Spotnet.Mac.Network;

public sealed class CommentService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SpotDatabaseService _dbService;
    private readonly UsenetConnection _connection;
    private readonly Services.IUserKeyService _userKeyService;
    private readonly Services.UserPreferencesService? _prefsService;

    public CommentService(IAppPaths appPaths, ISecretStore secretStore, SpotDatabaseService dbService,
                          Services.IUserKeyService? userKeyService = null,
                          Services.UserPreferencesService? prefsService = null)
    {
        _dbService = dbService;
        _connection = new UsenetConnection(appPaths, secretStore);
        _userKeyService = userKeyService ?? new Services.UserKeyService(dbService);
        _prefsService = prefsService;
    }

    /// <summary>The group Spotnet replies are posted to (Windows' ReplyGroup setting).</summary>
    public const string ReplyGroup = "free.usenet";

    /// <summary>
    /// Fetches a spot's comments from the reply group. The comment index built during
    /// sync says which article numbers carry them; each article is then read in full so
    /// the sender, date and X-User-Key are available — the same fields Windows shows
    /// above a comment ("pzh (RtUpBA) | 3 sep 2026 12:20").
    /// Comments are cached in SQLite so a second visit to the spot is instant.
    /// </summary>
    public async Task<List<CommentItem>> FetchCommentsAsync(SpotItem spot, CancellationToken cancellationToken = default)
    {
        var comments = new List<CommentItem>();

        var articles = await _dbService.FindCommentArticlesAsync(spot.MsgId);
        if (articles.Count == 0) return comments;

        try
        {
            using var client = await _connection.OpenAsync(ServerRole.Headers, cancellationToken);
            if (client == null) return comments;

            await client.SelectGroupAsync(ReplyGroup, cancellationToken);

            bool checkSignatures = _prefsService?.Current.CheckSignatures ?? true;

            foreach (long article in articles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string? raw = await client.ReadArticleAsync(article.ToString(CultureInfo.InvariantCulture), cancellationToken);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var comment = ParseCommentArticle(raw, spot.MsgId.Trim('<', '>'), checkSignatures);
                if (comment != null) comments.Add(comment);
            }

            if (comments.Count > 0)
            {
                await _dbService.InsertCommentsAsync(comments);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to fetch comments for {0}: {1}", spot.MsgId, ex.Message);
        }

        return comments;
    }

    /// <summary>
    /// Parses one reply article into a comment. Mirrors Spotnet.Model.Comment.Parse:
    /// headers up to the first blank line, the display name is the part of From before
    /// "&lt;", and X-User-Key carries the poster's modulus.
    /// </summary>
    internal static CommentItem? ParseCommentArticle(string article, string spotMsgId, bool checkSignatures = false)
    {
        var (headers, rawBody) = SpotArticle.Split(article);

        // The wire is read as Latin-1; comments are posted as UTF-8, so the emoji people
        // put in them only come out right after decoding again.
        string body = SpotArticle.ReinterpretUtf8(rawBody.TrimEnd('\r', '\n'));
        if (string.IsNullOrWhiteSpace(body)) return null;

        string from = "", msgId = "", modulus = "", signature = "";
        long date = 0;

        foreach (var header in headers)
        {
            string line = header.Key + ": " + header.Value;
            if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase))
            {
                from = SpotArticle.ReinterpretUtf8(line[5..].Trim());
                int bracket = from.IndexOf('<', StringComparison.Ordinal);
                if (bracket >= 0) from = from[..bracket].Trim();
            }
            else if (line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTimeOffset.TryParse(line[5..].Trim(), CultureInfo.InvariantCulture,
                                            DateTimeStyles.None, out var parsed))
                {
                    date = parsed.ToUnixTimeSeconds();
                }
            }
            else if (line.StartsWith("Message-ID:", StringComparison.OrdinalIgnoreCase))
            {
                msgId = line[11..].Trim().Trim('<', '>');
            }
            else if (line.StartsWith("X-User-Key:", StringComparison.OrdinalIgnoreCase))
            {
                string key = line[11..].Trim();
                // Older clients send the raw modulus; newer ones an RSA key XML.
                int start = key.IndexOf("<Modulus>", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    key = key[(start + 9)..];
                    int end = key.IndexOf('<', StringComparison.Ordinal);
                    if (end >= 0) key = key[..end];
                    modulus = key;
                }
                else
                {
                    modulus = PosterIdentity.Unescape(key);
                }
            }
            else if (line.StartsWith("X-User-Signature:", StringComparison.OrdinalIgnoreCase))
            {
                signature = line[17..].Trim();
            }
        }

        if (from.Length == 0 || msgId.Length == 0) return null;

        string cleanBody = body.Replace("\r\n..", "\r\n.", StringComparison.Ordinal);

        var comment = new CommentItem
        {
            MsgId = msgId,
            Date = date,
            Sender = from,
            SpotMsgId = spotMsgId,
            Modulus = modulus,
            Signature = signature,
            Body = cleanBody
        };

        // Windows' Comment.Parse rejects a comment whose signature does not verify
        // while CheckSignatures is on; an unsigned comment fails too, because there is
        // then no key to verify against.
        if (checkSignatures)
        {
            comment.ValidSignature = VerifyCommentSignature(modulus, signature, msgId, cleanBody, from);
            if (!comment.ValidSignature) return null;
        }

        return comment;
    }

    /// <summary>
    /// Mirrors SpotHelper.CheckUserSignature over the two payloads Windows tries
    /// (Comment.cs): the comment's own Message-ID, and for older clients the
    /// Message-ID plus body and display name.
    /// </summary>
    private static bool VerifyCommentSignature(string modulus, string signature, string msgId, string body, string from)
    {
        if (string.IsNullOrEmpty(modulus) || string.IsNullOrEmpty(signature)) return false;

        // MakeRsa hands back a cached verifier, so it must not be disposed here.
        RSA? rsa = SpotnetSignatureVerifier.MakeRsa(modulus);
        if (rsa == null) return false;

        byte[] sigBytes;
        try
        {
            sigBytes = Convert.FromBase64String(SpotnetSignatureVerifier.UnescapeBase64(signature));
        }
        catch (FormatException)
        {
            return false;
        }

        string withBrackets = msgId.StartsWith('<') ? msgId : $"<{msgId}>";
        return rsa.VerifyHash(SHA1.HashData(Encoding.Latin1.GetBytes(withBrackets)), sigBytes,
                              HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)
            || rsa.VerifyHash(SHA1.HashData(Encoding.Latin1.GetBytes(withBrackets + body + "\r\n" + from)), sigBytes,
                              HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
    }

    public async Task<(bool success, CommentItem? comment, string message)> PostCommentAsync(
        SpotItem spot,
        string sender,
        string commentText,
        CancellationToken cancellationToken = default)
    {
        // Wording taken from the Windows resources (Words.nl) so both clients say the
        // same thing when a reply is rejected.
        if (string.IsNullOrWhiteSpace(sender))
        {
            return (false, null, "Afzender niet ingevuld.");
        }

        if (sender.Trim().Length > 60)
        {
            return (false, null, "Afzender is te lang.");
        }

        if (string.IsNullOrWhiteSpace(commentText))
        {
            return (false, null, "Vul een reactie in.");
        }

        if (commentText.Trim().Length < 3)
        {
            return (false, null, "Reactie is te kort.");
        }

        if (commentText.Length > 900)
        {
            return (false, null, "Reactie is te lang.");
        }

        try
        {
            // Checked before the key work below, which is expensive enough to be worth
            // skipping when there is nothing to post to.
            if (_connection.LoadServerConfig(ServerRole.Upload) == null)
            {
                return (false, null, "Geen Usenet server geconfigureerd in Instellingen.");
            }

            // 1. Get or generate user RSA key
            using var rsa = await _userKeyService.GetOrCreateUserRsaKeyAsync();
            string pubKeyXml = rsa.ToXmlString(includePrivateParameters: false);

            string spotMsgId = spot.MsgId.Trim('<', '>');

            // Windows signs a reply over the Message-ID the reply itself is posted
            // with (Spots.CreateComment: CreateUserSignature(MakeMsg(hashMessageId))),
            // and that id carries the proof-of-work hash. Signing the spot's id instead,
            // as this used to do, made signature-checking clients drop our replies.
            string commentMsgId = CreateProofOfWorkMsgId();
            byte[] signatureBytes = rsa.SignData(Encoding.Latin1.GetBytes(commentMsgId),
                                                 HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            string signature = SpecialString(Convert.ToBase64String(signatureBytes));

            // 2. Connect and authenticate. Posting goes to the upload server, which
            // several providers run on a separate hostname from the reader.
            using var client = await _connection.OpenAsync(ServerRole.Upload, cancellationToken);
            if (client == null)
            {
                return (false, null, "Geen Usenet server geconfigureerd in Instellingen.");
            }

            // 3. Post to free.usenet
            string subject = $"Re: {spot.Subject}";
            string from = $"{sender.Trim()} <spotnet@spot.net>";
            string references = $"<{spotMsgId}>";
            string extraHeaders = $"Message-ID: {commentMsgId}\r\nX-User-Signature: {signature}\r\nX-User-Key: {pubKeyXml}";

            var (postSuccess, postMsg) = await client.PostArticleAsync(
                ReplyGroup,
                subject,
                from,
                references,
                extraHeaders,
                commentText.Trim(),
                cancellationToken
            );

            if (!postSuccess)
            {
                return (false, null, postMsg);
            }

            // 4. Save to local SQLite comments under the id that went on the wire.
            var newComment = new CommentItem
            {
                MsgId = commentMsgId.Trim('<', '>'),
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Sender = sender.Trim(),
                Rating = 0,
                SpotMsgId = spotMsgId,
                Body = commentText.Trim()
            };

            await _dbService.InsertCommentsAsync(new[] { newComment });
            Log.Info("Saved posted comment locally for spot {0}", spotMsgId);

            return (true, newComment, "Uw reactie is gepost");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fout bij plaatsen van reactie: {0}", ex.Message);
            return (false, null, $"Fout: {ex.Message}");
        }
    }

    /// <summary>Windows' SpotHelper.SpecialString: URL-safe base64 without padding.</summary>
    private static string SpecialString(string value) =>
        value.Replace("/", "-s", StringComparison.Ordinal)
             .Replace("+", "-p", StringComparison.Ordinal)
             .Replace("=", "");

    /// <summary>
    /// Builds a Message-ID whose SHA1 digest over the Latin-1 bytes starts with two
    /// zero bytes — the proof-of-work SpotHelper.CreateHash gives Windows-generated
    /// ids, and which SpotHelper.CheckHash and the signature verifier both require.
    /// Roughly one candidate in 65536 qualifies, so the loop ends almost immediately.
    /// </summary>
    internal static string CreateProofOfWorkMsgId()
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9))
            .Replace('/', 's').Replace('+', 'p').TrimEnd('=');

        for (int nonce = 0; nonce < 1_000_000; nonce++)
        {
            string id = $"<{token}.{nonce}@spot.net>";
            byte[] hash = SHA1.HashData(Encoding.Latin1.GetBytes(id));
            if (hash[0] == 0 && hash[1] == 0) return id;
        }

        return $"<{token}@spot.net>";
    }
}
