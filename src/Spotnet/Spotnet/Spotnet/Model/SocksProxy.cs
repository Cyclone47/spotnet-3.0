using System;
using System.Net.Sockets;
using System.Security;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;
using Spotnet.Network;

namespace Spotnet.Model;

internal class SocksProxy
{
	private static readonly Logger Log;

	public static string Host;

	public static int Port;

	public static SecureString Username;

	public static SecureString Password;

	private readonly object _lockSocksClient = new object();

	private Socks5Client _client;

	private EventHandler<CreateConnectionAsyncCompletedEventArgs> _onCreateConnectionAsyncCompleted;

	public static bool GlobalyEnabled
	{
		get
		{
			if (!Host.IsNullOrEmpty() && Port > 0 && Username != null)
			{
				return Password != null;
			}
			return false;
		}
	}

	public static event Action<bool> StateChanged;

	static SocksProxy()
	{
		Log = LogManager.GetCurrentClassLogger();
		TryLoadSettings();
	}

	private static void TryLoadSettings()
	{
		try
		{
			string path = Path.Combine(Directory.GetParent(AppHelper.AppPath()).FullName, "socks.config");
			if (!File.Exists(path))
			{
				return;
			}
			string[] array = File.ReadAllLines(path);
			if (array.Length == 4)
			{
				Host = array[0].Trim();
				if (int.TryParse(array[1], out Port))
				{
					Username = array[2].Trim().ToSecureString();
					Password = array[3].Trim().ToSecureString();
					SocksProxy.StateChanged?.Invoke(obj: false);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public static void ChangeState(bool enable)
	{
		if (GlobalyEnabled)
		{
			Settings.Default.UseSocksProxy = enable;
			Settings.Default.Save();
			AppHelper.ResetAllUsenetConnections();
			SocksProxy.StateChanged?.Invoke(enable);
		}
	}

	public void ConnectAsync(TcpClient socketClient, string host, int port, EventHandler<CreateConnectionAsyncCompletedEventArgs> onConnectAsyncCompleted = null)
	{
		lock (_lockSocksClient)
		{
			if (_client != null)
			{
				Close();
			}
			_client = new Socks5Client(Username.ToInsecureString(), Password.ToInsecureString())
			{
				TcpClient = socketClient
			};
			if (onConnectAsyncCompleted != null)
			{
				_onCreateConnectionAsyncCompleted = onConnectAsyncCompleted;
				_client.CreateConnectionAsyncCompleted += _onCreateConnectionAsyncCompleted;
			}
			_client.CreateConnectionAsync(host, port);
		}
	}

	public void Close()
	{
		if (_client == null)
		{
			return;
		}
		lock (_lockSocksClient)
		{
			if (_client == null)
			{
				return;
			}
			try
			{
				if (_onCreateConnectionAsyncCompleted != null)
				{
					_client.CreateConnectionAsyncCompleted -= _onCreateConnectionAsyncCompleted;
					_onCreateConnectionAsyncCompleted = null;
				}
				_client.CancelAsync();
				_client = null;
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}
	}
}
