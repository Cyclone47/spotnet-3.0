using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using Spotnet.Mvvm.Threading;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.Controls;
using Spotnet.DAL;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Model;
using Spotnet.Phuse;
using Spotnet.Properties;
using Spotnet.Utilities;

namespace Spotnet.Helpers;

public static class AppHelper
{
	public enum SpotCategory
	{
		Video = 1,
		Music = 2,
		Game = 3,
		Software = 4,
		Movies = 5,
		Unknown = 65535
	}

	private enum ConvTo
	{
		B,
		KB,
		MB,
		GB,
		TB,
		PB,
		EB,
		ZI,
		YI
	}

	internal const string CancelMsg = "Canceled";

	internal const string DefaultFilter = "cat < 9";

	internal const string Spotname = "Spotnet";

	private static readonly Logger Log;

	internal static readonly DateTime Epoch;

	public static readonly double Epsilon;

	private static readonly string[] Keys;

	internal static string LastServer;

	internal static Servers ServersDb;

	private static readonly bool[] TranslateInfoCat1;

	private static readonly bool[] TranslateInfoCat2;

	private static readonly bool[] TranslateInfoCat3;

	private static readonly bool[] TranslateInfoCat4;

	private static string _appPath;

	private static CRC32 _crc32;

	private static bool _didOnce;

	private static Engine _mPhuse;

	private static Engine _dPhuse;

	private static Engine _hPhuse;

	private static Engine _uPhuse;

	private static readonly object LockmPhuse;

	private static readonly object LockdPhuse;

	private static readonly object LockhPhuse;

	private static readonly object LockuPhuse;

	private static bool _keysLoaded;

	private static bool _translateInfoLoaded;

	private static string _hostUniqueId;

	public static bool IsLocalSettingsFolder;

	private static IOrderedEnumerable<string> _badWordsSet;

	private static bool? _isSnelNlProvider;

	private static bool? _is5EuroProvider;

	private static Popup _autoHideablePopup;

	internal static string SettingsFolder;

	internal static string DesktopDirectory;

	internal static string FiltersFolder;

	private static string _tempPath;

	private static string _smileysPath;

	internal static string SmileysPath
	{
		get
		{
			if (_smileysPath.IsNullOrEmpty())
			{
				_smileysPath = System.IO.Path.Combine(SettingsFolder.Replace("\\", "/").Replace("\"", "\"\""), "Images/smileys/");
			}
			return _smileysPath;
		}
	}

	internal static Engine DownloadPhuse
	{
		get
		{
			ServerInfo server = GetServer(ServerType.Download);
			if (server == null || server.Server.IsNullOrWhiteSpace())
			{
				return null;
			}
			if (_dPhuse != null)
			{
				return _dPhuse;
			}
			lock (LockdPhuse)
			{
				if (_dPhuse == null)
				{
					_dPhuse = CreatePhuse((ServerInfo)server.Clone()) ?? new Engine();
				}
				return _dPhuse;
			}
		}
	}

	internal static Engine MasterCachePhuse
	{
		get
		{
			ServerInfo server = GetServer(ServerType.MasterCache);
			if (server == null || server.Server.IsNullOrWhiteSpace())
			{
				return null;
			}
			if (_mPhuse != null)
			{
				return _mPhuse;
			}
			lock (LockmPhuse)
			{
				object obj = _mPhuse;
				if (obj == null)
				{
					obj = CreatePhuse(server) ?? new Engine();
					_mPhuse = (Engine)obj;
				}
				return (Engine)obj;
			}
		}
	}

	internal static Engine HeaderPhuse
	{
		get
		{
			ServerInfo server = GetServer(ServerType.Headers);
			if (server == null || server.Server.IsNullOrWhiteSpace())
			{
				return null;
			}
			if (_hPhuse != null)
			{
				return _hPhuse;
			}
			lock (LockhPhuse)
			{
				return _hPhuse ?? (_hPhuse = CreatePhuse(server));
			}
		}
	}

	internal static Engine UploadPhuse
	{
		get
		{
			ServerInfo server = GetServer(ServerType.Headers);
			ServerInfo server2 = GetServer(ServerType.Upload);
			if (_uPhuse != null)
			{
				return _uPhuse;
			}
			lock (LockuPhuse)
			{
				return _uPhuse ?? (_uPhuse = ((!SameServer(server, server2)) ? CreatePhuse(server2) : HeaderPhuse));
			}
		}
	}

	public static string HostUniqueId => LazyInitializer.EnsureInitialized(ref _hostUniqueId, () => Sha1(MacAddress));

	public static string MacAddress => (from nic in NetworkInterface.GetAllNetworkInterfaces()
		where nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
		select nic.GetPhysicalAddress().ToString()).FirstOrDefault();

	public static Version AppVersion
	{
		get
		{
			try
			{
				// Always use the Spotnet assembly's own version, ensuring correct behavior
				// both in Spotnet.exe and in test runners where GetEntryAssembly is testhost.
				return typeof(AppHelper).Assembly.GetName().Version ?? Assembly.GetEntryAssembly()?.GetName().Version;
			}
			catch (Exception ex)
			{
				Log.Exception(ex, showToClient: true);
				return null;
			}
		}
	}

	public static bool ShiftKeyDown => (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) != 0;

	// Popup menus have their own resource scope. Never inject Windows Aero here:
	// its light menu templates override the application's dark palette.
	internal static ResourceDictionary GetMenuResourceDictionary => new ResourceDictionary
	{
		Source = new Uri("pack://application:,,,/Spotnet;component/Style/MainMenuStyle.xaml", UriKind.Absolute)
	};

	internal static bool IsSnelNlProvider
	{
		get
		{
			if (!_isSnelNlProvider.HasValue)
			{
				string text = GetServer(ServerType.Download).Server.ToLower();
				_isSnelNlProvider = text.Contains(".snelnl.") || text.Trim().Equals("news.sslusenet.com");
			}
			return _isSnelNlProvider.GetValueOrDefault();
		}
	}

	internal static bool Is5EuroProvider
	{
		get
		{
			if (!_is5EuroProvider.HasValue)
			{
				string text = GetServer(ServerType.Download).Server.ToLower();
				_is5EuroProvider = text.EndsWith(".5eurousenet.com");
			}
			return _is5EuroProvider.GetValueOrDefault();
		}
	}

	internal static event Action OnDbSettingsUpdate;

	static AppHelper()
	{
		Log = LogManager.GetCurrentClassLogger();
		Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		Epsilon = 1E-08;
		Keys = new string[11];
		LastServer = "";
		ServersDb = new Servers();
		TranslateInfoCat1 = new bool[101];
		TranslateInfoCat2 = new bool[101];
		TranslateInfoCat3 = new bool[101];
		TranslateInfoCat4 = new bool[101];
		_appPath = "";
		LockmPhuse = new object();
		LockdPhuse = new object();
		LockhPhuse = new object();
		LockuPhuse = new object();
		DesktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		SettingsFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Spotnet/");
		if (Spotnet.Deployment.InstalledProfile.Enabled)
		{
			SettingsFolder = Spotnet.Deployment.InstalledProfile.DataDirectory + System.IO.Path.DirectorySeparatorChar;
			IsLocalSettingsFolder = true;
		}
		FiltersFolder = System.IO.Path.Combine(SettingsFolder, "Filters.v2/");
	}

	internal static string GetTempPath()
	{
		if (_tempPath.IsNullOrEmpty())
		{
			string text = System.IO.Path.GetTempPath().Trim();
			if (!EnsureDirectoryExist(text))
			{
				throw new Exception("Failed to create temp directory: " + text);
			}
			string text2 = System.IO.Path.Combine(text, "Spotnet");
			if (!EnsureDirectoryExist(text2))
			{
				throw new Exception("Failed to create temp directory: " + text2);
			}
			_tempPath = text2;
		}
		return _tempPath;
	}

	internal static string GetTempFileName(string ext = null, string filename = null)
	{
		if (ext == null)
		{
			ext = "tmp";
		}
		if (filename.IsNullOrEmpty())
		{
			filename = Guid.NewGuid().ToString();
		}
		return System.IO.Path.Combine(GetTempPath(), filename + "." + ext);
	}

