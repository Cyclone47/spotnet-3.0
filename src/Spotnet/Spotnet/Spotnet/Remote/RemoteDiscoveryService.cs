using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Remote;

public class RemoteDiscoveryService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Lazy<RemoteDiscoveryService> InstanceHolder = new Lazy<RemoteDiscoveryService>(() => new RemoteDiscoveryService());
    public static RemoteDiscoveryService Instance => InstanceHolder.Value;

    public const int DiscoveryPort = 8771;
    public const string PingPrefix = "SPOTNET_DISCOVER";
    public const string PongPrefix = "SPOTNET_DISCOVERY_PONG:";
    public const string BeaconPrefix = "SPOTNET_BEACON:";

    private UdpClient _udpClient;
    private CancellationTokenSource _cts;
    private Task _listenTask;
    private Task _beaconTask;
    private readonly object _lock = new object();

    public bool IsRunning { get; private set; }

    public void Start(int remotePort, bool requireAuth)
    {
        lock (_lock)
        {
            if (IsRunning) return;

            try
            {
                _cts = new CancellationTokenSource();
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _udpClient.EnableBroadcast = true;

                IsRunning = true;
                _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token, remotePort, requireAuth));
                _beaconTask = Task.Run(() => BeaconLoopAsync(_cts.Token, remotePort, requireAuth));

                Log.Info("RemoteDiscoveryService started on UDP port {0}", DiscoveryPort);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to start RemoteDiscoveryService on UDP {0}: {1}", DiscoveryPort, ex.Message);
                Stop();
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning && _udpClient == null) return;

            try
            {
                _cts?.Cancel();
                _udpClient?.Close();
                _udpClient?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug(ex.Message);
            }
            finally
            {
                _udpClient = null;
                _cts = null;
                _listenTask = null;
                _beaconTask = null;
                IsRunning = false;
                Log.Info("RemoteDiscoveryService stopped.");
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct, int remotePort, bool requireAuth)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync(ct);
                string message = Encoding.UTF8.GetString(result.Buffer);

                if (message.StartsWith(PingPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string payload = BuildDiscoveryPayload(remotePort, requireAuth);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(PongPrefix + payload);
                    await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                    Log.Debug("Discovery ping received from {0}, sent response", result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Log.Debug("RemoteDiscoveryService listen error: {0}", ex.Message);
                    await Task.Delay(500, ct);
                }
            }
        }
    }

    private async Task BeaconLoopAsync(CancellationToken ct, int remotePort, bool requireAuth)
    {
        var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                string payload = BuildDiscoveryPayload(remotePort, requireAuth);
                byte[] beaconBytes = Encoding.UTF8.GetBytes(BeaconPrefix + payload);
                await _udpClient.SendAsync(beaconBytes, beaconBytes.Length, broadcastEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug("RemoteDiscoveryService beacon error: {0}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public static string BuildDiscoveryPayload(int remotePort, bool requireAuth)
    {
        var data = new
        {
            service = "spotnet-remote",
            name = "Spotnet Desktop",
            version = AppHelper.AppVersion?.ToString() ?? "3.0",
            port = remotePort,
            machine = Environment.MachineName,
            requireAuth = requireAuth
        };
        return JsonSerializer.Serialize(data);
    }
}
