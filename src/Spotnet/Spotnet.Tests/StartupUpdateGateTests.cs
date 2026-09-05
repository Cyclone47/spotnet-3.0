using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Deployment;
using Xunit;

namespace Spotnet.Tests;

public sealed class StartupUpdateGateTests
{
    private static UpdateManifest Release()
    {
        string json = "{\"schema\":1,\"clientUpdate\":1,\"version\":\"3.0.99.0\",\"size\":100," +
            "\"sha256\":\"" + new string('a', 64) + "\",\"url\":\"https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.99/Setup.exe\"}";
        Assert.True(UpdateManifest.TryParse(json, out var release, out _));
        return release;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task StartupWaitsForTheCheckAndThenForTheUser(bool required, bool proceed)
    {
        var check = new TaskCompletionSource<(UpdateManifest, UpdateDecision, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new StartupUpdateGate();
        Task<bool> startup = gate.RunAsync(_ => check.Task, (_, _) => {
            prompted.SetResult(true);
            return choice.Task;
        }, TimeSpan.FromSeconds(5));
        Assert.False(startup.IsCompleted);
        Assert.False(prompted.Task.IsCompleted);
        check.SetResult((Release(), new UpdateDecision(required ? UpdateAction.Required : UpdateAction.Offer, "new version"), null));
        await prompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(startup.IsCompleted);
        choice.SetResult(proceed);
        Assert.Equal(proceed, await startup);
        Assert.True(gate.Answered);
    }

    [Fact]
    public async Task OfflineTimeoutCancelsLookupAndAllowsStartup()
    {
        CancellationToken observed = default;
        var gate = new StartupUpdateGate();
        bool proceed = await gate.RunAsync(async token => {
            observed = token;
            await Task.Delay(Timeout.Infinite, token);
            return (null, default, null);
        }, (_, _) => throw new Exception("Must not prompt"), TimeSpan.FromMilliseconds(30));
        Assert.True(proceed);
        Assert.True(observed.IsCancellationRequested);
        Assert.False(gate.Answered);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("server unavailable", false)]
    public async Task NoOfferContinuesWithoutPrompting(string error, bool answered)
    {
        var gate = new StartupUpdateGate();
        Assert.True(await gate.RunAsync(_ => Task.FromResult((Release(),
            new UpdateDecision(UpdateAction.None, "not offered"), error)),
            (_, _) => throw new Exception("Must not prompt"), TimeSpan.FromSeconds(1)));
        Assert.Equal(answered, gate.Answered);
    }

    [Fact]
    public async Task ADialogFailureDoesNotSilentlyStartTheApplication()
    {
        var gate = new StartupUpdateGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(_ => Task.FromResult((Release(),
            new UpdateDecision(UpdateAction.Offer, "new version"), (string)null)),
            (_, _) => throw new InvalidOperationException("Cannot show decision"), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WpfCannotCreateTheMainWindowBeforeTheStartupGate()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Spotnet.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string xaml = File.ReadAllText(Path.Combine(root.FullName, "Spotnet", "app.xaml"));
        Assert.DoesNotContain("StartupUri=", xaml);
        string startup = File.ReadAllText(Path.Combine(root.FullName, "Spotnet", "Spotnet", "App.cs"));
        int gate = startup.IndexOf("await AppUpdater.CheckOnStartupAsync", StringComparison.Ordinal);
        int main = startup.IndexOf("new Views.MainWindow()", StringComparison.Ordinal);
        Assert.True(gate >= 0 && main > gate);
        Assert.Contains("if (!proceed || Sys.IsShutdownRequested) return;", startup.Substring(gate, main - gate));
    }
}
