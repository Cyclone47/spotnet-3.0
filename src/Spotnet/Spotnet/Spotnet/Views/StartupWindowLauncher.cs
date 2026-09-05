using System;
using System.Windows;

namespace Spotnet.Views;

internal static class StartupWindowLauncher
{
    internal static void CreateMainWindow(Application application, Func<Window> create)
    {
        // The window owns its readiness/visibility. Show() would override Hidden
        // and raise Loaded while its asynchronous server initialization is pending.
        application.MainWindow = create();
    }
}
