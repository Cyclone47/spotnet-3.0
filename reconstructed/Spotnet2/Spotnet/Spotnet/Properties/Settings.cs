using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Spotnet.Properties;

[CompilerGenerated]
[GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "15.9.0.0")]
internal sealed class Settings : ApplicationSettingsBase
{
	private static Settings defaultInstance = CreateDefault();

	private static Settings CreateDefault()
	{
		var settings = Spotnet.Deployment.InstalledProfile.Enabled
			? CreateInstalled(Spotnet.Deployment.InstalledProfile.SettingsPath) : new Settings();
		return (Settings)SettingsBase.Synchronized(settings);
	}

	internal static Settings CreateInstalled(string path)
	{
		var settings = new Settings();
		var provider = new Spotnet.Deployment.InstalledSettingsProvider(path);
		provider.Initialize(null, null);
		settings.Providers.Add(provider);
		foreach (SettingsProperty property in settings.Properties) property.Provider = provider;
		return settings;
	}

	public static Settings Default => defaultInstance;

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long Unique
	{
		get
		{
			return (long)this["Unique"];
		}
		set
		{
			this["Unique"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long LastRun
	{
		get
		{
			return (long)this["LastRun"];
		}
		set
		{
			this["LastRun"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long FirstRun
	{
		get
		{
			return (long)this["FirstRun"];
		}
		set
		{
			this["FirstRun"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long NumberOfRuns
	{
		get
		{
			return (long)this["NumberOfRuns"];
		}
		set
		{
			this["NumberOfRuns"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("free.pt")]
	public string HeaderGroup
	{
		get
		{
			return (string)this["HeaderGroup"];
		}
		set
		{
			this["HeaderGroup"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("alt.binaries.ftd")]
	public string NZBGroup
	{
		get
		{
			return (string)this["NZBGroup"];
		}
		set
		{
			this["NZBGroup"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("free.usenet")]
	public string ReplyGroup
	{
		get
		{
			return (string)this["ReplyGroup"];
		}
		set
		{
			this["ReplyGroup"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("free.willey")]
	public string ReportGroup
	{
		get
		{
			return (string)this["ReportGroup"];
		}
		set
		{
			this["ReportGroup"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool CheckSignatures
	{
		get
		{
			return (bool)this["CheckSignatures"];
		}
		set
		{
			this["CheckSignatures"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public byte DownloadAction
	{
		get
		{
			return (byte)this["DownloadAction"];
		}
		set
		{
			this["DownloadAction"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string DownloadFolder
	{
		get
		{
			return (string)this["DownloadFolder"];
		}
		set
		{
			this["DownloadFolder"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string Nickname
	{
		get
		{
			return (string)this["Nickname"];
		}
		set
		{
			this["Nickname"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string Tagname
	{
		get
		{
			return (string)this["Tagname"];
		}
		set
		{
			this["Tagname"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string Avatar
	{
		get
		{
			return (string)this["Avatar"];
		}
		set
		{
			this["Avatar"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("14")]
	public byte FontSize
	{
		get
		{
			return (byte)this["FontSize"];
		}
		set
		{
			this["FontSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("1500")]
	public int NzbGetRefresh
	{
		get
		{
			return (int)this["NzbGetRefresh"];
		}
		set
		{
			this["NzbGetRefresh"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool SystemTray
	{
		get
		{
			return (bool)this["SystemTray"];
		}
		set
		{
			this["SystemTray"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool GoogleSuggest
	{
		get
		{
			return (bool)this["GoogleSuggest"];
		}
		set
		{
			this["GoogleSuggest"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool GoogleAnalytics
	{
		get
		{
			return (bool)this["GoogleAnalytics"];
		}
		set
		{
			this["GoogleAnalytics"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ShowComments
	{
		get
		{
			return (bool)this["ShowComments"];
		}
		set
		{
			this["ShowComments"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool SaveTabs
	{
		get
		{
			return (bool)this["SaveTabs"];
		}
		set
		{
			this["SaveTabs"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string LastFolder
	{
		get
		{
			return (string)this["LastFolder"];
		}
		set
		{
			this["LastFolder"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string Moderator
	{
		get
		{
			return (string)this["Moderator"];
		}
		set
		{
			this["Moderator"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("5000")]
	public int MaxResults
	{
		get
		{
			return (int)this["MaxResults"];
		}
		set
		{
			this["MaxResults"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("rowid")]
	public string SortColumn
	{
		get
		{
			return (string)this["SortColumn"];
		}
		set
		{
			this["SortColumn"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("DESC")]
	public string SortDirection
	{
		get
		{
			return (string)this["SortDirection"];
		}
		set
		{
			this["SortDirection"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("01020304000005080000")]
	public string Columns
	{
		get
		{
			return (string)this["Columns"];
		}
		set
		{
			this["Columns"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool AdvancedSearch
	{
		get
		{
			return (bool)this["AdvancedSearch"];
		}
		set
		{
			this["AdvancedSearch"] = value;
		}
	}

	/// <summary>Open web links from a spot in the Windows default browser.</summary>
	/// <remarks>
	/// Defaults on: an IMDb or YouTube link out of a spot body belongs in the user's own
	/// browser, with their sessions and extensions, not in an embedded WebView2 tab.
	/// Turning it off keeps those links inside Spotnet.
	/// </remarks>
	/// <summary>
	/// How far back the very first synchronisation reaches, in days. Zero means the whole
	/// group.
	/// </summary>
	/// <remarks>
	/// Only consulted when the spots database is empty. A fresh install used to start at
	/// the group's oldest article - fifteen years of headers, hours of syncing, and nearly
	/// all of it discarded again on save for being out of retention. Ninety days is enough
	/// to fill the list with everything still downloadable, and the normal incremental sync
	/// keeps reaching further back on its own afterwards.
	/// </remarks>
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("90")]
	public int InitialFetchDays
	{
		get
		{
			return (int)this["InitialFetchDays"];
		}
		set
		{
			this["InitialFetchDays"] = value;
		}
	}

	/// <summary>Master switch for Windows desktop notifications.</summary>
	/// <remarks>
	/// On by default. Off silences every notification regardless of the per-event settings
	/// such as <see cref="NotifyAboutDownloadComplete"/>.
	/// </remarks>
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ShowDesktopNotifications
	{
		get
		{
			return (bool)this["ShowDesktopNotifications"];
		}
		set
		{
			this["ShowDesktopNotifications"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ExternalBrowser
	{
		get
		{
			return (bool)this["ExternalBrowser"];
		}
		set
		{
			this["ExternalBrowser"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long DatabaseMax
	{
		get
		{
			return (long)this["DatabaseMax"];
		}
		set
		{
			this["DatabaseMax"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long DatabaseCount
	{
		get
		{
			return (long)this["DatabaseCount"];
		}
		set
		{
			this["DatabaseCount"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("72500")]
	public int DatabaseCache
	{
		get
		{
			return (int)this["DatabaseCache"];
		}
		set
		{
			this["DatabaseCache"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long DatabaseFilter
	{
		get
		{
			return (long)this["DatabaseFilter"];
		}
		set
		{
			this["DatabaseFilter"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ExternalSigning
	{
		get
		{
			return (bool)this["ExternalSigning"];
		}
		set
		{
			this["ExternalSigning"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string KeysURL
	{
		get
		{
			return (string)this["KeysURL"];
		}
		set
		{
			this["KeysURL"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string WhitelistURL
	{
		get
		{
			return (string)this["WhitelistURL"];
		}
		set
		{
			this["WhitelistURL"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string BlacklistURL
	{
		get
		{
			return (string)this["BlacklistURL"];
		}
		set
		{
			this["BlacklistURL"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool EnableLogging
	{
		get
		{
			return (bool)this["EnableLogging"];
		}
		set
		{
			this["EnableLogging"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public byte SpotsListType
	{
		get
		{
			return (byte)this["SpotsListType"];
		}
		set
		{
			this["SpotsListType"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleStatusBar
	{
		get
		{
			return (bool)this["VisibleStatusBar"];
		}
		set
		{
			this["VisibleStatusBar"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleSearch
	{
		get
		{
			return (bool)this["VisibleSearch"];
		}
		set
		{
			this["VisibleSearch"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleFilters
	{
		get
		{
			return (bool)this["VisibleFilters"];
		}
		set
		{
			this["VisibleFilters"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleAddFilter
	{
		get
		{
			return (bool)this["VisibleAddFilter"];
		}
		set
		{
			this["VisibleAddFilter"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleMainMenu
	{
		get
		{
			return (bool)this["VisibleMainMenu"];
		}
		set
		{
			this["VisibleMainMenu"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleLeftPanel
	{
		get
		{
			return (bool)this["VisibleLeftPanel"];
		}
		set
		{
			this["VisibleLeftPanel"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public long DatabaseMin
	{
		get
		{
			return (long)this["DatabaseMin"];
		}
		set
		{
			this["DatabaseMin"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("0")]
	public string ApplicationVersion
	{
		get
		{
			return (string)this["ApplicationVersion"];
		}
		set
		{
			this["ApplicationVersion"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("10")]
	public int DbAutoUpdateIntervalMin
	{
		get
		{
			return (int)this["DbAutoUpdateIntervalMin"];
		}
		set
		{
			this["DbAutoUpdateIntervalMin"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool ShowEroticaInSearchResults
	{
		get
		{
			return (bool)this["ShowEroticaInSearchResults"];
		}
		set
		{
			this["ShowEroticaInSearchResults"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool DbAutoUpdateEnabled
	{
		get
		{
			return (bool)this["DbAutoUpdateEnabled"];
		}
		set
		{
			this["DbAutoUpdateEnabled"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool DbUpdateCompressionEnabled
	{
		get
		{
			return (bool)this["DbUpdateCompressionEnabled"];
		}
		set
		{
			this["DbUpdateCompressionEnabled"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("free.at")]
	public string ThumbsGroup
	{
		get
		{
			return (string)this["ThumbsGroup"];
		}
		set
		{
			this["ThumbsGroup"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool LoadComments
	{
		get
		{
			return (bool)this["LoadComments"];
		}
		set
		{
			this["LoadComments"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("Blue")]
	public string UIColor
	{
		get
		{
			return (string)this["UIColor"];
		}
		set
		{
			this["UIColor"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool FiltersMode2
	{
		get
		{
			return (bool)this["FiltersMode2"];
		}
		set
		{
			this["FiltersMode2"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("nl")]
	public string UserLanguage
	{
		get
		{
			return (string)this["UserLanguage"];
		}
		set
		{
			this["UserLanguage"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool IsNewVersion
	{
		get
		{
			return (bool)this["IsNewVersion"];
		}
		set
		{
			this["IsNewVersion"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool HideCommentsWithLinks
	{
		get
		{
			return (bool)this["HideCommentsWithLinks"];
		}
		set
		{
			this["HideCommentsWithLinks"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("5")]
	public int NumOfSpamReportsToSpotHide
	{
		get
		{
			return (int)this["NumOfSpamReportsToSpotHide"];
		}
		set
		{
			this["NumOfSpamReportsToSpotHide"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool VisibleMainToolbar
	{
		get
		{
			return (bool)this["VisibleMainToolbar"];
		}
		set
		{
			this["VisibleMainToolbar"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool HideBlacklistedSpots
	{
		get
		{
			return (bool)this["HideBlacklistedSpots"];
		}
		set
		{
			this["HideBlacklistedSpots"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool IsEnabledUbbForSpot
	{
		get
		{
			return (bool)this["IsEnabledUbbForSpot"];
		}
		set
		{
			this["IsEnabledUbbForSpot"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool IsEnabledUbbForComment
	{
		get
		{
			return (bool)this["IsEnabledUbbForComment"];
		}
		set
		{
			this["IsEnabledUbbForComment"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool IsEnabledSmiles
	{
		get
		{
			return (bool)this["IsEnabledSmiles"];
		}
		set
		{
			this["IsEnabledSmiles"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ShowTrustedOnlyEnabled
	{
		get
		{
			return (bool)this["ShowTrustedOnlyEnabled"];
		}
		set
		{
			this["ShowTrustedOnlyEnabled"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("120")]
	public int ExternalListsUpdateInterval
	{
		get
		{
			return (int)this["ExternalListsUpdateInterval"];
		}
		set
		{
			this["ExternalListsUpdateInterval"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string SpotWhitelistURL
	{
		get
		{
			return (string)this["SpotWhitelistURL"];
		}
		set
		{
			this["SpotWhitelistURL"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string SpotBlacklistURL
	{
		get
		{
			return (string)this["SpotBlacklistURL"];
		}
		set
		{
			this["SpotBlacklistURL"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool PromoteSpotnetInComment
	{
		get
		{
			return (bool)this["PromoteSpotnetInComment"];
		}
		set
		{
			this["PromoteSpotnetInComment"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string ColumnsSize
	{
		get
		{
			return (string)this["ColumnsSize"];
		}
		set
		{
			this["ColumnsSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool IsEnabledBadWordsFilterForComment
	{
		get
		{
			return (bool)this["IsEnabledBadWordsFilterForComment"];
		}
		set
		{
			this["IsEnabledBadWordsFilterForComment"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool DownloadExternalLists
	{
		get
		{
			return (bool)this["DownloadExternalLists"];
		}
		set
		{
			this["DownloadExternalLists"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetControlIP
	{
		get
		{
			return (string)this["NzbGetControlIP"];
		}
		set
		{
			this["NzbGetControlIP"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetControlPort
	{
		get
		{
			return (string)this["NzbGetControlPort"];
		}
		set
		{
			this["NzbGetControlPort"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("12")]
	public byte SpotFontSize
	{
		get
		{
			return (byte)this["SpotFontSize"];
		}
		set
		{
			this["SpotFontSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("5000")]
	public int SpotChunkSize
	{
		get
		{
			return (int)this["SpotChunkSize"];
		}
		set
		{
			this["SpotChunkSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("40000")]
	public int CommentChunkSize
	{
		get
		{
			return (int)this["CommentChunkSize"];
		}
		set
		{
			this["CommentChunkSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("5000")]
	public int SpamReportChunkSize
	{
		get
		{
			return (int)this["SpamReportChunkSize"];
		}
		set
		{
			this["SpamReportChunkSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool ExternalNzbGet
	{
		get
		{
			return (bool)this["ExternalNzbGet"];
		}
		set
		{
			this["ExternalNzbGet"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetControlUsername
	{
		get
		{
			return (string)this["NzbGetControlUsername"];
		}
		set
		{
			this["NzbGetControlUsername"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetControlPassword
	{
		get
		{
			return (string)this["NzbGetControlPassword"];
		}
		set
		{
			this["NzbGetControlPassword"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-1")]
	public int RemoveFilesOnDownloadRemove
	{
		get
		{
			return (int)this["RemoveFilesOnDownloadRemove"];
		}
		set
		{
			this["RemoveFilesOnDownloadRemove"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetDestDir
	{
		get
		{
			return (string)this["NzbGetDestDir"];
		}
		set
		{
			this["NzbGetDestDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetInterDir
	{
		get
		{
			return (string)this["NzbGetInterDir"];
		}
		set
		{
			this["NzbGetInterDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetNzbDir
	{
		get
		{
			return (string)this["NzbGetNzbDir"];
		}
		set
		{
			this["NzbGetNzbDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetQueueDir
	{
		get
		{
			return (string)this["NzbGetQueueDir"];
		}
		set
		{
			this["NzbGetQueueDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetTempDir
	{
		get
		{
			return (string)this["NzbGetTempDir"];
		}
		set
		{
			this["NzbGetTempDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetWebDir
	{
		get
		{
			return (string)this["NzbGetWebDir"];
		}
		set
		{
			this["NzbGetWebDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetScriptDir
	{
		get
		{
			return (string)this["NzbGetScriptDir"];
		}
		set
		{
			this["NzbGetScriptDir"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetLockFile
	{
		get
		{
			return (string)this["NzbGetLockFile"];
		}
		set
		{
			this["NzbGetLockFile"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetServer1Host
	{
		get
		{
			return (string)this["NzbGetServer1Host"];
		}
		set
		{
			this["NzbGetServer1Host"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetServer1Port
	{
		get
		{
			return (string)this["NzbGetServer1Port"];
		}
		set
		{
			this["NzbGetServer1Port"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetServer1Username
	{
		get
		{
			return (string)this["NzbGetServer1Username"];
		}
		set
		{
			this["NzbGetServer1Username"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetServer1Password
	{
		get
		{
			return (string)this["NzbGetServer1Password"];
		}
		set
		{
			this["NzbGetServer1Password"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-")]
	public string NzbGetServer1Encryption
	{
		get
		{
			return (string)this["NzbGetServer1Encryption"];
		}
		set
		{
			this["NzbGetServer1Encryption"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ShowFavorites
	{
		get
		{
			return (bool)this["ShowFavorites"];
		}
		set
		{
			this["ShowFavorites"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool StartXamarinServer
	{
		get
		{
			return (bool)this["StartXamarinServer"];
		}
		set
		{
			this["StartXamarinServer"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("9581")]
	public int XamarinServerPort
	{
		get
		{
			return (int)this["XamarinServerPort"];
		}
		set
		{
			this["XamarinServerPort"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool SpotsDbFileMalformed
	{
		get
		{
			return (bool)this["SpotsDbFileMalformed"];
		}
		set
		{
			this["SpotsDbFileMalformed"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool CommentsDbFileMalformed
	{
		get
		{
			return (bool)this["CommentsDbFileMalformed"];
		}
		set
		{
			this["CommentsDbFileMalformed"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool AutoShowNewSpotsInTheList
	{
		get
		{
			return (bool)this["AutoShowNewSpotsInTheList"];
		}
		set
		{
			this["AutoShowNewSpotsInTheList"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-1")]
	public int Retention
	{
		get
		{
			return (int)this["Retention"];
		}
		set
		{
			this["Retention"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ColoringSpots
	{
		get
		{
			return (bool)this["ColoringSpots"];
		}
		set
		{
			this["ColoringSpots"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ColoringFilters
	{
		get
		{
			return (bool)this["ColoringFilters"];
		}
		set
		{
			this["ColoringFilters"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("256")]
	public int LeftPanelWidth
	{
		get
		{
			return (int)this["LeftPanelWidth"];
		}
		set
		{
			this["LeftPanelWidth"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool LoadImageOnSpotTab
	{
		get
		{
			return (bool)this["LoadImageOnSpotTab"];
		}
		set
		{
			this["LoadImageOnSpotTab"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("100")]
	public int PlayerVolume
	{
		get
		{
			return (int)this["PlayerVolume"];
		}
		set
		{
			this["PlayerVolume"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("20")]
	public int DownloaderCacheSizeMb
	{
		get
		{
			return (int)this["DownloaderCacheSizeMb"];
		}
		set
		{
			this["DownloaderCacheSizeMb"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("3")]
	public int DownloaderRetries
	{
		get
		{
			return (int)this["DownloaderRetries"];
		}
		set
		{
			this["DownloaderRetries"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("10")]
	public int DownloaderRetryIntervalSec
	{
		get
		{
			return (int)this["DownloaderRetryIntervalSec"];
		}
		set
		{
			this["DownloaderRetryIntervalSec"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool MigrationFromNzbGetDone
	{
		get
		{
			return (bool)this["MigrationFromNzbGetDone"];
		}
		set
		{
			this["MigrationFromNzbGetDone"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool IsCachingEnabled
	{
		get
		{
			return (bool)this["IsCachingEnabled"];
		}
		set
		{
			this["IsCachingEnabled"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("10000")]
	public int ConnectionTimeout
	{
		get
		{
			return (int)this["ConnectionTimeout"];
		}
		set
		{
			this["ConnectionTimeout"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("60000")]
	public int DataReceivingTimeout
	{
		get
		{
			return (int)this["DataReceivingTimeout"];
		}
		set
		{
			this["DataReceivingTimeout"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool RecreateDbScheduled
	{
		get
		{
			return (bool)this["RecreateDbScheduled"];
		}
		set
		{
			this["RecreateDbScheduled"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("-1")]
	public int SpeedLimit
	{
		get
		{
			return (int)this["SpeedLimit"];
		}
		set
		{
			this["SpeedLimit"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("010203040506070000")]
	public string ColumnsDownloads
	{
		get
		{
			return (string)this["ColumnsDownloads"];
		}
		set
		{
			this["ColumnsDownloads"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string ColumnsDownloadsSize
	{
		get
		{
			return (string)this["ColumnsDownloadsSize"];
		}
		set
		{
			this["ColumnsDownloadsSize"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("DESC")]
	public string SortDownloadsDirection
	{
		get
		{
			return (string)this["SortDownloadsDirection"];
		}
		set
		{
			this["SortDownloadsDirection"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("Pri")]
	public string SortDownloadsColumn
	{
		get
		{
			return (string)this["SortDownloadsColumn"];
		}
		set
		{
			this["SortDownloadsColumn"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool RemovePar2FilesAfterDownload
	{
		get
		{
			return (bool)this["RemovePar2FilesAfterDownload"];
		}
		set
		{
			this["RemovePar2FilesAfterDownload"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("10/26/2016 14:52:00")]
	public DateTime DownloaderStartTime
	{
		get
		{
			return (DateTime)this["DownloaderStartTime"];
		}
		set
		{
			this["DownloaderStartTime"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("10/26/2016 14:52:00")]
	public DateTime DownloaderEndTime
	{
		get
		{
			return (DateTime)this["DownloaderEndTime"];
		}
		set
		{
			this["DownloaderEndTime"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool DownloaderSchedule
	{
		get
		{
			return (bool)this["DownloaderSchedule"];
		}
		set
		{
			this["DownloaderSchedule"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("Default")]
	public string ActiveTheme
	{
		get
		{
			return (string)this["ActiveTheme"];
		}
		set
		{
			this["ActiveTheme"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool CommentPreviewShow
	{
		get
		{
			return (bool)this["CommentPreviewShow"];
		}
		set
		{
			this["CommentPreviewShow"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool CommentSmilesShow
	{
		get
		{
			return (bool)this["CommentSmilesShow"];
		}
		set
		{
			this["CommentSmilesShow"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("60000")]
	public int ConnectionIdleTimeout
	{
		get
		{
			return (int)this["ConnectionIdleTimeout"];
		}
		set
		{
			this["ConnectionIdleTimeout"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("5000")]
	public int ConnectionIdleTimeoutSlave
	{
		get
		{
			return (int)this["ConnectionIdleTimeoutSlave"];
		}
		set
		{
			this["ConnectionIdleTimeoutSlave"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool SpotDetailsShowNewsreader
	{
		get
		{
			return (bool)this["SpotDetailsShowNewsreader"];
		}
		set
		{
			this["SpotDetailsShowNewsreader"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string Filter
	{
		get
		{
			return (string)this["Filter"];
		}
		set
		{
			this["Filter"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool ShowTabToolbar
	{
		get
		{
			return (bool)this["ShowTabToolbar"];
		}
		set
		{
			this["ShowTabToolbar"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string AvatarFolder
	{
		get
		{
			return (string)this["AvatarFolder"];
		}
		set
		{
			this["AvatarFolder"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool SpotImdbShow
	{
		get
		{
			return (bool)this["SpotImdbShow"];
		}
		set
		{
			this["SpotImdbShow"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool FiltersAreInitialized
	{
		get
		{
			return (bool)this["FiltersAreInitialized"];
		}
		set
		{
			this["FiltersAreInitialized"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("2017-01-01")]
	public DateTime PromoLastDate
	{
		get
		{
			return (DateTime)this["PromoLastDate"];
		}
		set
		{
			this["PromoLastDate"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool NotifyAboutDownloadComplete
	{
		get
		{
			return (bool)this["NotifyAboutDownloadComplete"];
		}
		set
		{
			this["NotifyAboutDownloadComplete"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool QuickCheck
	{
		get
		{
			return (bool)this["QuickCheck"];
		}
		set
		{
			this["QuickCheck"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool UseSocksProxy
	{
		get
		{
			return (bool)this["UseSocksProxy"];
		}
		set
		{
			this["UseSocksProxy"] = value;
		}
	}

	/// <summary>
	/// Accept a news server's TLS certificate even when it fails validation.
	/// Only enable this for a provider using a self-signed certificate: it removes the
	/// protection against another machine impersonating the server.
	/// </summary>
	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("False")]
	public bool AllowInvalidServerCertificate
	{
		get
		{
			return (bool)this["AllowInvalidServerCertificate"];
		}
		set
		{
			this["AllowInvalidServerCertificate"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("True")]
	public bool AutoUpdateEnabled
	{
		get
		{
			return (bool)this["AutoUpdateEnabled"];
		}
		set
		{
			this["AutoUpdateEnabled"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string UpdateSkippedVersion
	{
		get
		{
			return (string)this["UpdateSkippedVersion"];
		}
		set
		{
			this["UpdateSkippedVersion"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("")]
	public string UpdateManifestUrl
	{
		get
		{
			return (string)this["UpdateManifestUrl"];
		}
		set
		{
			this["UpdateManifestUrl"] = value;
		}
	}

	[UserScopedSetting]
	[DebuggerNonUserCode]
	[DefaultSettingValue("ClassicLight")]
	public string AppTheme
	{
		get
		{
			return (string)(this["AppTheme"] ?? "ClassicLight");
		}
		set
		{
			this["AppTheme"] = value;
		}
	}
}
