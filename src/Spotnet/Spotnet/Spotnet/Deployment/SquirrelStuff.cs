using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Permissions;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NLog;
using Splat;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;
using Spotnet.ViewModel;
using Squirrel;

namespace Spotnet.Deployment;

internal static class SquirrelStuff
{
	private static readonly Logger Log;

	private static int _isUpdateManagerDisposed;

	private static readonly UpdateManager UpdateManager;

	private static string _updateChannel;

	private static DateTime _lastUpdateCheckDateTime;

	private static readonly object LockUpdate;

	private static Version _lastVersion;

	private static Timer _newVersionCheckTimer;

	private static bool _httpNzbAlreadyChecked;

	internal static bool IsNewVersion { get; private set; }

	private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;

	internal static string UpdateChannel
	{
		get
		{
			if (_updateChannel.IsNullOrWhiteSpace())
			{
				string updateChannel = "release";
				try
				{
					string path = System.IO.Path.Combine(System.IO.Directory.GetParent(AppHelper.AppPath()).FullName, "Update.channel");
					if (System.IO.File.Exists(path))
					{
						string text = System.IO.File.ReadAllText(path).Trim().ToLower();
						switch (text)
						{
						case "alpha":
						case "beta":
							updateChannel = text;
							break;
						default:
							Log.Debug("Custom channel is wrong: " + text + ". Use release channel.");
							break;
						case "release":
							break;
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
				_updateChannel = updateChannel;
			}
			return _updateChannel;
		}
	}

	private static Version FakeVersion
	{
		get
		{
			Version appVersion = AppHelper.AppVersion;
			return new Version(1, appVersion.Minor, appVersion.Revision, appVersion.Build);
		}
	}

	public static Version LastVersion => _lastVersion ?? (_lastVersion = AppHelper.AppVersion);

	private static string DeploymentPackagesFolder
	{
		get
		{
			System.IO.DirectoryInfo parent = System.IO.Directory.GetParent(AppHelper.AppPath()).Parent;
			if (parent == null)
			{
				Log.Debug("Unknown application parent");
				return null;
			}
			string text = System.IO.Path.Combine(parent.FullName, "Spotnet\\packages.downloaded\\");
			AppHelper.EnsureDirectoryExist(text);
			return text;
		}
	}

	private static string PathToConfig => ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;

	static SquirrelStuff()
	{
		Log = LogManager.GetCurrentClassLogger();
		_isUpdateManagerDisposed = 1;
		_lastUpdateCheckDateTime = DateTime.Now - TimeSpan.FromDays(1.0);
		LockUpdate = new object();
		// Inno installations own their shortcuts/profile and must never enter the legacy update feed.
		if (InstalledProfile.Enabled) return;
		string deploymentPackagesFolder = DeploymentPackagesFolder;
		if (deploymentPackagesFolder == null)
		{
			throw new Exception("Unknown app parent path");
		}
		UpdateManager = new UpdateManager((UpdateChannel != "alpha") ? deploymentPackagesFolder : "D:\\ShuMisha.GoogleNet\\Work\\oDesk\\workwithjanton.SpotNet\\Scripts\\Squirrel\\Releases\\", "Spotnet", FrameworkVersion.Net45);
	}

	internal static void DisposeUpdateManager()
	{
		if (InstalledProfile.Enabled) return;
		WaitForCheckForUpdateLockAcquire();
		if (1 == Interlocked.Exchange(ref _isUpdateManagerDisposed, 0))
		{
			UpdateManager.Dispose();
		}
	}

	internal static void RestartApplication()
	{
		if (InstalledProfile.Enabled)
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
				"--restart-from=" + System.Diagnostics.Process.GetCurrentProcess().Id) { UseShellExecute = true });
			Sys.Shutdown();
			return;
		}
		UpdateManager.RestartApp();
	}

	private static void OnUpgradeSuccess(Version version)
	{
		Sys.StatsReporter.ReportOnSpotnetUpdatePerformedAsync(version, isSuccess: true);
		MainWindowVm.AddNewWarningOnce(Words.NewVersionIsReadyWarning);
	}

