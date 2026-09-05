using System;
using System.Threading.Tasks;

namespace Spotnet.Model.StatsReporter;

internal interface IStatsReport
{
	string OsVersion { get; }

	Task<bool> ReportOnStartAsync();

	Task<bool> ReportOnStopAsync();

	Task<bool> ReportOnSpotOpenAsync(string messageId);

	Task<bool> ReportOnSpotnetUpdateDownloadedAsync(Version version);

	Task<bool> ReportOnSpotnetUpdatePerformedAsync(Version version, bool isSuccess);
}
