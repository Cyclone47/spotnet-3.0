using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Spotnet.Deployment;

/// <summary>Network lookup is bounded; the user's decision is deliberately not.</summary>
internal sealed class StartupUpdateGate
{
    internal bool Answered { get; private set; }

    internal async Task<bool> RunAsync(
        Func<CancellationToken, Task<(UpdateManifest Manifest, UpdateDecision Decision, string Error)>> check,
        Func<UpdateManifest, UpdateDecision, Task<bool>> prompt,
        TimeSpan budget)
    {
        (UpdateManifest Manifest, UpdateDecision Decision, string Error) result;
        using var deadline = new CancellationTokenSource(budget);
        try
        {
            result = await check(deadline.Token).WaitAsync(deadline.Token);
            Answered = result.Error == null;
            if (!Answered)
                LogManager.GetCurrentClassLogger().Debug("Startup update check: {0}", result.Error);
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Debug("Startup update check unavailable: {0}", ex.Message);
            return true;
        }
        if (result.Error != null || result.Manifest == null || !result.Decision.ShouldPrompt) return true;
        // Do not treat a failed dialog as permission to start. Only the network check
        // fails open; the explicit decision must complete before app initialization.
        return await prompt(result.Manifest, result.Decision);
    }
}
