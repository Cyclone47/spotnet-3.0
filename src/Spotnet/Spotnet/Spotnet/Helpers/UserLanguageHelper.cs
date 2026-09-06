using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using NLog;
using Spotnet.Localization;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;
using Spotnet.Downloader.ViewModel;

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

	/// <summary>
	/// Raised on the UI thread after the culture has changed, for the parts of the
	/// interface that build their text in code instead of in XAML and therefore cannot
	/// pick the new language up through a binding.
	/// </summary>
	public static event Action LanguageChanged;

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

	/// <summary>Applies the stored language at startup. No listeners exist yet, so nothing is notified.</summary>
	public static void Initialize(string language)
	{
		Apply(language, notify: false);
	}

	/// <summary>
	/// Switches the running client to <paramref name="language"/>. XAML labels follow
	/// through <see cref="TranslationSource"/>; <see cref="LanguageChanged"/> covers the
	/// rest. Restarting is no longer needed.
	/// </summary>
	public static void SetLanguage(string language)
	{
		if (Normalize(language) == Language) return;
		Apply(language, notify: true);
	}

	private static void Apply(string language, bool notify)
	{
		language = Normalize(language);
		Settings.Default.UserLanguage = language;
		Settings.Default.Save();
		Culture = CultureInfo.CreateSpecificCulture(language);

		if (notify)
		{
			TranslationSource.InvalidateAll();
			RefreshUserInterface();
			try
			{
				LanguageChanged?.Invoke();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		Log.Debug("Lang: " + language);
	}

	/// <summary>
	/// Repaints what a binding cannot reach: the view models with computed text, the
	/// cached category tree, the rendered spot pages, and the rows of the two grids.
	/// Everything declared in XAML follows <see cref="TranslationSource"/> on its own.
	/// </summary>
	private static void RefreshUserInterface()
	{
		System.Windows.Threading.Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null) return;
		if (!dispatcher.CheckAccess())
		{
			dispatcher.Invoke(RefreshUserInterface);
			return;
		}

		try
		{
			// Node names come out of the Categories resources, so the tree the filter
			// and new-spot dialogs share is only valid for the language it was built in.
			FilterCatViewModel.ResetCache();

			// A null property name means "re-read everything" to WPF, which is exactly
			// what these view models need: their labels are computed in the getters.
			if (Application.Current.Resources["Locator"] is ViewModelLocator locator)
			{
				locator.MainWindow.RaisePropertyChanged(null);
				locator.StatusBar.RaisePropertyChanged(null);
				locator.SpotsList.RaisePropertyChanged(null);
				locator.Visibility.RaisePropertyChanged(null);
				locator.SocksProxyTooltip.RaisePropertyChanged(null);
			}

			// Queued downloads sit idle without raising anything, so their status column
			// would keep the old language until the next progress tick.
			DownloaderItemViewModel[] downloads = Sys.Downloader?.Items?.ToArray();
			if (downloads != null)
			{
				foreach (DownloaderItemViewModel item in downloads)
				{
					item.NotifyPropertyChanged(null);
				}
			}

			// Spot pages are rendered HTML with translated labels baked in, and the list
			// rows carry translated category and format text.
			Sys.MainWindow?.RetranslateSearchTab();
			Sys.MainWindow?.ReloadAllSpotPages();
			Sys.MainWindow?.RefreshSpotsList(force: true);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	private static string Normalize(string language)
	{
		if (language == null || !LocalesSupported.Contains(language))
		{
			return DefaultLanguage;
		}
		return language;
	}
}
