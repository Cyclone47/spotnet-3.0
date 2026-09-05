using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Extensions;

namespace Spotnet.Network;

/// <summary>Result of a proxied connection attempt.</summary>
public sealed class CreateConnectionAsyncCompletedEventArgs : EventArgs
{
	public CreateConnectionAsyncCompletedEventArgs(Exception error, bool cancelled)
	{
		Error = error;
		Cancelled = cancelled;
	}

	/// <summary>What went wrong, or null when the tunnel is open.</summary>
	public Exception Error { get; }

	public bool Cancelled { get; }
}

/// <summary>
/// Opens a TCP tunnel through a SOCKS5 proxy, on a socket already connected to it.
/// </summary>
/// <remarks>
/// Replaces Starksoft.Aspen, which this application used for exactly one thing: the
/// SOCKS5 CONNECT handshake in front of NNTP. That package is .NET Framework only, 1.1.8
/// is the last release there will be, and it was the final dependency of its kind here.
/// The handshake itself is RFC 1928, with RFC 1929 for username and password, and it is
/// short enough to own.
///
/// The socket arrives already connected to the proxy - <c>SocketBase</c> dials the proxy
/// address and hands the result over - so this speaks the handshake over that stream and
/// leaves the socket in place for the caller to keep using.
///
/// Unlike the package it replaces, this is covered by tests: Socks5ClientTests runs it
/// against a proxy on loopback, for the greeting, authentication, address forms, and the
/// failure replies.
/// </remarks>
internal sealed class Socks5Client
{
	private const byte Version = 0x05;

	private const byte CommandConnect = 0x01;

	private const byte MethodNoAuthentication = 0x00;

	private const byte MethodUsernamePassword = 0x02;

	private const byte MethodNone = 0xFF;

	private const byte AddressIPv4 = 0x01;

	private const byte AddressDomain = 0x03;

	private const byte AddressIPv6 = 0x04;

	private const byte ReplySucceeded = 0x00;

	private const byte AuthenticationVersion = 0x01;

	private readonly string _username;

	private readonly string _password;

	private CancellationTokenSource _cancellation;

	internal Socks5Client(string username, string password)
	{
		_username = username ?? "";
		_password = password ?? "";
	}

	/// <summary>The socket already connected to the proxy.</summary>
	internal TcpClient TcpClient { get; set; }

	internal event EventHandler<CreateConnectionAsyncCompletedEventArgs> CreateConnectionAsyncCompleted;

