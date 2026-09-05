using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NLog;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Model;
using Spotnet.Platform;

namespace Spotnet.Mac.Network;

public sealed class SpotSyncService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;
    private readonly SpotDatabaseService _dbService;
    private readonly UserPreferencesService? _preferences;

    public bool IsSyncing { get; private set; }

    public event Action<int, int, string>? ProgressChanged;

    public SpotSyncService(IAppPaths appPaths, ISecretStore secretStore, SpotDatabaseService dbService, UserPreferencesService? preferences = null)
    {
        _appPaths = appPaths;
        _secretStore = secretStore;
        _dbService = dbService;
        _preferences = preferences;
    }

    public async Task<int> SyncSpotsAsync(CancellationToken cancellationToken = default)
    {
        if (IsSyncing)
        {
            Log.Warn("Sync is already running.");
            return 0;
        }

        IsSyncing = true;
        try
        {
            var serverInfo = LoadServerConfig();
            if (serverInfo == null)
            {
                ProgressChanged?.Invoke(0, 0, "Geen Usenet server geconfigureerd in Instellingen.");
                return 0;
            }

            ProgressChanged?.Invoke(0, 100, $"Verbinden met {serverInfo.Server}...");

            using var client = new NntpClient();
            await client.ConnectAsync(serverInfo.Server, serverInfo.Port, serverInfo.SSL,
                                      _preferences.Current.AllowInvalidServerCertificate,
                                      ProxySettings.FromPreferences(_preferences.Current, _secretStore),
                                      cancellationToken);

            if (!string.IsNullOrEmpty(serverInfo.Username))
            {
                ProgressChanged?.Invoke(5, 100, "Authenticeren...");
                await client.AuthenticateAsync(serverInfo.Username, serverInfo.Password, cancellationToken);
            }

            ProgressChanged?.Invoke(10, 100, "Spotnet nieuwsgroep (free.pt) selecteren...");
            var (_, count, low, high, _) = await client.SelectGroupAsync("free.pt", cancellationToken);

            if (high <= 0 || high < low)
            {
                ProgressChanged?.Invoke(100, 100, "Geen spots gevonden in free.pt.");
                return 0;
            }

            long lastArticle = await _dbService.GetLastSyncedArticleAsync();
            long start;
            long end = high;

            if (lastArticle <= 0)
            {
                int initialDays = _preferences?.Current.InitialFetchDays ?? 90;
                if (initialDays > 0)
                {
                    ProgressChanged?.Invoke(12, 100, $"Startpunt zoeken voor laatste {initialDays} dagen...");
                    DateTime cutoffUtc = DateTime.UtcNow.AddDays(-initialDays);

                    long watermark = ArticleWatermark.FindFirstArticleOnOrAfter(low, high, cutoffUtc, (from, to) =>
                    {
                        try
                        {
                            var lines = client.GetOverviewAsync(from, to, cancellationToken).GetAwaiter().GetResult();
                            if (lines.Count == 0) return null;
                            string joined = string.Join("\n", lines);
                            return ArticleWatermark.FirstStampIn(joined);
                        }
                        catch
                        {
                            return null;
                        }
                    });

                    if (watermark > 0)
                    {
                        start = Math.Clamp(watermark, low, high);
                        Log.Info("Initial fetch watermark found: article {0} for cutoff {1}", start, cutoffUtc);
                    }
                    else
                    {
                        start = Math.Max(low, high - 10000);
                        Log.Info("Watermark undetermined; using fallback start {0}", start);
                    }
                }
                else
                {
                    start = low;
                    Log.Info("Initial fetch configured for all articles; starting at {0}", start);
                }
            }
            else
            {
                // Incremental sync
                start = lastArticle + 1;
            }

            if (start > end)
            {
                ProgressChanged?.Invoke(100, 100, "Database is al up-to-date!");
                return 0;
            }

            long totalRange = end - start + 1;
            long currentStart = start;
            int totalInserted = 0;
            long highestProcessedArticle = lastArticle;

            while (currentStart <= end)
            {
                if (cancellationToken.IsCancellationRequested) break;

                long currentEnd = Math.Min(currentStart + 499, end);
                int progressPercent = (int)Math.Clamp(((currentStart - start) * 100.0) / totalRange, 0, 100);
                ProgressChanged?.Invoke(progressPercent, 100, $"Spots ophalen... ({totalInserted} toegevoegd)");

                var lines = await client.GetOverviewAsync(currentStart, currentEnd, cancellationToken);
                var spotsToAdd = new List<SpotItem>();

                foreach (var line in lines)
                {
                    var spot = SpotnetHeaderParser.ParseOverviewLine(line, out long articleNum);
                    if (spot != null)
                    {
                        spotsToAdd.Add(spot);
                        if (articleNum > highestProcessedArticle)
                        {
                            highestProcessedArticle = articleNum;
                        }
                    }
                }

                if (spotsToAdd.Count > 0)
                {
                    int inserted = await _dbService.InsertSpotsAsync(spotsToAdd);
                    totalInserted += inserted;
                }

                if (highestProcessedArticle > 0)
                {
                    await _dbService.SetLastSyncedArticleAsync(highestProcessedArticle);
                }

                currentStart = currentEnd + 1;
            }

            await IndexCommentsAsync(client, cancellationToken);

            ProgressChanged?.Invoke(100, 100, $"Klaar! {totalInserted} nieuwe spots binnengehaald.");
            return totalInserted;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fout tijdens spots synchronisatie: {0}", ex.Message);
            ProgressChanged?.Invoke(0, 100, $"Fout bij synchronisatie: {ex.Message}");
            return 0;
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private ServerInfo? LoadServerConfig()
    {
        string serversXmlPath = Path.Combine(_appPaths.DataFolder, "servers.xml");
        if (!File.Exists(serversXmlPath)) return null;

        try
        {
            var doc = XDocument.Load(serversXmlPath);
            var serverEl = doc.Root?.Element("Server");
            if (serverEl == null) return null;

            string host = serverEl.Attribute("Server")?.Value ?? "";
            int port = int.TryParse(serverEl.Attribute("Port")?.Value, out var p) ? p : 563;
            bool ssl = (serverEl.Attribute("SSL")?.Value ?? "1") == "1";
            string user = serverEl.Attribute("Username")?.Value ?? "";
            string pass = _secretStore.GetSecret($"Spotnet_{host}_{user}") ?? "";

            return new ServerInfo
            {
                Server = host,
                Port = port,
                SSL = ssl,
                Username = user,
                Password = pass
            };
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to parse servers.xml");
            return null;
        }
    }

    /// <summary>
    /// Indexes the reply group so a spot's comments can be found later. Only article
    /// numbers and Message-IDs are stored — the bodies are fetched on demand when a spot
    /// is opened. Windows keeps the same index in a separate comments database.
    /// </summary>
    private async Task IndexCommentsAsync(NntpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var (_, _, low, high, _) = await client.SelectGroupAsync(CommentService.ReplyGroup, cancellationToken);
            if (high <= 0 || high < low) return;

            long last = await _dbService.GetLastIndexedCommentAsync();
            // First run indexes the same depth as the spot sync, so the comments on the
            // spots that were just fetched are covered.
            long start = last <= 0 ? Math.Max(low, high - 2500) : last + 1;
            if (start > high) return;

            long highest = last;
            for (long from = start; from <= high; from += 500)
            {
                if (cancellationToken.IsCancellationRequested) break;

                long to = Math.Min(from + 499, high);
                ProgressChanged?.Invoke(100, 100, "Reacties indexeren...");

                var lines = await client.GetOverviewAsync(from, to, cancellationToken);
                var entries = new List<(long article, string msgId)>();
                foreach (string line in lines)
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 5) continue;
                    if (!long.TryParse(parts[0], out long article)) continue;

                    entries.Add((article, parts[4].Trim().Trim('<', '>')));
                    if (article > highest) highest = article;
                }

                if (entries.Count > 0)
                {
                    await _dbService.IndexCommentArticlesAsync(entries);
                }
            }

            if (highest > last)
            {
                await _dbService.SetLastIndexedCommentAsync(highest);
            }
        }
        catch (Exception ex)
        {
            // A missing reply group must not fail the spot sync.
            Log.Warn(ex, "Could not index the reply group: {0}", ex.Message);
        }
    }
}
