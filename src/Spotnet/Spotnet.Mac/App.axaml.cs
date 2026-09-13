using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Mac.Updates;
using Spotnet.Mac.ViewModels;
using Spotnet.Mac.Views;
using Spotnet.Platform;

namespace Spotnet.Mac;

public partial class App : Application
{
    private MacUpdater? _onboardingUpdater;
    private bool _onboardingUpdateChecked;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnOnboardingOpened(object? sender, System.EventArgs e)
    {
        if (_onboardingUpdateChecked || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            || sender is not OnboardingWindow onboardingWindow)
        {
            return;
        }

        _onboardingUpdateChecked = true;
        _onboardingUpdater = new MacUpdater(
            new StandardAppPaths().DataFolder,
            applicationPath: AppContext.BaseDirectory);

        try
        {
            var result = await _onboardingUpdater.CheckAsync(MacUpdateVersion.Current);
            if (result.Decision.ShouldPrompt && result.Manifest != null)
            {
                var updateWindow = new MacUpdateWindow(
                    _onboardingUpdater, result.Manifest, result.Decision);
                await updateWindow.ShowDialog(onboardingWindow);
            }
        }
        catch
        {
            // Startup updates must never prevent onboarding from opening.
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appPaths = new StandardAppPaths();
            Spotnet.Mac.Models.ProviderCatalogueSource.AppPaths = appPaths;
            _ = Spotnet.Mac.Models.ProviderCatalogueSource.RefreshAsync();
            var prefsService = new UserPreferencesService(appPaths);
            var secretStore = new MacKeychainSecretStore();

            // Apply saved theme style
            ThemeService.Instance.ApplyTheme(prefsService.Current.ThemeStyle);

            string serversXmlPath = Path.Combine(appPaths.DataFolder, "servers.xml");
            bool needsOnboarding = !prefsService.Current.IsOnboardingCompleted || !File.Exists(serversXmlPath);

            if (needsOnboarding)
            {
                var onboardingVm = new OnboardingViewModel(appPaths, secretStore, prefsService);
                var onboardingWindow = new OnboardingWindow(onboardingVm);

                onboardingVm.OnboardingFinished += () =>
                {
                    var mainWindow = new MainWindow();
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    onboardingWindow.Close();
                };

                desktop.MainWindow = onboardingWindow;
                onboardingWindow.Opened += OnOnboardingOpened;
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}