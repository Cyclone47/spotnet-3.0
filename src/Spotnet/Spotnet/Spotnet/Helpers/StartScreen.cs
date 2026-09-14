using System;

namespace Spotnet.Helpers;

/// <summary>
/// The values stored in <see cref="Properties.Settings.DefaultStartScreen" />: which main-window
/// tab the app opens on.
/// </summary>
/// <remarks>
/// Kept as strings rather than an <see langword="enum" /> because that is how
/// <c>ApplicationSettingsBase</c> persists them, and a string degrades gracefully: a value written
/// by a newer build reads as "not Downloads" and falls back to the historical Overzicht start
/// instead of throwing on deserialization.
/// </remarks>
internal static class StartScreen
{
	/// <summary>The spots list - "Overzicht" in Dutch. The historical and default start.</summary>
	internal const string Spots = "Spots";

	/// <summary>The downloads tab.</summary>
	internal const string Downloads = "Downloads";

	internal static bool IsDownloads(string value)
	{
		return Downloads.Equals(value, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Normalizes a stored value to one of the two known screens, so callers never have to
	/// handle an unrecognized string.
	/// </summary>
	internal static string Normalize(string value)
	{
		return IsDownloads(value) ? Downloads : Spots;
	}
}
