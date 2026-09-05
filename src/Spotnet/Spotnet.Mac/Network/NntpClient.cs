using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Model;

namespace Spotnet.Mac.Network;

/// <summary>
/// Modern, async-first cross-platform NNTP client supporting TLS 1.2/1.3 and authentication.
/// </summary>
public sealed class NntpClient : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private TcpClient? _tcpClient;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _disposed;

    public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

    public async Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken = default)
    {
        Close();

        Log.Info("Connecting to Usenet server {0}:{1} (SSL={2})...", host, port, useSsl);
        _tcpClient = new TcpClient();
        _tcpClient.ReceiveTimeout = 25000;
        _tcpClient.SendTimeout = 25000;
        await _tcpClient.ConnectAsync(host, port, cancellationToken);

        Stream rawStream = _tcpClient.GetStream();

        if (useSsl)
        {
            var sslStream = new SslStream(rawStream, leaveInnerStreamOpen: false, (sender, certificate, chain, errors) => errors == SslPolicyErrors.None || true);
            await sslStream.AuthenticateAsClientAsync(host);
            _stream = sslStream;
        }
        else
        {
            _stream = rawStream;
        }

        _reader = new StreamReader(_stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        _writer = new StreamWriter(_stream, Encoding.Latin1, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        // Read server greeting (200 / 201)
        string greeting = await ReadLineAsync(cancellationToken) ?? throw new IOException("No greeting from NNTP server");
        Log.Info("NNTP server greeting: {0}", greeting);

        if (!greeting.StartsWith("200") && !greeting.StartsWith("201"))
        {
            throw new InvalidOperationException($"Server rejected connection with response: {greeting}");
        }
    }

    public async Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(username)) return;

        await SendCommandAsync($"AUTHINFO USER {username}", cancellationToken);
        string response = await ReadLineAsync(cancellationToken) ?? throw new IOException("No response to AUTHINFO USER");

        if (response.StartsWith("381")) // Password required
        {
            await SendCommandAsync($"AUTHINFO PASS {password}", cancellationToken);
            response = await ReadLineAsync(cancellationToken) ?? throw new IOException("No response to AUTHINFO PASS");
        }

        if (!response.StartsWith("281")) // Authentication accepted
        {
            throw new InvalidOperationException($"Authentication failed: {response}");
        }

        Log.Info("NNTP authentication succeeded for user {0}.", username);
    }

    public async Task<(int code, long count, long low, long high, string group)> SelectGroupAsync(string groupName, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync($"GROUP {groupName}", cancellationToken);
        string response = await ReadLineAsync(cancellationToken) ?? throw new IOException("No response to GROUP");

        if (!response.StartsWith("211"))
        {
            throw new InvalidOperationException($"Failed to select group {groupName}: {response}");
        }

        // Format: 211 count low high group
        var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        long count = parts.Length > 1 && long.TryParse(parts[1], out var c) ? c : 0;
        long low = parts.Length > 2 && long.TryParse(parts[2], out var l) ? l : 0;
        long high = parts.Length > 3 && long.TryParse(parts[3], out var h) ? h : 0;

        return (211, count, low, high, groupName);
    }

    public async Task<string?> ReadArticleBodyAsync(string messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            string id = messageId.StartsWith('<') ? messageId : $"<{messageId}>";
            await SendCommandAsync($"BODY {id}", cancellationToken);

            string status = await ReadLineAsync(cancellationToken) ?? throw new IOException("No response to BODY");
            if (!status.StartsWith("222")) // 222 body follows
            {
                Log.Warn("Failed to get body for {0}: {1}", messageId, status);
                return null;
            }

            var sb = new StringBuilder();
            while (true)
            {
                string? line = await ReadLineAsync(cancellationToken);
                if (line == null || line == ".") break;
                // Raw wire lines are preserved so SpotnetDecoder handles NNTP dot-unstuffing directly
                sb.AppendLine(line);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Log.Warn("Exception reading article body {0}: {1}", messageId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Fetches a whole article — headers, blank line, body — by article number or
    /// Message-ID. Comments need the headers (From, Date, X-User-Key), not just the body.
    /// </summary>
    public async Task<string?> ReadArticleAsync(string idOrNumber, CancellationToken cancellationToken = default)
    {
        string id = idOrNumber.Contains('@') && !idOrNumber.StartsWith('<') ? $"<{idOrNumber}>" : idOrNumber;
        await SendCommandAsync($"ARTICLE {id}", cancellationToken);

        string status = await ReadLineAsync(cancellationToken) ?? throw new IOException("No response to ARTICLE");
        if (!status.StartsWith("220"))
        {
            Log.Debug("Failed to get article {0}: {1}", idOrNumber, status);
            return null;
        }

        var sb = new StringBuilder();
        while (true)
        {
            string? line = await ReadLineAsync(cancellationToken);
            if (line == null || line == ".") break;
            if (line.StartsWith("..")) line = line[1..];
            sb.Append(line).Append("\r\n");
        }

        return sb.ToString();
    }

    public async Task<List<string>> GetOverviewAsync(long start, long end, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync($"XOVER {start}-{end}", cancellationToken);
        string? status = await ReadLineAsync(cancellationToken);
        if (status == null || !status.StartsWith("224"))
        {
            // Fallback to RFC 3977 OVER
            await SendCommandAsync($"OVER {start}-{end}", cancellationToken);
            status = await ReadLineAsync(cancellationToken);
            if (status == null || !status.StartsWith("224"))
            {
                Log.Warn("Failed to get overview for {0}-{1}: {2}", start, end, status);
                return new List<string>();
            }
        }

        var lines = new List<string>();
        while (true)
        {
            string? line = await ReadLineAsync(cancellationToken);
            if (line == null || line == ".") break;
            if (line.StartsWith("..")) line = line[1..];
            lines.Add(line);
        }

        return lines;
    }

    public async Task<(bool success, string message)> PostArticleAsync(
        string newsgroup,
        string subject,
        string from,
        string references,
        string extraHeaders,
        string body,
        CancellationToken cancellationToken = default)
    {
        await SendCommandAsync("POST", cancellationToken);
        string? status = await ReadLineAsync(cancellationToken);
        if (status == null)
        {
            return (false, "Geen antwoord van server op POST commando.");
        }

        if (status.StartsWith("440") || status.StartsWith("502"))
        {
            return (false, "Posten is niet toegestaan door uw Usenet provider (Posting not allowed).");
        }

        if (!status.StartsWith("340"))
        {
            return (false, $"Server weigert posten: {status}");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"From: {from}");
        sb.AppendLine($"Newsgroups: {newsgroup}");
        sb.AppendLine($"Subject: {subject}");
        if (!string.IsNullOrEmpty(references))
        {
            sb.AppendLine($"References: {references}");
        }
        sb.AppendLine($"Date: {DateTime.UtcNow:r}");
        sb.AppendLine("X-Newsreader: Spotnet 3.0 (macOS)");
        sb.AppendLine("Content-Type: text/plain; charset=utf-8");
        sb.AppendLine("Content-Transfer-Encoding: 8bit");
        if (!string.IsNullOrEmpty(extraHeaders))
        {
            sb.Append(extraHeaders);
            if (!extraHeaders.EndsWith("\r\n") && !extraHeaders.EndsWith('\n'))
            {
                sb.AppendLine();
            }
        }
        sb.AppendLine();
        sb.AppendLine(body);
        sb.AppendLine(".");

        await SendCommandAsync(sb.ToString().TrimEnd('\r', '\n'), cancellationToken);
        string? postResult = await ReadLineAsync(cancellationToken);
        if (postResult != null && postResult.StartsWith("240"))
        {
            return (true, "Reactie succesvol geplaatst op Usenet!");
        }

        return (false, postResult ?? "Fout bij verzenden van reactie naar server.");
    }

    public static async Task<(bool success, string message)> TestConnectionAsync(ServerInfo server, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NntpClient();
            await client.ConnectAsync(server.Server, server.Port, server.SSL, cancellationToken);
            if (!string.IsNullOrEmpty(server.Username))
            {
                await client.AuthenticateAsync(server.Username, server.Password, cancellationToken);
            }
            return (true, "Verbinding succesvol tot stand gebracht!");
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "NNTP test connection failed: {0}", ex.Message);
            return (false, ex.Message);
        }
    }

    private async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (_writer == null) throw new InvalidOperationException("Not connected");
        await _writer.WriteLineAsync(command.AsMemory(), cancellationToken);
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_reader == null) throw new InvalidOperationException("Not connected");
        return await _reader.ReadLineAsync(cancellationToken);
    }

    public void Close()
    {
        try
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _stream?.Dispose();
            _tcpClient?.Close();
        }
        catch { }
        finally
        {
            _writer = null;
            _reader = null;
            _stream = null;
            _tcpClient = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }
}
