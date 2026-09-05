using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

/// <summary>
/// Fetches what a spot carries beyond its header: the description the poster wrote and
/// the cover image. Both come from the spot's own article in free.pt — specifically from
/// its X-XML headers, which hold a &lt;Spotnet&gt;&lt;Posting&gt; document naming a
/// second article that holds the image bytes.
/// </summary>
public sealed class SpotBodyService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly UsenetConnection _connection;

    public SpotBodyService(IAppPaths appPaths, ISecretStore secretStore)
    {
        _connection = new UsenetConnection(appPaths, secretStore);
    }

    public sealed record SpotBody(string Description, byte[]? Image, string? ImageUrl, string? Website, string? Poster);

    /// <summary>
    /// Returns the spot's description and cover image, or null when the article is gone
    /// from the server (spots outrun retention) or no server is configured.
    /// </summary>
    public async Task<SpotBody?> FetchAsync(SpotItem spot, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = await _connection.OpenAsync(cancellationToken);
            if (client == null) return null;

            await client.SelectGroupAsync("free.pt", cancellationToken);

            string? article = await client.ReadArticleAsync(spot.MsgId, cancellationToken);
            if (string.IsNullOrWhiteSpace(article))
            {
                Log.Debug("No article for spot {0}", spot.MsgId);
                return null;
            }

            var (headers, body) = SpotArticle.Split(article);
            var posting = SpotArticle.ParsePosting(SpotArticle.ExtractXml(headers));

            // The body is a plain-text copy of the description; fall back to it when the
            // X-XML headers are missing or unparseable.
            string description = SpotArticle.ReinterpretUtf8(
                posting != null && posting.Description.Length > 0 ? posting.Description : body.TrimEnd('\r', '\n'));

            // The cover is split over as many articles as it needs; join them in order.
            byte[]? image = null;
            if (posting != null && posting.ImageSegments.Count > 0)
            {
                var bytes = new List<byte>();
                foreach (string segment in posting.ImageSegments)
                {
                    string? imageRaw = await client.ReadArticleBodyAsync(segment, cancellationToken);
                    if (string.IsNullOrWhiteSpace(imageRaw))
                    {
                        Log.Debug("Image segment {0} is gone", segment);
                        bytes.Clear();
                        break;
                    }
                    bytes.AddRange(SpotArticle.DecodeBinary(imageRaw));
                }
                if (bytes.Count > 0) image = bytes.ToArray();
            }

            return new SpotBody(description, image, posting?.ImageUrl, posting?.Website, posting?.Poster);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to fetch spot body for {0}: {1}", spot.MsgId, ex.Message);
            return null;
        }
    }
}
