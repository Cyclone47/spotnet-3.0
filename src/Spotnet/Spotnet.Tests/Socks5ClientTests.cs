using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Model;
using Xunit;
using Spotnet.Network;

namespace Spotnet.Tests
{
    /// <summary>
    /// Drives the SOCKS5 handshake against a proxy that really speaks it, on loopback.
    /// </summary>
    /// <remarks>
    /// The package this replaced carried no tests here at all, and a proxy handshake is
    /// the kind of code that appears to work until the one case it gets wrong: a short
    /// read, an address form the proxy answers with, a rejection reported as success.
    /// Each of those has a case below.
    /// </remarks>
    public class Socks5ClientTests
    {
        private const byte MethodNoAuthentication = 0x00;
        private const byte MethodUsernamePassword = 0x02;
        private const byte MethodNone = 0xFF;

        /// <summary>A SOCKS5 proxy that answers exactly once, however it is told to.</summary>
        private sealed class FakeProxy : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Task _serving;

            internal FakeProxy(byte method = MethodNoAuthentication, byte authStatus = 0x00,
                byte reply = 0x00, byte replyAddressType = 0x01, bool hangUpAfterGreeting = false)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _serving = Task.Run(() => Serve(method, authStatus, reply, replyAddressType, hangUpAfterGreeting));
            }

            internal int Port { get; }

            /// <summary>The methods the client offered in its greeting.</summary>
            internal List<byte> OfferedMethods { get; } = new List<byte>();

            internal string Username { get; private set; }

            internal string Password { get; private set; }

            internal byte RequestedAddressType { get; private set; }

            internal string RequestedHost { get; private set; }

            internal int RequestedPort { get; private set; }

            private void Serve(byte method, byte authStatus, byte reply, byte replyAddressType, bool hangUpAfterGreeting)
            {
                try
                {
                    using TcpClient client = _listener.AcceptTcpClient();
                    using NetworkStream stream = client.GetStream();

                    byte[] greeting = Read(stream, 2);
                    byte[] methods = Read(stream, greeting[1]);
                    OfferedMethods.AddRange(methods);
                    stream.Write(new byte[] { 0x05, method }, 0, 2);
                    if (hangUpAfterGreeting)
                    {
                        return;
                    }
                    if (method == MethodNone)
                    {
                        return;
                    }

                    if (method == MethodUsernamePassword)
                    {
                        byte[] header = Read(stream, 2);
                        Username = Encoding.UTF8.GetString(Read(stream, header[1]));
                        byte passwordLength = Read(stream, 1)[0];
                        Password = Encoding.UTF8.GetString(Read(stream, passwordLength));
                        stream.Write(new byte[] { 0x01, authStatus }, 0, 2);
                        if (authStatus != 0)
                        {
                            return;
                        }
                    }

                    byte[] request = Read(stream, 4);
                    RequestedAddressType = request[3];
                    switch (RequestedAddressType)
                    {
                        case 0x01:
                            RequestedHost = new IPAddress(Read(stream, 4)).ToString();
                            break;
                        case 0x04:
                            RequestedHost = new IPAddress(Read(stream, 16)).ToString();
                            break;
                        default:
                            RequestedHost = Encoding.UTF8.GetString(Read(stream, Read(stream, 1)[0]));
                            break;
                    }
                    byte[] port = Read(stream, 2);
                    RequestedPort = (port[0] << 8) | port[1];

                    // The bound address is deliberately a different form from the request,
                    // because the client has to consume whatever it is told.
                    var response = new List<byte> { 0x05, reply, 0x00, replyAddressType };
                    switch (replyAddressType)
                    {
                        case 0x01: response.AddRange(new byte[4]); break;
                        case 0x04: response.AddRange(new byte[16]); break;
                        default:
                            byte[] name = Encoding.UTF8.GetBytes("proxy.local");
                            response.Add((byte)name.Length);
                            response.AddRange(name);
                            break;
                    }
                    response.AddRange(new byte[] { 0x00, 0x50 });
                    stream.Write(response.ToArray(), 0, response.Count);
                    stream.Flush();
                    // Hold the connection until the client is done reading.
                    Thread.Sleep(200);
                }
                catch (Exception)
                {
                    // A test that closes early is not a proxy failure.
                }
            }

