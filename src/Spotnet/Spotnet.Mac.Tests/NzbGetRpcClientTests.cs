using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Spotnet.Mac.Network;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests for fase 3 item 4: the external NZBGet RPC client. Runs a tiny local HTTP
/// listener that answers like NZBGet's JSON-RPC endpoint, so the request shaping
/// (jsonrpc 1.0 envelope, base64 NZB, append parameters) and result handling are
/// tested against a real socket.
/// </summary>
public sealed class NzbGetRpcClientTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _listenUrl;
    private readonly List<(string method, string[] parameters)> _requests = new();

    /// <summary>The result the fake server answers with; null answers without a result member.</summary>
    public object? FakeResult { get; set; } = 42;

    public NzbGetRpcClientTests()
    {
        // Pick a free port so parallel runs never clash.
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }
        _listenUrl = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_listenUrl);
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                string body = await new StreamReader(ctx.Request.InputStream, Encoding.UTF8).ReadToEndAsync();
                var doc = System.Text.Json.JsonDocument.Parse(body);
                string method = doc.RootElement.GetProperty("method").GetString() ?? "";
                var parameters = new List<string>();
                if (doc.RootElement.TryGetProperty("params", out var arr) &&
                    arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        parameters.Add(el.ToString());
                    }
                }
                _requests.Add((method, parameters.ToArray()));

                string response = FakeResult == null
                    ? "{\"result\":null,\"error\":null,\"id\":1}"
                    : System.Text.Json.JsonSerializer.Serialize(new { result = FakeResult, error = (object?)null, id = 1 });
                byte[] bytes = Encoding.UTF8.GetBytes(response);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch
            {
                // Listener stopped or a malformed probe; exit quietly.
                return;
            }
        }
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }

    private Spotnet.Mac.Services.UserPreferences Prefs =>
        new()
        {
            NzbGetControlIP = "127.0.0.1",
            NzbGetControlPort = new Uri(_listenUrl).Port.ToString(),
            NzbGetControlUsername = "nzbget",
            NzbGetControlPassword = "geheim"
        };

    [Fact]
    public void RpcUrl_matches_the_windows_shape()
    {
        var client = new NzbGetRpcClient(() => Prefs);
        Assert.Equal(
            $"http://127.0.0.1:{new Uri(_listenUrl).Port}/nzbget:geheim/jsonrpc",
            client.BuildRpcUrl());
    }

    [Fact]
    public async Task Append_sends_the_windows_parameter_list_and_returns_the_queue_id()
    {
        string nzbPath = Path.Combine(Path.GetTempPath(), "spotnet-rpc-" + Guid.NewGuid().ToString("N") + ".nzb");
        await File.WriteAllTextAsync(nzbPath, "<nzb><file/></nzb>");
        try
        {
            var client = new NzbGetRpcClient(() => Prefs);
            int id = await client.AppendAsync(nzbPath, "Films");

            Assert.Equal(42, id);

            var (method, parameters) = Assert.Single(_requests);
            Assert.Equal("append", method);
            Assert.Equal(9, parameters.Length);
            Assert.Equal(Path.GetFileNameWithoutExtension(nzbPath), parameters[0]); // filename without extension
            byte[] decoded = Convert.FromBase64String(parameters[1]);
            Assert.StartsWith((char)60 + "?xml", Encoding.UTF8.GetString(decoded), StringComparison.Ordinal);
            Assert.Equal("Films", parameters[2]);                // category
            Assert.Equal("all", parameters[8]);                  // priority suffix, as Windows sends
        }
        finally
        {
            File.Delete(nzbPath);
        }
    }

    [Fact]
    public async Task Append_without_a_result_id_returns_zero()
    {
        FakeResult = null;
        string nzbPath = Path.Combine(Path.GetTempPath(), "spotnet-rpc-" + Guid.NewGuid().ToString("N") + ".nzb");
        await File.WriteAllTextAsync(nzbPath, "<nzb/>");
        try
        {
            var client = new NzbGetRpcClient(() => Prefs);
            int id = await client.AppendAsync(nzbPath);
            Assert.Equal(0, id);
        }
        finally
        {
            File.Delete(nzbPath);
        }
    }

    [Fact]
    public async Task Append_to_a_dead_server_returns_zero_instead_of_throwing()
    {
        // Port 1 is never our listener.
        var deadPrefs = Prefs;
        deadPrefs.NzbGetControlPort = "1";
        var client = new NzbGetRpcClient(() => deadPrefs);
        string nzbPath = Path.Combine(Path.GetTempPath(), "spotnet-rpc-" + Guid.NewGuid().ToString("N") + ".nzb");
        await File.WriteAllTextAsync(nzbPath, "<nzb/>");
        try
        {
            int id = await client.AppendAsync(nzbPath);
            Assert.Equal(0, id);
        }
        finally
        {
            File.Delete(nzbPath);
        }
    }

    [Fact]
    public void FixXmlEncodingInfo_prepends_the_declaration_only_when_missing()
    {
        byte[] withDeclaration = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><nzb/>");
        Assert.Same(withDeclaration, NzbGetRpcClient.FixXmlEncodingInfo(withDeclaration));

        byte[] bare = Encoding.UTF8.GetBytes("<nzb/>");
        byte[] fixedUp = NzbGetRpcClient.FixXmlEncodingInfo(bare);
        string text = Encoding.UTF8.GetString(fixedUp);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text, StringComparison.Ordinal);
        Assert.EndsWith("<nzb/>", text, StringComparison.Ordinal);
    }
}