	/// <summary>
	/// Negotiates a tunnel to <paramref name="destinationHost"/> and reports the outcome
	/// on <see cref="CreateConnectionAsyncCompleted"/>.
	/// </summary>
	internal void CreateConnectionAsync(string destinationHost, int destinationPort)
	{
		CancellationTokenSource cancellation = new CancellationTokenSource();
		_cancellation = cancellation;
		Task.Run(async delegate
		{
			Exception failure = null;
			try
			{
				await ConnectAsync(destinationHost, destinationPort, cancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				this.CreateConnectionAsyncCompleted?.Invoke(this, new CreateConnectionAsyncCompletedEventArgs(null, cancelled: true));
				return;
			}
			catch (Exception ex)
			{
				failure = ex;
			}
			if (cancellation.IsCancellationRequested)
			{
				this.CreateConnectionAsyncCompleted?.Invoke(this, new CreateConnectionAsyncCompletedEventArgs(null, cancelled: true));
				return;
			}
			this.CreateConnectionAsyncCompleted?.Invoke(this, new CreateConnectionAsyncCompletedEventArgs(failure, cancelled: false));
		});
	}

	internal void CancelAsync()
	{
		_cancellation?.Cancel();
	}

	internal async Task ConnectAsync(string destinationHost, int destinationPort, CancellationToken cancellationToken)
	{
		if (destinationHost.IsNullOrWhiteSpace())
		{
			throw new ArgumentException("A destination host is required.", nameof(destinationHost));
		}
		if (destinationPort < 1 || destinationPort > 65535)
		{
			throw new ArgumentOutOfRangeException(nameof(destinationPort));
		}
		TcpClient client = TcpClient;
		if (client == null || !client.Connected)
		{
			throw new InvalidOperationException("The socket is not connected to the proxy.");
		}

		NetworkStream stream = client.GetStream();
		await GreetAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await RequestConnectAsync(stream, destinationHost, destinationPort, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	/// <summary>Offers the methods this client supports and runs the one chosen.</summary>
	private async Task GreetAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		bool hasCredentials = !_username.IsNullOrEmpty();
		byte[] greeting = hasCredentials
			? new byte[] { Version, 2, MethodNoAuthentication, MethodUsernamePassword }
			: new byte[] { Version, 1, MethodNoAuthentication };
		await stream.WriteAsync(greeting, 0, greeting.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

		byte[] choice = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (choice[0] != Version)
		{
			throw new IOException("The proxy answered with SOCKS version " + choice[0] + " instead of 5.");
		}
		switch (choice[1])
		{
		case MethodNoAuthentication:
			return;
		case MethodUsernamePassword:
			if (!hasCredentials)
			{
				throw new IOException("The proxy asked for a username and password, and none are configured.");
			}
			await AuthenticateAsync(stream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return;
		case MethodNone:
			throw new IOException("The proxy rejected every authentication method offered.");
		default:
			throw new IOException("The proxy chose authentication method " + choice[1] + ", which is not supported.");
		}
	}

	/// <summary>Username and password authentication, RFC 1929.</summary>
	private async Task AuthenticateAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		byte[] username = Encoding.UTF8.GetBytes(_username);
		byte[] password = Encoding.UTF8.GetBytes(_password);
		if (username.Length > 255 || password.Length > 255)
		{
			throw new IOException("The proxy username or password is longer than the protocol allows.");
		}

		byte[] request = new byte[3 + username.Length + password.Length];
		int at = 0;
		request[at++] = AuthenticationVersion;
		request[at++] = (byte)username.Length;
		Buffer.BlockCopy(username, 0, request, at, username.Length);
		at += username.Length;
		request[at++] = (byte)password.Length;
		Buffer.BlockCopy(password, 0, request, at, password.Length);

		await stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

		byte[] reply = await ReadAsync(stream, 2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (reply[1] != 0)
		{
			throw new IOException("The proxy rejected the username and password.");
		}
	}

	/// <summary>Asks the proxy to connect onward, and reads its reply in full.</summary>
	private async Task RequestConnectAsync(NetworkStream stream, string destinationHost, int destinationPort, CancellationToken cancellationToken)
	{
		byte[] request = BuildConnectRequest(destinationHost, destinationPort);
		await stream.WriteAsync(request, 0, request.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

		byte[] header = await ReadAsync(stream, 4, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (header[0] != Version)
		{
			throw new IOException("The proxy answered with SOCKS version " + header[0] + " instead of 5.");
		}
		if (header[1] != ReplySucceeded)
		{
			throw new IOException(DescribeReply(header[1]));
		}

		// The bound address has to be consumed even though it is not used, or it would be
		// left in the stream for the protocol that follows.
		int addressLength;
		switch (header[3])
		{
		case AddressIPv4:
			addressLength = 4;
			break;
		case AddressIPv6:
			addressLength = 16;
			break;
		case AddressDomain:
			addressLength = (await ReadAsync(stream, 1, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))[0];
			break;
		default:
			throw new IOException("The proxy replied with address type " + header[3] + ", which is not supported.");
		}
		await ReadAsync(stream, addressLength + 2, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	/// <summary>
	/// Builds a CONNECT request, sending a literal address as an address and anything
	/// else as a domain name for the proxy to resolve.
	/// </summary>
	internal static byte[] BuildConnectRequest(string destinationHost, int destinationPort)
	{
		byte[] address;
		byte addressType;
		if (IPAddress.TryParse(destinationHost, out IPAddress parsed))
		{
			address = parsed.GetAddressBytes();
			addressType = (parsed.AddressFamily == AddressFamily.InterNetworkV6) ? AddressIPv6 : AddressIPv4;
		}
		else
		{
			byte[] name = Encoding.UTF8.GetBytes(destinationHost);
			if (name.Length > 255)
			{
				throw new IOException("The destination host name is longer than the protocol allows.");
			}
			address = new byte[name.Length + 1];
			address[0] = (byte)name.Length;
			Buffer.BlockCopy(name, 0, address, 1, name.Length);
			addressType = AddressDomain;
		}

		byte[] request = new byte[4 + address.Length + 2];
		request[0] = Version;
		request[1] = CommandConnect;
		request[2] = 0x00;
		request[3] = addressType;
		Buffer.BlockCopy(address, 0, request, 4, address.Length);
		request[request.Length - 2] = (byte)(destinationPort >> 8);
		request[request.Length - 1] = (byte)(destinationPort & 0xFF);
		return request;
	}

	internal static string DescribeReply(byte reply)
	{
		switch (reply)
		{
		case 0x01: return "The proxy reported a general failure.";
		case 0x02: return "The proxy is not allowed to make this connection.";
		case 0x03: return "The proxy reported that the network is unreachable.";
		case 0x04: return "The proxy reported that the host is unreachable.";
		case 0x05: return "The destination refused the connection.";
		case 0x06: return "The connection through the proxy timed out.";
		case 0x07: return "The proxy does not support this command.";
		case 0x08: return "The proxy does not support this address type.";
		default: return "The proxy refused the connection with code " + reply + ".";
		}
	}

	/// <summary>Reads exactly <paramref name="count"/> bytes, or fails.</summary>
	/// <remarks>
	/// A stream read may return fewer bytes than asked for, and every field of this
	/// handshake is fixed width, so a short read has to be looped rather than trusted.
	/// </remarks>
	private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[count];
		int read = 0;
		while (read < count)
		{
			int chunk = await stream.ReadAsync(buffer, read, count - read, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (chunk <= 0)
			{
				throw new IOException("The proxy closed the connection during the handshake.");
			}
			read += chunk;
		}
		return buffer;
	}
}
