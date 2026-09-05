using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.Services;

namespace Spotnet.Mac.Network;

/// <summary>
/// Client for the external NZBGet installation's JSON-RPC interface, the Mac
/// counterpart of the RPC layer of Windows' NzbGetDownloader.
/// </summary>
public sealed class NzbGetRpcClient
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly Func<UserPreferences> _prefs;

    public NzbGetRpcClient(Func<UserPreferences> prefs)
    {
        _prefs = prefs;
    }

    /// <summary>
    /// Same shape as Windows' NzbGetDownloader.RpcUrl:
    /// http://ip:port/username:password/jsonrpc
    /// </summary>
    public string BuildRpcUrl()
    {
        var p = _prefs();
        return string.Format(
            "http://{0}:{1}/{2}:{3}/jsonrpc",
            p.NzbGetControlIP, p.NzbGetControlPort,
            p.NzbGetControlUsername, p.NzbGetControlPassword);
    }

    /// <summary>
    /// Sends a JSON-RPC request and returns the result member, or null when the
    /// server answered with an error or nothing. Mirrors Windows' JsonRpcRequest.
    /// </summary>
    public async Task<JsonElement?> RpcAsync(string method, params object[] parameters)
    {
        try
        {
            var payload = new
            {
                jsonrpc = "1.0",
                id = 1,
                method,
                @params = parameters ?? Array.Empty<object>()
            };
            string json = JsonSerializer.Serialize(payload);
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            using var content = new StringContent(json, Encoding.UTF8, "application/json-rpc");
            using var response = await http.PostAsync(BuildRpcUrl(), content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                return result.Clone();
            }
            Log.Warn("NZBGet RPC {0}: response without result member", method);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "NZBGet RPC call {0} failed", method);
            return null;
        }
    }

    /// <summary>
    /// Appends an NZB file to NZBGet's queue. Returns the NZBGet queue id (greater
    /// than 0) on success, 0 on failure. Mirrors Windows' AddDownloadToNzbGet.
    /// </summary>
    public async Task<int> AppendAsync(string nzbPath, string categoryName = "")
    {
        try
        {
            byte[] xml = File.ReadAllBytes(nzbPath);
            string base64 = Convert.ToBase64String(FixXmlEncodingInfo(xml));
            string name = Path.GetFileNameWithoutExtension(nzbPath);
            JsonElement? result = await RpcAsync(
                "append", name, base64, categoryName, 0, false, false, "", 0, "all").ConfigureAwait(false);
            if (result is JsonElement el &&
                el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out int id) &&
                id > 0)
            {
                return id;
            }
            Log.Warn("NZBGet append returned no queue id for {0}", nzbPath);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "NZBGet append failed for {0}", nzbPath);
            return 0;
        }
    }

    /// <summary>
    /// Windows' FixXmlEncodingInfo: an NZB without an XML declaration is rejected by
    /// NZBGet, so Windows checks the first 200 bytes and prepends one when missing.
    /// </summary>
    public static byte[] FixXmlEncodingInfo(byte[] xmlContentBytes)
    {
        int count = Math.Min(200, xmlContentBytes.Length);
        string head = Encoding.ASCII.GetString(xmlContentBytes, 0, count).Trim().ToUpperInvariant();
        if (head.Contains("<?XML"))
        {
            return xmlContentBytes;
        }
        return Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            .Concat(xmlContentBytes).ToArray();
    }
}
