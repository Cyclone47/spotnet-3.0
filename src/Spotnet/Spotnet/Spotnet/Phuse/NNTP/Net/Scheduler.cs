using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Spotnet.Phuse.NNTP.Net;

internal class Scheduler
{
	private readonly IndexedCollection _servers = new IndexedCollection();

	private readonly IndexedCollection _stacks = new IndexedCollection();

	internal Connections Connections;

	internal Slots Slots = new Slots();

	public int Count => _servers.Count;

	private IndexedCollection Servers => _servers;

	private List<int> WaitingSlots
	{
		get
		{
			List<VirtualSlot> list = Slots.ListStatus(SlotStatus.Downloading);
			if (list == null || list.Count == 0)
			{
				return new List<int>();
			}
			List<int> list2 = new List<int>(list.Count);
			foreach (VirtualSlot item in list)
			{
				list2.Add(item.ID);
			}
			return list2;
		}
	}

	internal string Xml
	{
		get
		{
			StringBuilder stringBuilder = new StringBuilder();
			XmlWriter xR = Module.CreateWriter(stringBuilder);
			if (!WriteXml(xR))
			{
				return "";
			}
			return stringBuilder.ToString();
		}
	}

	internal Scheduler()
	{
		Connections = new Connections(this);
	}

	~Scheduler()
	{
		Close();
	}

	private bool SlotExist(int slotID)
	{
		return Slots.ContainsKey(slotID);
	}

	private bool StackExist(int serverID)
	{
		return _stacks.ContainsKey(serverID);
	}

	internal List<int> ListID(int serverId = -1)
	{
		return _servers.KeyList(serverId);
	}

	internal VirtualServer Item(int serverId)
	{
		return (VirtualServer)_servers.Item(serverId);
	}

	private List<VirtualServer> List(int serverId = -1)
	{
		return _servers.ObjectList(serverId).Cast<VirtualServer>().ToList();
	}

	internal VirtualServer Add(string host, string username = "", string password = "", int port = 119, int maxConnections = 1, bool ssl = false, ServerPriority priority = ServerPriority.Default)
	{
		VirtualServer virtualServer = new VirtualServer(Connections, host, username, password, port, maxConnections, ssl, priority);
		if (!_servers.Add(virtualServer))
		{
			return null;
		}
		for (int i = 0; i < virtualServer.MaxConnectionsAllowed; i++)
		{
			Connections.Add(virtualServer.ID);
		}
		return virtualServer;
	}

	internal bool Remove(int serverId = -1)
	{
		if (serverId == -1)
		{
			Connections?.Clear();
			_servers.Clear();
			_stacks.Clear();
			return true;
		}
		Connections?.RemoveServer(serverId);
		_servers.Remove(serverId);
		_stacks.Remove(serverId);
		return true;
	}

	internal IndexedCollection Stack(int serverId, int slotId)
	{
		if (!SlotExist(slotId) || Servers.Item(serverId) == null)
		{
			return null;
		}
		if (!_stacks.ContainsKey(serverId))
		{
			_stacks.Add(serverId, new VirtualStack());
		}
		return ((VirtualStack)_stacks.Item(serverId)).Stack(slotId);
	}

	internal bool Close()
	{
		Slots?.Remove();
		Remove();
		return true;
	}

	internal IndexedCollection SwitchStack(int slotId, VirtualConnection vConnection)
	{
		while (SlotExist(slotId))
		{
			if (vConnection.Cancelled)
			{
				return null;
			}
			List<int> list = SmartStack(vConnection);
			if (list.Count == 0)
			{
				return null;
			}
			foreach (int item in list)
			{
				IndexedCollection indexedCollection = Stack(item, slotId);
				if (indexedCollection != null)
				{
					return indexedCollection;
				}
			}
		}
		return null;
	}

	private bool ServerActive(int serverId)
	{
		if (_servers.ContainsKey(serverId))
		{
			return Connections.List(serverId, ConnectionStatus.Enabled).Count > 0;
		}
		return false;
	}

