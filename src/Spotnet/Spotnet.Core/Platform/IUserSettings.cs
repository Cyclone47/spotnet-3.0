namespace Spotnet.Platform;

/// <summary>
/// Cross-platform settings abstraction replacing static Properties.Settings.Default.
/// </summary>
public interface IUserSettings
{
	string HeaderGroup { get; set; }
	string CommentsGroup { get; set; }
	string ReportGroup { get; set; }
	string NZBGroup { get; set; }
	string ThumbsGroup { get; set; }

	int Retention { get; set; }
	int MaxSpots { get; set; }

	string DownloadFolder { get; set; }
	bool CheckSignatures { get; set; }

	string GetValue(string key, string defaultValue = "");
	void SetValue(string key, string value);

	void Save();
}
