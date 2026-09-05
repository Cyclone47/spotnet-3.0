using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spotnet.Mac.Platform;

/// <summary>
/// Classifies command line arguments the way Windows' RunPipeParameterProcessingAsync
/// does: spotnet:// URLs open the spot, .nzb file paths schedule the binary download.
/// </summary>
public static class MacStartupArguments
{
    /// <summary>An argument that is neither an .nzb path nor a spotnet:// URL.</summary>
    public const string? None = null;

    /// <summary>
    /// Windows' RunPipeParameterProcessingAsync: anything not starting with
    /// "spotnet://" is treated as an NZB file path. Returns the argument or null.
    /// </summary>
    public static string? Classify(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return None;
        string trimmed = arg.Trim('"');
        if (trimmed.StartsWith("spotnet://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        if (trimmed.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed))
        {
            return trimmed;
        }
        return None;
    }

    /// <summary>Returns the startup arguments that are NZB paths or spotnet:// links.</summary>
    public static IReadOnlyList<string> GetStartupTargets(string[] args)
    {
        return (args ?? Array.Empty<string>())
            .Select(Classify)
            .Where(a => a != null)
            .Cast<string>()
            .ToList();
    }
}
