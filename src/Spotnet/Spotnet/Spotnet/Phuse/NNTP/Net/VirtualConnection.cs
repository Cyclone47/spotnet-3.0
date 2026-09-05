using System;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Downloader;

namespace Spotnet.Phuse.NNTP.Net;

internal class VirtualConnection : IndexedObject
{
	private readonly VirtualServer _srv;

	private readonly CancellationTokenSource _vCancel;

	private readonly ManualResetEventSlim _vIdle;

	private readonly Scheduler _zSched;

	private ConnectionTask _zConnection;

	private NNTP _zNntp;

	private int _zStatus = 1;

	public bool IsTestConnection { get; private set; }

	internal bool IsCacheSlave { get; }

	internal VirtualServer Server => _srv;

	internal Scheduler Scheduler => _zSched;

	internal CancellationTokenSource Token => _vCancel;

	internal ManualResetEventSlim Idle => _vIdle;

	internal bool Cancelled => _vCancel.IsCancellationRequested;

	internal ConnectionStatus Status
	{
		get
		{
			if (Cancelled)
			{
				_zStatus = 1;
			}
			return (ConnectionStatus)_zStatus;
		}
		set
		{
			if (value != 0 || !Cancelled)
			{
				_zStatus = (int)value;
			}
		}
	}

	public bool Enabled
	{
		get
		{
			return Status == ConnectionStatus.Enabled;
		}
		set
		{
			if (value)
			{
				Status = ConnectionStatus.Enabled;
			}
			else
			{
				Status = ConnectionStatus.Disabled;
			}
		}
	}

	public int ID { get; set; }

	public int Index { get; set; }

	internal NNTPStatus SocketStatus => _zNntp.SocketStatus;

	internal VirtualConnection(Scheduler scheduler, VirtualServer cServer)
	{
		_srv = cServer;
		IsCacheSlave = CachingSystem.IsCacheSlave(cServer.Host);
		_zSched = scheduler;
		_zNntp = new NNTP(_srv, this);
		_zConnection = new ConnectionTask();
		_vIdle = new ManualResetEventSlim();
		_vCancel = new CancellationTokenSource();
	}

	internal void ClearData()
	{
		_zNntp?.ClearData();
	}

	public int CompareTo(object obj)
	{
		return CompareTo(obj as IndexedObject);
	}

	public int CompareTo(IndexedObject obj)
	{
		return Index.CompareTo(obj.Index);
	}

	internal void Start()
	{
		_zConnection.Task(this).Start(TaskScheduler.Default);
	}

	internal void Remove()
	{
		_zSched.Connections.RemoveConnection(ID);
	}

	internal void Cancel()
	{
		Enabled = false;
		_vCancel?.Cancel(throwOnFirstException: true);
		NNTP zNntp = _zNntp;
		if (zNntp != null)
		{
			_zNntp = null;
			zNntp.Disconnect(998, "Cancelled. ID: " + ID, bSendQuit: true);
		}
		_zConnection = null;
		_vIdle?.Set();
	}

	internal void Disconnect(string msg)
	{
		_zNntp.Disconnect(998, msg + ". ID: " + ID, bSendQuit: true);
	}

	internal NNTPCommands ExecuteCommand(NNTPCommands zCommand)
	{
		IsTestConnection = zCommand.HaveTestConnectionMark();
		if (IsTestConnection)
		{
			zCommand.RemoveTestConnectionMark();
		}
		return _zNntp.ExecuteCommand(zCommand, Token.Token);
	}

	internal void LogError(int commandId, NNTPError zErr)
	{
		_srv.WriteStatus("Command #" + Convert.ToString(commandId) + " - Error " + Module.MakeErr(zErr));
	}
}
