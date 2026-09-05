using System;
using System.Management;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Model.StatsReporter;

internal abstract class StatsReporter : IStatsReport
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public string OsVersion => Environment.OSVersion.Version.ToString();

	protected string OsVersionFriendlyName
	{
		get
		{
			string text = string.Empty;
			using (ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem").Get().GetEnumerator())
			{
				if (managementObjectEnumerator.MoveNext())
				{
					text = managementObjectEnumerator.Current["Caption"].ToString();
				}
			}
			return text + (Environment.Is64BitOperatingSystem ? " 64 bit" : " 32 bit");
		}
	}

	public Task<bool> ReportOnStartAsync()
	{
		return Task.Run(() => Send(startApp: true));
	}

	public Task<bool> ReportOnStopAsync()
	{
		return Task.Run(() => Send(startApp: false));
	}

	public Task<bool> ReportOnSpotOpenAsync(string messageId)
	{
		return Task.Run(() => SendOnSpotOpen(messageId));
	}

	public Task<bool> ReportOnSpotnetUpdateDownloadedAsync(Version version)
	{
		return Task.Run(() => SendOnSpotnetUpdateDownloaded(version));
	}

	public Task<bool> ReportOnSpotnetUpdatePerformedAsync(Version version, bool isSuccess)
	{
		return Task.Run(() => SendOnSpotnetUpdatePerformed(version, isSuccess));
	}

	protected abstract bool Send(bool startApp);

	protected abstract bool SendOnSpotOpen(string messageId);

	protected abstract bool SendOnSpotnetUpdateDownloaded(Version version);

	protected abstract bool SendOnSpotnetUpdatePerformed(Version version, bool isSuccess);
}
