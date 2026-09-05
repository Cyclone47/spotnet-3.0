using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NLog;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Phuse.NNTP.Net;

internal class VirtualNNTP
{
	public delegate void ClosedEventHandler(VirtualNNTP sender);

	public delegate void ConnectedEventHandler(int iCode, string sLine, VirtualNNTP sender);

	public delegate void FailedEventHandler(int iCode, string sError, string sLog, VirtualNNTP sender);

	public delegate void ReceivedEventHandler(Stream sData, VirtualNNTP sender);

	public delegate void ResponseEventHandler(int iCode, string sLine, VirtualNNTP sender);

	private static readonly Logger Log;

	private static Dictionary<string, Action<long>> _onNewDataForSpeedReceivedActions;

	private static int _lastId;

	private static int _kbpsLimit;

	private static readonly Stopwatch SpeedCalcWatch;

	private static long _speedCalcBytes;

	public static int NumberOfConnections;

	public readonly int ID;

	private readonly VirtualServer _iServer;

	private readonly Timer _timerToAvoidBlockingCacheByAvg;

	protected readonly VirtualConnection ViConnection;

	private bool _compressionRequested;

	private bool _bridgedModeRequested;

	private bool _bridgeServersRequested;

	private string _currentCommand;

	private bool _isDataCompressed;

	private IVirtualSocket _iSocket;

	private int _masterCacheNumberOfLinesExpected = 2;

	private int _statusCode;

	private string _statusLine = "";

	protected bool IsCacheMaster;

	protected bool IsCacheServer;

	protected DateTime LastDataReceivedTime;

	internal NNTPStatus SocketStatus = NNTPStatus.Closed;

	public bool IsConnected
	{
		get
		{
			if (_iSocket != null)
			{
				return _iSocket.IsConnected();
			}
			return false;
		}
	}

	public event ClosedEventHandler Closed;

	public event ConnectedEventHandler Connected;

	public event ReceivedEventHandler Received;

	public event FailedEventHandler Failed;

	public event ResponseEventHandler Response;

	static VirtualNNTP()
	{
		Log = LogManager.GetCurrentClassLogger();
		SpeedCalcWatch = new Stopwatch();
		SpeedCalcWatch.Start();
	}

	internal VirtualNNTP(VirtualServer svr, VirtualConnection vc)
	{
		ID = ++_lastId;
		_iServer = svr;
		IsCacheServer = CachingSystem.IsCacheServer(_iServer.Host);
		IsCacheMaster = CachingSystem.IsCacheMaster(_iServer.Host);
		if (IsCacheMaster)
		{
			_timerToAvoidBlockingCacheByAvg = new Timer(AvoidBlockingCacheByAvg, null, Configuration.AvgBlockingIssueLongPeriod, Configuration.AvgBlockingIssueLongPeriod);
		}
		ViConnection = vc;
		_onNewDataForSpeedReceivedActions = new Dictionary<string, Action<long>>();
	}

	~VirtualNNTP()
	{
		Disconnect(997, "Cancelled", bSendQuit: true);
	}

	private void iConnected(object sender, WorkArgs e)
	{
		Interlocked.Increment(ref NumberOfConnections);
		Receive();
	}

	private void iDisconnected(object sender, WorkArgs e)
	{
		Interlocked.Decrement(ref NumberOfConnections);
		_iSocket = null;
		SocketStatus = NNTPStatus.Closed;
		try
		{
			this.Failed?.Invoke(e.Code, e.Message, _iServer.LogFormat, this);
		}
		catch
		{
		}
		this.Closed?.Invoke(this);
	}

	private bool Receive()
	{
		if (ViConnection.Cancelled)
		{
			return false;
		}
		try
		{
			_iSocket.Receive();
			return true;
		}
		catch (Exception ex)
		{
			Disconnect(990, "Receive: " + ex.Message, bSendQuit: false);
			return false;
		}
	}

