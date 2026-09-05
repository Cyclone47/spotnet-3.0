using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using NLog;
using Spotnet.Properties;

namespace Spotnet.Phuse.NNTP.Net;

internal class NNTP : VirtualNNTP
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly ManualResetEventSlim _virtualEvent = new ManualResetEventSlim();

	private string _lastGroup;

	private NNTPCommands _cCommand;

	internal NNTP(VirtualServer svr, VirtualConnection vc)
		: base(svr, vc)
	{
		base.Closed += VirtualEvents_Closed;
		base.Failed += VirtualEvents_Failed;
		base.Received += VirtualEvents_Received;
		base.Response += VirtualEvents_Response;
		base.Connected += VirtualEvents_Connected;
	}

	public NNTPCommands ExecuteCommand(NNTPCommands zCommand, CancellationToken cCancel)
	{
		_cCommand = zCommand;
		_cCommand.Reset();
		_cCommand.Status = WorkStatus.Downloading;
		_virtualEvent.Reset();
		if (!Connect())
		{
			Fail(980, "Connect");
			NNTPCommands cCommand = _cCommand;
			_cCommand = null;
			return cCommand;
		}
		int millisecondsTimeout = ((Settings.Default.ConnectionTimeout > 0) ? Settings.Default.ConnectionTimeout : 5000);
		_virtualEvent.Wait(millisecondsTimeout, cCancel);
		if (cCancel.IsCancellationRequested)
		{
			NNTPCommands cCommand2 = _cCommand;
			_cCommand = null;
			return cCommand2;
		}
		if (!_virtualEvent.IsSet)
		{
			if (!base.IsConnected || SocketStatus == NNTPStatus.Closed || SocketStatus == NNTPStatus.Connecting)
			{
				Disconnect(931, "OnConnect. " + Module.TranslateError(SocketError.TimedOut).Message, bSendQuit: false);
			}
			while (true)
			{
				int num = ((Settings.Default.DataReceivingTimeout > 0) ? Settings.Default.DataReceivingTimeout : 60000);
				_virtualEvent.Wait(num / 10, cCancel);
				if (_virtualEvent.IsSet || cCancel.IsCancellationRequested)
				{
					break;
				}
				if ((DateTime.Now - LastDataReceivedTime).TotalMilliseconds > (double)num)
				{
					Disconnect(931, Module.TranslateError(SocketError.TimedOut).Message, bSendQuit: false);
				}
			}
		}
		NNTPCommands cCommand3 = _cCommand;
		_cCommand = null;
		return cCommand3;
	}

	private void VirtualEvents_Connected(int iCode, string sLine, VirtualNNTP sender)
	{
		if (iCode != 0)
		{
			_lastGroup = null;
		}
		if (!SendNext())
		{
			Fail(965, "SendNext");
		}
	}

	private bool SendNext()
	{
		string next = _cCommand.Next;
		if (next.Length == 0)
		{
			return false;
		}
		if (next.ToLower() == _lastGroup && !_cCommand.Finished)
		{
			next = _cCommand.Next;
		}
		if (IsCacheServer && next.StartsWith("GROUP "))
		{
			next = _cCommand.Next;
		}
		return SendLines(next, _cCommand.Expected);
	}

	private void VirtualEvents_Received(Stream sData, VirtualNNTP sender)
	{
		Done(sData);
	}

	private void Done(Stream sData)
	{
		sData.Position = 0L;
		_cCommand.Data = sData;
		_cCommand.Status = WorkStatus.Completed;
		_virtualEvent.Set();
	}

	private void VirtualEvents_Failed(int iCode, string sError, string sLog, VirtualNNTP sender)
	{
		Fail(iCode, sError, sLog);
	}

	private void Fail(int iCode, string sError, string sLog = "")
	{
		if (_cCommand != null && _cCommand.Status != WorkStatus.Failed)
		{
			if (iCode <= 0)
			{
				iCode = 983;
			}
			if (sError == null)
			{
				sError = "";
			}
			if (sError.Length == 0)
			{
				sError = "Unknown";
			}
			_cCommand.Data = null;
			_cCommand.Error.Code = iCode;
			_cCommand.Status = WorkStatus.Failed;
			_cCommand.Error.Log = sLog + Environment.NewLine;
			_cCommand.Error.Message = $"{sError} ({iCode}){Environment.NewLine}";
			_virtualEvent.Set();
		}
	}

	private void VirtualEvents_Closed(VirtualNNTP sender)
	{
		_lastGroup = null;
	}

	private void VirtualEvents_Response(int nntpCode, string sLine, VirtualNNTP sender)
	{
		if (_cCommand == null || !CommandOK(nntpCode))
		{
			switch (nntpCode)
			{
			case 411:
			case 412:
				_lastGroup = null;
				break;
			default:
				_lastGroup = null;
				if (nntpCode <= 0)
				{
					nntpCode = 991;
				}
				Disconnect(nntpCode, sLine, bSendQuit: true);
				return;
			case 420:
			case 421:
			case 422:
			case 423:
			case 430:
			case 440:
			case 441:
				break;
			}
			Fail(nntpCode, sLine);
		}
		else
		{
			if (nntpCode == 211)
			{
				_lastGroup = _cCommand.Current.ToLower();
			}
			if (_cCommand.Finished)
			{
				Done(Module.GetStream(sLine));
			}
			else if (!SendNext())
			{
				Fail(966, "SendNext");
			}
		}
	}

	private bool CommandOK(int nntpCode)
	{
		if (nntpCode <= 240)
		{
			if (nntpCode <= 224)
			{
				switch (nntpCode)
				{
				case 212:
				case 213:
				case 214:
				case 216:
				case 217:
				case 219:
					return false;
				default:
					return false;
				case 111:
				case 211:
				case 215:
				case 218:
				case 220:
				case 221:
				case 222:
				case 223:
				case 224:
					break;
				}
			}
			else if (nntpCode != 230 && nntpCode != 240)
			{
				return false;
			}
		}
		else if (nntpCode <= 288)
		{
			if (nntpCode != 282 && nntpCode != 288)
			{
				return false;
			}
		}
		else if (nntpCode != 335 && nntpCode != 340)
		{
			return false;
		}
		return true;
	}
}
