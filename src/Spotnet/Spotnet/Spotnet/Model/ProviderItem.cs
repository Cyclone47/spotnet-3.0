using System;
using Spotnet.Properties;

namespace Spotnet.Model;

/// <summary>One row in the provider dropdown.</summary>
/// <remarks>
/// These were public fields in 2.0, when the dropdown only ever called ToString(). The redesigned
/// dialog binds a real item template and groups the list, and WPF bindings do not see fields.
/// </remarks>
internal class ProviderItem
{
	public string Name { get; set; }

	public string Download { get; set; }

	public string Upload { get; set; }

	public string Headers { get; set; }

	public int DownloadPort { get; set; }

	public int UploadPort { get; set; }

	public int HeadersPort { get; set; }

	/// <summary>Which section of the provider dropdown this entry is listed under.</summary>
	public string Group { get; set; }

	/// <summary>The "Other..." entry, which carries no servers and opens the advanced panel instead.</summary>
	public bool IsManual { get; set; }

	public ProviderItem()
	{
		Name = "";
		Download = "";
		Upload = "";
		Headers = "";
		Group = UsenetProviders.Manual;
	}

	/// <summary>The localized dropdown section header. Grouping binds to this, so it must be a property.</summary>
	public string GroupDisplayName
	{
		get
		{
			switch (Group)
			{
				case UsenetProviders.Netherlands: return Words.ProviderGroupNetherlands;
				case UsenetProviders.International: return Words.ProviderGroupInternational;
				default: return Words.ProviderGroupManual;
			}
		}
	}

	/// <summary>Shown under the provider name, so the server is visible before connecting.</summary>
	public string Subtitle => IsManual || string.IsNullOrEmpty(Headers)
		? Words.ProviderManualSubtitle
		: Headers + ":" + HeadersPort;

	/// <summary>Matched against what the user types, so "eweka", "reader." and "farm" all narrow the list.</summary>
	public bool Matches(string term)
	{
		if (string.IsNullOrWhiteSpace(term)) return true;
		term = term.Trim();
		return Contains(Name, term) || Contains(Headers, term) || Contains(Download, term) || Contains(Upload, term);
	}

	private static bool Contains(string value, string term) =>
		!string.IsNullOrEmpty(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

	public override string ToString()
	{
		return Name;
	}
}
