using System;

namespace Spotnet.Deployment;

/// <summary>What the client should do about a manifest it just read.</summary>
internal enum UpdateAction
{
    /// <summary>Nothing to offer. <see cref="UpdateDecision.Reason"/> says why, for the log.</summary>
    None,
    /// <summary>Offer it; the user may decline or skip the version.</summary>
    Offer,
    /// <summary>Offer it without a Skip button: the publisher marked it required.</summary>
    Required,
}

internal readonly struct UpdateDecision
{
    internal UpdateDecision(UpdateAction action, string reason)
    {
        Action = action;
        Reason = reason;
    }

    internal UpdateAction Action { get; }

    internal string Reason { get; }

    internal bool ShouldPrompt => Action != UpdateAction.None;
}

/// <summary>
/// The rules that decide whether a published build is offered to this client. Kept apart
/// from the download and the window so they can be read, and tested, on their own.
/// </summary>
internal static class UpdatePolicy
{
    internal static UpdateDecision Evaluate(UpdateManifest manifest, Version current, string skippedVersion)
    {
        if (manifest == null)
        {
            return new UpdateDecision(UpdateAction.None, "No manifest.");
        }
        if (!manifest.ClientUpdate)
        {
            return new UpdateDecision(UpdateAction.None, $"{manifest.Version} is published but not released to clients.");
        }
        if (current == null)
        {
            return new UpdateDecision(UpdateAction.None, "The running version is unknown.");
        }
        if (manifest.Version <= current)
        {
            return new UpdateDecision(UpdateAction.None, $"{manifest.Version} is not newer than the running {current}.");
        }

        // A version the publisher requires, or one this build is too old to be left on,
        // is offered even if the user skipped it earlier.
        if (manifest.Forced || current < manifest.MinimumVersion)
        {
            return new UpdateDecision(UpdateAction.Required, $"{manifest.Version} is required.");
        }

        if (!string.IsNullOrWhiteSpace(skippedVersion)
            && Version.TryParse(skippedVersion.Trim(), out Version skipped)
            && skipped >= manifest.Version)
        {
            return new UpdateDecision(UpdateAction.None, $"The user skipped {manifest.Version}.");
        }

        return new UpdateDecision(UpdateAction.Offer, $"{manifest.Version} is available.");
    }
}
