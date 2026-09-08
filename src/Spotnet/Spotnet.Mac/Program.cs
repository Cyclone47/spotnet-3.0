using Avalonia;
using System;
using System.Collections.Generic;
using Spotnet.Mac.Platform;

namespace Spotnet.Mac;

internal sealed class Program
{
    /// <summary>
    /// Actionable launch arguments (.nzb file paths, spotnet:// links) captured before
    /// the UI starts, consumed by MainWindow after it initializes.
    /// </summary>
    public static IReadOnlyList<string> StartupTargets { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static void Main(string[] args)
    {
        StartupTargets = MacStartupArguments.GetStartupTargets(args ?? Array.Empty<string>());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