	private static UpdateInfoEx OnUpgradeFailed(Version version, string errorMsg, bool reportFailure, bool uploadLogs)
	{
		Log.Error(errorMsg + ", server " + AppHelper.ServersDb.OHeader.Server);
		if (reportFailure && version != null)
		{
			Sys.StatsReporter.ReportOnSpotnetUpdatePerformedAsync(version, isSuccess: false);
		}
		if (uploadLogs)
		{
			AppHelper.UploadFile(LogHelper.ZipLogFiles(), Spotnet.Properties.Configuration.UpgradeFailuresUploadUrl, null);
		}
		return new UpdateInfoEx(errorMsg);
	}

	private static void WaitForCheckForUpdateLockAcquire()
	{
		TimeSpan timeSpan = _lastUpdateCheckDateTime + TimeSpan.FromMilliseconds(2000.0) - DateTime.Now;
		if (timeSpan > TimeSpan.Zero)
		{
			Thread.Sleep(timeSpan);
		}
	}

	internal static void StartNewVersionCheckTimer()
	{
		if (InstalledProfile.Enabled) return;
		if (_newVersionCheckTimer == null)
		{
			_newVersionCheckTimer = new Timer(OnNewVersionCheckTimer, null, TimeSpan.Zero, TimeSpan.FromHours(2.0));
		}
	}

	internal static void StopNewVersionCheckTimer()
	{
		if (_newVersionCheckTimer != null)
		{
			_newVersionCheckTimer.Dispose();
			_newVersionCheckTimer = null;
		}
	}

	private static void OnNewVersionCheckTimer(object state)
	{
		ScheduleNewVersionCheck();
	}

	private static Task<UpdateInfoEx> ScheduleNewVersionCheck()
	{
		return Task.Run(delegate
		{
			lock (LockUpdate)
			{
				string text = null;
				try
				{
					if (!UpdateChannel.Equals("alpha"))
					{
						text = DownloadAndVerifyNzb(out var isCurrentVersionFound);
						if (text.IsNullOrEmpty())
						{
							if (isCurrentVersionFound)
							{
								Log.Debug("The last version is used");
								return (UpdateInfoEx)null;
							}
							return OnUpgradeFailed(null, "Failed to download update nzb", reportFailure: false, uploadLogs: false);
						}
						Version version = ExtractVersionFromNzbFileName(text);
						if (version == null)
						{
							return OnUpgradeFailed(FakeVersion, "Failed to extract version from nzb file", reportFailure: false, uploadLogs: true);
						}
						if (LastVersion >= version)
						{
							Log.Debug("The last version is used");
							return (UpdateInfoEx)null;
						}
						Log.Debug("New version {0} is available for {1} channel", version, UpdateChannel);
						string text2 = DownloadUpdateFileViaSpotnet(text);
						if (!text2.IsNullOrEmpty())
						{
							bool flag = !text2.StartsWith("Error on getting segment: Removed") && !text2.StartsWith("Error on getting segment: Cancelled") && !text2.EndsWith("Error: Removed") && !text2.Contains("502 Authentication Failed") && !text2.Contains("502 Too many connections");
							return OnUpgradeFailed(version, text2, flag, flag);
						}
						Log.Debug("New version downloaded");
					}
					return DeployLocalUpdate().Result;
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					return OnUpgradeFailed(null, "Failed to upgrade: " + ex.Message, reportFailure: false, uploadLogs: true);
				}
				finally
				{
					try
					{
						if (!text.IsNullOrEmpty())
						{
							System.IO.File.Delete(text);
						}
					}
					catch
					{
					}
				}
			}
		});
	}

	private static Version ExtractVersionFromNzbFileName(string nzbFilePath)
	{
		Regex regex = new Regex("^Spotnet\\.update\\.([0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+)");
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(nzbFilePath);
		if (fileNameWithoutExtension.IsNullOrWhiteSpace())
		{
			Log.Error("NZB file name is empty");
			return null;
		}
		Match match = regex.Match(fileNameWithoutExtension);
		if (match.Success)
		{
			return new Version(match.Groups[1].Value);
		}
		return null;
	}