	private void NewDataForSpeedReportSend(long lastRcvBytes)
	{
		foreach (KeyValuePair<string, Action<long>> item in _onNewDataForSpeedReceivedActions.Where((KeyValuePair<string, Action<long>> a) => a.Key.Equals(_currentCommand)))
		{
			try
			{
				item.Value?.Invoke(lastRcvBytes);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}
	}

	internal static bool NewDataForSpeedReportSubscribe(string command, Action<long> action)
	{
		if (action == null)
		{
			return false;
		}
		if (_onNewDataForSpeedReceivedActions.ContainsKey(command))
		{
			return false;
		}
		_onNewDataForSpeedReceivedActions[command] = action;
		return true;
	}

	private void AvoidBlockingCacheByAvg(object state)
	{
		Disconnect(2000, "Avg cache blocking problem detected", bSendQuit: true);
	}

	internal static void NewDataForSpeedReportUnsubscribe(string command)
	{
		if (_onNewDataForSpeedReceivedActions.ContainsKey(command))
		{
			_onNewDataForSpeedReceivedActions.Remove(command);
		}
	}

	private void IReceived(object sender, WorkArgs e)
	{
		try
		{
			int num = 0;
			byte[] array = e.Bytes;
			if (e.BytesReceived > 0)
			{
				LastDataReceivedTime = DateTime.Now;
				if (SocketStatus > NNTPStatus.Authenticating)
				{
					LimitDownloadSpeed(e.BytesReceived);
					NewDataForSpeedReportSend(e.BytesReceived);
				}
				e.Data.Write(array, e.Offset, e.BytesReceived);
			}
			int num2 = e.Offset + e.BytesReceived;
			long length = e.Data.Length;
			if (e.BytesReceived < 5)
			{
				if (length < 5)
				{
					Receive();
					return;
				}
				array = new byte[5];
				Module.GetBytes(e.Data, length - 5, 5L).CopyTo(array, 0);
				e.Data.Seek(length, SeekOrigin.Begin);
				num2 = 5;
			}
			bool flag;
			if (IsCacheMaster)
			{
				string @string = Module.GetString(e.Data, 0L, length);
				int num3 = @string.CountLines();
				flag = _masterCacheNumberOfLinesExpected == num3;
				num = Module.GetCode(Module.GetFirstLine(@string));
				if (flag && (_masterCacheNumberOfLinesExpected > 2 || num == 430))
				{
					SocketStatus = NNTPStatus.Multiline;
				}
				if (_bridgeServersRequested && SocketStatus == NNTPStatus.Authenticating && (num == 201 || num == 220))
				{
					flag = Module.GetString(array, num2 - 5, 5).EndsWith("\r\n.\r\n");
					if (flag)
					{
						_timerToAvoidBlockingCacheByAvg.Change(Configuration.AvgBlockingIssueLongPeriod, Configuration.AvgBlockingIssueLongPeriod);
						CachingSystem.NumberOfBridgeServers = num3 - 3;
					}
					else
					{
						_timerToAvoidBlockingCacheByAvg.Change(Configuration.AvgBlockingIssueWaitingPeriod, Configuration.AvgBlockingIssueLongPeriod);
					}
				}
			}
			else
			{
				if (SocketStatus != NNTPStatus.Multiline)
				{
					string firstLine;
					if (length > e.BytesReceived - e.Offset)
					{
						firstLine = Module.GetFirstLine(e.Data);
						e.Data.Seek(length, SeekOrigin.Begin);
					}
					else
					{
						firstLine = Module.GetFirstLine(array, e.Offset, e.BytesReceived);
					}
					num = Module.GetCode(firstLine);
					switch (num)
					{
					case 215:
					case 218:
					case 220:
					case 221:
					case 222:
					case 223:
					case 224:
					case 230:
					case 282:
					case 288:
					{
						SocketStatus = NNTPStatus.Multiline;
						string xFeatureParams = Module.GetXFeatureParams(firstLine);
						if (xFeatureParams != null && xFeatureParams.Contains("COMPRESS=GZIP"))
						{
							_isDataCompressed = true;
						}
						break;
					}
					}
				}
				string string2 = Module.GetString(array, num2 - 5, 5);
				flag = ((SocketStatus != NNTPStatus.Multiline) ? string2.EndsWith("\r\n") : (_isDataCompressed ? string2.EndsWith(".\r\n") : string2.EndsWith("\r\n.\r\n")));
			}
			if (!flag)
			{
				Receive();
				return;
			}
			e.Data.Seek(0L, SeekOrigin.Begin);
			Process(num, e.Data);
		}
		catch (Exception ex)
		{
			Disconnect(992, "Received: " + ex.Message, bSendQuit: false);
		}
	}

	internal static void SetDownloadSpeedLimit(int kbpsLimit)
	{
		if (kbpsLimit > 0)
		{
			Log.Debug("Set download speed limit to " + kbpsLimit + " KB/s");
		}
		else if (_kbpsLimit > 0)
		{
			Log.Debug("Download speed limit remove");
		}
		_kbpsLimit = kbpsLimit;
	}

	private void LimitDownloadSpeed(long bytesReceived)
	{
		if (_kbpsLimit <= 0)
		{
			return;
		}
		_speedCalcBytes += bytesReceived;
		long elapsedMilliseconds = SpeedCalcWatch.ElapsedMilliseconds;
		if (elapsedMilliseconds <= 500)
		{
			return;
		}
		long num = _kbpsLimit * elapsedMilliseconds / 1000;
		double num2 = (double)_speedCalcBytes / 1024.0 - (double)num;
		if (num2 > 1.0)
		{
			int num3 = (int)(num2 / (double)_kbpsLimit * 1000.0);
			DateTime dateTime = DateTime.Now + TimeSpan.FromMilliseconds(num3);
			int num4 = ((Settings.Default.DataReceivingTimeout > 0) ? Settings.Default.DataReceivingTimeout : 60000);
			num4 -= 2000;
			while (DateTime.Now < dateTime)
			{
				Thread.Sleep((num3 > num4) ? num4 : num3);
				LastDataReceivedTime = DateTime.Now;
				num3 -= num4;
			}
		}
		if (SpeedCalcWatch.ElapsedMilliseconds > elapsedMilliseconds)
		{
			_speedCalcBytes = 0L;
			SpeedCalcWatch.Restart();
		}
	}

	private void Process(int nntpCode, Stream bData)
	{
		string text = $"Binary ({bData.Length} bytes)";
		if (SocketStatus != NNTPStatus.Multiline)
		{
			text = Module.GetReader(bData).ReadLine();
		}
		if (ViConnection.Cancelled || nntpCode == 205 || nntpCode == 512)
		{
			if (nntpCode <= 0)
			{
				nntpCode = 991;
			}
			Disconnect(nntpCode, text, ViConnection.Cancelled);
			return;
		}
		if (IsCacheServer && SocketStatus == NNTPStatus.Connecting && (nntpCode == 200 || nntpCode == 201))
		{
			_bridgedModeRequested = false;
			_bridgeServersRequested = false;
			_statusLine = text;
			SocketStatus = NNTPStatus.Authenticating;
		}
		switch (SocketStatus)
		{
		case NNTPStatus.Connecting:
			_compressionRequested = false;
			if ((uint)(nntpCode - 200) <= 1u)
			{
				_statusLine = text;
				SocketStatus = NNTPStatus.Authenticating;
				SendLines("MODE READER");
				break;
			}
			if (nntpCode <= 0)
			{
				nntpCode = 991;
			}
			Disconnect(nntpCode, text, bSendQuit: true);
			break;
		case NNTPStatus.Authenticating:
			switch (nntpCode)
			{
			case 200:
			case 201:
				if (_bridgedModeRequested)
				{
					CachingSystem.IsBridgedModeOn = true;
					if (_bridgeServersRequested)
					{
						Ready(_statusCode, _statusLine);
					}
					else
					{
						RequestForBridgeServers();
					}
				}
				else if (_iServer.Username.Trim().Length == 0)
				{
					if (Settings.Default.DbUpdateCompressionEnabled && !IsCacheServer)
					{
						RequestForCompression(nntpCode);
					}
					else
					{
						Ready(nntpCode, _statusLine);
					}
				}
				else
				{
					SendLines("AUTHINFO USER " + _iServer.Username);
				}
				return;
			case 250:
			case 281:
				if (text != null && !IsCacheServer)
				{
					Check5EuroUsenetRetention(text);
				}
				if (IsCacheMaster)
				{
					RequestForBridgedMode(nntpCode);
				}
				else if (Settings.Default.DbUpdateCompressionEnabled && !IsCacheServer)
				{
					RequestForCompression(nntpCode);
				}
				else
				{
					Ready(nntpCode, _statusLine);
				}
				return;
			case 381:
				SendLines("AUTHINFO PASS " + _iServer.Password, 0, bReceive: true, Encoding.UTF8);
				return;
			case 290:
			case 400:
				if (_compressionRequested)
				{
					Ready(_statusCode, _statusLine);
					return;
				}
				break;
			case 450:
			case 480:
				SendLines("AUTHINFO USER " + _iServer.Username);
				return;
			case 500:
				if (_compressionRequested)
				{
					Ready(_statusCode, _statusLine);
					return;
				}
				break;
			case 501:
			case 502:
				if (_bridgedModeRequested)
				{
					CachingSystem.IsBridgedModeOn = false;
					Ready(_statusCode, _statusLine);
					return;
				}
				break;
			}
			if (nntpCode <= 0)
			{
				nntpCode = 991;
			}
			Disconnect(nntpCode, text, bSendQuit: true);
			break;
		case NNTPStatus.Singleline:
			this.Response?.Invoke(nntpCode, text, this);
			break;
		case NNTPStatus.Multiline:
			this.Received?.Invoke(Uncompress(bData), this);
			break;
		default:
			Disconnect(993, "Status", bSendQuit: true);
			break;
		}
	}

	private void Check5EuroUsenetRetention(string text)
	{
		int num = text.IndexOf(", retention: ", StringComparison.InvariantCultureIgnoreCase);
		if (num > -1 && int.TryParse(text.Substring(num + ", retention: ".Length), out var result) && result > 0)
		{
			Sys.EuroUsenetRetention = result;
		}
	}

	private Stream Uncompress(Stream data)
	{
		if (!_isDataCompressed)
		{
			return data;
		}
		return Module.UnzipResponse(data);
	}

	private void RequestForCompression(int code)
	{
		_statusCode = code;
		_compressionRequested = true;
		SendLines("XFEATURE COMPRESS GZIP");
	}

	private void RequestForBridgedMode(int code)
	{
		_statusCode = code;
		_bridgedModeRequested = true;
		SendLines("SET BRIDGE MODE ON");
	}

	private void RequestForBridgeServers()
	{
		_bridgeServersRequested = true;
		SendLines("GET BRIDGE SERVERS");
	}

	private void Ready(int code, string status)
	{
		SocketStatus = NNTPStatus.Singleline;
		_isDataCompressed = false;
		this.Connected?.Invoke(code, status, this);
	}

	internal void ClearData()
	{
		_iSocket?.ClearData();
	}

	internal bool SendLines(string sCommand, int expectedBytesReturned = 0, bool bReceive = true, Encoding enc = null)
	{
		try
		{
			LastDataReceivedTime = DateTime.Now;
			_currentCommand = sCommand;
			if (!sCommand.EndsWith(Environment.NewLine))
			{
				sCommand += Environment.NewLine;
			}
			if (_iSocket == null)
			{
				return false;
			}
			if (IsCacheMaster)
			{
				_masterCacheNumberOfLinesExpected = sCommand.CountLines();
			}
			if (!_iSocket.Send(Module.GetStream(sCommand, enc), expectedBytesReturned))
			{
				return false;
			}
			return !bReceive || Receive();
		}
		catch (Exception ex)
		{
			Disconnect(994, "SendLine: " + ex.Message, bSendQuit: false);
			return false;
		}
	}

	public bool Connect()
	{
		try
		{
			_isDataCompressed = false;
			if (ViConnection.Cancelled)
			{
				return false;
			}
			if (SocketStatus == NNTPStatus.Multiline)
			{
				SocketStatus = NNTPStatus.Singleline;
			}
			if (SocketStatus == NNTPStatus.Singleline)
			{
				Ready(0, "");
				return true;
			}
			SocketStatus = NNTPStatus.Connecting;
			if (_iSocket == null)
			{
				if (!_iServer.SSL)
				{
					_iSocket = new Socket();
				}
				else
				{
					_iSocket = new SSLSocket();
				}
				_iSocket.Received += IReceived;
				_iSocket.Connected += iConnected;
				_iSocket.Disconnected += iDisconnected;
			}
			return _iSocket.Connect(_iServer);
		}
		catch (Exception ex)
		{
			Disconnect(996, ex.Message, bSendQuit: false);
			return false;
		}
	}

	internal bool Disconnect(int iCode, string sError, bool bSendQuit)
	{
		if (_iSocket != null && bSendQuit && iCode != 970 && _iSocket.IsConnected() && !SendLines("QUIT", 0, bReceive: false))
		{
			return true;
		}
		if (_iSocket != null && _iSocket.IsConnected())
		{
			_iSocket.Close(iCode, sError);
			return true;
		}
		iDisconnected(null, new WorkArgs
		{
			Code = iCode,
			Message = sError
		});
		return true;
	}
}
