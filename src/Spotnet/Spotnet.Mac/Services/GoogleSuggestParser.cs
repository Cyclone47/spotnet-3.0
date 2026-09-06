using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Spotnet.Mac.Services;

/// <summary>
/// Parseert het XML-antwoord van Google's suggestie-eindpunt
/// (<c>complete/search?hl=nl&amp;output=toolbar</c>), zoals Windows'
/// <c>Suggest_DownloadStringCompleted</c>: elk <c>CompleteSuggestion</c>-element
/// levert één <c>suggestion/@data</c>-attribuut op.
/// </summary>
public static class GoogleSuggestParser
{
    /// <summary>
    /// Geeft de suggesties, of een lege lijst bij een onleesbaar antwoord. De parser
    /// lost geen externe entiteiten op, zoals het origineel.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? xml)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return result;
        }

        try
        {
            var document = new XmlDocument
            {
                XmlResolver = null
            };
            document.LoadXml(xml);
            foreach (XmlNode item in document.SelectNodes("//CompleteSuggestion")!)
            {
                if (item.SelectSingleNode("suggestion/@data") is XmlNode data
                    && !string.IsNullOrEmpty(data.Value))
                {
                    result.Add(data.Value);
                }
            }
        }
        catch (XmlException)
        {
            // Geen geldige XML — geen suggesties.
        }

        return result;
    }
}
