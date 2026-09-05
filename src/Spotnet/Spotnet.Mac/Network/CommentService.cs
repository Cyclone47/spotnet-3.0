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

namespace Spotnet.Mac.Network;

public sealed class CommentService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SpotDatabaseService _dbService;
    private readonly UsenetConnection _connection;

    public CommentService(IAppPaths appPaths, ISecretStore secretStore, SpotDatabaseService dbService)
    {
        _dbService = dbService;
        _connection = new UsenetConnection(appPaths, secretStore);
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
            using var client = await _connection.OpenAsync(cancellationToken);
            if (client == null) return comments;

            await client.SelectGroupAsync(ReplyGroup, cancellationToken);

            foreach (long article in articles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string? raw = await client.ReadArticleAsync(article.ToString(CultureInfo.InvariantCulture), cancellationToken);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var comment = ParseCommentArticle(raw, spot.MsgId.Trim('<', '>'));
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
    internal static CommentItem? ParseCommentArticle(string article, string spotMsgId)
    {
        var (headers, rawBody) = SpotArticle.Split(article);

        // The wire is read as Latin-1; comments are posted as UTF-8, so the emoji people
        // put in them only come out right after decoding again.
        string body = SpotArticle.ReinterpretUtf8(rawBody.TrimEnd('\r', '\n'));
        if (string.IsNullOrWhiteSpace(body)) return null;

        string from = "", msgId = "", modulus = "";
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
        }

        if (from.Length == 0 || msgId.Length == 0) return null;

        return new CommentItem
        {
            MsgId = msgId,
            Date = date,
            Sender = from,
            SpotMsgId = spotMsgId,
            Modulus = modulus,
            Body = body.Replace("\r\n..", "\r\n.", StringComparison.Ordinal)
        };
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
            var serverInfo = _connection.LoadServerConfig();
            if (serverInfo == null)
            {
                return (false, null, "Geen Usenet server geconfigureerd in Instellingen.");
            }

            // 1. Get or generate user RSA key
            using var rsa = await GetOrCreateUserRsaKeyAsync();
            string pubKeyXml = rsa.ToXmlString(includePrivateParameters: false);

            string spotMsgId = spot.MsgId.Trim('<', '>');
            byte[] msgIdBytes = Encoding.UTF8.GetBytes(spotMsgId);
            byte[] signatureBytes = rsa.SignData(msgIdBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            string signature = Convert.ToBase64String(signatureBytes);

            // 2. Connect and authenticate
            using var client = new NntpClient();
            await client.ConnectAsync(serverInfo.Server, serverInfo.Port, serverInfo.SSL, cancellationToken);
            if (!string.IsNullOrEmpty(serverInfo.Username))
            {
                await client.AuthenticateAsync(serverInfo.Username, serverInfo.Password, cancellationToken);
            }

            // 3. Post to free.usenet
            string subject = $"Re: {spot.Subject}";
            string from = $"{sender.Trim()} <spotnet@spot.net>";
            string references = $"<{spotMsgId}>";
            string extraHeaders = $"X-User-Signature: {signature}\r\nX-User-Key: {pubKeyXml}";

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

            // 4. Save to local SQLite comments
            string commentMsgId = $"{Guid.NewGuid():N}@spot.net";
            var newComment = new CommentItem
            {
                MsgId = commentMsgId,
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

    private async Task<RSA> GetOrCreateUserRsaKeyAsync()
    {
        string? existingKeyXml = await _dbService.GetUserKeyXmlAsync();
        var rsa = RSA.Create(2048);

        if (!string.IsNullOrEmpty(existingKeyXml))
        {
            try
            {
                rsa.FromXmlString(existingKeyXml);
                return rsa;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Failed to load existing RSA key from database, generating new key.");
            }
        }

        // Generate new key and store in SQLite
        string newKeyXml = rsa.ToXmlString(includePrivateParameters: true);
        await _dbService.SetUserKeyXmlAsync(newKeyXml);
        return rsa;
    }
}