	private static bool IsStringContainsIllegalPathChars(string filename)
	{
		if (filename.IsNullOrWhiteSpace())
		{
			return false;
		}
		char[] illegalC = System.IO.Path.GetInvalidFileNameChars();
		return filename.Any((char ch) => illegalC.Contains(ch));
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
	public static extern int GetDoubleClickTime();

	internal static string GenerateNzbFilePath(string fileName)
	{
		string text = ((fileName.IsNullOrWhiteSpace() || IsStringContainsIllegalPathChars(fileName)) ? ((Settings.Default.DownloadAction > 1 || Settings.Default.ExternalNzbGet) ? System.IO.Path.GetTempFileName() : GetTempFileName()) : ((Settings.Default.DownloadAction > 1 || Settings.Default.ExternalNzbGet) ? System.IO.Path.Combine(System.IO.Path.GetTempPath().Trim(), fileName) : System.IO.Path.Combine(GetTempPath(), fileName)));
		if (!text.ToLower().EndsWith(".nzb"))
		{
			text += ".nzb";
		}
		return text;
	}

	internal static bool IsDomainName(string domainName)
	{
		return Regex.IsMatch(domainName, " # Rev:2013-03-26\r\n                                                # Match DNS host domain having one or more subdomains.\r\n                                                # Top level domain subset taken from IANA.ORG. See:\r\n                                                # http://data.iana.org/TLD/tlds-alpha-by-domain.txt\r\n                                                ^                  # Anchor to start of string.\r\n                                                (?!.{256})         # Whole domain must be 255 or less.\r\n                                                (?:                # Group for one or more sub-domains.\r\n                                                    [a-z0-9]         # Either subdomain length from 2-63.\r\n                                                    [a-z0-9-]{0,61}  # Middle part may have dashes.\r\n                                                    [a-z0-9]         # Starts and ends with alphanum.\r\n                                                    \\.               # Dot separates subdomains.\r\n                                                | [a-z0-9]         # or subdomain length == 1 char.\r\n                                                    \\.               # Dot separates subdomains.\r\n                                                )+                 # One or more sub-domains.\r\n                                                (?:                # Top level domain alternatives.\r\n                                                    [a-z]{2}         # Either any 2 char country code,\r\n                                                | AERO|ARPA|ASIA|BIZ|CAT|COM|COOP|EDU|  # or TLD \r\n                                                    GOV|INFO|INT|JOBS|MIL|MOBI|MUSEUM|    # from list.\r\n                                                    NAME|NET|ORG|POST|PRO|TEL|TRAVEL|XXX  # IANA.ORG\r\n                                                )                  # End group of TLD alternatives.\r\n                                                $                  # Anchor to end of string.", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
	}

	internal static bool IsIp(string ip)
	{
		return Regex.IsMatch(ip, "^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$");
	}

	internal static string GetProviderDomainHtmlSave()
	{
		string server = GetServer(ServerType.Download)?.Server;
		if (string.IsNullOrEmpty(server))
		{
			return null;
		}
		string[] array = server.Split('.');
		if (array.Length == 4 && int.TryParse(array[array.Length - 1], out var result) && int.TryParse(array[array.Length - 2], out result))
		{
			return null;
		}
		return WebUtility.HtmlEncode((array.Length < 3) ? server : string.Join(".", array, array.Length - 2, array.Length - 1));
	}

	public static void SerializeDict(Dictionary<string, string> dict, string filename)
	{
		DataContractSerializer dataContractSerializer = new DataContractSerializer(dict.GetType());
		using FileStream fileStream = System.IO.File.Open(filename, FileMode.Create);
		dataContractSerializer.WriteObject(fileStream, dict);
		fileStream.Flush();
	}

	public static Dictionary<string, string> RestoreDict(string filename)
	{
		if (!System.IO.File.Exists(filename))
		{
			return new Dictionary<string, string>();
		}
		byte[] buffer;
		try
		{
			buffer = System.IO.File.ReadAllBytes(filename);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return new Dictionary<string, string>();
		}
		try
		{
			using XmlDictionaryReader reader = XmlDictionaryReader.CreateTextReader(buffer, new XmlDictionaryReaderQuotas());
			return (Dictionary<string, string>)new DataContractSerializer(typeof(Dictionary<string, string>)).ReadObject(reader, verifyObjectName: true);
		}
		catch (Exception ex2)
		{
			Log.Debug("Failed to parse: " + System.IO.File.ReadAllText(filename));
			Log.Exception(ex2, showToClient: true);
			return new Dictionary<string, string>();
		}
	}

	public static bool EnsureDirectoryExist(string newDir)
	{
		if (!System.IO.Directory.Exists(newDir))
		{
			try
			{
				System.IO.Directory.CreateDirectory(newDir);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				try
				{
					System.IO.Directory.CreateDirectory(newDir);
				}
				catch
				{
				}
			}
			return System.IO.Directory.Exists(newDir);
		}
		return true;
	}

	public static void RenameHard(string oldPath, string newPath)
	{
		if (System.IO.File.Exists(oldPath))
		{
			FileRenameHard(oldPath, newPath);
			return;
		}
		if (System.IO.Directory.Exists(oldPath))
		{
			DirectoryRenameHard(oldPath, newPath);
			return;
		}
		throw new Exception("File or directory does not exist: " + oldPath);
	}

	private static void DirectoryRenameHard(string oldPath, string newPath)
	{
		if (!System.IO.Directory.Exists(oldPath))
		{
			throw new Exception("Directory does not exist: " + oldPath);
		}
		newPath = GetSafePath(newPath);
		for (int i = 0; i < 15; i++)
		{
			try
			{
				System.IO.Directory.Move(oldPath, newPath);
				return;
			}
			catch (Exception)
			{
			}
		}
		throw new Exception("Failed to move directory.");
	}

	private static void FileRenameHard(string oldPath, string newPath)
	{
		if (!System.IO.File.Exists(oldPath))
		{
			throw new Exception("File does not exist: " + oldPath);
		}
		newPath = GetSafePath(newPath);
		for (int i = 0; i < 15; i++)
		{
			try
			{
				System.IO.File.Move(oldPath, newPath);
				return;
			}
			catch (Exception)
			{
			}
		}
		throw new Exception("Failed to move file.");
	}

	public static string GetSafePath(string path)
	{
		return string.Join("_", path.Split(System.IO.Path.GetInvalidPathChars()));
	}

	public static string WriteBytesToTmpFile(byte[] bytes, string extension, string filename = "")
	{
		string tempFileName = GetTempFileName(extension, filename);
		try
		{
			StreamWriter streamWriter = new StreamWriter(tempFileName, append: false, LatinEnc());
			new BinaryWriter(streamWriter.BaseStream, LatinEnc()).Write(bytes);
			streamWriter.Close();
			return tempFileName;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return null;
		}
	}

	public static bool TryToCheckOtherUsenetPorts()
	{
		int port = ServersDb.OHeader.Port;
		int[] array = new int[4] { 563, 80, 119, 443 };
		foreach (int num in array)
		{
			if (num != port)
			{
				Log.Debug("Trying port: " + num);
				ServerInfo serverInfo = (ServerInfo)ServersDb.OUp.Clone();
				ServerInfo serverInfo2 = (ServerInfo)ServersDb.ODown.Clone();
				ServerInfo serverInfo3 = (ServerInfo)ServersDb.OHeader.Clone();
				serverInfo.Port = (serverInfo2.Port = (serverInfo3.Port = num));
				if (TestConnections(new List<ServerInfo> { serverInfo, serverInfo2, serverInfo3 }, Settings.Default.UseSocksProxy, out var _))
				{
					ServersDb.OUp = serverInfo;
					ServersDb.ODown = serverInfo2;
					ServersDb.OHeader = serverInfo3;
					Log.Info("Use " + serverInfo2.Port + " port because of original (" + port + ") is not available.");
					return true;
				}
			}
		}
		return false;
	}

	public static bool TestConnections(List<ServerInfo> serverInfos, bool useSocksProxy, out string errorMsg)
	{
		errorMsg = null;
		if (!serverInfos.Any())
		{
			return true;
		}
		bool useSocksProxy2 = Settings.Default.UseSocksProxy;
		try
		{
			DbUpdater.DbUpdateTimerStop();
			DbUpdater.Stop();
			while (DbUpdater.IsDbUpdateInProgress)
			{
				Thread.Sleep(TimeSpan.FromMilliseconds(200.0));
			}
			Settings.Default.UseSocksProxy = useSocksProxy;
			ResetAllUsenetConnections();
			foreach (ServerInfo item in from s in serverInfos
				group s by s.Server.ToUpperInvariant() into grp
				select grp.First())
			{
				ServerInfo obj = (ServerInfo)item.Clone();
				obj.SSL = item.DoesProviderUseSsl();
				obj.Connections = 1;
				using (Engine tPhuse = CreatePhuse(obj))
				{
					if (!Spots.TestConnection(tPhuse, Settings.Default.HeaderGroup, out errorMsg))
					{
						return false;
					}
				}
				if (item.Connections == 0)
				{
					item.Connections = Spots.GetMaxConnectionsNumber(item) - 2;
					if (item.Connections < 1)
					{
						item.Connections = 1;
					}
				}
			}
		}
		finally
		{
			Settings.Default.UseSocksProxy = useSocksProxy2;
			DbUpdater.DbUpdateTimerStart();
		}
		return true;
	}

	public static string Sha1(string sVal)
	{
		if (sVal.IsNullOrEmpty())
		{
			return "Unknown";
		}
		return StripNonAlphaNumericCharacters(Convert.ToBase64String(SHA1.HashData(MakeLatin(sVal))));
	}

	public static void SwitchSpotnetToUseLocalSettingsFolder()
	{
		Log.Warn("Switch Spotnet to use local settings folder");
		SettingsFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotnet\\Data\\");
		IsLocalSettingsFolder = true;
	}

	internal static bool UploadFile(string zippedLog, string url, UploadFileCompletedEventHandler completedHandler)
	{
		WebClient webClient = new WebClient();
		string uriString = $"{url}/{AppVersion}_{UserKeyHelper.GetModulusUriCompatable()}.zip";
		if (completedHandler != null)
		{
			webClient.UploadFileCompleted += completedHandler;
		}
		webClient.UploadFileAsync(new Uri(uriString), "PUT", zippedLog);
		return true;
	}

	private static void ClearEngine(ref Engine engine)
	{
		if (engine != null)
		{
			try
			{
				engine.Close();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			engine = null;
		}
	}

	private static double ConvertBytes(long bytes, ConvTo convertTo)
	{
		if (Enum.IsDefined(typeof(ConvTo), convertTo))
		{
			return (double)bytes / Math.Pow(1024.0, (double)convertTo);
		}
		return -1.0;
	}

	private static string CreateHash(string sLeft, string sRight)
	{
		// SHA1.Create() picks the platform implementation instead of the CAPI provider,
		// which a FIPS-policy machine will not create. Same digest, same wire format.
		using SHA1 sHA1CryptoServiceProvider = SHA1.Create();
		byte[] array = MakeLatin(sLeft);
		byte[] array2 = MakeLatin(sRight);
		int num = array.Length;
		int num2 = array2.Length;
		byte[] array3 = new byte[num + num2 - 2 + 5 + 1];
		for (int i = 0; i < array.Length; i++)
		{
			array3[i] = array[i];
		}
		for (int j = array3.Length - num2; j < array3.Length; j++)
		{
			array3[j] = array2[j - (array3.Length - num2)];
		}
		int num3 = array3.Length - 4 - num2;
		int num4 = array3.Length - 3 - num2;
		int num5 = array3.Length - 2 - num2;
		int num6 = array3.Length - 1 - num2;
		byte[] array4 = new byte[62];
		byte[] array5 = new byte[62];
		byte[] array6 = new byte[62];
		byte[] array7 = new byte[62];
		for (int k = 0; k < 62; k++)
		{
			array4[k] = (byte)k;
			array5[k] = (byte)k;
			array6[k] = (byte)k;
			array7[k] = (byte)k;
		}
		Random random = new Random();
		for (int l = 0; l < 62; l++)
		{
			int num7 = random.Next(0, 62);
			byte b = array4[l];
			array4[l] = array4[num7];
			array4[num7] = b;
			num7 = random.Next(0, 62);
			b = array5[l];
			array5[l] = array5[num7];
			array5[num7] = b;
			num7 = random.Next(0, 62);
			b = array6[l];
			array6[l] = array6[num7];
			array6[num7] = b;
			num7 = random.Next(0, 62);
			b = array7[l];
			array7[l] = array7[num7];
			array7[num7] = b;
		}
		for (int m = 0; m < 62; m++)
		{
			array4[m] = GetBaseChar(array4[m]);
			array5[m] = GetBaseChar(array5[m]);
			array6[m] = GetBaseChar(array6[m]);
			array7[m] = GetBaseChar(array7[m]);
		}
		for (int n = 0; n < 62; n++)
		{
			for (int num8 = 0; num8 < 62; num8++)
			{
				for (int num9 = 0; num9 < 62; num9++)
				{
					for (int num10 = 0; num10 < 62; num10++)
					{
						array3[num3] = array4[n];
						array3[num4] = array5[num8];
						array3[num5] = array6[num9];
						array3[num6] = array7[num10];
						byte[] array8 = sHA1CryptoServiceProvider.ComputeHash(array3);
						if (array8[0] == 0 && array8[1] == 0)
						{
							return GetLatin(array3);
						}
					}
				}
			}
		}
		throw new Exception("Error 422");
	}

	private static string DownloadString(string zUrl, ref string zError)
	{
		try
		{
			return new WebClient().DownloadString(zUrl);
		}
		catch (Exception ex)
		{
			zError = ex.Message;
			return null;
		}
	}

	internal static bool RecreateAllDatabases()
	{
		Log.Debug("Try to recreate spots and comments databases");
		ShowInfoMsg(Words.DatabaseStructureChangedMessage, Words.DatabaseStructureChangedTitle);
		string dbFilename = GetDbFilename("dbs");
		string dbFilename2 = GetDbFilename("dbc");
		bool result = false;
		try
		{
			SQliteDb.CloseAllConnections();
			if (!TryToRemoveFile(dbFilename, 5))
			{
				Log.Error("Failed to remove spots db file");
				Error("Failed to remove spots db file");
				return false;
			}
			if (!TryToRemoveFile(dbFilename2, 5))
			{
				Log.Error("Failed to remove comments db file");
				Error("Failed to remove comments db file");
				return false;
			}
			if (Sys.MainWindow.SpotProvider.OpenDb() && !Sys.MainWindow.SpotProvider.Corrupted)
			{
				SpotSaver.InitializeSpamReportsDb();
				SpotSaver.InitializeCommentsDb();
				Settings.Default.DatabaseMax = -1L;
				Settings.Default.DatabaseMin = -1L;
				Settings.Default.DatabaseCount = 0L;
				Settings.Default.DatabaseFilter = 0L;
				Settings.Default.Save();
				AppHelper.OnDbSettingsUpdate?.Invoke();
				Sys.MainWindow.ResetNewSpotsCount();
				DispatcherHelper.CheckBeginInvokeOnUI(delegate
				{
					Sys.MainWindow.CloseAllSpotsTab();
					Sys.LeftPanel.NoFilter(bForce: true);
				});
				result = true;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return result;
	}

	internal static bool ClearSpotsDb()
	{
		Log.Debug("Clear spots db");
		if (!Settings.Default.RecreateDbScheduled)
		{
			ShowInfoMsg("Database files structure for spots is wrong and has to be recreated", "Recreate spots db");
		}
		string dbFilename = GetDbFilename("dbs");
		try
		{
			SQliteDb.CloseAllConnections();
			if (!TryToRemoveFile(dbFilename, 5))
			{
				Log.Error("Failed to remove spots db file");
				Error("Failed to remove spots db file");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
	}

	internal static bool ClearCommentsDb()
	{
		Log.Debug("Clear comments db");
		if (!Settings.Default.RecreateDbScheduled)
		{
			ShowInfoMsg("Database files structure for comments is wrong and has to be recreated", "Recreate comments db");
		}
		string dbFilename = GetDbFilename("dbc");
		try
		{
			SQliteDb.CloseAllConnections();
			if (!TryToRemoveFile(dbFilename, 5))
			{
				Log.Error("Failed to move comments db file");
				Error("Failed to move comments db file");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
	}

	private static bool TryToRemoveFile(string file, int tries)
	{
		bool flag = false;
		int num = 1;
		while (num < tries && !flag)
		{
			try
			{
				if (System.IO.File.Exists(file))
				{
					System.IO.File.Delete(file);
				}
				flag = true;
			}
			catch (IOException ex)
			{
				Log.Debug(ex.Message);
				Thread.Sleep(num * 100);
				num++;
			}
		}
		return flag;
	}

	private static bool SameServer(ServerInfo si1, ServerInfo si2)
	{
		if (si1.SSL == si2.SSL && si1.Port == si2.Port && si1.Server.Trim().EqualsIgnoreCase(si2.Server.Trim()))
		{
			return si1.Username.Trim().EqualsIgnoreCase(si2.Username.Trim());
		}
		return false;
	}

	internal static bool UpdateKeysFileFromTheNet(string zUrl, string sFile, bool showError = true)
	{
		string content = "";
		if (UpdateFileFromTheNet(zUrl, sFile, ref content, showError))
		{
			if (System.IO.Path.GetExtension(zUrl).EqualsIgnoreCase(".xml") && !ValidList(content))
			{
				return false;
			}
			return true;
		}
		return false;
	}

	internal static bool UpdateFileFromTheNet(string zUrl, string sFile, ref string content, bool showError = true)
	{
		try
		{
			string zError = "";
			content = DownloadString(zUrl, ref zError);
			if (content.IsNullOrEmpty())
			{
				return false;
			}
			StreamWriter streamWriter = new StreamWriter(sFile, append: false, Encoding.UTF8);
			streamWriter.Write(content);
			streamWriter.Close();
			return true;
		}
		catch (Exception ex)
		{
			if (showError)
			{
				Log.Exception(ex);
			}
		}
		return false;
	}

	public static string FormatSizeMegaBytes(double sizeMegaBytes)
	{
		if (sizeMegaBytes >= 104857600.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0 / 1024.0, 0) + " TB";
		}
		if (sizeMegaBytes >= 10485760.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0 / 1024.0, 1) + " TB";
		}
		if (sizeMegaBytes >= 1024000.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0 / 1024.0, 2) + " TB";
		}
		if (sizeMegaBytes >= 102400.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0, 0) + " GB";
		}
		if (sizeMegaBytes >= 10240.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0, 1) + " GB";
		}
		if (sizeMegaBytes >= 1000.0)
		{
			return Math.Round(sizeMegaBytes / 1024.0, 2) + " GB";
		}
		if (sizeMegaBytes >= 100.0)
		{
			return Math.Round(sizeMegaBytes, 0) + " MB";
		}
		if (sizeMegaBytes >= 10.0)
		{
			return Math.Round(sizeMegaBytes, 1) + " MB";
		}
		if (sizeMegaBytes >= 1.0)
		{
			return Math.Round(sizeMegaBytes, 2) + " MB";
		}
		if (sizeMegaBytes >= 0.1)
		{
			return Math.Round(sizeMegaBytes * 1024.0, 0) + " KB";
		}
		if (sizeMegaBytes >= 0.01)
		{
			return Math.Round(sizeMegaBytes * 1024.0, 1) + " KB";
		}
		return Math.Round(sizeMegaBytes * 1024.0, 2) + " KB";
	}

	internal static ulong GetDiskSpace(string directory)
	{
		return OperatingSystemHelper.GetFreeSpaceOfPathInBytes(directory);
	}

	internal static string CatDesc(int cat, byte subCat = 0)
	{
		List<string> list = new List<string>
		{
			Categories.CatFilms,
			Categories.CatSeries,
			Categories.CatBooks,
			Categories.CatErotica,
			Categories.CatImages
		};
		List<string> list2 = new List<string>
		{
			Categories.CatMusic,
			Categories.MGLiveset,
			Categories.MGPodcast,
			Categories.MGAudiobook
		};
		if (subCat > 0)
		{
			subCat--;
		}
		switch (cat)
		{
		case 1:
			if (subCat < list.Count)
			{
				return list[subCat];
			}
			break;
		case 2:
			if (subCat < list2.Count)
			{
				return list2[subCat];
			}
			break;
		case 3:
			return Categories.CatGames;
		case 4:
			return Categories.CatApplications;
		case 5:
			return Categories.CatBooks;
		case 6:
			return Categories.CatSeries;
		case 9:
			return Categories.CatErotica;
		}
		return Words.Error;
	}

	internal static void ResetAllUsenetConnections()
	{
		ClearDownloadPhuse();
		ClearHeaderPhuse();
		ClearUploadPhuse();
		ClearMasterCachePhuse();
		ClearSlavesCachePhuse();
	}

	internal static void ClearHeaderPhuse()
	{
		ClearEngine(ref _hPhuse);
	}

	internal static void ClearDownloadPhuse()
	{
		ClearEngine(ref _dPhuse);
	}

	internal static void ClearUploadPhuse()
	{
		ClearEngine(ref _uPhuse);
	}

	internal static void ClearSlavesCachePhuse(string slaveName = null)
	{
		CachingSystem.ClearSlavePhuses(slaveName);
	}

	internal static void ClearMasterCachePhuse()
	{
		ClearEngine(ref _mPhuse);
	}

	internal static string GetCommentThemeFile()
	{
		return SettingsFolder + "\\TabThemes\\" + Settings.Default.ActiveTheme + "\\comment.htm";
	}

	internal static IOrderedEnumerable<string> BadWordsSet()
	{
		if (_badWordsSet != null)
		{
			return _badWordsSet;
		}
		string path = SettingsFolder + "badwords.txt";
		if (!System.IO.File.Exists(path))
		{
			try
			{
				StreamWriter streamWriter = new StreamWriter(path, append: false, LatinEnc());
				streamWriter.Write(Resources.badwords);
				streamWriter.Close();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				return null;
			}
		}
		_badWordsSet = from s in System.IO.File.ReadAllLines(path)
			orderby s.Trim().Replace("\\b", string.Empty).Length
			select s;
		return _badWordsSet;
	}

	internal static string CreateMsgId(string sPrefix = "")
	{
		byte[] array = new byte[8];
		new Random().NextBytes(array);
		int value = checked((int)Math.Round((DateTime.UtcNow - Epoch).TotalSeconds));
		string text = (Convert.ToBase64String(array) + Convert.ToBase64String(BitConverter.GetBytes(value))).Replace("/", "s").Replace("+", "p").Replace("=", "");
		if (!sPrefix.IsNullOrEmpty())
		{
			return CreateHash("<" + sPrefix.Replace(".", "") + ".0." + text + ".", "@spot.net>");
		}
		return CreateHash("<" + text, "@spot.net>");
	}

	internal static void ResetProviderDetermination()
	{
		_isSnelNlProvider = null;
		_is5EuroProvider = null;
	}

	internal static Engine CreatePhuse(ServerInfo serverInfo, bool isSlave = false)
	{
		if (serverInfo == null)
		{
			return null;
		}
		if (serverInfo.Server.IsNullOrEmpty())
		{
			return null;
		}
		string text;
		if (isSlave)
		{
			text = DownloaderProps.DefaultServer1Username;
		}
		else
		{
			text = serverInfo.Username;
			string text2 = serverInfo.Server.ToLower();
			if (text2.Contains(".snelnl.") || text2.Trim().Equals("news.sslusenet.com"))
			{
				text = text.Replace('@', '_');
			}
		}
		Engine engine = new Engine();
		engine.Servers.Add(serverInfo.Server, text, serverInfo.Password, serverInfo.Port, serverInfo.Connections, serverInfo.SSL);
		return engine;
	}

	internal static byte[] GetAvatar()
	{
		if (Settings.Default.Avatar.IsNullOrEmpty())
		{
			return null;
		}
		try
		{
			MemoryStream memoryStream = new MemoryStream();
			byte[] array = Convert.FromBase64String(Settings.Default.Avatar);
			memoryStream.Write(array, 0, array.Length);
			BitmapFrame bitmapFrame = BitmapFrame.Create(memoryStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
			if (!((bitmapFrame.PixelWidth > 1) & (bitmapFrame.PixelHeight > 1)))
			{
				return null;
			}
			return array;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return null;
	}

	internal static CRC32 GetCrc()
	{
		return _crc32 ?? (_crc32 = new CRC32());
	}

	internal static string GetDbFilename(string sExtension)
	{
		try
		{
			string text = SafeName(ServersDb.ODown.Server.Trim()).ToLower();
			if (text.IsNullOrEmpty())
			{
				return null;
			}
			return System.IO.Path.Combine(SettingsFolder, text + "." + sExtension);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return null;
	}

	internal static string GetHeader(object zt)
	{
		if (!(zt is System.Windows.Controls.Panel panel) || panel.Children.Count < 2)
		{
			return "";
		}
		if (!(panel.Children[1] is TextBlock textBlock))
		{
			return "";
		}
		return textBlock.Text;
	}

	internal static ImageSource GetHeaderIcon(object zt)
	{
		if (!(zt is System.Windows.Controls.Panel panel) || panel.Children.Count == 0)
		{
			return null;
		}
		return (panel.Children[0] as Image)?.Source;
	}

	internal static Image GetIcon(string sKey)
	{
		try
		{
			return new Image
			{
				Source = new BitmapImage(new Uri("pack://application:,,,/Spotnet;component/Resources/ImagesInternal/" + sKey + ".ico"))
			};
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return null;
		}
	}

	internal static BitmapSource GetImage(string sKey)
	{
		try
		{
			return new BitmapImage(new Uri("pack://application:,,,/Spotnet;component/Resources/ImagesInternal/" + sKey));
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return null;
		}
	}

	internal static ServerInfo GetServer(ServerType type)
	{
		return type switch
		{
			ServerType.Headers => ServersDb.OHeader, 
			ServerType.Upload => ServersDb.OUp, 
			ServerType.Download => ServersDb.ODown, 
			ServerType.MasterCache => ServersDb.OMasterCache, 
			ServerType.SlaveCache => ServersDb.OSlaveCache, 
			_ => null, 
		};
	}

	internal static NntpSettings HeaderSettings(bool bIncludePosition)
	{
		try
		{
			NntpSettings nntpSettings = new NntpSettings
			{
				BlackList = BlackAndWhite.BlackList(),
				WhiteList = BlackAndWhite.WhiteList(),
				TrustedKeys = LoadKeys(),
				GroupName = Settings.Default.HeaderGroup,
				CheckSignatures = Settings.Default.CheckSignatures
			};
			if (bIncludePosition)
			{
				nntpSettings.Position = Sys.MainWindow.SpotProvider.GetIdPosition("spots");
				if (nntpSettings.Position.First > 0)
				{
					Spot spot = new Spot
					{
						Article = nntpSettings.Position.First
					};
					if (spot.GetSpotStampFromDb())
					{
						nntpSettings.Position.FirstDateTime = spot.Stamp.FromUnixTime();
					}
				}
			}
			return nntpSettings;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			throw new Exception("HeaderSettings: " + ex.Message);
		}
	}

	internal static bool IsSearchQuery(string sQuery)
	{
		return sQuery.ToLower().Contains(" match ");
	}

	internal static string[] LoadKeys()
	{
		if (_keysLoaded)
		{
			return Keys;
		}
		string text = System.IO.Path.Combine(SettingsFolder, "keys.xml");
		try
		{
			if (!Settings.Default.KeysURL.IsNullOrEmpty())
			{
				UpdateKeysFileFromTheNet(AddHttp(Settings.Default.KeysURL), text);
			}
			if (!System.IO.File.Exists(text))
			{
				CreateKeys();
			}
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.XmlResolver = null;
			xmlDocument.Load(text);
			XmlElement documentElement = xmlDocument.DocumentElement;
			if (documentElement != null)
			{
				foreach (XmlElement item in documentElement)
				{
					string attribute = item.GetAttribute("ID");
					double num = Convert.ToDouble(attribute);
					if (attribute != string.Empty && num >= 2.0 && num <= 8.0)
					{
						Keys[(int)num] = (item.InnerXml.ToLower().Contains("rsakeyvalue") ? item.ChildNodes[0].ChildNodes[0].InnerText : item.InnerText);
					}
				}
			}
			_keysLoaded = true;
			return Keys;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			throw new Exception("Load_Keys: " + ex.Message + ". File: " + text);
		}
	}

	internal static string MakeMd5(string baseString)
	{
		if (baseString.IsNullOrEmpty())
		{
			return "6e235dd829ee3807d903ea0fc830b160";
		}
		try
		{
			using MD5 mD = MD5.Create();
			return BitConverter.ToString(mD.ComputeHash(MakeLatin(baseString))).Replace("-", "");
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return "3d77c3bf94a0a851ccf01932af6eab31";
		}
	}

	internal static string MakeMd5(byte[] bytes)
	{
		return BitConverter.ToString(bytes).Replace("-", "").ToLower();
	}

	internal static string MakeMd5(byte[] bytes, int startIndex, int length)
	{
		return BitConverter.ToString(bytes, startIndex, length).Replace("-", "").ToLower();
	}

	internal static string MakeUnique(string modulus)
	{
		if (modulus.IsNullOrEmpty() || modulus.Equals("none"))
		{
			return Words.Unknown;
		}
		try
		{
			return StripNonAlphaNumericCharacters(Convert.ToBase64String(BitConverter.GetBytes(GetCrc().Calculate(Convert.FromBase64String(modulus)))));
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return Words.Unknown;
		}
	}

	internal static void ShowInfoMsg(string sMsg, string sTitle)
	{
		if (Thread.CurrentThread != System.Windows.Application.Current.Dispatcher.Thread)
		{
			System.Windows.Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(Dialog));
		}
		else
		{
			Dialog();
		}
		void Dialog()
		{
			Interaction.MsgBox(sMsg, MsgBoxStyle.Information, sTitle);
		}
	}

	internal static MsgBoxResult AskYesNo(string msg, string title)
	{
		if (Thread.CurrentThread != System.Windows.Application.Current.Dispatcher.Thread)
		{
			return (MsgBoxResult)System.Windows.Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Func<MsgBoxResult>(Dialog));
		}
		return Dialog();
		MsgBoxResult Dialog()
		{
			return Interaction.MsgBox(msg, MsgBoxStyle.YesNo, title);
		}
	}

	internal static string GetSpotThemeFile()
	{
		string text = SettingsFolder + "\\TabThemes\\" + Settings.Default.ActiveTheme + "\\spot.htm";
		if (System.IO.File.Exists(text))
		{
			return text;
		}
		Log.Error("Theme file not found: " + text + ". Try to use tmp files.");
		text = System.IO.Path.Combine(GetTempPath(), "Default\\spot.htm");
		if (!System.IO.File.Exists(text))
		{
			string tempFileName = GetTempFileName("zip", "Default.theme");
			try
			{
				System.IO.File.WriteAllBytes(tempFileName, Resources.Default_theme);
				SafeZip.ExtractAll(tempFileName, GetTempPath(), overwrite: true);
			}
			catch (IOException ex)
			{
				Log.Exception(ex);
			}
		}
		if (!System.IO.File.Exists(text))
		{
			throw new Exception("Failed to restore theme file");
		}
		return text;
	}

	internal static bool ValidList(string sXml)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument
			{
				XmlResolver = null
			};
			xmlDocument.LoadXml(sXml);
			if (xmlDocument.DocumentElement == null || !xmlDocument.DocumentElement.Name.EqualsIgnoreCase("keys"))
			{
				throw new Exception("XML Error");
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return false;
	}

	public static string AddHttp(string text)
	{
		if (!text.IsNullOrEmpty() && !HasHttp(text))
		{
			return "http://" + text;
		}
		return text;
	}

	public static string AppPath()
	{
		if (!_didOnce)
		{
			try
			{
				_appPath = System.IO.Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
				_didOnce = true;
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}
		return _appPath;
	}

	public static string ConvertSize(long size, bool isItBits = false)
	{
		if (size < 1024)
		{
			double num = ConvertBytes(size, ConvTo.B);
			if (Math.Abs(num - -1.0) < Epsilon)
			{
				return null;
			}
			return Math.Round(num) + (isItBits ? " bits" : " bytes");
		}
		if (size >= 1024 && size < 1048576)
		{
			double num2 = ConvertBytes(size, ConvTo.KB);
			if (Math.Abs(num2 - -1.0) < Epsilon)
			{
				return null;
			}
			return Math.Round(num2) + (isItBits ? " Kb" : " KB");
		}
		if (size >= 1048576 && size < 1073741824)
		{
			double num3 = ConvertBytes(size, ConvTo.MB);
			if (Math.Abs(num3 - -1.0) < Epsilon)
			{
				return null;
			}
			return Math.Round(num3, 1) + (isItBits ? " Mb" : " MB");
		}
		if (size >= 1073741824 && size < 1099511627776L)
		{
			double num4 = ConvertBytes(size, ConvTo.GB);
			if (Math.Abs(num4 - -1.0) < Epsilon)
			{
				return null;
			}
			return Math.Round(num4, 1) + (isItBits ? " Gb" : " GB");
		}
		if (!(size >= 1099511627776L && size < 1125899906842624L))
		{
			return "";
		}
		double num5 = ConvertBytes(size, ConvTo.TB);
		if (Math.Abs(num5 - -1.0) < Epsilon)
		{
			return null;
		}
		return Math.Round(num5, 1) + (isItBits ? " Tb" : " TB");
	}

	public static int ConvertToTimestamp(DateTime value)
	{
		return checked((int)Math.Round((value - new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime()).TotalSeconds));
	}

	public static bool CreateKeys(bool force = false)
	{
		string[] array = new string[11];
		string path = System.IO.Path.Combine(SettingsFolder, "keys.xml");
		try
		{
			if (!System.IO.File.Exists(path) || force)
			{
				array[2] = "ys8WSlqonQMWT8ubG0tAA2Q07P36E+CJmb875wSR1XH7IFhEi0CCwlUzNqBFhC+P";
				array[3] = "uiyChPV23eguLAJNttC/o0nAsxXgdjtvUvidV2JL+hjNzc4Tc/PPo2JdYvsqUsat";
				array[4] = "1k6RNDVD6yBYWR6kHmwzmSud7JkNV4SMigBrs+jFgOK5Ldzwl17mKXJhl+su/GR9";
				StreamWriter streamWriter = new StreamWriter(path, append: false, Encoding.UTF8);
				streamWriter.WriteLine("<Keys>");
				int num = 0;
				do
				{
					if (!array[num].IsNullOrEmpty())
					{
						streamWriter.WriteLine("\t<Key ID='" + num.ToStringSafely() + "'>" + array[num] + "</Key>");
					}
					num++;
				}
				while (num <= 9);
				streamWriter.WriteLine("</Keys>");
				streamWriter.Close();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
		return true;
	}

	public static void Error(string sMsg)
	{
		TranslationsForError(ref sMsg);
		Error(sMsg, Words.Oops);
	}

	private static void TranslationsForError(ref string sMsg)
	{
		if (sMsg.Contains("database is locked"))
		{
			sMsg = sMsg.Replace("database is locked", Words.DbLockTimeout);
		}
	}

	public static void Error(string sMsg, string sCaption)
	{
		if (Sys.IsShutdownRequested)
		{
			return;
		}
		try
		{
			if (Thread.CurrentThread != System.Windows.Application.Current.Dispatcher.Thread)
			{
				Sys.MainWindow.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(Dialog));
			}
			else
			{
				Dialog();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		void Dialog()
		{
			App.CloseSplash();
			Interaction.MsgBox(sMsg, MsgBoxStyle.Critical, sCaption);
		}
	}

	public static string FindNzb(string sName, string sNewsGroup, string sTitle)
	{
		string text = Spots.FindNzb(sName, 600, sNewsGroup, 1400, allowIncomplete: true, extraChecks: false);
		if (text.IsNullOrEmpty())
		{
			ShowPopupMessage(Words.NZBCannotBeFound);
			return null;
		}
		string text2 = GenerateNzbFilePath(MakeFilename(sTitle).Trim() + ".nzb");
		try
		{
			StreamWriter streamWriter = new StreamWriter(text2, append: false, LatinEnc());
			streamWriter.Write(text);
			streamWriter.Close();
			return text2;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			Error(Words.ErrorOnWriting);
			return null;
		}
	}

	public static byte GetBaseChar(byte lIndex)
	{
		if ((uint)lIndex <= 25u)
		{
			return checked((byte)(65 + lIndex));
		}
		if ((uint)lIndex >= 26u && (uint)lIndex <= 51u)
		{
			return checked((byte)(97 + (lIndex - 26)));
		}
		if ((uint)lIndex >= 52u && (uint)lIndex <= 62u)
		{
			return checked((byte)(48 + (lIndex - 52)));
		}
		return 65;
	}

	public static IdPosition GetIdPosition(ISqlDb db, string sTable)
	{
		try
		{
			using ISqlDbTransaction transaction = db.BeginReadTransaction();
			return new IdPosition
			{
				First = db.ExecuteScalar("SELECT MIN(rowid) FROM " + sTable, transaction),
				Last = db.ExecuteScalar("SELECT MAX(rowid) FROM " + sTable, transaction)
			};
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			throw new Exception("GetIdPosition: " + ex.Message);
		}
	}

	public static string GetLatin(byte[] zText)
	{
		return LatinEnc().GetString(zText);
	}

	public static bool HasHttp(string text)
	{
		if (text.IsNullOrEmpty())
		{
			return false;
		}
		return text.Trim().ToLower().StartsWith("http");
	}

	public static DateTime GetBuildDateTime()
	{
		try
		{
			return DateTime.ParseExact(Resources.BuildDate.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture);
		}
		catch (Exception)
		{
			try
			{
				return DateTime.ParseExact(Resources.BuildDate.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
			}
			catch (Exception ex)
			{
				Log.Exception(ex, showToClient: true);
				return DateTime.MinValue;
			}
		}
	}

	public static string HtmlDecode(string text)
	{
		if (!text.IsNullOrEmpty())
		{
			return WebUtility.HtmlDecode(text.Replace("&amp;", "&")).Replace("\n", "").Replace("\r", "")
				.Replace("\t", "");
		}
		return "";
	}

	public static string HtmlEncode(string text)
	{
		if (text.IsNullOrEmpty())
		{
			return "";
		}
		char[] array = WebUtility.HtmlEncode(HtmlDecode(text)).ToCharArray();
		StringBuilder stringBuilder = new StringBuilder(checked(array.Length * 2));
		char[] array2 = array;
		foreach (char value in array2)
		{
			int num = Convert.ToInt32(value);
			if (num > 31 && num < 127 && num != 96)
			{
				stringBuilder.Append(value);
				continue;
			}
			stringBuilder.Append("&#");
			stringBuilder.Append(num);
			stringBuilder.Append(";");
		}
		return stringBuilder.ToStringSafely();
	}

	public static Encoding LatinEnc()
	{
		return Encoding.GetEncoding(28591);
	}

	/// <summary>The Windows ANSI code page, for data that was written with it.</summary>
	/// <remarks>
	/// Named explicitly because Encoding.Default no longer means this. On .NET Framework
	/// it was the system ANSI code page; from .NET Core onward it is UTF-8, which decodes
	/// the same bytes into different text. Every place that relied on the old meaning now
	/// says so. EncodingSetup registers the provider this needs.
	/// </remarks>
	public static Encoding AnsiEnc()
	{
		return Encoding.GetEncoding(1252);
	}

	public static void LaunchInExternalProgram(string sUrl)
	{
		try
		{
			Process.Start(sUrl);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			Error("LaunchInExternalProgram:: " + Information.Err().Description);
		}
	}

	public static DoubleAnimationUsingKeyFrames DoubleAnimation(double to, TimeSpan time)
	{
		return new DoubleAnimationUsingKeyFrames
		{
			Duration = new Duration(time),
			KeyFrames = new DoubleKeyFrameCollection
			{
				new EasingDoubleKeyFrame(to, KeyTime.FromPercent(1.0), new PowerEase
				{
					EasingMode = EasingMode.EaseInOut
				})
			}
		};
	}

	public static string MakeFilename(string sIn)
	{
		return sIn.Replace("\"", "").Replace("*", "").Replace(":", "")
			.Replace("<", "")
			.Replace(">", "")
			.Replace("?", "")
			.Replace("\\", "")
			.Replace("/", "")
			.Replace("|", "")
			.Replace("%", "")
			.Replace("[", "")
			.Replace("]", "")
			.Replace(";", "")
			.Replace("=", "")
			.Replace(",", "");
	}

	public static byte[] MakeLatin(string zText)
	{
		return LatinEnc().GetBytes(zText);
	}

	public static byte[] MakeUtf8(string zText)
	{
		return Encoding.UTF8.GetBytes(zText);
	}

	public static string ExtCatToString(int extCat)
	{
		string text = extCat.ToString(CultureInfo.InvariantCulture);
		byte b = Convert.ToByte(text.Substring(0, 1));
		byte b2 = Convert.ToByte(text.Substring(1));
		if (b2 > 98)
		{
			return "";
		}
		return TranslateCat((SpotCategory)b, (b switch
		{
			3 => 'c', 
			4 => 'b', 
			_ => 'd', 
		}).ToString(CultureInfo.InvariantCulture) + b2.ToString(CultureInfo.InvariantCulture));
	}

	public static string SafeHref(string text)
	{
		return AddHttp(text).Replace("\"", "%22").Replace("`", "%60").Replace("'", "%27");
	}

	public static string SafeName(string text)
	{
		string text2 = "";
		bool flag = false;
		text = text.Trim();
		if (Versioned.IsNumeric(text.Replace(".", "").Replace(":", "")))
		{
			flag = true;
		}
		for (int num = text.Length; num > 0; num--)
		{
			int num2 = Strings.Asc(Strings.Mid(text, num, 1));
			int num3 = num2;
			if (num3 >= 48 && num3 <= 57)
			{
				if (flag)
				{
					text2 = Strings.Chr(num2).ToStringSafely() + text2;
				}
			}
			else if ((num3 >= 65 && num3 <= 90) || (num3 >= 97 && num3 <= 122))
			{
				text2 = Strings.Chr(num2).ToStringSafely() + text2;
			}
			else if (num3 >= 97 && num3 <= 122)
			{
				text2 = Strings.Chr(checked(num2 - 32)).ToStringSafely() + text2;
			}
			else if (num3 == 46)
			{
				text2 = Strings.Chr(num2).ToStringSafely() + text2;
			}
		}
		return text2;
	}

	public static string StripNonAlphaNumericCharacters(string sText)
	{
		if (!sText.IsNullOrEmpty())
		{
			return Regex.Replace(sText, "[^A-Za-z0-9]", "").Trim();
		}
		return "";
	}

	public static string TranslateCat(SpotCategory cat, string subCat, bool strict = false)
	{
		if (subCat.Length < 2)
		{
			return "";
		}
		int num = Convert.ToInt32(subCat.Substring(1));
		switch (cat)
		{
		case SpotCategory.Music:
			switch (subCat.Substring(0, 1).ToLower())
			{
			case "a":
			{
				List<string> list2 = new List<string> { "MP3", "WMA", "WAV", "OGG", "EAC", "DTS", "AAC", "APE", "FLAC" };
				if (num >= list2.Count)
				{
					return "";
				}
				return list2[num];
			}
			case "b":
			{
				if (num == 2 && strict)
				{
					return "";
				}
				List<string> list = new List<string>
				{
					"CD",
					Categories.TRadio,
					Categories.MCompilation,
					"DVD",
					"",
					Categories.TVinyl,
					Categories.TStream
				};
				if (num >= list.Count)
				{
					return "";
				}
				return list[num];
			}
			case "c":
				return num switch
				{
					0 => Categories.BitrateVariable, 
					1 => "< 96kbit", 
					2 => "96kbit", 
					3 => "128kbit", 
					4 => "160kbit", 
					5 => "192kbit", 
					6 => "256kbit", 
					7 => "320kbit", 
					8 => Categories.BitrateLossless, 
					9 => "", 
					_ => "", 
				};
			case "d":
				switch (num)
				{
				case 0:
					return Categories.MBlues;
				case 1:
					return Categories.MCompilation;
				case 2:
					return Categories.MCabaret;
				case 3:
					return Categories.MDance;
				case 4:
					return Categories.MVarious;
				case 5:
					return Categories.MHardstyle;
				case 6:
					return Categories.MWorld;
				case 7:
					return Categories.MJazz;
				case 8:
					return Categories.MYouth;
				case 9:
					return Categories.MClassical;
				case 10:
					if (!strict)
					{
						return Categories.MSmallArt;
					}
					return "";
				case 11:
					return Categories.MHollands;
				case 12:
					if (!strict)
					{
						return Categories.MNewAge;
					}
					return "";
				case 13:
					return Categories.MPop;
				case 14:
					return Categories.MRnB;
				case 15:
					return Categories.MHiphop;
				case 16:
					return Categories.MReggae;
				case 17:
					return Categories.MReligious;
				case 18:
					return Categories.MRock;
				case 19:
					return Categories.MSoundtrack;
				case 20:
					return "";
				case 21:
					if (!strict)
					{
						return Categories.MHardstyle;
					}
					return "";
				case 22:
					if (!strict)
					{
						return Categories.MAsian;
					}
					return "";
				case 23:
					return Categories.MDisco;
				case 24:
					return Categories.MClassics;
				case 25:
					return Categories.MMetal;
				case 26:
					return Categories.MCountry;
				case 27:
					return Categories.MDubstep;
				case 28:
					if (!strict)
					{
						return Categories.MNederhop;
					}
					return "";
				case 29:
					return Categories.MDnB;
				case 30:
					return Categories.MElectro;
				case 31:
					return Categories.MFolk;
				case 32:
					return Categories.MSoul;
				case 33:
					return Categories.MTrance;
				case 34:
					return Categories.MBalkans;
				case 35:
					return Categories.MTechno;
				case 36:
					return Categories.MAmbient;
				case 37:
					return Categories.MLatin;
				case 38:
					return Categories.MLive;
				default:
					return "";
				}
			case "z":
				return num switch
				{
					0 => Categories.MGAlbum, 
					1 => Categories.MGLiveset, 
					2 => Categories.MGPodcast, 
					3 => Categories.MGAudiobook, 
					_ => "", 
				};
			default:
				return "";
			}
		case SpotCategory.Game:
			if (subCat.Substring(0, 1).ToLower().Equals("a"))
			{
				return num switch
				{
					0 => "Windows", 
					1 => "Macintosh", 
					2 => "Linux", 
					3 => "Playstation", 
					4 => "Playstation 2", 
					5 => "PSP", 
					6 => "XBox", 
					7 => "XBox 360", 
					8 => "Gameboy Advance", 
					9 => "Gamecube", 
					10 => "Nintendo DS", 
					11 => "Nintendo Wii", 
					12 => "Playstation 3", 
					13 => "Windows Phone", 
					14 => "iOs", 
					15 => "Android", 
					16 => "Nintendo 3DS", 
					_ => "", 
				};
			}
			if (subCat.Substring(0, 1).ToLower().Equals("b"))
			{
				switch (num)
				{
				case 0:
					if (!strict)
					{
						return "ISO";
					}
					return "";
				case 1:
					return "Rip";
				case 2:
					return "Retail";
				case 3:
					return "DLC";
				case 4:
					return "";
				case 5:
					return "Patch";
				case 6:
					return "Crack";
				default:
					return "";
				}
			}
			if (!subCat.Substring(0, 1).ToLower().Equals("c"))
			{
				return "";
			}
			return num switch
			{
				0 => Categories.GAction, 
				1 => Categories.GAdventure, 
				2 => Categories.GStrategy, 
				3 => Categories.GRoleplay, 
				4 => Categories.GSimulation, 
				5 => Categories.GRace, 
				6 => Categories.GFly, 
				7 => Categories.GShooter, 
				8 => Categories.GPlatform, 
				9 => Categories.GSport, 
				10 => Categories.GYouth, 
				11 => Categories.GPuzzel, 
				12 => "", 
				13 => Categories.GBoardGame, 
				14 => Categories.GCards, 
				15 => Categories.GEducational, 
				16 => Categories.GMusic, 
				17 => Categories.GParty, 
				_ => "", 
			};
		case SpotCategory.Software:
		{
			string a2 = subCat.Substring(0, 1);
			if (string.Equals(a2, "a", StringComparison.OrdinalIgnoreCase))
			{
				return num switch
				{
					0 => "Windows", 
					1 => "Macintosh", 
					2 => "Linux", 
					3 => "OS/2", 
					4 => "Windows Phone", 
					5 => "Navi", 
					6 => "iOs", 
					7 => "Android", 
					_ => "", 
				};
			}
			if (!string.Equals(a2, "b", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			switch (num)
			{
			case 0:
				return Categories.SwAudio;
			case 1:
				return Categories.SwVideo;
			case 2:
				return Categories.SwGraphic;
			case 3:
				if (!strict)
				{
					return "CD/DVD Tools";
				}
				return "";
			case 4:
				if (!strict)
				{
					return "Media Players";
				}
				return "";
			case 5:
				if (!strict)
				{
					return "Rippers & Encoders";
				}
				return "";
			case 6:
				if (!strict)
				{
					return "Plugins";
				}
				return "";
			case 7:
				if (!strict)
				{
					return "Database Tools";
				}
				return "";
			case 8:
				if (!strict)
				{
					return "Email Software";
				}
				return "";
			case 9:
				return Categories.SwPhoto;
			case 10:
				if (!strict)
				{
					return "Screensavers";
				}
				return "";
			case 11:
				if (!strict)
				{
					return "Skin Software";
				}
				return "";
			case 12:
				if (!strict)
				{
					return "Drivers";
				}
				return "";
			case 13:
				if (!strict)
				{
					return "Browsers";
				}
				return "";
			case 14:
				if (!strict)
				{
					return "Download Managers";
				}
				return "";
			case 15:
				return Categories.SwDownload;
			case 16:
				if (!strict)
				{
					return "Usenet Software";
				}
				return "";
			case 17:
				if (!strict)
				{
					return "RSS Readers";
				}
				return "";
			case 18:
				if (!strict)
				{
					return "FTP Software";
				}
				return "";
			case 19:
				if (!strict)
				{
					return "Firewalls";
				}
				return "";
			case 20:
				if (!strict)
				{
					return "Antivirus Software";
				}
				return "";
			case 21:
				if (!strict)
				{
					return "Antispyware Software";
				}
				return "";
			case 22:
				if (!strict)
				{
					return "Optimization Software";
				}
				return "";
			case 23:
				return Categories.SwSafeguard;
			case 24:
				return Categories.SwSystem;
			case 25:
				return "";
			case 26:
				return Categories.SwEducational;
			case 27:
				return Categories.SwOffice;
			case 28:
				return Categories.SwInternet;
			case 29:
				return Categories.SwCommunication;
			case 30:
				return Categories.SwDevelopment;
			case 31:
				return Categories.SwSpotnet;
			default:
				return "";
			}
		}
		default:
		{
			string a = subCat.Substring(0, 1);
			if (string.Equals(a, "a", StringComparison.OrdinalIgnoreCase))
			{
				switch (num)
				{
				case 0:
					return "DivX";
				case 1:
					return "WMV";
				case 2:
					return "MPG";
				case 3:
					return "DVD5";
				case 4:
					if (!strict)
					{
						return "HD " + Words.Remaining;
					}
					return "";
				case 5:
					return "ePub";
				case 6:
					return "Bluray";
				case 7:
					if (!strict)
					{
						return "HD-DVD";
					}
					return "";
				case 8:
					if (!strict)
					{
						return "WMV HD";
					}
					return "";
				case 9:
					return "x264";
				case 10:
					return "DVD9";
				case 11:
					return "PDF";
				case 12:
					return "Bitmap";
				case 13:
					return "Vector";
				default:
					return "";
				}
			}
			if (string.Equals(a, "b", StringComparison.OrdinalIgnoreCase))
			{
				switch (num)
				{
				case 0:
					return "Cam";
				case 1:
					if (!strict)
					{
						return "(S)VCD";
					}
					return "";
				case 2:
					if (!strict)
					{
						return "Promo";
					}
					return "";
				case 3:
					return "Retail";
				case 4:
					if (!strict)
					{
						return "TV";
					}
					return "";
				case 5:
					return "";
				case 6:
					if (!strict)
					{
						return "Satellite";
					}
					return "";
				case 7:
					return "R5";
				case 8:
					if (!strict)
					{
						return "Telecine";
					}
					return "";
				case 9:
					return "Telesync";
				case 10:
					return "Scan";
				default:
					return "";
				}
			}
			if (string.Equals(a, "c", StringComparison.OrdinalIgnoreCase))
			{
				switch (num)
				{
				case 0:
					return Categories.LangNoSubtitles;
				case 1:
					return Categories.LangNlSubsExt;
				case 2:
					if (cat == SpotCategory.Movies)
					{
						return Categories.LangNlWritten;
					}
					return Categories.LangNlSubsInt;
				case 3:
					return Categories.LangEnSubsExt;
				case 4:
					if (cat == SpotCategory.Movies)
					{
						return Categories.LangEnWritten;
					}
					return Categories.LangEnSubsInt;
				case 5:
					return "";
				case 6:
					return Categories.LangNlSubsAdj;
				case 7:
					return Categories.LangEnSubsAdj;
				case 10:
					return Categories.LangEnSpoken;
				case 11:
					return Categories.LangNlSpoken;
				case 12:
					if (cat == SpotCategory.Movies)
					{
						return Categories.LangGrWritten;
					}
					return Categories.LangGrSpoken;
				case 13:
					if (cat == SpotCategory.Movies)
					{
						return Categories.LangFrWritten;
					}
					return Categories.LangFrSpoken;
				case 14:
					if (cat == SpotCategory.Movies)
					{
						return Categories.LangSpWritten;
					}
					return Categories.LangSpSpoken;
				default:
					return "";
				}
			}
			if (string.Equals(a, "d", StringComparison.OrdinalIgnoreCase))
			{
				switch (num)
				{
				case 0:
					return Categories.GAction;
				case 1:
					return Categories.GAdventure;
				case 2:
					return Categories.GAnimation;
				case 3:
					return Categories.GCabare;
				case 4:
					return Categories.GComedy;
				case 5:
					return Categories.GCrime;
				case 6:
					return Categories.GDocumentary;
				case 7:
					return Categories.GDrama;
				case 8:
					return Categories.GFamily;
				case 9:
					return Categories.GFantasy;
				case 10:
					return Categories.GFilmHouse;
				case 11:
					if (!strict)
					{
						return Categories.GTelevision;
					}
					return "";
				case 12:
					return Categories.GHorror;
				case 13:
					return Categories.GMusic;
				case 14:
					return Categories.GMusical;
				case 15:
					return Categories.GMystery;
				case 16:
					return Categories.GRomantic;
				case 17:
					return Categories.GSciFiction;
				case 18:
					return Categories.GSport;
				case 19:
					return Categories.GShort;
				case 20:
					return Categories.GThriller;
				case 21:
					return Categories.GWar;
				case 22:
					return Categories.GWestern;
				case 23:
					return Categories.SexHetero;
				case 24:
					return Categories.SexHomo;
				case 25:
					return Categories.SexLesbo;
				case 26:
					return Categories.SexBi;
				case 27:
					return "";
				case 28:
					return Categories.GAsian;
				case 29:
					return Categories.GAnime;
				case 30:
					return Categories.BGCover;
				case 31:
					return Categories.BGComicStrip;
				case 32:
					return Categories.GCartoon;
				case 33:
					return Categories.GYouth;
				case 34:
					return Categories.BGBusiness;
				case 35:
					return Categories.BGComputer;
				case 36:
					return Categories.BGHobby;
				case 37:
					return Categories.BGCooking;
				case 38:
					return Categories.BGCrafts;
				case 39:
					return Categories.BGHandicraft;
				case 40:
					return Categories.BGHealth;
				case 41:
					return Categories.GHistory;
				case 42:
					return Categories.BGPsychology;
				case 43:
					return Categories.BGNewspaper;
				case 44:
					return Categories.BGJournal;
				case 45:
					return Categories.BGScience;
				case 46:
					return Categories.GWoman;
				case 47:
					return Categories.BGReligion;
				case 48:
					return Categories.BGNovel;
				case 49:
					return Categories.BGBiography;
				case 50:
					return Categories.GDetective;
				case 51:
					return Categories.GAnimals;
				case 52:
					return "";
				case 53:
					return Categories.BGTravel;
				case 54:
					return Categories.GWhatHappened;
				case 55:
					return Categories.BGNonFiction;
				case 57:
					return Categories.BGPoetry;
				case 58:
					return Categories.BGFairytale;
				case 72:
					if (!strict)
					{
						return Categories.SexBi;
					}
					return "";
				case 73:
					if (!strict)
					{
						return Categories.SexLesbo;
					}
					return "";
				case 74:
					if (!strict)
					{
						return Categories.SexHomo;
					}
					return "";
				case 75:
					if (!strict)
					{
						return Categories.SexHetero;
					}
					return "";
				case 76:
					return Categories.SexAmateur;
				case 77:
					return Categories.SexGroup;
				case 78:
					return Categories.SexPOV;
				case 79:
					return Categories.SexSolo;
				case 80:
					return Categories.SexYoung;
				case 81:
					return Categories.SexSoft;
				case 82:
					return Categories.SexFetich;
				case 83:
					return Categories.SexOld;
				case 84:
					return Categories.SexBBW;
				case 85:
					return Categories.SexSM;
				case 86:
					return Categories.SexHard;
				case 87:
					return Categories.SexDark;
				case 88:
					return Categories.SexHentai;
				case 89:
					return Categories.SexOutside;
				default:
					return "";
				}
			}
			if (!string.Equals(a, "z", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return num switch
			{
				0 => Categories.CatFilm, 
				1 => Categories.CatSerie, 
				2 => Categories.CatBook, 
				3 => Categories.CatErotica, 
				4 => Categories.CatImages, 
				_ => "", 
			};
		}
		}
	}

	public static string TranslateCatDesc(SpotCategory hCat, string sCat)
	{
		switch (hCat)
		{
		case SpotCategory.Music:
		{
			string a2 = sCat.Substring(0, 1);
			if (string.Equals(a2, "a", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnFormat;
			}
			if (string.Equals(a2, "b", StringComparison.OrdinalIgnoreCase))
			{
				return Categories.Source;
			}
			if (string.Equals(a2, "c", StringComparison.OrdinalIgnoreCase))
			{
				return Categories.Bitrate;
			}
			if (string.Equals(a2, "d", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnGenre;
			}
			if (!string.Equals(a2, "z", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return Categories.Category;
		}
		case SpotCategory.Game:
		{
			string a4 = sCat.Substring(0, 1);
			if (string.Equals(a4, "a", StringComparison.OrdinalIgnoreCase))
			{
				return Categories.Platform;
			}
			if (string.Equals(a4, "b", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnFormat;
			}
			if (string.Equals(a4, "c", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnGenre;
			}
			if (!string.Equals(a4, "z", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return Categories.Category;
		}
		case SpotCategory.Software:
		{
			string a3 = sCat.Substring(0, 1);
			if (string.Equals(a3, "a", StringComparison.OrdinalIgnoreCase))
			{
				return Categories.Platform;
			}
			if (string.Equals(a3, "b", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnGenre;
			}
			if (!string.Equals(a3, "z", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return Categories.Category;
		}
		default:
		{
			string a = sCat.Substring(0, 1);
			if (string.Equals(a, "a", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnFormat;
			}
			if (string.Equals(a, "b", StringComparison.OrdinalIgnoreCase))
			{
				return Categories.Source;
			}
			if (string.Equals(a, "c", StringComparison.OrdinalIgnoreCase))
			{
				return Categories.Language;
			}
			if (string.Equals(a, "d", StringComparison.OrdinalIgnoreCase))
			{
				return Words.ColumnGenre;
			}
			if (!string.Equals(a, "z", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			return Categories.Category;
		}
		}
	}

	public static string TranslateCatShort(int hCat, int sCat)
	{
		return hCat switch
		{
			2 => sCat switch
			{
				0 => "MP3", 
				1 => "WMA", 
				2 => "WAV", 
				3 => "OGG", 
				4 => "EAC", 
				5 => "DTS", 
				6 => "AAC", 
				7 => "APE", 
				8 => "FLAC", 
				_ => "", 
			}, 
			3 => sCat switch
			{
				0 => "Win", 
				1 => "Mac", 
				2 => "Linux", 
				3 => "PSX", 
				4 => "PS2", 
				5 => "PSP", 
				6 => "XBox", 
				7 => "360", 
				8 => "GBA", 
				9 => "GC", 
				10 => "NDS", 
				11 => "Wii", 
				12 => "PS3", 
				13 => "WP7", 
				14 => "iOs", 
				15 => "Android", 
				_ => "", 
			}, 
			4 => sCat switch
			{
				0 => "Win", 
				1 => "Mac", 
				2 => "Linux", 
				3 => "OS2", 
				4 => "WP7", 
				5 => "Navi", 
				6 => "iOs", 
				7 => "Android", 
				_ => "", 
			}, 
			_ => sCat switch
			{
				0 => "DivX", 
				1 => "WMV", 
				2 => "MPG", 
				3 => "DVD5", 
				4 => "HD", 
				5 => "ePub", 
				6 => "Bluray", 
				7 => "HD", 
				8 => "HD", 
				9 => "x264", 
				10 => "DVD9", 
				11 => "PDF", 
				12 => "Bitmap", 
				13 => "Vector", 
				_ => "", 
			}, 
		};
	}

	public static string TranslateColToId(string zS)
	{
		return new Dictionary<string, string>
		{
			{
				"rowid",
				Words.ColumnAge
			},
			{
				"date",
				Words.ColumnDate
			},
			{
				"subject",
				Words.ColumnSubject
			},
			{
				"subcat",
				Words.ColumnFormat
			},
			{
				"extcat",
				Words.ColumnGenre
			},
			{
				"sender",
				Words.ColumnSender
			},
			{
				"tag",
				Words.ColumnTag
			},
			{
				"filesize",
				Words.ColumnSize
			},
			{
				"msgid",
				Words.ColumnMessageID
			}
		}.FirstOrDefault((KeyValuePair<string, string> x) => x.Value.EqualsIgnoreCase(zS)).Key ?? "rowid";
	}

	public static string TranslateIdToCol(string zS)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			{
				"rowid",
				Words.ColumnAge
			},
			{
				"date",
				Words.ColumnDate
			},
			{
				"subject",
				Words.ColumnSubject
			},
			{
				"subcat",
				Words.ColumnFormat
			},
			{
				"extcat",
				Words.ColumnGenre
			},
			{
				"sender",
				Words.ColumnSender
			},
			{
				"tag",
				Words.ColumnTag
			},
			{
				"filesize",
				Words.ColumnSize
			},
			{
				"msgid",
				Words.ColumnMessageID
			}
		};
		string text = zS.ToLower();
		if (!dictionary.ContainsKey(text))
		{
			return text;
		}
		return dictionary[text];
	}

	public static byte TranslateInfo(int hCat, string sCats)
	{
		int num = -1;
		if (!_translateInfoLoaded)
		{
			for (int i = 0; i <= 100; i++)
			{
				string str = TranslateCat(SpotCategory.Video, "d" + i);
				TranslateInfoCat1[i] = !str.IsNullOrEmpty();
			}
			for (int j = 0; j <= 100; j++)
			{
				string str2 = TranslateCat(SpotCategory.Music, "d" + j);
				TranslateInfoCat2[j] = !str2.IsNullOrEmpty();
			}
			for (int k = 0; k <= 100; k++)
			{
				string str3 = TranslateCat(SpotCategory.Game, "c" + k);
				TranslateInfoCat3[k] = !str3.IsNullOrEmpty();
			}
			for (int l = 0; l <= 100; l++)
			{
				string str4 = TranslateCat(SpotCategory.Software, "b" + l);
				TranslateInfoCat4[l] = !str4.IsNullOrEmpty();
			}
			_translateInfoLoaded = true;
		}
		checked
		{
			try
			{
				char value = hCat switch
				{
					3 => 'c', 
					4 => 'b', 
					_ => 'd', 
				};
				while (true)
				{
					num = sCats.IndexOf(value, num + 1);
					if (num == -1)
					{
						break;
					}
					byte b = (byte)Math.Round(Conversion.Val(Regex.Match(sCats, ".{" + (num + 1) + "}(?<code>\\d+)").Groups["code"].Value));
					if (b > 100 || (hCat == 9 && unchecked((uint)(b - 23) <= 3u || (uint)(b - 72) <= 3u) && sCats.IndexOf(value, num + 1) > -1))
					{
						continue;
					}
					switch (hCat)
					{
					case 2:
						if (TranslateInfoCat2[b])
						{
							return b;
						}
						break;
					case 3:
						if (TranslateInfoCat3[b])
						{
							return b;
						}
						break;
					case 4:
						if (TranslateInfoCat4[b])
						{
							return b;
						}
						break;
					default:
						if (TranslateInfoCat1[b])
						{
							return b;
						}
						break;
					}
				}
				return 99;
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			return 99;
		}
	}

	public static string UrlEncode(string sDest)
	{
		for (int num = sDest.Length; num >= 1; num--)
		{
			int num2 = Strings.Asc(Strings.Mid(sDest, num, 1));
			if ((num2 < 48 || num2 > 57) && (num2 < 65 || num2 > 90) && (num2 < 97 || num2 > 122))
			{
				switch (num2)
				{
				case 32:
					StringType.MidStmtStr(ref sDest, num, 1, "+");
					break;
				default:
					sDest = Strings.Left(sDest, num - 1) + "%" + Conversion.Hex(num2) + Strings.Mid(sDest, num + 1);
					break;
				case 42:
				case 46:
				case 47:
				case 58:
				case 95:
					break;
				}
			}
		}
		return sDest;
	}

	public static void ShowPopupMessage(string message, bool inTheCenter = true, TimeSpan timeout = default(TimeSpan))
	{
		DispatcherHelper.UIDispatcher.BeginInvoke((Action)async delegate
		{
			PlacementMode placement = PlacementMode.Center;
			if (!inTheCenter)
			{
				placement = PlacementMode.Custom;
			}
			bool flag = timeout != default(TimeSpan);
			if (_autoHideablePopup != null)
			{
				_autoHideablePopup.IsOpen = false;
			}
			_autoHideablePopup = new Popup
			{
				PlacementTarget = Sys.MainWindow,
				Placement = placement,
				AllowsTransparency = true,
				Child = new AutoHideMessage(message),
				StaysOpen = flag
			};
			if (!inTheCenter)
			{
				Popup autoHideablePopup = _autoHideablePopup;
				autoHideablePopup.CustomPopupPlacementCallback = (CustomPopupPlacementCallback)Delegate.Combine(autoHideablePopup.CustomPopupPlacementCallback, (CustomPopupPlacementCallback)((Size popupSize, Size targetSize, Point offset) => new CustomPopupPlacement[1]
				{
					new CustomPopupPlacement
					{
						Point = new Point(targetSize.Width - popupSize.Width - 20.0, targetSize.Height - popupSize.Height - 25.0)
					}
				}));
			}
			_autoHideablePopup.IsOpen = true;
			if (flag)
			{
				Popup origPopup = _autoHideablePopup;
				await Task.Delay(timeout);
				origPopup.IsOpen = false;
			}
		}, DispatcherPriority.Background);
	}

	public static string FixDirectoryName(string path)
	{
		if (path.IsNullOrEmpty())
		{
			return path;
		}
		path = path.TrimEnd();
		while (path.EndsWith("."))
		{
			path = path.Substring(0, path.Length - 1);
		}
		return path.Trim();
	}

	public static void MoveFilesRecursively(string destDir, string targetDir, CancellationToken cToken, string[] dirsToIgnore = null)
	{
		destDir = FixDirectoryName(destDir);
		targetDir = FixDirectoryName(targetDir);
		EnsureDirectoryExist(destDir);
		EnsureDirectoryExist(targetDir);
		int num = 3;
		int num2 = 1;
		while (true)
		{
			try
			{
				string[] directories = System.IO.Directory.GetDirectories(destDir, "*", SearchOption.AllDirectories);
				foreach (string dirPath in directories)
				{
					if (dirsToIgnore == null || !dirsToIgnore.Any((string dirToIgnore) => dirPath.Contains(dirToIgnore)))
					{
						cToken.ThrowIfCancellationRequested();
						EnsureDirectoryExist(targetDir + "\\" + dirPath.Substring(destDir.Length));
					}
				}
				directories = System.IO.Directory.GetFiles(destDir, "*", SearchOption.AllDirectories);
				foreach (string filePath in directories)
				{
					if (dirsToIgnore == null || !(from dirToIgnore in dirsToIgnore
						let directoryName = System.IO.Path.GetDirectoryName(filePath).Trim()
						where directoryName != null && directoryName.Contains(dirToIgnore)
						select dirToIgnore).Any())
					{
						cToken.ThrowIfCancellationRequested();
						string text = targetDir + "\\" + filePath.Substring(destDir.Length);
						if (System.IO.File.Exists(text))
						{
							System.IO.File.Delete(text);
						}
						if (System.IO.Directory.Exists(text))
						{
							System.IO.Directory.Delete(text, recursive: true);
						}
						try
						{
							System.IO.File.Move(filePath, text);
						}
						catch (IOException ex)
						{
							Log.Warn("Failed to move files, so try to copy, then remove. Error: " + ex.Message);
							System.IO.File.Copy(filePath, text, overwrite: true);
						}
					}
				}
				break;
			}
			catch (Exception ex2)
			{
				num2++;
				if (num2 <= num)
				{
					Log.Debug("Failed to move files because of error: " + ex2.Message + ". Try one more time in 10 sec.");
					Thread.Sleep(10000);
					continue;
				}
				throw;
			}
		}
	}

	public static bool DeleteDirectoryHard(string destinationDir)
	{
		if (!System.IO.Directory.Exists(destinationDir))
		{
			return true;
		}
		for (int i = 1; i <= 10; i++)
		{
			try
			{
				DeleteDirectory(destinationDir);
			}
			catch (Exception)
			{
			}
			if (!System.IO.Directory.Exists(destinationDir))
			{
				return true;
			}
			Thread.Sleep(200);
		}
		return false;
	}

	private static void DeleteDirectory(string path)
	{
		string[] directories = System.IO.Directory.GetDirectories(path);
		for (int i = 0; i < directories.Length; i++)
		{
			DeleteDirectory(directories[i]);
		}
		try
		{
			System.IO.Directory.Delete(path, recursive: true);
		}
		catch (DirectoryNotFoundException)
		{
		}
		catch (IOException)
		{
			System.IO.Directory.Delete(path, recursive: true);
		}
		catch (UnauthorizedAccessException)
		{
			System.IO.Directory.Delete(path, recursive: true);
		}
	}
}