	private static string DownloadUpdateFileViaSpotnet(string nzbFilePath)
	{
		if (!System.IO.File.Exists(nzbFilePath))
		{
			return "NZB file not found";
		}
		List<string> list;
		string errorMsg;
		try
		{
			using FileStream xml = System.IO.File.OpenRead(nzbFilePath);
			string pathToDownload = System.IO.Path.GetTempPath().Trim();
			list = SpotnetUpgradeNzb.Download(xml, pathToDownload, IsPublisherSpot, out errorMsg);
		}
		finally
		{
			try
			{
				if (System.IO.File.Exists(nzbFilePath))
				{
					System.IO.File.Delete(nzbFilePath);
				}
			}
			catch (Exception)
			{
			}
		}
		try
		{
			if (list == null || !errorMsg.IsNullOrEmpty())
			{
				return errorMsg;
			}
			if (list.Count != 1)
			{
				return "Number of update files received is not 1: " + list.Count;
			}
			string deploymentPackagesFolder = DeploymentPackagesFolder;
			if (deploymentPackagesFolder.IsNullOrEmpty())
			{
				return "Cannot get deployment folder";
			}
			try
			{
				System.IO.Directory.Delete(deploymentPackagesFolder, recursive: true);
				AppHelper.EnsureDirectoryExist(deploymentPackagesFolder);
			}
			catch (Exception ex2)
			{
				Log.Warn("Failed to refresh deployment dir: " + ex2.Message);
			}
			SafeZip.ExtractAll(list.First(), deploymentPackagesFolder, overwrite: true);
		}
		finally
		{
			try
			{
				if (list != null && list.Count > 0)
				{
					foreach (string item2 in list)
					{
						if (System.IO.File.Exists(item2))
						{
							System.IO.File.Delete(item2);
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}
		return null;
	}

	private static string DownloadAndVerifyNzb(out bool isCurrentVersionFound)
	{
		return DownloadUsenetNzb(out isCurrentVersionFound) ?? DownloadHttpsNzb();
	}

	internal static string UnzipNzb(string zipFilePath)
	{
		using System.IO.Compression.ZipArchive source = System.IO.Compression.ZipFile.OpenRead(zipFilePath);
		System.IO.Compression.ZipArchiveEntry zipEntry = source.Entries.FirstOrDefault((System.IO.Compression.ZipArchiveEntry entry) => !string.IsNullOrEmpty(entry.Name));
		if (zipEntry == null)
		{
			Log.Error("Update archive does not contain an NZB file");
			return null;
		}
		long uncompressedSize = zipEntry.Length;
		if (uncompressedSize < 3000 || uncompressedSize > 50000)
		{
			Log.Error("NZB file size is not good: " + uncompressedSize);
			return null;
		}
		string text = System.IO.Path.GetTempPath().Trim();
		return SafeZip.ExtractEntry(zipEntry, text, overwrite: true);
	}

	internal static string DownloadUsenetNzb(out bool isCurrentVersionFound)
	{
		Spotnet.Model.NNTP nNTP = new Spotnet.Model.NNTP(AppHelper.HeaderPhuse);
		string[] obj = (UpdateChannel.Equals("beta") ? Spotnet.Properties.Configuration.UpdateGroupsBeta : Spotnet.Properties.Configuration.UpdateGroupsRelease);
		isCurrentVersionFound = false;
		string[] array = obj;
		foreach (string text in array)
		{
			long first = 0L;
			long last = 0L;
			long count = 0L;
			if (!nNTP.SelectGroup(text, ref first, ref last, ref count, out var result, out var errorMsg))
			{
				continue;
			}
			if (last - 100 > first)
			{
				first = last - 100;
			}
			string headers = nNTP.GetHeaders(text, first, last, null, out result, out errorMsg);
			if (!errorMsg.IsNullOrEmpty() || !headers.StartsWith("224"))
			{
				continue;
			}
			foreach (string item in headers.Split(new string[1] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Reverse()
				.Skip(1))
			{
				Match match = new Regex("^[0-9]+\\t(.*) \\[[0-9]+/[0-9]+\\]\\t(.*)\\t.*\\t(.*)\\t.*\\t(.*)\\t.*\\t.*$").Match(item);
				if (!match.Success)
				{
					continue;
				}
				Spot spot = new Spot
				{
					Title = match.Groups[1].Value,
					MessageId = match.Groups[3].Value,
					Poster = match.Groups[2].Value
				};
				if (int.TryParse(match.Groups[4].Value, out var result2))
				{
					spot.Filesize = result2;
				}
				if (!IsNzbSpot(spot, text) || spot.Filesize >= 10000)
				{
					continue;
				}
				Version version = VersionExtractedFromNzbSpotTitle(spot.Title);
				if (version == AppHelper.AppVersion)
				{
					isCurrentVersionFound = true;
				}
				if (version == null || version <= AppHelper.AppVersion)
				{
					continue;
				}
				Engine headerPhuse = AppHelper.HeaderPhuse;
				string thumbsGroup = Settings.Default.ThumbsGroup;
				SpotHelper.GetBinary(headerPhuse, thumbsGroup, new List<string> { spot.MessageId }, out var sxOut, out var sError, decodeGzip: false);
				if (sxOut == null || sxOut.Length < 10)
				{
					Log.Error("NZB content received is empty");
					continue;
				}
				string tempFileName = AppHelper.GetTempFileName();
				using (FileStream outStream = System.IO.File.Open(tempFileName, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					if (!new YEnc(outStream).DecodeBytes(sxOut))
					{
						Log.Error("Failed to decode yEnc nzb from usenet");
						continue;
					}
				}
				try
				{
					string text2 = UnzipNzb(tempFileName);
					if (!SpotnetUpdateVerifier.VerifyFileSign(text2))
					{
						Log.Error("Failed to verify nzb file from group: " + text + ". MessageId: " + spot.MessageId);
						System.IO.File.Delete(text2);
						continue;
					}
					sError = text2;
					return sError;
				}
				catch (Exception ex)
				{
					Log.Error(ex.Message + ". MsgId: " + spot.MessageId + ". Group: " + text);
				}
			}
		}
		return null;
	}

	private static Version VersionExtractedFromNzbSpotTitle(string title)
	{
		try
		{
			Match match = new Regex("^.*\\.Spotnet\\.update\\.([0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+)\\.nzb.*").Match(title);
			if (match.Success)
			{
				return new Version(match.Groups[1].Value);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return null;
	}

	private static bool IsPublisherSpot(Spot spot, string groups, bool isNzb)
	{
		string text = spot.MessageId.Replace("<", "").Replace(">", "");
		int num = text.IndexOf('.');
		if (num < 0)
		{
			return false;
		}
		string text2 = text.Substring(num + 1);
		if (text2.IsNullOrEmpty())
		{
			return false;
		}
		int num2 = spot.Title.IndexOf('.');
		if (num2 < 0)
		{
			return false;
		}
		if (spot.Title.Substring(0, num2).Equals(GetEncryptedSubjectPrefix(spot.Poster, text2, groups)))
		{
			return VerifySubjectSignature(spot.Title, isNzb);
		}
		return false;
	}

	internal static bool VerifySubjectSignature(string title, bool isNzb)
	{
		Match match = (isNzb ? new Regex("^(\\w+)\\.(.+)\\.(Spotnet\\.update\\..+)$") : new Regex("^(\\w+)\\.(.+)\\.(Spotnet\\.update\\..+) \\[[0-9]+\\/[0-9]+\\]$")).Match(title);
		if (match.Success)
		{
			string sSignature = match.Groups[2].Value.Replace("-p", "+").Replace("-s", "/");
			return SpotHelper.CheckUserSignature($"{match.Groups[1].Value}.{match.Groups[3].Value}", sSignature, "w/1Tee2JNW5SW2ciaPsVwfXq/p4sCmgv1SVBTZoImzBjZEoOaZz+f1bqkYM1QdEt");
		}
		return false;
	}

	private static bool IsNzbSpot(Spot spot, string group)
	{
		if (!IsPublisherSpot(spot, group, isNzb: true))
		{
			return false;
		}
		int num = spot.Title.IndexOf('.');
		if (num > 0)
		{
			return spot.Title.Substring(num).Contains("nzb");
		}
		return false;
	}

	private static string GetEncryptedSubjectPrefix(string str1, string str2, string str3)
	{
		string str4 = string.Format("kde{0}qwer{1}e3{2}l{1}sw", str1, str2, str3);
		using MD5 mD = MD5.Create();
		return BitConverter.ToString(mD.ComputeHash(str4.ToByteArray())).Replace("-", "").ToLower();
	}

	private static string DownloadHttpsNzb()
	{
		if (_httpNzbAlreadyChecked)
		{
			return null;
		}
		_httpNzbAlreadyChecked = true;
		using WebClient webClient = new WebClient();
		string[] updateUrls = Spotnet.Properties.Configuration.UpdateUrls;
		foreach (string text in updateUrls)
		{
			string address = $"{text}/{UpdateChannel}/{Spotnet.Properties.Configuration.NzbArchiveFileName}";
			RemoteCertificateValidationCallback remoteCertificateValidationCallback = (object _003Cp0_003E, X509Certificate _003Cp1_003E, X509Chain _003Cp2_003E, SslPolicyErrors _003Cp3_003E) => true;
			try
			{
				ServicePointManager.ServerCertificateValidationCallback = (RemoteCertificateValidationCallback)Delegate.Combine(ServicePointManager.ServerCertificateValidationCallback, remoteCertificateValidationCallback);
				webClient.OpenRead(address);
				long num = Convert.ToInt64(webClient.ResponseHeaders["Content-Length"]);
				if (num > 500 && num < 10000)
				{
					string tempFileName = AppHelper.GetTempFileName();
					webClient.DownloadFile(address, tempFileName);
					string text2 = UnzipNzb(tempFileName);
					try
					{
						System.IO.File.Delete(tempFileName);
					}
					catch
					{
					}
					if (SpotnetUpdateVerifier.VerifyFileSign(text2))
					{
						return text2;
					}
					Log.Error("Failed to verify nzb file from url: " + text);
					System.IO.File.Delete(text2);
				}
			}
			catch (WebException ex)
			{
				Log.Error(ex.Message);
			}
			finally
			{
				ServicePointManager.ServerCertificateValidationCallback = (RemoteCertificateValidationCallback)Delegate.Remove(ServicePointManager.ServerCertificateValidationCallback, remoteCertificateValidationCallback);
			}
		}
		return null;
	}

	private static async Task<UpdateInfoEx> DeployLocalUpdate()
	{
		SetupSplatLogger logger = new SetupSplatLogger();
		try
		{
			Locator.CurrentMutable.Register(() => logger, typeof(Splat.ILogger));
			UpdateInfoEx info = await CheckForUpdate(ignoreDeltaUpdates: true);
			if (info.Exception != null)
			{
				return OnUpgradeFailed(null, "Check for new version failed: " + info.Exception.Message, reportFailure: false, uploadLogs: true);
			}
			if (!info.IsNewVersionAvailable)
			{
				Log.Debug("The last version is used: " + info.FutureReleaseEntry.Version);
				return info;
			}
			int i = 0;
			try
			{
				await UpdateManager.DownloadReleases(info.ReleasesToApply);
				i = 1;
				CopyAppSettingsForTheNextUpdate();
				await UpdateManager.ApplyReleases(info);
				i = 2;
				await UpdateManager.CreateUninstallerRegistryEntry();
			}
			catch (Exception ex)
			{
				if (i == 0)
				{
					Log.Debug("Releases download failed");
				}
				if (i == 1)
				{
					Log.Debug("Releases apply failed");
				}
				if (i == 2)
				{
					Log.Debug("Uninstaller registry entry creation failed");
				}
				Version version = info.FutureReleaseEntry.Version;
				string errorMsg = $"Update to new version {version} failed: {ex.Message}";
				return OnUpgradeFailed(version, errorMsg, reportFailure: true, uploadLogs: true);
			}
			ReleaseEntry releaseEntry = info.ReleasesToApply.OrderBy((ReleaseEntry x) => x.Version).Last();
			if (releaseEntry.Version != null && releaseEntry.Version > AppHelper.AppVersion)
			{
				OnUpgradeSuccess(releaseEntry.Version);
				_lastVersion = releaseEntry.Version;
				return info;
			}
			return OnUpgradeFailed(null, "Failed to upgrade", reportFailure: false, uploadLogs: true);
		}
		finally
		{
			if (logger != null)
			{
				((IDisposable)logger).Dispose();
			}
		}
	}

	private static void CopyAppSettingsForTheNextUpdate()
	{
		try
		{
			string pathToConfig = PathToConfig;
			if (System.IO.File.Exists(pathToConfig))
			{
				try
				{
					Settings.Default.IsNewVersion = true;
					Settings.Default.Save();
					string fileName = System.IO.Path.GetFileName(pathToConfig);
					string destinationPath = System.IO.Path.Combine(System.IO.Directory.GetParent(AppHelper.AppPath()).FullName, fileName);
					System.IO.File.Copy(pathToConfig, destinationPath, overwrite: true);
					return;
				}
				finally
				{
					Settings.Default.IsNewVersion = false;
					Settings.Default.Save();
				}
			}
			Log.Error("Path to config not found: " + pathToConfig);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	private static void RestoreAppSettings()
	{
		try
		{
			string fileName = System.IO.Path.GetFileName(PathToConfig);
			string text = System.IO.Path.Combine(System.IO.Directory.GetParent(AppHelper.AppPath()).FullName, fileName);
			if (System.IO.File.Exists(text))
			{
				string directoryName = System.IO.Path.GetDirectoryName(PathToConfig);
				if (directoryName != null)
				{
					AppHelper.EnsureDirectoryExist(directoryName);
				}
				System.IO.File.Copy(text, PathToConfig, overwrite: true);
				Settings.Default.Reload();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	internal static bool ProcessStateChangedEvents(List<string> args)
	{
		if (InstalledProfile.Enabled) return false;
		bool shutdownAfterMethodExit = false;
		try
		{
			IsNewVersion = Settings.Default.IsNewVersion;
			SquirrelAwareApp.HandleEvents(delegate
			{
				try
				{
					Log.Debug("New version {0} is installed", AppHelper.AppVersion);
					RestoreAppSettings();
					if (!Settings.Default.IsNewVersion)
					{
						Settings.Default.IsNewVersion = true;
						Settings.Default.Save();
					}
					string fileName3 = System.IO.Path.GetFileName(Assembly.GetExecutingAssembly().Location);
					UpdateManager.CreateShortcutsForExecutable(fileName3, ShortcutLocation.StartMenu | ShortcutLocation.Desktop, updateOnly: false);
					OnInstallAndUpdateActions();
				}
				catch (Exception ex4)
				{
					Log.Exception(ex4);
				}
				finally
				{
					shutdownAfterMethodExit = true;
				}
			}, delegate
			{
				try
				{
					Log.Debug("Spotnet updated to {0}", AppHelper.AppVersion);
					RestoreAppSettings();
					if (!Settings.Default.IsNewVersion)
					{
						Settings.Default.IsNewVersion = true;
						Settings.Default.Save();
					}
					string fileName2 = System.IO.Path.GetFileName(Assembly.GetExecutingAssembly().Location);
					UpdateManager.CreateShortcutsForExecutable(fileName2, ShortcutLocation.StartMenu | ShortcutLocation.Desktop, updateOnly: true);
					OnInstallAndUpdateActions();
				}
				catch (Exception ex3)
				{
					Log.Exception(ex3);
				}
				finally
				{
					shutdownAfterMethodExit = true;
				}
			}, delegate
			{
				try
				{
					Log.Debug("{0} is obsoleted", AppHelper.AppVersion);
				}
				finally
				{
					shutdownAfterMethodExit = true;
				}
			}, delegate
			{
				try
				{
					if (!SendExitCommandToOtherSpotnetInstance())
					{
						AppHelper.Error("Spotnet is running already, so some app files can't be removed. Please remove it by hands.");
					}
					Log.Debug("Uninstalling " + AppHelper.AppVersion);
					string fileName = System.IO.Path.GetFileName(Assembly.GetExecutingAssembly().Location);
					UpdateManager.RemoveShortcutsForExecutable(fileName, ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
					RemoveStartMenuFolderOnUninstallIfEmpty();
				}
				catch (Exception ex2)
				{
					Log.Exception(ex2);
				}
				finally
				{
					shutdownAfterMethodExit = true;
				}
			}, delegate
			{
				IsNewVersion = true;
			}, args.ToArray());
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		return shutdownAfterMethodExit;
	}

	private static bool SendExitCommandToOtherSpotnetInstance()
	{
		if (OtherInstancesCommunicator.OtherSpotnetProcessesRunning().FirstOrDefault() != null)
		{
			Log.Debug("Other Spotnet instance is running, so it should be stopped before uninstall");
			OtherInstancesCommunicator.SendExitCommandToPipe();
			DateTime now = DateTime.Now;
			while (OtherInstancesCommunicator.OtherSpotnetProcessesRunning().FirstOrDefault() != null && (DateTime.Now - now).TotalSeconds < 10.0)
			{
				Thread.Sleep(50);
			}
			if (OtherInstancesCommunicator.OtherSpotnetProcessesRunning().FirstOrDefault() != null)
			{
				return false;
			}
		}
		return true;
	}

	private static async Task<UpdateInfoEx> CheckForUpdate(bool ignoreDeltaUpdates)
	{
		try
		{
			_lastUpdateCheckDateTime = DateTime.Now;
			return new UpdateInfoEx(await UpdateManager.CheckForUpdate(ignoreDeltaUpdates));
		}
		catch (Exception ex)
		{
			return new UpdateInfoEx(ex);
		}
	}

	private static void RemoveStartMenuFolderOnUninstallIfEmpty()
	{
		string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs\\Spotnet\\");
		try
		{
			if (System.IO.Directory.Exists(path) && !System.IO.Directory.GetFileSystemEntries(path).Any())
			{
				System.IO.Directory.Delete(path, recursive: false);
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool GrantAclFullControl(string fullPath)
	{
		try
		{
			if (!System.IO.Directory.Exists(fullPath) && !System.IO.File.Exists(fullPath))
			{
				return false;
			}
			System.IO.DirectoryInfo directoryInfo = new System.IO.DirectoryInfo(fullPath);
			DirectorySecurity accessControl = directoryInfo.GetAccessControl();
			accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
			directoryInfo.SetAccessControl(accessControl);
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	private static bool CheckAccess(WindowsIdentity user, string path, FileSystemRights expectedRights)
	{
		if (!System.IO.Directory.Exists(path) && !System.IO.File.Exists(path))
		{
			return false;
		}
		IEnumerable<AuthorizationRule> enumerable = from AuthorizationRule rule in new System.IO.FileInfo(path).GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
			where user.User.Equals(rule.IdentityReference) || user.Groups.Contains(rule.IdentityReference)
			select rule;
		FileSystemRights fileSystemRights = (FileSystemRights)0;
		FileSystemRights fileSystemRights2 = (FileSystemRights)0;
		foreach (FileSystemAccessRule item in enumerable)
		{
			if (item.AccessControlType.Equals(AccessControlType.Deny))
			{
				fileSystemRights |= item.FileSystemRights;
			}
			else if (item.AccessControlType.Equals(AccessControlType.Allow))
			{
				fileSystemRights2 |= item.FileSystemRights;
			}
		}
		fileSystemRights2 &= ~fileSystemRights;
		return (fileSystemRights2 & expectedRights) == expectedRights;
	}

	public static void CopyDataToProgramData()
	{
		string sourceDirName = System.IO.Path.Combine(AppHelper.AppPath(), "Data\\");
		try
		{
			DirectoryCopyRecursive(sourceDirName, AppHelper.SettingsFolder);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	private static void DirectoryCopyRecursive(string sourceDirName, string destDirName)
	{
		System.IO.DirectoryInfo directoryInfo = new System.IO.DirectoryInfo(sourceDirName);
		if (!directoryInfo.Exists)
		{
			Log.Warn("No folder found: " + sourceDirName);
			return;
		}
		AppHelper.EnsureDirectoryExist(destDirName);
		System.IO.FileInfo[] files = directoryInfo.GetFiles();
		foreach (System.IO.FileInfo fileInfo in files)
		{
			string text = System.IO.Path.Combine(destDirName, fileInfo.Name);
			if (!System.IO.File.Exists(text))
			{
				try
				{
					fileInfo.CopyTo(text, overwrite: true);
				}
				catch (Exception)
				{
					Log.Warn("Failed to copy file to " + text);
				}
			}
		}
		System.IO.DirectoryInfo[] directories = directoryInfo.GetDirectories();
		foreach (System.IO.DirectoryInfo directoryInfo2 in directories)
		{
			Log.Debug("Restore data directory: " + directoryInfo2);
			string destDirName2 = System.IO.Path.Combine(destDirName, directoryInfo2.Name);
			DirectoryCopyRecursive(directoryInfo2.FullName, destDirName2);
		}
	}

	internal static bool CreateProgramDataAndGetPermissionsToIt()
	{
		if (InstalledProfile.Enabled)
		{
			// A per-user profile inherits the user's ACL; never grant access to Builtin Users.
			System.IO.Directory.CreateDirectory(InstalledProfile.DataDirectory);
			return true;
		}
		if (!System.IO.Directory.Exists(AppHelper.SettingsFolder))
		{
			try
			{
				System.IO.Directory.CreateDirectory(AppHelper.SettingsFolder);
				GrantAclFullControl(AppHelper.SettingsFolder);
			}
			catch (Exception ex)
			{
				AppHelper.Error("Failed to create " + AppHelper.SettingsFolder + " folder: " + ex.Message);
			}
			try
			{
				new FileIOPermission(FileIOPermissionAccess.AllAccess, AppHelper.SettingsFolder).Demand();
			}
			catch (SecurityException ex2)
			{
				AppHelper.Error("Cannot get CAS permissions to '" + AppHelper.SettingsFolder + "' folder: " + ex2.Message);
				return false;
			}
			try
			{
				WindowsIdentity current = WindowsIdentity.GetCurrent();
				if (!CheckAccess(current, AppHelper.SettingsFolder, FileSystemRights.Read | FileSystemRights.Write))
				{
					GrantAclFullControl(AppHelper.SettingsFolder);
					if (!CheckAccess(current, AppHelper.SettingsFolder, FileSystemRights.Read | FileSystemRights.Write))
					{
						if (AppHelper.IsLocalSettingsFolder)
						{
							AppHelper.Error("Cannot get R/W permissions to '" + AppHelper.SettingsFolder + "' folder");
							return false;
						}
						AppHelper.SwitchSpotnetToUseLocalSettingsFolder();
						return CreateProgramDataAndGetPermissionsToIt();
					}
				}
			}
			catch (Exception ex3)
			{
				AppHelper.Error("Cannot get R/W permissions to '" + AppHelper.SettingsFolder + "' folder: " + ex3.Message);
				return false;
			}
		}
		return true;
	}

	internal static bool VerifyAndRestoreSettings()
	{
		if (InstalledProfile.Enabled)
		{
			// Seed only missing resources, preserving migrated filters and themes.
			CopyDataToProgramData();
			return true;
		}
		bool flag = false;
		try
		{
			Log.Debug("Is new version: " + Settings.Default.IsNewVersion);
		}
		catch (ConfigurationErrorsException ex)
		{
			Log.Error(ex.Message);
			AppHelper.Error("Settings are corrupted. Try to recreate the settings file.");
			try
			{
				if (!(ex.InnerException is ConfigurationErrorsException ex2) || ex2.Filename.IsNullOrEmpty() || !System.IO.File.Exists(ex2.Filename))
				{
					AppHelper.Error("Failed to locate settings file path.");
					return false;
				}
				System.IO.File.Delete(ex2.Filename);
				flag = true;
			}
			catch (Exception ex3)
			{
				Log.Exception(ex3, showToClient: true);
				return false;
			}
		}
		try
		{
			if (flag || Settings.Default.IsNewVersion)
			{
				RestoreAppSettings();
				string fileName = System.IO.Path.GetFileName(Assembly.GetExecutingAssembly().Location);
				UpdateManager.CreateShortcutsForExecutable(fileName, ShortcutLocation.StartMenu | ShortcutLocation.Desktop, updateOnly: true);
			}
		}
		catch (Exception ex4)
		{
			Log.Error("Failed to restore settings or create shortcuts: " + ex4.Message);
		}
		return true;
	}

	private static void OnInstallAndUpdateActions()
	{
		CopyDataToProgramData();
		if (!Settings.Default.FiltersAreInitialized)
		{
			if (!Filters.InitializeDefaultFilters())
			{
				string text = "Failed to initialize default filters";
				Log.Error(text);
				AppHelper.Error(text);
			}
			else
			{
				Settings.Default.FiltersAreInitialized = true;
				Settings.Default.Save();
			}
		}
	}

	internal static void AfterDeploymentActions()
	{
		if (InstalledProfile.Enabled) return;
		AppHelper.GetTempPath();
		if (IsNewVersion)
		{
			string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotnet\\");
			string iconPath = System.IO.Path.Combine(path, "app.ico");
			string openWith = "\"" + System.IO.Path.Combine(path, "Update.exe") + "\" --processStart Spotnet.exe --process-start-args";
			FileAssociator.SetProtocolAssociation("spotnet", openWith, iconPath);
		}
	}
}
