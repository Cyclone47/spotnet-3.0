using System;
using System.Collections.Generic;
using NLog;
using System.IO;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Browser;

public class ReleaseNotesPage : WebView2Page
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public const string PageTitleOfReleaseNotes = "Release Notes";

	public static readonly Uri ReleaseNotesUri = new Uri("file:///" + GetReleaseNotesUrl());

	public ReleaseNotesPage()
	{
		base.Uri = ReleaseNotesUri;
		Title = "Release Notes";
		base.PageDefaultType = PageTypeEnum.ReleaseNotes;
	}

	public static string GetReleaseNotesUrl()
	{
		string tempFileName = AppHelper.GetTempFileName("html", "ReleaseNotes");
		string tempFileName2 = AppHelper.GetTempFileName("css", "ReleaseNotes");
		string value = "";
		if (IsItChristmasTime())
		{
			string snowFallJsFile = GetSnowFallJsFile();
			if (snowFallJsFile != null)
			{
				value = $"<script src=\"file:///{snowFallJsFile}\" type=\"text/javascript\"></script>";
			}
		}
		bool isDutch = UserLanguageHelper.Language == UserLanguageHelper.Dutch;
		string bundledNotes = isDutch ? Spotnet.Properties.Resources.whatsnew_nl : Spotnet.Properties.Resources.whatsnew;

		Dictionary<string, string> valueDict = new Dictionary<string, string>
		{
			{
				"VERSION",
				AppHelper.AppVersion.ToString()
			},
			{ "JAVASCRIPT", value },
			{
				"RESPONSEURL",
				ResponsePage.GetResponseSiteUrl()
			},
			{
				// The project's GitHub releases are the notes; the built-in list is what
				// a client that has never reached GitHub falls back to.
				"WHATSNEW",
				ReleaseNotesFeed.GetNotesHtml(bundledNotes)
			},
			{
				"THEMECLASS",
				ThemeHelper.IsModernDark ? "theme-dark" : "theme-light"
			}
		};
		string contents = ((UserLanguageHelper.Language == "en") ? Spotnet.Properties.Resources.ReleaseNotes_en : Spotnet.Properties.Resources.ReleaseNotes).FormatFromDictionary(valueDict).FormatFromDictionary(valueDict);
		File.WriteAllText(tempFileName, contents);
		File.WriteAllText(tempFileName2, Spotnet.Properties.Resources.ReleaseNotesCss);
		return tempFileName;
	}

	private static bool IsItChristmasTime()
	{
		int month = DateTime.Now.Month;
		int day = DateTime.Now.Day;
		if (month != 12 || day < 15)
		{
			if (month == 1)
			{
				return day <= 10;
			}
			return false;
		}
		return true;
	}

	private static string GetSnowFallJsFile()
	{
		string text = AppHelper.GetTempFileName("js", "snow-fall");
		try
		{
			File.WriteAllText(text, Spotnet.Properties.Resources.SnowFall);
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
			text = null;
		}
		return text;
	}
}
