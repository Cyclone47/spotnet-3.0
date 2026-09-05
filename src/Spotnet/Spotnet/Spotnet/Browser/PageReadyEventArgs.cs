using System;

namespace Spotnet.Browser;

/// <summary>
/// How far a page has got in loading.
/// </summary>
/// <remarks>
/// Mirrors the two browser states this application actually reacts to without exposing
/// an engine-specific event type through <see cref="IPage"/>.
/// </remarks>
public enum PageReadyState
{
	/// <summary>The document object is available; subresources may still be loading.</summary>
	Ready,

	/// <summary>The document and its subresources have finished loading.</summary>
	Loaded
}

public sealed class PageReadyEventArgs : EventArgs
{
	public PageReadyEventArgs(PageReadyState readyState)
	{
		ReadyState = readyState;
	}

	public PageReadyState ReadyState { get; }
}
