using System.Collections.Generic;
using System.Linq;

namespace Spotnet.Phuse.NNTP.Net;

internal class Connections
{
	private readonly IndexedCollection _zCol;

	private readonly Scheduler _zServers;

	internal Connections(Scheduler lServers)
	{
		_zServers = lServers;
		_zCol = new IndexedCollection();
	}

	internal VirtualConnection Item(int connectionId)
	{
		return (VirtualConnection)_zCol.Item(connectionId);
	}

	internal List<int> ListID()
	{
		return _zCol.KeyList();
	}

	internal int Count(int serverId = -1)
	{
		if (serverId == -1)
		{
			return _zCol.Count;
		}
		return List(serverId).Count;
	}

	internal void Clear()
	{
		CancelConnection();
		_zCol.Clear();
	}

	internal List<VirtualConnection> List(int serverId = -1)
	{
		if (serverId == -1)
		{
			return _zCol.ObjectList().Cast<VirtualConnection>().ToList();
		}
		return (from vcon in ListID().Select(Item)
			where vcon != null && vcon.Server.ID == serverId
			select vcon).ToList();
	}

	internal List<int> List(int serverId, ConnectionStatus cStatus)
	{
		return (from c in List(serverId)
			where c.Status == cStatus
			select c.ID).ToList();
	}

	internal bool CancelConnection(int connectionId = -1)
	{
		foreach (VirtualConnection item in _zCol.ObjectList(connectionId).Cast<VirtualConnection>().ToList())
		{
			item.Cancel();
		}
		return true;
	}

	internal bool RemoveServer(int serverId = -1)
	{
		foreach (VirtualConnection item in List(serverId))
		{
			RemoveConnection(item.ID);
		}
		return true;
	}

	internal bool RemoveConnection(int connectionId = -1)
	{
		foreach (VirtualConnection item in _zCol.ObjectList(connectionId).Cast<VirtualConnection>().ToList())
		{
			CancelConnection(item.ID);
			bool result = _zCol.Remove(item.ID);
			if (connectionId != -1)
			{
				return result;
			}
		}
		return true;
	}

	internal VirtualConnection Add(int serverId)
	{
		VirtualServer virtualServer = _zServers.Item(serverId);
		if (virtualServer == null)
		{
			return null;
		}
		VirtualConnection virtualConnection = new VirtualConnection(_zServers, virtualServer);
		if (!_zCol.Add(virtualConnection))
		{
			return null;
		}
		virtualConnection.Start();
		return virtualConnection;
	}
}
