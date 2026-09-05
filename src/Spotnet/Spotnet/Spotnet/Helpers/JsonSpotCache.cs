using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NLog;
using Spotnet.Model;

namespace Spotnet.Helpers;

/// <summary>Disposable, versioned spot cache. Never reads legacy binary objects.</summary>
public sealed class JsonSpotCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly string directory;
    private readonly long limit;
    private readonly object gate;

    public JsonSpotCache(string profileDirectory, long maxBytes = 50 * 1024 * 1024)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        directory = Path.GetFullPath(Path.Combine(profileDirectory, "Cache", "Json-v1"));
        limit = maxBytes;
        gate = Gates.GetOrAdd(directory, _ => new object());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info => {
            // Spot's persistent data is in public fields. Computed UI properties such
            // as PosterIdent may access the database and must never be evaluated.
            if (info.Type != typeof(SpotEx) && info.Type != typeof(Spot) &&
                info.Type != typeof(UserInfo) && info.Type != typeof(FTDInfo)) return;
            for (int i = info.Properties.Count - 1; i >= 0; i--)
                if (info.Properties[i].AttributeProvider is not FieldInfo)
                    info.Properties.RemoveAt(i);
        });
        return new JsonSerializerOptions { IncludeFields = true, TypeInfoResolver = resolver, MaxDepth = 32 };
    }

    private sealed class Entry
    {
        public Entry() { }
        public int Version { get; set; } = 1;
        public SpotEx Spot { get; set; }
    }

    private string Filename(string messageId) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageId))) + ".json");

    public SpotEx Get(string messageId)
    {
        if (string.IsNullOrEmpty(messageId)) return null;
        lock (gate)
        {
            try
            {
                string path = Filename(messageId);
                if (!File.Exists(path)) return null;
                using var stream = File.OpenRead(path);
                if (stream.Length > Math.Min(limit, 8 * 1024 * 1024)) return null;
                var entry = JsonSerializer.Deserialize<Entry>(stream, Options);
                if (entry?.Version != 1 || entry.Spot?.MessageId != messageId) return null;
                return entry.Spot;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                Log.Debug("Spot cache read skipped: " + ex.Message);
                return null; // A cache miss falls back to normal retrieval.
            }
        }
    }

    public void Save(SpotEx spot)
    {
        if (spot == null || string.IsNullOrEmpty(spot.MessageId)) return;
        lock (gate)
        {
            string temporary = null;
            try
            {
                var copy = spot.ShallowCopy();
                var previous = Get(copy.MessageId);
                if (string.IsNullOrEmpty(copy.Body)) copy.Body = previous?.Body ?? copy.Body;
                if (copy.ImageSource == null || copy.ImageSource.Length == 0)
                    copy.ImageSource = previous?.ImageSource ?? copy.ImageSource;
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Entry { Spot = copy }, Options);
                if (bytes.LongLength > Math.Min(limit, 8 * 1024 * 1024)) return;
                Directory.CreateDirectory(directory);
                string destination = Filename(copy.MessageId);
                temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                // Evict oldest writes, only inside this cache's own versioned folder.
                var files = new DirectoryInfo(directory).GetFiles("*.json")
                    .Where(f => !f.FullName.Equals(destination, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.LastWriteTimeUtc).ToArray();
                long total = files.Sum(f => f.Length) + bytes.LongLength;
                foreach (var file in files)
                {
                    if (total <= limit) break;
                    long length = file.Length;
                    file.Delete();
                    total -= length;
                }
                File.Move(temporary, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                Log.Debug("Spot cache write skipped: " + ex.Message);
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
