using System.Windows;
using NLog;
using Spotnet.Controls;
using Spotnet.Downloader;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Model.StatsReporter;
using Spotnet.Views;

namespace Spotnet.Model;

internal static class Sys
{
	internal static volatile bool IsShutdownRequested;

	internal static readonly IStatsReport StatsReporter;

	internal static IDownloader Downloader;

	internal static LeftPanelUserControl LeftPanel;

	public static bool ShutdownPCAfterDownloads;

	internal static int EuroUsenetRetention;

	internal static MainWindow MainWindow { get; set; }

	public static PlayerViewModel DownloadsPlayer { get; set; }

	static Sys()
	{
		StatsReporter = new GoogleAnalyticsStatsReporter();
		ShutdownPCAfterDownloads = false;
		EuroUsenetRetention = 0;
	}

	public static void Shutdown()
	{
		IsShutdownRequested = true;
		if (MainWindow != null && MainWindow.IsInitialized)
		{
			MainWindow.DispatchAsync(delegate
			{
				MainWindow.Close();
			});
		}
		else
		{
			LogManager.Flush();
			LogManager.Shutdown();
			Application.Current.Shutdown();
		}
	}
}
