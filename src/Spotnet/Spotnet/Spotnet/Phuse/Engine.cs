using System;
using Spotnet.Phuse.NNTP.Net;

namespace Spotnet.Phuse;

public class Engine : IDisposable
{
	internal Scheduler Scheduler = new Scheduler();

	private readonly object _lockRoot = new object();

	public SlotList Slots
	{
		get
		{
			lock (_lockRoot)
			{
				return new SlotList(Scheduler);
			}
		}
	}

	public ServerList Servers
	{
		get
		{
			lock (_lockRoot)
			{
				return new ServerList(Scheduler);
			}
		}
	}

	public string Xml
	{
		get
		{
			lock (_lockRoot)
			{
				if (Scheduler != null && Scheduler.Slots != null)
				{
					return Scheduler.Slots.XML;
				}
				return "";
			}
		}
	}

	~Engine()
	{
		Close();
	}

	public void Dispose()
	{
		Close();
		GC.SuppressFinalize(this);
	}

	public bool Close()
	{
		if (Scheduler == null)
		{
			return true;
		}
		lock (_lockRoot)
		{
			bool result = false;
			if (Scheduler != null)
			{
				result = Scheduler.Close();
				Scheduler = null;
			}
			return result;
		}
	}
}
