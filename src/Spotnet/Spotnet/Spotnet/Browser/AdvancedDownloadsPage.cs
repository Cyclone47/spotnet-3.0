using System;
using NLog;
using Spotnet.Downloader;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Browser;

public class AdvancedDownloadsPage : WebView2Page
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly string _pageTitleOfAdvancedDownloads = Words.MenuDownloadsAdvanced;

	private static Uri AdvancedDownloadsUri => new Uri($"http://{DownloaderProps.ControlIp}:{DownloaderProps.ControlPort}/{DownloaderProps.ControlUsername}:{DownloaderProps.ControlPassword}/");

	public override string Title => _pageTitleOfAdvancedDownloads;

	public AdvancedDownloadsPage()
	{
		base.Uri = AdvancedDownloadsUri;
		base.PageDefaultType = PageTypeEnum.AdvancedDownloads;
	}
}
