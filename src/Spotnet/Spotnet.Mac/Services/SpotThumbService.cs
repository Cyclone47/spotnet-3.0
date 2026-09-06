using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Platform;

namespace Spotnet.Mac.Services;

/// <summary>
/// Miniaturen voor de thumbnailweergave, de Mac-port van Windows'
/// <c>ImageHelper.LoadSpotThumb</c> + <c>FileCacheManager</c>: de miniatuur van een
/// spot hangt in de groep <c>free.at</c> onder een message-id dat afgeleid is van het
/// spot-message-id (md5-scheme van <c>ThumbsUploader.GetThumbMessageId</c>). De bytes
/// worden geschaald naar 143×210 zoals Windows en op schijf gecachet.
/// </summary>
public sealed class SpotThumbService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Doelafmetingen, zoals Windows' ImageResize(…, 143, 210).</summary>
    public const int ThumbWidth = 143;
    public const int ThumbHeight = 210;

    private readonly UsenetConnection _connection;
    private readonly UserPreferencesService _prefsService;
    private readonly string _cacheFolder;
    private readonly ConcurrentDictionary<string, byte?> _failedLookups = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _inFlight = new();
    private readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
    private bool _disposed;

    public SpotThumbService(IAppPaths appPaths, ISecretStore secretStore, UserPreferencesService prefsService)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(prefsService);
        _prefsService = prefsService;
        _cacheFolder = Path.Combine(appPaths.CacheFolder, "Thumbs");
        _connection = new UsenetConnection(appPaths, secretStore);
    }

    /// <summary>
    /// Het message-id van de miniatuur van een spot, exact Windows'
    /// <c>ThumbsUploader.GetThumbMessageId</c> + <c>SpotHelper.MakeMsg</c>:
    /// md5 van "msgid\t" … nee: md5 van het ongemarkeerde message-id + "sup.secure",
    /// de tekens 2..12 (Substring(2, 10)), gevolgd door een punt en het id zelf.
    /// </summary>
    public static string GetThumbMessageId(string spotMsgId)
    {
        string clean = spotMsgId.StartsWith('<')
            ? spotMsgId[1..^1]
            : spotMsgId;
        string hash = MakeMd5(clean + "sup.secure");
        return $"{hash.Substring(2, 10)}.{clean}";
    }

    /// <summary>
    /// md5 als hex-tekenreeks, zoals Windows' MakeMd5: BitConverter.ToString levert
    /// HOOFDLETTERS, en het miniatuur-message-id is daarvan afgeleid — kleine letters
    /// zouden een ander (niet bestaand) artikel opleveren.
    /// </summary>
    private static string MakeMd5(string input)
    {
        return BitConverter.ToString(MD5.HashData(Encoding.Latin1.GetBytes(input))).Replace("-", "");
    }

    /// <summary>
    /// Geeft de miniatuur (143×210), of null als er geen is of het ophalen mislukt.
    /// Resultaten en mislukkingen worden gecachet zodat scrollen de server niet spamt.
    /// </summary>
    public async Task<Bitmap?> GetThumbAsync(SpotItem spot, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(spot?.MsgId) || spot.IsPlaceholder)
        {
            return null;
        }

        string cacheKey = GetThumbMessageId(spot.MsgId);
        if (_memoryCache.TryGetValue(cacheKey, out Bitmap? cached))
        {
            return cached;
        }
        if (_failedLookups.ContainsKey(cacheKey))
        {
            return null;
        }

        var gate = _inFlight.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cached)) return cached;
            if (_failedLookups.ContainsKey(cacheKey)) return null;

            byte[]? bytes = await LoadThumbBytesAsync(cacheKey, cancellationToken);
            if (bytes == null)
            {
                _failedLookups[cacheKey] = null;
                return null;
            }

            bytes = Resize(bytes);
            try
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                _memoryCache[cacheKey] = bitmap;
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Thumb for {0} is not a readable bitmap", spot.MsgId);
                _failedLookups[cacheKey] = null;
                return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Haalt de bytes op: eerst de schijfcache, dan Usenet (BODY uit free.at).</summary>
    private async Task<byte[]?> LoadThumbBytesAsync(string thumbMsgId, CancellationToken cancellationToken)
    {
        string cachePath = Path.Combine(_cacheFolder, SanitizeCacheFileName(thumbMsgId) + ".jpg");
        try
        {
            if (File.Exists(cachePath))
            {
                byte[] disk = await File.ReadAllBytesAsync(cachePath, cancellationToken);
                if (disk.Length > 0)
                {
                    return disk;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not read thumb cache {0}", cachePath);
        }

        try
        {
            using var client = await _connection.OpenAsync(ServerRole.Headers, cancellationToken);
            if (client == null)
            {
                return null;
            }

            await client.SelectGroupAsync(_prefsService.Current.ThumbsGroup, cancellationToken);
            string? body = await client.ReadArticleBodyAsync(thumbMsgId, cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            byte[] bytes = SpotArticle.DecodeBinary(body);
            if (bytes.Length == 0)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(_cacheFolder);
                await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not write thumb cache {0}", cachePath);
            }

            return bytes;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to fetch thumb {0}", thumbMsgId);
            return null;
        }
    }

    /// <summary>Schaalt naar 143×210 zoals Windows' ImageResize; behoud verhouding binnen dat kader.</summary>
    private static byte[] Resize(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var source = new Bitmap(stream);
            double scale = Math.Min((double)ThumbWidth / source.PixelSize.Width,
                                     (double)ThumbHeight / source.PixelSize.Height);
            if (scale <= 0 || (scale >= 1.0 && source.PixelSize.Width <= ThumbWidth && source.PixelSize.Height <= ThumbHeight))
            {
                return bytes;
            }

            int targetWidth = Math.Max(1, (int)Math.Round(source.PixelSize.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(source.PixelSize.Height * scale));
            using var resized = source.CreateScaledBitmap(new Avalonia.PixelSize(targetWidth, targetHeight));
            using var outStream = new MemoryStream();
            resized.Save(outStream);
            return outStream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Thumb resize failed; returning original bytes");
            return bytes;
        }
    }

    private static string SanitizeCacheFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            builder.Append(char.IsWhiteSpace(c) || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }
        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bitmap in _memoryCache.Values)
        {
            bitmap.Dispose();
        }
        _memoryCache.Clear();
        foreach (var gate in _inFlight.Values)
        {
            gate.Dispose();
        }
        _inFlight.Clear();
    }
}
