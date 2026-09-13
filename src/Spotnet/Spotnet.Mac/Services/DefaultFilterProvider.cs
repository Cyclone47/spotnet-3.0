using System.Collections.Generic;
using Spotnet.Mac.Models;

namespace Spotnet.Mac.Services;

/// <summary>
/// The bundled advanced-filter tree — the same <c>FiltersAdvanced</c> XML the Windows
/// client ships. Parsing and the icon mapping live in <see cref="FilterSetService"/>
/// now, which reads and writes the same format from Filters.v2 on disk.
/// </summary>
public static class DefaultFilterProvider
{
    public static List<FilterItem> Load() => FilterSetService.DefaultTree("Geavanceerd NL");
}
