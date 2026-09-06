using System;
using System.Collections.Generic;
using Spotnet.Helpers;

namespace Spotnet.Localization;

/// <summary>
/// Translates standard/default filter names dynamically for display in the UI.
/// The underlying filter XML file remains untouched on disk, so custom user modifications
/// and queries are preserved while the UI adapts live to the chosen language.
/// </summary>
public static class FilterTranslationHelper
{
	// Case-insensitive dictionary for Dutch -> English
	private static readonly Dictionary<string, string> NlToEn = new(StringComparer.OrdinalIgnoreCase)
	{
		// Top-level & main categories
		{ "Favorieten", "Favorites" },
		{ "Favorites", "Favorites" },
		{ "Nieuw", "New" },
		{ "Overzicht", "Overview" },
		{ "All", "Overview" },
		{ "Films", "Movies" },
		{ "Series", "Series" },
		{ "Boeken", "Books" },
		{ "Muziek", "Music" },
		{ "Audio", "Music" },
		{ "Spellen", "Games" },
		{ "Applicaties", "Applications" },
		{ "Software", "Applications" },
		{ "Erotiek", "Erotica" },
		{ "Laatste 24 uur", "Last 24 hours" },
		{ "Vandaag", "Today" },
		{ "Movies Vandaag", "Movies Today" },
		{ "Series Vandaag", "Series Today" },
		{ "Books Vandaag", "Books Today" },
		{ "Audio Vandaag", "Audio Today" },
		{ "Games Vandaag", "Games Today" },
		{ "Software Vandaag", "Software Today" },
		{ "Erotica Vandaag", "Erotica Today" },
		{ "Beeld", "Movies" },
		{ "Beeld - Genres", "Movies - Genres" },
		{ "Beeld - TV Series", "Movies - TV Series" },
		{ "Muziek - Genres", "Music - Genres" },
		{ "Spellen - Console", "Games - Console" },
		{ "Spellen - Mobile", "Games - Mobile" },
		{ "Applicaties - Mobile", "Applications - Mobile" },
		{ "Software - Mobile", "Applications - Mobile" },

		// Genres & sub-filters
		{ "Actie", "Action" },
		{ "Avontuur", "Adventure" },
		{ "Cabaret", "Cabaret" },
		{ "Comedy", "Comedy" },
		{ "Komedie", "Comedy" },
		{ "Documentaire", "Documentary" },
		{ "Drama", "Drama" },
		{ "Horror", "Horror" },
		{ "Kerst", "Christmas" },
		{ "Kerstmis", "Christmas" },
		{ "Kids", "Kids" },
		{ "Kinderen", "Kids" },
		{ "Muziek DVD", "Music DVD" },
		{ "Oorlog", "War" },
		{ "Science Fiction", "Science Fiction" },
		{ "Sport", "Sport" },
		{ "Thriller", "Thriller" },
		{ "Animatie", "Animation" },
		{ "Misdaad", "Crime" },
		{ "Romantiek", "Romance" },
		{ "Familie", "Family" },
		{ "Fantasy", "Fantasy" },
		{ "Western", "Western" },

		// Books sub-filters
		{ "Boeken NL", "Books NL" },
		{ "CD/DVD Covers", "CD/DVD Covers" },
		{ "CD/DVD Hoezen", "CD/DVD Covers" },
		{ "Epub", "Epub" },
		{ "Knipvellen", "Cuttingsheets" },
		{ "Magazines", "Magazines" },
		{ "Tijdschriften", "Magazines" },
		{ "Strips", "Comics" },

		// Music sub-filters
		{ "Gecomprimeerd", "Compressed" },
		{ "Compressed", "Compressed" },
		{ "Discografie", "Discography" },
		{ "Lossless", "Lossless" },
		{ "Luisterboeken", "Audiobooks" },
		{ "Klassiek", "Classics" },
		{ "Hollands", "Dutch" },
		{ "Nederlands", "Dutch" },
		{ "Diversen", "Miscellaneous" },

		// Software sub-filters
		{ "Navigatie", "Navigators" },

		// Erotica sub-filters
		{ "Hetero", "Hetero" },
		{ "Homo", "Gay" },
		{ "Gay-mannen", "Gay Men" },
		{ "Lesbo", "Lesbian" },
		{ "Gay-vrouwen", "Lesbian" },
		{ "Bi", "Bi" },
		{ "Afbeeldingen", "Pictures" },
		{ "3D Films", "3D Movies" },

		// Filter set names
		{ "Aangepast", "Custom" },
	};

