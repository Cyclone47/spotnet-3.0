using System;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

/// <summary>
/// Validates and sanitizes URLs before they are opened by the shell or browser.
/// </summary>
public static class WebLinkHelper
{
	/// <summary>
	/// Decides whether a link out of a spot page may leave the application, and what should
	/// be opened if so. Only absolute http and https links are permitted.
	/// </summary>
	public static bool TryResolveWebLink(string? url, out string? target)
	{
		target = null;
		if (string.IsNullOrWhiteSpace(url))
		{
			return false;
		}
		string candidate = url.Trim();
		if (candidate.EqualsIgnoreCase("undefined"))
		{
			return false;
		}
		if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed)
			|| (!parsed.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp)
				&& !parsed.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
		{
			return false;
		}
		target = candidate;
		return true;
	}
}