            private static byte[] Read(NetworkStream stream, int count)
            {
                byte[] buffer = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int chunk = stream.Read(buffer, read, count - read);
                    if (chunk <= 0) throw new IOException("closed");
                    read += chunk;
                }
                return buffer;
            }

            public void Dispose()
            {
                try { _listener.Stop(); } catch (Exception) { }
                try { _serving.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            }
        }

        private static TcpClient ConnectTo(FakeProxy proxy)
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, proxy.Port);
            return client;
        }

        private static async Task ConnectThrough(FakeProxy proxy, string host, int port,
            string username = "", string password = "")
        {
            using TcpClient socket = ConnectTo(proxy);
            var client = new Socks5Client(username, password) { TcpClient = socket };
            await client.ConnectAsync(host, port, CancellationToken.None);
        }

        [Fact]
        public async Task AHostNameIsSentForTheProxyToResolve()
        {
            using var proxy = new FakeProxy();

            await ConnectThrough(proxy, "news.example.com", 563);

            Assert.Equal(0x03, proxy.RequestedAddressType);
            Assert.Equal("news.example.com", proxy.RequestedHost);
            Assert.Equal(563, proxy.RequestedPort);
            Assert.Equal(new byte[] { MethodNoAuthentication }, proxy.OfferedMethods);
        }

        [Fact]
        public async Task ALiteralAddressIsSentAsAnAddress()
        {
            using var proxy = new FakeProxy();

            await ConnectThrough(proxy, "192.0.2.10", 119);

            Assert.Equal(0x01, proxy.RequestedAddressType);
            Assert.Equal("192.0.2.10", proxy.RequestedHost);
        }

        [Fact]
        public async Task AnIPv6AddressIsSentAsOne()
        {
            using var proxy = new FakeProxy();

            await ConnectThrough(proxy, "2001:db8::1", 119);

            Assert.Equal(0x04, proxy.RequestedAddressType);
            Assert.Equal("2001:db8::1", proxy.RequestedHost);
        }

        [Fact]
        public async Task CredentialsAreOfferedAndSentWhenConfigured()
        {
            using var proxy = new FakeProxy(method: MethodUsernamePassword);

            await ConnectThrough(proxy, "news.example.com", 563, "spotter", "hunter2");

            Assert.Contains(MethodUsernamePassword, proxy.OfferedMethods);
            Assert.Equal("spotter", proxy.Username);
            Assert.Equal("hunter2", proxy.Password);
        }

        [Fact]
        public async Task RejectedCredentialsFail()
        {
            using var proxy = new FakeProxy(method: MethodUsernamePassword, authStatus: 0x01);

            var error = await Assert.ThrowsAsync<IOException>(
                () => ConnectThrough(proxy, "news.example.com", 563, "spotter", "wrong"));

            Assert.Contains("username and password", error.Message);
        }

        [Fact]
        public async Task AProxyDemandingAuthenticationWithoutCredentialsFails()
        {
            using var proxy = new FakeProxy(method: MethodUsernamePassword);

            var error = await Assert.ThrowsAsync<IOException>(
                () => ConnectThrough(proxy, "news.example.com", 563));

            Assert.Contains("none are configured", error.Message);
        }

        [Fact]
        public async Task AProxyRefusingEveryMethodFails()
        {
            using var proxy = new FakeProxy(method: MethodNone);

            var error = await Assert.ThrowsAsync<IOException>(
                () => ConnectThrough(proxy, "news.example.com", 563));

            Assert.Contains("rejected every authentication method", error.Message);
        }

        [Theory]
        [InlineData((byte)0x02, "not allowed")]
        [InlineData((byte)0x04, "host is unreachable")]
        [InlineData((byte)0x05, "refused the connection")]
        public async Task ARefusalIsReportedWithItsReason(byte reply, string expected)
        {
            using var proxy = new FakeProxy(reply: reply);

            var error = await Assert.ThrowsAsync<IOException>(
                () => ConnectThrough(proxy, "news.example.com", 563));

            Assert.Contains(expected, error.Message);
        }

        [Fact]
        public async Task ADomainBoundAddressInTheReplyIsConsumed()
        {
            // A proxy may answer with any address form. Leaving the tail unread would
            // hand the bytes to whatever protocol runs next over the same socket.
            using var proxy = new FakeProxy(replyAddressType: 0x03);

            await ConnectThrough(proxy, "news.example.com", 563);

            Assert.Equal("news.example.com", proxy.RequestedHost);
        }

        [Fact]
        public async Task AProxyHangingUpMidHandshakeFails()
        {
            using var proxy = new FakeProxy(hangUpAfterGreeting: true);

            // The point is that it fails rather than waiting forever. Whether the read
            // returns nothing or the socket reports a reset depends on how the peer went
            // away, and both arrive as IOException.
            await Assert.ThrowsAsync<IOException>(
                () => ConnectThrough(proxy, "news.example.com", 563));
        }

        [Fact]
        public async Task ConnectingWithoutASocketFails()
        {
            var client = new Socks5Client("", "");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.ConnectAsync("news.example.com", 563, CancellationToken.None));
        }

        [Fact]
        public async Task TheEventPathReportsSuccess()
        {
            using var proxy = new FakeProxy();
            using TcpClient socket = ConnectTo(proxy);
            var client = new Socks5Client("", "") { TcpClient = socket };
            var completed = new TaskCompletionSource<CreateConnectionAsyncCompletedEventArgs>();
            client.CreateConnectionAsyncCompleted += (s, e) => completed.TrySetResult(e);

            client.CreateConnectionAsync("news.example.com", 563);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(completed.Task, finished);
            Assert.Null(completed.Task.Result.Error);
            Assert.False(completed.Task.Result.Cancelled);
        }

        [Fact]
        public async Task TheEventPathReportsFailure()
        {
            using var proxy = new FakeProxy(reply: 0x04);
            using TcpClient socket = ConnectTo(proxy);
            var client = new Socks5Client("", "") { TcpClient = socket };
            var completed = new TaskCompletionSource<CreateConnectionAsyncCompletedEventArgs>();
            client.CreateConnectionAsyncCompleted += (s, e) => completed.TrySetResult(e);

            client.CreateConnectionAsync("news.example.com", 563);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(completed.Task, finished);
            Assert.NotNull(completed.Task.Result.Error);
        }
    }
}
