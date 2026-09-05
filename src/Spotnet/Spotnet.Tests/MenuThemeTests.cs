using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Data;
using System.Threading.Tasks;
using Spotnet.Controls;
using Spotnet.ViewModel;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests;

[CollectionDefinition("WPF menu resources", DisableParallelization = true)]
public sealed class MenuThemeCollection { }

[Collection("WPF menu resources")]
public sealed class MenuThemeTests
{
    private static ResourceDictionary Load(string path) => new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/" + path, UriKind.Absolute)
    };

    private static void Layout(FrameworkElement element)
    {
        element.ApplyTemplate();
        element.Measure(new Size(800, 1200));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }

    private static Color ColorOf(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static double Luminance(Color color)
    {
        double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static void Readable(Brush foreground, Brush background)
    {
        Color fg = ColorOf(foreground), bg = ColorOf(background);
        Assert.Equal(255, fg.A);
        Assert.Equal(255, bg.A);
        double a = Luminance(fg), b = Luminance(bg);
        double contrast = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        Assert.True(contrast >= 4.5, $"Unreadable menu colors {fg} on {bg}: {contrast:F2}:1");
    }

    private static void Highlight(MenuItem item, bool value) => typeof(MenuItem)
        .GetProperty(nameof(MenuItem.IsHighlighted)).GetSetMethod(true).Invoke(item, new object[] { value });

    private static Border PopupSurface(MenuItem item)
    {
        item.ApplyTemplate();
        var popup = Assert.IsType<Popup>(item.Template.FindName("PART_Popup", item));
        Assert.True(popup.Child is Border, $"{item.Header}: wrong popup {popup.Child?.GetType().Name}; style source={DependencyPropertyHelper.GetValueSource(item, FrameworkElement.StyleProperty).BaseValueSource}; template source={DependencyPropertyHelper.GetValueSource(item, Control.TemplateProperty).BaseValueSource}; expected style={ReferenceEquals(item.Style, Application.Current.FindResource(typeof(MenuItem)))}; expected template={ReferenceEquals(item.Template, Application.Current.FindResource("SpotnetMenuItemRow"))}; style setters={item.Style?.Setters.Count}");
        var border = Assert.IsType<Border>(popup.Child);
        Layout(border); // Real generated submenu containers, without showing a desktop window.
        return border;
    }

    private static void CheckDeferredStartupWindow(Application app)
    {
        Window previous = app.MainWindow;
        bool ready = false;
        bool loadedBeforeReady = false;
        int loaded = 0;
        // Real WPF lifecycle, without a Spotnet profile or visible desktop window.
        var window = new Window {
            Visibility = Visibility.Hidden, Opacity = 0, ShowInTaskbar = false,
            Width = 1, Height = 1, Left = -10000, Top = -10000
        };
        window.Loaded += (_, _) => {
            loadedBeforeReady |= !ready;
            loaded++;
        };
        try
        {
            Spotnet.Views.StartupWindowLauncher.CreateMainWindow(app, () => window);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(loadedBeforeReady, "Loaded ran before server settings were available.");
            Assert.Equal(0, loaded);
            Assert.False(window.IsVisible);
            ready = true;
            window.Visibility = Visibility.Visible;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, loaded);
        }
        finally
        {
            window.Close();
            app.MainWindow = previous;
        }
    }

    private static void CheckRemotePasswordSetup()
    {
        var config = new Spotnet.Remote.RemoteConfig { Enabled = false, RequireAuth = true };
        int prompts = 0;
        var settings = new SettingsForRemote(config, () => { prompts++; return null; });
        Layout(settings);
        Assert.True(((Border)settings.FindName("RemoteConfigPanel")).IsEnabled);
        Assert.True(((Button)settings.FindName("ChangePasswordButton")).IsEnabled);
        var enable = (CheckBox)settings.FindName("EnableRemoteCheckBox");
        enable.IsChecked = true;
        enable.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(1, prompts);
        Assert.False(enable.IsChecked);
        Assert.False(config.Enabled);
        Assert.Empty(config.PasswordHash);

        var dialog = new RemotePasswordWindow();
        try
        {
            Layout(dialog);
            Assert.IsType<PasswordBox>(dialog.FindName("PasswordInput"));
            Assert.IsType<PasswordBox>(dialog.FindName("ConfirmInput"));
            string output = Environment.GetEnvironmentVariable("SPOTNET_REMOTE_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                var content = (FrameworkElement)dialog.Content;
                dialog.Content = null;
                content.Margin = new Thickness(0);
                content.Width = 382;
                var surface = new Border { Background = Brushes.White, Padding = new Thickness(24), Child = content };
                Layout(surface);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(output);
                using var file = File.Create(System.IO.Path.Combine(output, "remote-password.png"));
                encoder.Save(file);
            }
        }
        finally { dialog.Close(); }
    }

    private static void CheckRow(MenuItem item, Brush background)
    {
        item.ApplyTemplate();
        var row = Assert.IsType<Border>(item.Template.FindName("RowBorder", item));
        Assert.Equal(ColorOf(background), ColorOf(row.Background));
        Assert.Equal(1.0, item.Opacity);
        Readable(item.Foreground, row.Background);
        var shortcut = Assert.IsType<TextBlock>(item.Template.FindName("ShortcutText", item));
        Assert.Equal(ColorOf(item.Foreground), ColorOf(shortcut.Foreground));
        var arrow = Assert.IsType<System.Windows.Shapes.Path>(item.Template.FindName("SubmenuArrow", item));
        Assert.Equal(ColorOf(item.Foreground), ColorOf(arrow.Fill));
        var check = Assert.IsType<System.Windows.Shapes.Path>(item.Template.FindName("Checkmark", item));
        Assert.Equal(ColorOf(item.Foreground), ColorOf(check.Stroke));
        Assert.Equal(item.IsChecked ? Visibility.Visible : Visibility.Collapsed, check.Visibility);
    }

    private static void SavePreview(MenuItem parent, string theme)
    {
        string output = Environment.GetEnvironmentVariable("SPOTNET_MENU_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) return;
        Border border = PopupSurface(parent);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(border.ActualWidth), (int)Math.Ceiling(border.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(border);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output);
        using (var stream = File.Create(System.IO.Path.Combine(output, "menu-" + theme + ".png"))) encoder.Save(stream);
    }

    [Fact]
    public void MenusSwitchLiveAndNewContextMenusUseTheCurrentTheme()
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            Application app = null;
            try
            {
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Match the production merge order, without starting Spotnet or loading a user profile.
                app.Resources.MergedDictionaries.Add(Load("MahApps.Metro;component/Styles/Controls.xaml"));
                app.Resources.MergedDictionaries.Add(Load("MahApps.Metro;component/Styles/Fonts.xaml"));
                app.Resources.MergedDictionaries.Add(Load("MahApps.Metro;component/Styles/Themes/Light.Blue.xaml"));
                app.Resources.MergedDictionaries.Add(Load("Spotnet;component/Style/MainMenuStyle.xaml"));
                var palette = Load("Spotnet;component/Style/classiclight.xaml");
                app.Resources.MergedDictionaries.Add(palette);

                var menu = new Menu();
                var parent = new MenuItem { Header = "_Settings" };
                var normal = new MenuItem { Header = "Provider settings", InputGestureText = "Ctrl+P" };
                var hovered = new MenuItem { Header = "Download folder", InputGestureText = "Ctrl+D" };
                var disabled = new MenuItem { Header = "Database update in progress", IsEnabled = false };
                var checkable = new MenuItem { Header = "Modern Dark", IsCheckable = true, IsChecked = true };
                var nested = new MenuItem { Header = "View options" };
                var deep = new MenuItem { Header = "Columns" };
                var leaf = new MenuItem { Header = "Show category", IsCheckable = true, IsChecked = true };
                var separator = new Separator();
                parent.Items.Add(normal);
                parent.Items.Add(hovered);
                parent.Items.Add(disabled);
                parent.Items.Add(separator);
                parent.Items.Add(checkable);
                parent.Items.Add(nested);
                nested.Items.Add(deep);
                deep.Items.Add(leaf);
                menu.Items.Add(parent);
                var toolbar = new ToolBar { Style = (Style)app.FindResource("SpotnetToolBarStyle") };
                toolbar.Items.Add(menu);
                // Application resource-change notifications are delivered through windows.
                // This host is never shown and never opens the real application/profile.
                var host = new Window { Content = toolbar, Width = 800, Height = 400 };

                foreach (string theme in new[] { "classiclight", "moderndark", "classiclight", "moderndark" })
                {
                    app.Resources.MergedDictionaries.Remove(palette);
                    palette = Load("Spotnet;component/Style/" + theme + ".xaml");
                    app.Resources.MergedDictionaries.Add(palette);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    Brush background = (Brush)palette["SpotnetMenuBackground"];
                    Brush highlighted = (Brush)palette["SpotnetMenuHighlightBackground"];
                    Layout(host);
                    Layout(toolbar);
                    Assert.True(menu.ItemContainerStyleSelector is MenuItemStyleSelector,
                        $"Toolbar Menu style source={DependencyPropertyHelper.GetValueSource(menu, FrameworkElement.StyleProperty).BaseValueSource}; selector={menu.ItemContainerStyleSelector}; container style={menu.ItemContainerStyle}");
                    Assert.Equal(ColorOf(background), ColorOf(PopupSurface(parent).Background));
                    Highlight(hovered, true);
                    CheckRow(parent, background);
                    CheckRow(normal, background);
                    CheckRow(hovered, highlighted);
                    CheckRow(disabled, background);
                    CheckRow(checkable, background);
                    Assert.Equal(ColorOf(background), ColorOf(PopupSurface(nested).Background));
                    Assert.Equal(ColorOf(background), ColorOf(PopupSurface(deep).Background));
                    CheckRow(nested, background);
                    CheckRow(deep, background);
                    CheckRow(leaf, background);
                    Assert.Equal(ColorOf((Brush)palette["SpotnetMenuBorder"]), ColorOf(separator.Background));
                    // The spot/download/filter handlers construct a new detached menu on demand.
                    var context = new ContextMenu { Resources = AppHelper.GetMenuResourceDictionary };
                    var contextItem = new MenuItem { Header = "Download spot", InputGestureText = "Enter" };
                    context.Items.Add(contextItem);
                    // Simulate colliding library aliases; our namespaced palette must win.
                    context.Resources["WhiteColorBrush"] = Brushes.White;
                    context.Resources["BlackBrush"] = Brushes.White;
                    Layout(context);
                    Assert.Equal(ColorOf(background), ColorOf(context.Background));
                    CheckRow(contextItem, background);
                    SavePreview(parent, theme);
                }
                CheckSpotSurfaces(app);
                CheckDeferredStartupWindow(app);
                CheckRemotePasswordSetup();
            }
            catch (Exception ex) { error = ex; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF menu test timed out.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    // Shares the one WPF Application allowed per test process. No real MainWindow,
    // provider, network connection, or persisted settings are created here.
    private static void CheckSpotSurfaces(Application app)
    {
        app.Resources.MergedDictionaries.Add(Load("Spotnet;component/Style/shared.xaml"));
        ThemeHelper.ApplyTheme(ThemeHelper.ModernDark, persist: false);
        MainWindowViewModel vm = Task.Run(() => new MainWindowViewModel()).GetAwaiter().GetResult();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Same(ThemeBrushes.DarkFilterBackground, vm.FiltersBackground);
        Assert.True(vm.FiltersBackground.IsFrozen);

        var panel = new StackPanel { Width = 900 };
        var filter = new Border { Height = 30 };
        filter.SetBinding(Border.BackgroundProperty, new Binding(nameof(vm.FiltersBackground)) { Source = vm });
        panel.Children.Add(filter);
        var notice = new WarningTip { Text = "Zoekresultaten kunnen onvolledig zijn, omdat de database nog niet up-to-date is." };
        panel.Children.Add(notice);
        var linkNotice = new WarningTipWithLink();
        panel.Children.Add(linkNotice);
        var host = new Window { Content = panel, Width = 930, Height = 550 };
        var rows = new[] {
            new RowState { PosterIdent = PosterIdentType.Unspecified },
            new RowState { PosterIdent = PosterIdentType.White },
            new RowState { PosterIdent = PosterIdentType.Black },
            new RowState { PosterIdent = PosterIdentType.White, IsInFavorites = true }
        };
        string[] keys = { "SpotRowForegroundBrush", "SpotRowTrustedBrush", "SpotRowMutedBrush", "SpotRowFavoriteBrush" };
        var cells = new DataGridCell[rows.Length];
        var titles = new TextBlock[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var wrapper = new { Data = rows[i] };
            cells[i] = new DataGridCell { Content = "Incoming spot " + i, DataContext = wrapper,
                Style = new Style(typeof(DataGridCell), (Style)app.FindResource("SpotRowTextStyle")), Padding = new Thickness(8) };
            cells[i].SetResourceReference(Control.BackgroundProperty, "SpotBackgroundBrush");
            panel.Children.Add(cells[i]);
            titles[i] = new TextBlock { Text = "Thumbnail title " + i, DataContext = wrapper,
                Style = new Style(typeof(TextBlock), (Style)app.FindResource("SpotRowTextStyle")) };
            panel.Children.Add(titles[i]);
        }
        foreach (string theme in new[] { ThemeHelper.ModernDark, ThemeHelper.ModernLight, ThemeHelper.Classic, ThemeHelper.ModernDark })
        {
            // Simulates filter loading while an already populated view changes style.
            Task.Run(() => vm.SetFiltersBackground("#FFFFFF")).GetAwaiter().GetResult();
            ThemeHelper.ApplyTheme(theme, persist: false);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Layout(host);
            Layout(panel);
            for (int i = 0; i < cells.Length; i++)
            {
                Brush expected = (Brush)app.FindResource(keys[i]);
                Assert.Equal(ColorOf(expected), ColorOf(cells[i].Foreground));
                Assert.Equal(ColorOf(expected), ColorOf(titles[i].Foreground));
                Readable(cells[i].Foreground, cells[i].Background);
            }
            Assert.True(vm.FiltersBackground.IsFrozen);
            if (theme == ThemeHelper.ModernDark) Assert.Same(ThemeBrushes.DarkFilterBackground, filter.Background);
            // Each tip's surface is its single ContentControl child. Compiled BAML keeps
            // that name in a generated field rather than a namescope FindName can query.
            foreach (ContentControl content in new[] { (ContentControl)notice.Content, (ContentControl)linkNotice.Content })
            {
                Layout(content);
                var surface = Assert.IsType<Border>(content.Template.FindName("Bd", content));
                Assert.Equal(ColorOf((Brush)app.FindResource("NoticeBackgroundBrush")), ColorOf(surface.Background));
                Readable((Brush)app.FindResource("NoticeForegroundBrush"), surface.Background);
                Readable((Brush)app.FindResource("NoticeLinkBrush"), surface.Background);
            }
            // The row wrappers survive a theme switch; recoloring must not reload data.
            Assert.Same(rows[0], ((dynamic)cells[0].DataContext).Data);
            string output = Environment.GetEnvironmentVariable("SPOTNET_MENU_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(System.IO.Path.Combine(output, "spots-" + theme + ".png"));
                encoder.Save(stream);
            }
        }
        // A filter set that omits the Image attribute used to throw while its node was
        // built, because Image trimmed a null. Building and reading must both be safe.
        var namedNode = new FilterViewModel("No icon", "cat=1", null, null);
        Assert.Null(Record.Exception(() => namedNode.Image));
        // Without a name no default icon is assigned, so the null path stays reachable.
        Assert.Null(new FilterViewModel("", "cat=1", null, null).Image);
        ThemeHelper.ApplyTheme(ThemeHelper.Classic, persist: false);
    }

    public sealed class RowState
    {
        public PosterIdentType PosterIdent { get; set; }
        public bool IsInFavorites { get; set; }
    }
}
