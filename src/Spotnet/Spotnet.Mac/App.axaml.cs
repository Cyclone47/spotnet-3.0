using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Mac.Views;
using Spotnet.Platform;

namespace Spotnet.Mac;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appPaths = new StandardAppPaths();
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
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}