	// Case-insensitive dictionary for English -> Dutch
	private static readonly Dictionary<string, string> EnToNl = new(StringComparer.OrdinalIgnoreCase)
	{
		// Top-level & main categories
		{ "Favorites", "Favorieten" },
		{ "Favorieten", "Favorieten" },
		{ "New", "Nieuw" },
		{ "Overview", "Overzicht" },
		{ "All", "Overzicht" },
		{ "Movies", "Films" },
		{ "Series", "Series" },
		{ "Books", "Boeken" },
		{ "Music", "Muziek" },
		{ "Audio", "Muziek" },
		{ "Games", "Spellen" },
		{ "Applications", "Applicaties" },
		{ "Software", "Applicaties" },
		{ "Erotica", "Erotiek" },
		{ "Last 24 hours", "Laatste 24 uur" },
		{ "Today", "Vandaag" },
		{ "Movies Today", "Movies Vandaag" },
		{ "Series Today", "Series Vandaag" },
		{ "Books Today", "Books Vandaag" },
		{ "Audio Today", "Audio Vandaag" },
		{ "Games Today", "Games Vandaag" },
		{ "Software Today", "Software Vandaag" },
		{ "Erotica Today", "Erotica Vandaag" },
		{ "Movies - Genres", "Beeld - Genres" },
		{ "Movies - TV Series", "Beeld - TV Series" },
		{ "Music - Genres", "Muziek - Genres" },
		{ "Games - Console", "Spellen - Console" },
		{ "Games - Mobile", "Spellen - Mobile" },
		{ "Applications - Mobile", "Applicaties - Mobile" },
		{ "Software - Mobile", "Applicaties - Mobile" },

		// Genres & sub-filters
		{ "Action", "Actie" },
		{ "Adventure", "Avontuur" },
		{ "Cabaret", "Cabaret" },
		{ "Comedy", "Comedy" },
		{ "Documentary", "Documentaire" },
		{ "Drama", "Drama" },
		{ "Horror", "Horror" },
		{ "Christmas", "Kerst" },
		{ "Kids", "Kids" },
		{ "Music DVD", "Muziek DVD" },
		{ "War", "Oorlog" },
		{ "Science Fiction", "Science Fiction" },
		{ "Sport", "Sport" },
		{ "Thriller", "Thriller" },
		{ "Animation", "Animatie" },
		{ "Crime", "Misdaad" },
		{ "Romance", "Romantiek" },
		{ "Family", "Familie" },
		{ "Fantasy", "Fantasy" },
		{ "Western", "Western" },

		// Books sub-filters
		{ "Books NL", "Boeken NL" },
		{ "CD/DVD Covers", "CD/DVD Covers" },
		{ "Cuttingsheets", "Knipvellen" },
		{ "Magazines", "Magazines" },
		{ "Comics", "Strips" },

		// Music sub-filters
		{ "Compressed", "Compressed" },
		{ "Discography", "Discografie" },
		{ "Lossless", "Lossless" },
		{ "Audiobooks", "Luisterboeken" },
		{ "Classics", "Klassiek" },
		{ "Dutch", "Nederlands" },
		{ "Miscellaneous", "Diversen" },

		// Software sub-filters
		{ "Navigators", "Navigatie" },

		// Erotica sub-filters
		{ "Hetero", "Hetero" },
		{ "Gay", "Homo" },
		{ "Gay Men", "Gay-mannen" },
		{ "Lesbian", "Lesbo" },
		{ "Bi", "Bi" },
		{ "Pictures", "Afbeeldingen" },
		{ "3D Movies", "3D Films" },

		// Filter set names
		{ "Custom", "Aangepast" },
	};

	/// <summary>
	/// Returns the translated filter name according to the active language,
	/// preserving any leading or trailing whitespace.
	/// </summary>
	public static string GetTranslatedName(string rawName)
	{
		if (string.IsNullOrEmpty(rawName)) return rawName;

		string trimmed = rawName.Trim();
		bool isEnglish = UserLanguageHelper.Language == "en";

		string translated = null;
		if (isEnglish)
		{
			NlToEn.TryGetValue(trimmed, out translated);
		}
		else
		{
			EnToNl.TryGetValue(trimmed, out translated);
		}

		if (translated == null) return rawName;

		int leadingSpaces = 0;
		while (leadingSpaces < rawName.Length && char.IsWhiteSpace(rawName[leadingSpaces]))
		{
			leadingSpaces++;
		}
		int trailingSpaces = 0;
		while (trailingSpaces < rawName.Length - leadingSpaces && char.IsWhiteSpace(rawName[rawName.Length - 1 - trailingSpaces]))
		{
			trailingSpaces++;
		}

		if (leadingSpaces > 0 || trailingSpaces > 0)
		{
			return rawName.Substring(0, leadingSpaces) + translated + rawName.Substring(rawName.Length - trailingSpaces);
		}

		return translated;
	}

	/// <summary>
	/// Returns the localized display name for a filter set (e.g. "Aangepast" <=> "Custom").
	/// </summary>
	public static string GetTranslatedFilterSetName(string filterSetName)
	{
		if (string.IsNullOrEmpty(filterSetName)) return filterSetName;
		if (UserLanguageHelper.Language == "en")
		{
			if (string.Equals(filterSetName, "Aangepast", StringComparison.OrdinalIgnoreCase))
				return "Custom";
		}
		else
		{
			if (string.Equals(filterSetName, "Custom", StringComparison.OrdinalIgnoreCase))
				return "Aangepast";
		}
		return filterSetName;
	}
}
