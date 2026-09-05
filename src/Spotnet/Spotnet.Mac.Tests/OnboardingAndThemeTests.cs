using System;
using System.IO;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public sealed class OnboardingAndThemeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StandardAppPaths _appPaths;
    private readonly MacKeychainSecretStore _secretStore;

    public OnboardingAndThemeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpotnetTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _appPaths = new StandardAppPaths(_tempDir);
        _secretStore = new MacKeychainSecretStore();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void UserPreferencesService_SavesAndLoadsCorrectly()
    {
        var service = new UserPreferencesService(_appPaths);
        Assert.False(service.Current.IsOnboardingCompleted);
        Assert.Equal(AppThemeStyle.ModernLight, service.Current.ThemeStyle);

        // Update preferences
        var prefs = service.Current;
        prefs.IsOnboardingCompleted = true;
        prefs.ThemeStyle = AppThemeStyle.Classic;
        prefs.SelectedProvider = "ViperNews";
        service.Save(prefs);

        // Create new service reading same folder
        var reloaded = new UserPreferencesService(_appPaths);
        Assert.True(reloaded.Current.IsOnboardingCompleted);
        Assert.Equal(AppThemeStyle.Classic, reloaded.Current.ThemeStyle);
        Assert.Equal("ViperNews", reloaded.Current.SelectedProvider);
    }

    [Fact]
    public void ThemeService_AppliesStylesAndFiresEvent()
    {
        var service = ThemeService.Instance;
        AppThemeStyle? notified = null;
        service.ThemeChanged += style => notified = style;

        service.ApplyTheme(AppThemeStyle.ModernDark);
        Assert.Equal(AppThemeStyle.ModernDark, service.CurrentStyle);
        Assert.Equal(AppThemeStyle.ModernDark, notified);

        service.ApplyTheme(AppThemeStyle.Classic);
        Assert.Equal(AppThemeStyle.Classic, service.CurrentStyle);
        Assert.Equal(AppThemeStyle.Classic, notified);

        service.ApplyTheme(AppThemeStyle.ModernLight);
        Assert.Equal(AppThemeStyle.ModernLight, service.CurrentStyle);
        Assert.Equal(AppThemeStyle.ModernLight, notified);
    }

    [Fact]
    public void OnboardingViewModel_StepProgressionAndPresetSelection()
    {
        var prefsService = new UserPreferencesService(_appPaths);
        var vm = new OnboardingViewModel(_appPaths, _secretStore, prefsService);

        // Initial state
        Assert.Equal(1, vm.CurrentStep);
        Assert.True(vm.IsStep1);
        Assert.False(vm.CanGoBack);
        Assert.True(vm.CanGoNext);

        // Step 1: Select Style
        vm.IsClassicSelected = true;
        Assert.Equal(AppThemeStyle.Classic, vm.SelectedStyle);

        // Move to Step 2
        vm.NextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.IsStep2);
        Assert.True(vm.CanGoBack);
        Assert.True(vm.CanGoNext);

        // Step 2: Select Provider
        vm.SelectedProvider = "Newshosting";
        Assert.Equal("news.newshosting.com", vm.Server);
        Assert.Equal(563, vm.Port);
        Assert.True(vm.Ssl);
        Assert.Equal(20, vm.Connections);

        // Move to Step 3
        vm.NextCommand.Execute(null);
        Assert.Equal(3, vm.CurrentStep);
        Assert.True(vm.IsStep3);
        Assert.True(vm.CanGoBack);
        Assert.False(vm.CanGoNext);

        // Finish onboarding
        bool finishedFired = false;
        vm.OnboardingFinished += () => finishedFired = true;

        vm.Password = "TestSecret123";
        vm.FinishCommand.Execute(null);

        Assert.True(finishedFired);

        // Check preferences were saved
        var reloadedPrefs = new UserPreferencesService(_appPaths);
        Assert.True(reloadedPrefs.Current.IsOnboardingCompleted);
        Assert.Equal(AppThemeStyle.Classic, reloadedPrefs.Current.ThemeStyle);
        Assert.Equal("Newshosting", reloadedPrefs.Current.SelectedProvider);

        // Check servers.xml was written
        string serversXml = Path.Combine(_appPaths.DataFolder, "servers.xml");
        Assert.True(File.Exists(serversXml));
        string xmlContent = File.ReadAllText(serversXml);
        Assert.Contains("news.newshosting.com", xmlContent);
    }
}
