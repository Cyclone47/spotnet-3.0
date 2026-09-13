using System;
using System.Security.Cryptography;
using System.Text;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Comment signatures follow the Windows protocol in Spotnet.Model.Comment.Parse:
/// with CheckSignatures on, a reply whose X-User-Signature does not verify against
/// the modulus in X-User-Key is rejected outright, over either the comment's own
/// Message-ID or, for older clients, Message-ID plus body and display name.
/// </summary>
public class CommentSignatureTests
{
    private const string MsgId = "abc123@spot.net";
    private const string Body = "Dit is een testreactie.";
    private const string DisplayFrom = "Piet";

    private static (RSA Rsa, string Modulus) CreateKeyPair()
    {
        var rsa = RSA.Create(1024);
        return (rsa, Convert.ToBase64String(rsa.ExportParameters(false).Modulus!));
    }

    /// <summary>SpotHelper.SpecialString, as the posting side escapes a signature.</summary>
    private static string SpecialString(string value) =>
        value.Replace("/", "-s", StringComparison.Ordinal)
             .Replace("+", "-p", StringComparison.Ordinal)
             .Replace("=", "");

    private static string Sign(RSA rsa, string payload) =>
        SpecialString(Convert.ToBase64String(
            rsa.SignData(Encoding.Latin1.GetBytes(payload), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)));

    private static string Article(string signature, string modulus)
    {
        var sb = new StringBuilder();
        sb.Append($"From: {DisplayFrom} <piet@example.com>\r\n");
        sb.Append("Date: Tue, 09 Sep 2026 12:00:00 +0000\r\n");
        sb.Append($"Message-ID: <{MsgId}>\r\n");
        sb.Append($"X-User-Key: <RSAKeyValue><Modulus>{modulus}</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>\r\n");
        if (signature.Length > 0)
        {
            sb.Append($"X-User-Signature: {signature}\r\n");
        }
        sb.Append("\r\n");
        sb.Append(Body).Append("\r\n");
        return sb.ToString();
    }

    [Fact]
    public void A_validly_signed_comment_is_accepted_while_checking()
    {
        var (rsa, modulus) = CreateKeyPair();
        string article = Article(Sign(rsa, $"<{MsgId}>"), modulus);

        var comment = CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: true);

        Assert.NotNull(comment);
        Assert.True(comment!.ValidSignature);
        Assert.Equal(modulus, comment.Modulus);
    }

    [Fact]
    public void An_unsigned_comment_is_dropped_while_checking_but_kept_without()
    {
        var (_, modulus) = CreateKeyPair();
        string article = Article(signature: "", modulus);

        Assert.Null(CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: true));

        var kept = CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: false);
        Assert.NotNull(kept);
        Assert.False(kept!.ValidSignature);
    }

    [Fact]
    public void A_tampered_signature_is_dropped_while_checking()
    {
        var (rsa, modulus) = CreateKeyPair();
        string signature = Sign(rsa, $"<{MsgId}>");
        // Flip one escaped character the way a broken transfer would.
        char[] chars = signature.ToCharArray();
        chars[10] = chars[10] == 'A' ? 'B' : 'A';
        string article = Article(new string(chars), modulus);

        Assert.Null(CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: true));
    }

    [Fact]
    public void A_signature_over_another_comment_is_rejected()
    {
        var (rsa, modulus) = CreateKeyPair();
        // Signed for a different Message-ID than the article carries.
        string article = Article(Sign(rsa, "<other@spot.net>"), modulus);

        Assert.Null(CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: true));
    }

    [Fact]
    public void The_legacy_payload_of_msgid_body_and_from_is_accepted()
    {
        var (rsa, modulus) = CreateKeyPair();
        string legacyPayload = $"<{MsgId}>" + Body + "\r\n" + DisplayFrom;
        string article = Article(Sign(rsa, legacyPayload), modulus);

        var comment = CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: true);

        Assert.NotNull(comment);
        Assert.True(comment!.ValidSignature);
    }

    [Fact]
    public void The_posted_message_id_carries_the_proof_of_work()
    {
        string id = CommentService.CreateProofOfWorkMsgId();

        Assert.StartsWith("<", id, StringComparison.Ordinal);
        Assert.EndsWith("@spot.net>", id, StringComparison.Ordinal);

        byte[] hash = SHA1.HashData(Encoding.Latin1.GetBytes(id));
        Assert.Equal(0, hash[0]);
        Assert.Equal(0, hash[1]);
    }

    [Fact]
    public void A_comment_signed_the_way_the_client_posts_is_accepted_back()
    {
        // Round-trip: sign exactly as PostCommentAsync does — over the Latin-1 bytes of
        // the comment's own bracketed Message-ID, escaped with SpecialString — and check
        // the parse path accepts it. This is what a signature-checking Windows client
        // does with our replies.
        var (rsa, modulus) = CreateKeyPair();
        string postedId = CommentService.CreateProofOfWorkMsgId();
        string signature = SpecialString(Convert.ToBase64String(
            rsa.SignData(Encoding.Latin1.GetBytes(postedId), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)));

        string article = Article(signature, modulus)
            .Replace($"<{MsgId}>", postedId, StringComparison.Ordinal);

        var comment = CommentService.ParseCommentArticle(article, "spot1@spot.net", checkSignatures: true);

        Assert.NotNull(comment);
        Assert.True(comment!.ValidSignature);
    }
}
