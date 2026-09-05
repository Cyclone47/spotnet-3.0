using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Network;

namespace Spotnet.Phuse.NNTP.Net;

internal abstract class SocketBase : IVirtualSocket
{
	private const int BufferSize = 16384;

	protected bool CancelSocket;

	private MemoryStream _dataStream;

	protected VirtualServer DestinationServer;

	private byte[] _rcvBuffer;

	private byte[] _sendBuffer;

	protected TcpClient SocketClient;

	protected Stream SocketStream;

	private readonly SocksProxy _socksProxy = new SocksProxy();

	public event EventHandler<WorkArgs> Received;

	public event EventHandler<WorkArgs> Connected;

	public event EventHandler<WorkArgs> Disconnected;

	public bool Close(int iCode, string sError)
	{
		Clear();
		SafeFire(this.Disconnected, new WorkArgs
		{
			Code = iCode,
			Message = sError
		});
		return true;
	}

	public bool Connect(VirtualServer svr)
	{
		try
		{
			Clear();
			CancelSocket = false;
			SocketStream = null;
			SocketClient = new TcpClient();
			int sendTimeout = ((Settings.Default.ConnectionTimeout > 0) ? Settings.Default.ConnectionTimeout : 5000);
			SocketClient.SendTimeout = sendTimeout;
			int receiveTimeout = ((Settings.Default.DataReceivingTimeout > 0) ? Settings.Default.DataReceivingTimeout : 60000);
			SocketClient.ReceiveTimeout = receiveTimeout;
			DestinationServer = svr;
			if (Settings.Default.UseSocksProxy)
			{
				SocketClient.BeginConnect(SocksProxy.Host, SocksProxy.Port, iConnect_Completed, SocketClient);
			}
			else
			{
				SocketClient.BeginConnect(svr.Host, svr.Port, iConnect_Completed, SocketClient);
			}
			return true;
		}
		catch (Exception ex)
		{
			Close(952, "Socket_Connect: " + ex.Message);
			return false;
		}
	}

	protected abstract void InitSocketStream();

	public bool Receive()
	{
		try
		{
			iReceive_Completed(SocketStream.Read(_rcvBuffer, 0, _rcvBuffer.Length));
			return true;
		}
		catch (Exception ex)
		{
			Close(952, "ReceiveAsync: " + ex.Message);
			return false;
		}
	}

	public bool IsConnected()
	{
		if (SocketClient != null)
		{
			return SocketClient.Connected;
		}
		return false;
	}

	public bool Send(Stream bData, int expectedBytesReturned = -1)
	{
		ClearBuffer();
		return InternalSend(bData);
	}

	public void ClearData()
	{
		ClearStream();
		_rcvBuffer = null;
		_sendBuffer = null;
	}

	private void ClearBuffer()
	{
		_dataStream = null;
		SetBuffer(16384);
	}

	private void Clear()
	{
		CancelSocket = true;
		ClearSocketStream();
		_socksProxy.Close();
		if (SocketClient != null)
		{
			try
			{
				SocketClient.Close();
			}
			catch
			{
			}
			SocketClient = null;
		}
		ClearStream();
	}

	private void ClearSocketStream()
	{
		if (SocketStream != null)
		{
			try
			{
				SocketStream.Close();
				SocketStream.Dispose();
			}
			catch
			{
			}
			SocketStream = null;
		}
	}

	protected void iAuth_Completed()
	{
		try
		{
			if (!CancelSocket)
			{
				ClearBuffer();
				SafeFire(this.Connected, new WorkArgs
				{
					Code = 951,
					Message = "Connected"
				});
			}
		}
		catch (Exception ex)
		{
			Close(955, "Server doesn't support SSL: " + ex.Message);
		}
	}

	private void ClearStream()
	{
		_dataStream = null;
	}

	private void iReceive_Completed(int bytesReceived)
	{
		if (CancelSocket)
		{
			return;
		}
		try
		{
			if (bytesReceived < 1)
			{
				Close(954, "Socket closed.");
				return;
			}
			if (_dataStream == null)
			{
				LazyInitializer.EnsureInitialized(ref _dataStream, () => new MemoryStream(bytesReceived + 1));
			}
			SafeFire(this.Received, new WorkArgs
			{
				Data = _dataStream,
				Bytes = _rcvBuffer,
				Offset = 0,
				BytesReceived = bytesReceived
			});
		}
		catch (Exception ex)
		{
			Close(960, "Rcv: " + ex.Message);
		}
	}

	private bool InternalSend(Stream bData)
	{
		if (bData == null)
		{
			return false;
		}
		try
		{
			// Copy straight through a reused buffer rather than materializing the whole
			// outbound stream into a fresh array on every send.
			if (_sendBuffer == null)
			{
				_sendBuffer = new byte[BufferSize];
			}
			bData.Position = 0L;
			int read;
			while ((read = bData.Read(_sendBuffer, 0, _sendBuffer.Length)) > 0)
			{
				SocketStream.Write(_sendBuffer, 0, read);
			}
			return true;
		}
		catch (Exception ex)
		{
			Close(970, "Send: " + ex.Message);
			return false;
		}
	}

	private void SetBuffer(int receiveBufferSize)
	{
		if (_rcvBuffer == null || _rcvBuffer.Length < receiveBufferSize)
		{
			_rcvBuffer = new byte[receiveBufferSize];
		}
	}

	private void SafeFire(EventHandler<WorkArgs> ev, WorkArgs args)
	{
		ev?.Invoke(this, args);
	}

	protected void iConnect_Completed(IAsyncResult e)
	{
		try
		{
			if (!CancelSocket && e != null && !e.CompletedSynchronously && e.AsyncState == SocketClient)
			{
				SocketClient.EndConnect(e);
				if (Settings.Default.UseSocksProxy)
				{
					_socksProxy.ConnectAsync(SocketClient, DestinationServer.Host, DestinationServer.Port, OnProxyConnectCompleted);
					return;
				}
				InitSocketStream();
				iAuth_Completed();
			}
		}
		catch (Exception ex)
		{
			Close(950, "Connect: " + ex.Message);
		}
	}

	private void OnProxyConnectCompleted(object sender, CreateConnectionAsyncCompletedEventArgs e)
	{
		try
		{
			if (!CancelSocket && e != null)
			{
				if (e.Error != null)
				{
					throw e.Error;
				}
				InitSocketStream();
				iAuth_Completed();
			}
		}
		catch (Exception ex)
		{
			Close(950, "Connect: " + ex.Message);
		}
	}
}