	private List<int> SmartStack(VirtualConnection vConnection)
	{
		int num = 0;
		List<int> waitingSlots = WaitingSlots;
		List<int> list = new List<int>();
		foreach (VirtualServer item in List())
		{
			if (!ServerActive(item.ID))
			{
				continue;
			}
			int num2 = WorkLoad(item.ID, waitingSlots);
			if (list.Count == 0 || num2 <= num)
			{
				if (num > 0)
				{
					list.Clear();
				}
				list.Add(item.ID);
				num = num2;
			}
		}
		if (list.Count == 0 && ServerActive(vConnection.Server.ID))
		{
			list.Add(vConnection.Server.ID);
		}
		return list;
	}

	private int WorkLoad(int serverId, List<int> vSlots)
	{
		int num = 0;
		if (vSlots == null)
		{
			return num;
		}
		if (vSlots.Count == 0)
		{
			return num;
		}
		if (!StackExist(serverId))
		{
			return num;
		}
		foreach (int vSlot in vSlots)
		{
			VirtualSlot virtualSlot = Slots.Item(vSlot);
			if (virtualSlot != null && virtualSlot.Status == SlotStatus.Downloading)
			{
				IndexedCollection indexedCollection = Stack(serverId, virtualSlot.ID);
				if (indexedCollection != null)
				{
					num += indexedCollection.Count;
				}
			}
		}
		return num;
	}

	internal NNTPCommands FindWork(VirtualConnection vConnection)
	{
		if (vConnection?.Server == null)
		{
			return null;
		}
		NNTPCommands nNTPCommands = SearchRandomStack(vConnection);
		if (nNTPCommands != null)
		{
			return nNTPCommands;
		}
		if (vConnection.Server.Priority == ServerPriority.Low)
		{
			return null;
		}
		return SearchRandomSlot(vConnection);
	}

	private IEnumerable<VirtualSlot> RandomSlots()
	{
		List<int> waitingSlots = WaitingSlots;
		if (waitingSlots == null)
		{
			yield break;
		}
		while (waitingSlots.Count > 0)
		{
			int num = waitingSlots[Module.Random.Next(0, waitingSlots.Count - 1)];
			VirtualSlot virtualSlot = Slots.Item(num);
			if (waitingSlots.Contains(num))
			{
				waitingSlots.Remove(num);
			}
			if (virtualSlot != null && virtualSlot.Status == SlotStatus.Downloading)
			{
				yield return virtualSlot;
			}
		}
	}

	private NNTPCommands SearchRandomSlot(VirtualConnection vConnection)
	{
		foreach (VirtualSlot item in RandomSlots())
		{
			NNTPCommands nNTPCommands = item.Take(vConnection);
			if (nNTPCommands != null)
			{
				return nNTPCommands;
			}
		}
		return null;
	}

	private NNTPCommands SearchRandomStack(VirtualConnection vConnection)
	{
		int iD = vConnection.Server.ID;
		if (!StackExist(iD) || !ServerActive(iD))
		{
			return null;
		}
		foreach (VirtualSlot item in RandomSlots())
		{
			IndexedCollection indexedCollection = Stack(iD, item.ID);
			if (indexedCollection != null && !indexedCollection.IsEmpty)
			{
				NNTPCommands nNTPCommands = (NNTPCommands)indexedCollection.Take();
				if (nNTPCommands != null)
				{
					return nNTPCommands;
				}
			}
		}
		return null;
	}

	internal bool WriteXml(XmlWriter xR)
	{
		if (Count == 0)
		{
			return false;
		}
		xR.WriteStartElement("servers");
		foreach (VirtualServer item in List())
		{
			if (!item.WriteXml(xR))
			{
				return false;
			}
		}
		xR.WriteEndElement();
		xR.Flush();
		return true;
	}
}
