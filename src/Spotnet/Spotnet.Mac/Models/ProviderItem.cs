using System;

namespace Spotnet.Mac.Models;

/// <summary>One row in the provider dropdown.</summary>
public class ProviderItem
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

    /// <summary>The "Other..." entry, which carries no servers and opens the manual inputs.</summary>
    public bool IsManual { get; set; }

    public ProviderItem()
    {
        Name = "";
        Download = "";
        Upload = "";
        Headers = "";
        Group = UsenetProviders.Manual;
    }

    /// <summary>The localized dropdown section header.</summary>
    public string GroupDisplayName
    {
        get
        {
            switch (Group)
            {
                case UsenetProviders.Netherlands: return "Nederlandse providers";
                case UsenetProviders.International: return "Internationale providers";
                default: return "Handmatig";
            }
        }
    }

    /// <summary>Shown under the provider name, so the server is visible before connecting.</summary>
    public string Subtitle => IsManual || string.IsNullOrEmpty(Headers)
        ? "Voer de servergegevens zelf in"
        : Headers + ":" + HeadersPort;

    /// <summary>Matched against what the user types, so "eweka", "reader." and "farm" all narrow the list.</summary>
    public bool Matches(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;
        term = term.Trim();
        return Contains(Name, term) || Contains(Headers, term) || Contains(Download, term) || Contains(Upload, term);
    }

    private static bool Contains(string value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}
