using System.Collections.Generic;

namespace Spotnet.Phuse.NNTP.Net;

public class ServerList : IApi
{
	private readonly Scheduler _zServers;

	public string Xml
	{
		get
		{
			lock (_zServers)
			{
				return _zServers.Xml;
			}
		}
	}

	public int Count
	{
		get
		{
			lock (_zServers)
			{
				return _zServers.Count;
			}
		}
	}

	public List<int> Items
	{
		get
		{
			lock (_zServers)
			{
				return _zServers.ListID();
			}
		}
	}

	internal ServerList(Scheduler lServers)
	{
		_zServers = lServers;
	}

	public bool Remove(int id)
	{
		lock (_zServers)
		{
			return _zServers.Remove(id);
		}
	}

	public int Add(string host, string username = "", string password = "", int port = 119, int connections = 1, bool ssl = false, ServerPriority priority = ServerPriority.Default)
	{
		lock (_zServers)
		{
			return _zServers.Add(host, username, password, port, connections, ssl, priority)?.ID ?? (-1);
		}
	}
}
