using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using Spotnet.Controls;
using Spotnet.Deployment;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Helpers;

public static class UserLanguageHelper
{
	public const string Dutch = "nl";

	public const string English = "en";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly List<string> LocalesSupported = new List<string> { "nl", "en" };

	public static List<string> Languages => LocalesSupported.ToList();

	public static string Language => Culture?.TwoLetterISOLanguageName ?? DefaultLanguage;

	public static string DefaultLanguage => "nl";

	public static CultureInfo Culture
	{
		get
		{
			return Words.Culture;
		}
		set
		{
			Words.Culture = value;
			Categories.Culture = value;
		}
	}

	public static void Initialize(string language, bool updateCulture = true)
	{
		if (language == null || !LocalesSupported.Contains(language))
		{
			language = DefaultLanguage;
		}
		Settings.Default.UserLanguage = language;
		Settings.Default.Save();
		if (updateCulture)
		{
			Culture = CultureInfo.CreateSpecificCulture(language);
		}
		else
		{
			ChangeLanguageConfirmDialog changeLanguageConfirmDialog = new ChangeLanguageConfirmDialog();
			changeLanguageConfirmDialog.Owner = Sys.MainWindow;
			changeLanguageConfirmDialog.ShowDialog();
			if (changeLanguageConfirmDialog.RestartNow)
			{
				SquirrelStuff.RestartApplication();
			}
		}
		Log.Debug("Lang: " + language);
	}
}
