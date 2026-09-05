using System;
using Spotnet.Helpers;

namespace Spotnet.Properties;

internal static class Configuration
{
	internal static string DownloadsTabPromoLink = ((UserLanguageHelper.Language == "en") ? "https://www.5eurousenet.com/en/packages/free-test?a=14161" : "https://www.5eurousenet.com/nl/pakketten/gratis-test?a=14161");

	internal const string ReportsScriptUrl = "http://spotcloud.spotnet.wf/spotnet/hello.html";

	internal static string ResponseSiteUrl = "https://spotcloud.spotnet.wf/spotnet/response/";

	internal static string ResponseSiteUploadLogsUrl = "http://spotcloud.spotnet.wf/upload/";

	internal static string UpgradeFailuresUploadUrl = "https://spotcloud.spotnet.wf/spotnet/upgrade.failures/";

	internal static string RemoteWhitelistUrl = "http://spotcloud.spotnet.wf/spotnet/lists.new/whitelist.csv";

	internal static string RemoteBlacklistUrl = "http://spotcloud.spotnet.wf/spotnet/lists.new/blacklist.csv";

	internal static string RemoteSpotWhitelistUrl = "http://spotcloud.spotnet.wf/spotnet/lists.new/spot_whitelist.csv";

	internal static string RemoteSpotBlacklistUrl = "http://spotcloud.spotnet.wf/spotnet/lists.new/spot_blacklist.csv";

	internal static string RemotePromoFolder = "http://spotcloud.spotnet.wf/spotnet/promo/";

	internal static string PromoteSpotnetUrl = "http://spotnet.tk/";

	internal static string PromoteSpotnetText = "\r\n---\r\nDeze reactie is geplaatst via Spotnet 3.0, deze kan worden gedownload via: " + PromoteSpotnetUrl + "\r\n";

	internal static string[] UpdateUrls = new string[1] { "https://spotcloud.spotnet.wf/spotnet/" };

	/// <summary>
	/// Where an installed copy looks for its next release. The file lives on the default
	/// branch, so publishing an update is a commit; until its clientUpdate flag is set,
	/// clients read the entry and ignore it.
	/// </summary>
	internal const string UpdateManifestUrl =
		"https://raw.githubusercontent.com/Cyclone47/spotnet-3.0/main/updates/latest.json";

	internal static string[] UpdateGroupsBeta = new string[1] { "free.beer" };

	internal static string[] UpdateGroupsRelease = new string[1] { "free.c" };

	internal static string NzbArchiveFileName = "last.nzb.zip";

	internal const string UpdatesPublicKeyXml = "<RSAKeyValue><Modulus>xJ8rOq1i0xsDWuHgRDbCngSyrYGBsamWnKzlFxHQXyPrNo9UjpFU4hONPTnzo5JJlX7SVnbVvY9k64xe3KbTQmXRnU+0GZQ0ikz0XjJgfHTpI+4MmSILx12ZMbN50rDDWHa6Mda/6O/xwV2Tcpi+dFxL63UoGnIW+13pEHg/Dfc=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

	internal static string UpdaterServiceName = "Spotnet Updater";

	internal const int ThumbMaxWidth = 143;

	internal const int ThumbMaxHeight = 210;

	internal const string TrayTitle = "Spotnet :: Tray";

	internal const SpotsSourceEnum SpotsSources = SpotsSourceEnum.FileCache | SpotsSourceEnum.ImageFromFullImagesGroup | SpotsSourceEnum.ImageByUrl | SpotsSourceEnum.ImageFromThumbsGroup;

	internal const int DefaultPageSize = 250;

	internal const int OwnSpotDeleteSafePeriodInDays = 5;

	internal static TimeSpan AvgBlockingIssueLongPeriod = TimeSpan.FromDays(1.0);

	internal static TimeSpan AvgBlockingIssueWaitingPeriod = TimeSpan.FromSeconds(3.0);

	public const string StartTestConnectionString = "START TEST CONNECTION";
}
