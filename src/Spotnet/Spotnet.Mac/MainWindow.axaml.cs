using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Mac.Updates;
using Spotnet.Mac.Views;
using Spotnet.Platform;

namespace Spotnet.Mac;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Avalonia Window disposes view model on Closed event")]
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;
    private readonly SpotDatabaseService _dbService;
    private readonly MacUpdater _updater;
    private bool _updateChecked;

    public MainWindow()
    {
        InitializeComponent();

        _appPaths = new StandardAppPaths();
        _secretStore = new MacKeychainSecretStore();

        string dbPath = _appPaths.GetDatabasePath("spots");
        var sqliteDb = new MacSqliteDb(dbPath);
        _dbService = new SpotDatabaseService(sqliteDb);
        _updater = new MacUpdater(_appPaths.DataFolder, applicationPath: AppContext.BaseDirectory);

        _viewModel = new MainWindowViewModel(_appPaths, _secretStore, _dbService);
        _viewModel.RequestOpenSettings += ShowSettingsWindow;
        _viewModel.RequestOpenOnboarding += ShowOnboardingWindow;
        _viewModel.RequestOpenReleaseNotes += ShowReleaseNotesWindow;
        _viewModel.RequestCheckForUpdates += () => _ = CheckForUpdatesAsync();
        _viewModel.RequestAddCustomFilter += ShowAddCustomFilterDialog;
        _viewModel.RequestEditFilter += ShowEditCustomFilterDialog;
        _viewModel.RequestSaveFilterSetAs += ShowSaveFilterSetAsDialog;
        FiltersHeader.PointerPressed += OnFiltersHeaderPressed;
        _viewModel.RequestOpenSpotWindow += detail => new SpotDetailWindow(detail).Show(this);
        _viewModel.RequestPickDownloadFolder += ShowPickDownloadFolderDialog;
        _viewModel.RequestOpenComplaintDialog += ShowComplaintDialog;
        _viewModel.RequestSetDownloadPassword += ShowSetPasswordDialog;
        _viewModel.RequestConfirmRemoveDownload = ShowConfirmRemoveDownloadDialog;
        _viewModel.RequestConfirmClearDownloads = ShowConfirmClearDownloadsDialog;
        _viewModel.RequestOpenSpotlinkDialog += () => _ = ShowOpenSpotlinkDialogAsync();
        _viewModel.RequestOpenNotificationCenter += () => _ = ShowNotificationCenterWindowAsync();

        // Afsluiten na downloads (fase 3, item 3): de Downloads-tab geeft het teken,
        // het venster toont het aftelvenster van Windows' ShutdownComputerDialog.
        _viewModel.DownloadsTab.RequestShutdownAfterDownloads += () =>
            _ = ShowShutdownAfterDownloadsDialogAsync();
        _viewModel.DownloadsTab.RequestAskRemoveFiles = item => ShowRemoveFilesFromDiskDialogAsync(item);
        _viewModel.DownloadsTab.RequestRememberRemoveFilesAnswer = () => _rememberRemoveFilesAnswer;

        DataContext = _viewModel;
        Opened += OnWindowOpened;
        Closed += (s, e) =>
        {
            _updater.Dispose();
            _viewModel.Dispose();
        };
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_updateChecked) return;
        _updateChecked = true;

        // Start the updater immediately after the window is visible. Database setup
        // can involve a large SQLite file and must not delay the startup update dialog.
        Task initializeTask = _viewModel.InitializeAsync();
        await CheckForUpdatesAsync();
        await initializeTask;
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            string? skipped = _updater.ReadSkippedVersion();
            var result = await _updater.CheckAsync(MacUpdateVersion.Current, skipped);
            if (!result.Decision.ShouldPrompt || result.Manifest == null) return;

            var window = new MacUpdateWindow(_updater, result.Manifest, result.Decision);
            await window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Updatecontrole mislukt: " + ex.Message;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            _ = _viewModel.SubmitSearchAsync();
        }
    }

    // Zoeksuggesties (fase 6): bij elke wijziging de lijst vullen, zoals Windows'
    // SearchBox TextChanged → UpdateSuggestions.
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = _viewModel.UpdateSuggestionsAsync();
    }

    private void OnFocusSearchClick(object? sender, RoutedEventArgs e)
    {
        SearchBox?.Focus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void ShowSettingsWindow()
    {
        var settingsVm = new SettingsViewModel(_secretStore, _appPaths, _viewModel.PreferencesService, _dbService);
        settingsVm.RequestShowPairing += () =>
        {
            if (_viewModel.RemoteHost is { } host)
            {
                var pairing = new Views.RemotePairingWindow(host);
                pairing.Show(this);
            }
        };
        var window = new SettingsWindow(settingsVm);
        await window.ShowDialog(this);
        _viewModel.OnSettingsSaved();
    }

    private async void ShowReleaseNotesWindow()
    {
        var window = new ReleaseNotesWindow();
        await window.ShowDialog(this);
    }

    private async Task ShowNotificationCenterWindowAsync()
    {
        var window = new NotificationCenterWindow(_viewModel.Notifications);
        await window.ShowDialog(this);
    }

    private async void ShowComplaintDialog(Spotnet.Mac.Models.SpotItem spot)
    {
        var vm = new ComplaintViewModel(spot, _viewModel.ComplaintService);
        vm.ComplaintSubmitted += async (success, message) =>
        {
            if (success)
            {
                _viewModel.StatusText = message;
                await _viewModel.RefreshSpamReportsAsync(spot);
            }
        };
        var window = new ComplaintWindow(vm);
        await window.ShowDialog(this);
    }

    private async void ShowOnboardingWindow()
    {
        var prefsService = new UserPreferencesService(_appPaths);
        var onboardingVm = new OnboardingViewModel(_appPaths, _secretStore, prefsService);
        var window = new OnboardingWindow(onboardingVm);
        onboardingVm.OnboardingFinished += () =>
        {
            window.Close();
            _ = _viewModel.RefreshSpotsAsync();
        };
        await window.ShowDialog(this);
    }

    /// <summary>
    /// The set menu behind the FILTERS header, as Windows' LeftPanelUserControl builds
    /// it: shipped sets in italic with a check on the active one, then the mutable
    /// sets, then save-as and, for a mutable active set, remove.
    /// </summary>
    private void OnFiltersHeaderPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;

        var menu = new ContextMenu();
        foreach (string setName in _viewModel.FilterSetNames)
        {
            string captured = setName;
            var item = new MenuItem { Header = setName };
            if (FilterSetService.ImmutableSetNames.Contains(setName, StringComparer.OrdinalIgnoreCase))
            {
                item.FontStyle = FontStyle.Italic;
            }
            if (string.Equals(setName, _viewModel.FilterSetLabel, StringComparison.OrdinalIgnoreCase))
            {
                item.IsChecked = true;
            }
            item.Click += (_, _) => _viewModel.SelectFilterSetCommand.Execute(captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var saveAs = new MenuItem { Header = "Opslaan als..." };
        saveAs.Click += (_, _) => _viewModel.SaveFilterSetAsCommand.Execute(null);
        menu.Items.Add(saveAs);

        if (!_viewModel.IsActiveFilterSetImmutable)
        {
            string active = _viewModel.FilterSetLabel;
            var remove = new MenuItem { Header = $"Verwijderen: {active}" };
            remove.Click += (_, _) => _viewModel.RemoveFilterSetCommand.Execute(active);
            menu.Items.Add(remove);
        }

        menu.Open(control);
    }

    private async void ShowAddCustomFilterDialog()
    {
        var dialog = new FilterEditorWindow();
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;

        _viewModel.AddCustomFilter(
            name: dialog.FilterName,
            icon: dialog.Icon,
            categoryIds: dialog.CategoryIds,
            subcatTag: dialog.SubcatTag,
            maxAgeHours: dialog.MaxAgeHours,
            keyword: dialog.Keyword);
    }

    private async void ShowEditCustomFilterDialog(Models.FilterItem item)
    {
        var dialog = new FilterEditorWindow(item);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;

        _viewModel.ApplyFilterEdit(item, dialog.FilterName, dialog.Icon, dialog.CategoryIds,
                                   dialog.SubcatTag, dialog.MaxAgeHours, dialog.Keyword);
    }

    private async void ShowSaveFilterSetAsDialog()
    {
        var nameBox = new TextBox { Watermark = "Naam van de filterlijst", Width = 260 };
        var okButton = new Button { Content = "Opslaan", Classes = { "accent" } };
        var cancelButton = new Button { Content = "Annuleren" };

        var dialog = new Window
        {
            Title = "Opslaan als...",
            Width = 320,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Voer een nieuwe filterlijstnaam in:" },
                    nameBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        bool confirmed = false;
        okButton.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);

        string name = nameBox.Text?.Trim() ?? "";
        // Windows' FilterSaveAsWindow rule: letters, digits and spaces, 1-17 chars.
        if (!confirmed || name.Length == 0 || name.Length > 17 ||
            !System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9\\ ]+$"))
        {
            return;
        }

        if (!_viewModel.SaveCurrentSetAs(name))
        {
            _viewModel.StatusText = "Die filterlijst bestaat al of is een standaardlijst.";
        }
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var aboutBox = new Window
        {
            Title = "Over Spotnet 3.0",
            Width = 400,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Spotnet 3.0 (macOS Alpha)", FontSize = 18, FontWeight = FontWeight.Bold },
                    new TextBlock
                    {
                        Text = $"Native macOS ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}) Client\nGebouwd met Avalonia UI, .NET 8 en SQLite FTS5.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock { Text = "© 2026 Spotnet Project Team", Foreground = Brushes.Gray, FontSize = 11 }
                }
            }
        };

        await aboutBox.ShowDialog(this);
    }

    private void OnSpotDoubleTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.OpenSpot(_viewModel.SelectedSpot);
    }

    private void OnDownloadDoubleTapped(object? sender, TappedEventArgs e)
    {
        var selected = _viewModel.DownloadsTab.Selected;
        if (selected != null)
        {
            if (selected.NeedsPassword)
            {
                ShowSetPasswordDialog(selected);
            }
            else
            {
                _viewModel.DownloadsTab.OpenCommand.Execute(selected);
            }
        }
    }

    private void OnDownloadsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_viewModel.DownloadsTab.Selected != null)
            {
                _viewModel.DownloadsTab.RemoveCommand.Execute(_viewModel.DownloadsTab.Selected);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Takes over sorting from the grid. The grid would order the rows it happens to
    /// hold, which with a virtualised list is a handful of pages out of the whole
    /// result; cancelling here and re-running the query puts the ORDER BY in SQL where
    /// it covers everything.
    /// </summary>
    private void OnSpotsSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        _ = _viewModel.ApplySortAsync(e.Column?.SortMemberPath);
    }

    private void OnSpotsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_viewModel.SelectedSpot != null)
            {
                _viewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.D &&
                 e.KeyModifiers.HasFlag(KeyModifiers.Meta) &&
                 e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (_viewModel.SelectedSpot != null)
            {
                _viewModel.DownloadSpotCommand.Execute(_viewModel.SelectedSpot);
                e.Handled = true;
            }
        }
    }

    private async void ShowSetPasswordDialog(Models.DownloadItem item)
    {
        var textBox = new TextBox
        {
            Text = item.UnpackPassword,
            Watermark = "Wachtwoord invoeren...",
            Width = 280,
            Margin = new Thickness(0, 8, 0, 8),
            PasswordChar = '\u2022',
            RevealPassword = false
        };

        var revealCheck = new CheckBox { Content = "Wachtwoord tonen", Margin = new Thickness(0, 0, 0, 12) };
        revealCheck.IsCheckedChanged += (_, _) => textBox.RevealPassword = revealCheck.IsChecked == true;

        var okBtn = new Button { Content = "OK", Classes = { "accent" } };
        var cancelBtn = new Button { Content = "Annuleren" };

        var dialog = new Window
        {
            Title = "Wachtwoord voor uitpakken",
            Width = 360,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = $"Wachtwoord voor '{item.Title}':", TextTrimming = TextTrimming.CharacterEllipsis },
                    textBox,
                    revealCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, okBtn }
                    }
                }
            }
        };

        bool confirmed = false;
        okBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);

        if (confirmed)
        {
            item.UnpackPassword = textBox.Text?.Trim() ?? "";
            _viewModel.DownloadsTab.SaveHistory();

            // Retry unpacking with the entered password
            await _viewModel.DownloadsTab.RunPostProcessAsync(item);
        }
    }

    private async void ShowPickDownloadFolderDialog()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Downloadmap selecteren",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is string path && !string.IsNullOrWhiteSpace(path))
        {
            _viewModel.DownloadFolder = path;
        }
    }

    private bool _rememberRemoveFilesAnswer;

    /// <summary>
    /// "Bestanden verwijderen van de schijf bij download verwijdering" — de Mac-versie
    /// van RemoveFilesFromTheDiskDialog, met dezelfde knoppen en hetzelfde
    /// "sla mijn antwoord op"-vinkje (tekst letterlijk uit Words.nl.resx).
    /// </summary>
    private async Task<bool> ShowRemoveFilesFromDiskDialogAsync(Models.DownloadItem item)
    {
        var messageBlock = new TextBlock
        {
            Text = $"Ook de bestanden van '{item.Title}' van de schijf verwijderen?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var rememberCheck = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "Sla mijn antwoord op en vraag niet opnieuw (u kunt de optie later veranderen via het Menu: Bewerken / Instellingen / Bestanden verwijderen van de schijf bij download verwijdering)",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            },
            Margin = new Thickness(0, 0, 0, 14)
        };

        var noBtn = new Button { Content = "Nee" };
        var yesBtn = new Button { Content = "Ja", Classes = { "accent" } };

        var dialog = new Window
        {
            Title = "Bestanden verwijderen van de schijf",
            Width = 520,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { messageBlock, rememberCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { noBtn, yesBtn }
                    } }
            }
        };

        bool deleteFiles = false;
        yesBtn.Click += (_, _) => { deleteFiles = true; dialog.Close(); };
        noBtn.Click += (_, _) => dialog.Close();

        // No is the default focus on Windows.
        noBtn.Focus();

        await dialog.ShowDialog(this);
        _rememberRemoveFilesAnswer = rememberCheck.IsChecked == true;
        return deleteFiles;
    }

    /// <summary>
    /// "Open Spotlink..." (Windows: OpenSpotlinkWindow, tekst "Geef de spotlink in"
    /// uit Words.nl.resx). De invoer gaat naar MainWindowViewModel.OpenSpotlinkAsync.
    /// </summary>
    private async Task ShowOpenSpotlinkDialogAsync()
    {
        var prompt = new TextBlock
        {
            Text = "Geef de spotlink in",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var input = new TextBox
        {
            Watermark = "spotnet://…",
            Margin = new Thickness(0, 0, 0, 14)
        };

        var cancelBtn = new Button { Content = "Annuleren" };
        var okBtn = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true };

        var dialog = new Window
        {
            Title = "Open Spotlink...",
            Width = 460,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { prompt, input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, okBtn }
                    } }
            }
        };

        string? link = null;
        okBtn.Click += (_, _) => { link = input.Text; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        input.Focus();

        await dialog.ShowDialog(this);

        if (!string.IsNullOrWhiteSpace(link))
        {
            await _viewModel.OpenSpotlinkAsync(link);
        }
    }

    /// <summary>
    /// Het aftelvenster van "sluit mijn pc nadat alle downloads zijn voltooid": 60
    /// seconden aftellen, annuleren kan altijd, met dezelfde teksten als Windows'
    /// ShutdownComputerDialog (uit Words.nl.resx).
    /// </summary>
    private async Task ShowShutdownAfterDownloadsDialogAsync()
    {
        const string message = "Alle downloads zijn voltooid, de PC wordt afgesloten.";
        int secondsLeft = 60;

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var secondsBlock = new TextBlock
        {
            Text = "(60)",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var cancelBtn = new Button { Content = "Afsluiten annuleren" };
        var nowBtn = new Button { Content = "NU AFSLUITEN", Classes = { "accent" } };

        var dialog = new Window
        {
            Title = "Spotnet",
            Width = 420,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { messageBlock, secondsBlock,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 12,
                        Children = { cancelBtn, nowBtn }
                    } }
            }
        };

        bool proceed = false;
        nowBtn.Click += (_, _) => { proceed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => { proceed = false; dialog.Close(); };

        // The countdown, as in ShutdownComputerDialog.TimerOnElapsed.
        var timer = new System.Timers.Timer(1000) { AutoReset = true };
        timer.Elapsed += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                secondsLeft--;
                if (secondsLeft <= 0)
                {
                    timer.Stop();
                    proceed = true;
                    dialog.Close();
                    return;
                }
                secondsBlock.Text = $"({secondsLeft})";
            });
        };

        cancelBtn.Focus();

        try
        {
            timer.Start();
            await dialog.ShowDialog(this);
        }
        finally
        {
            timer.Stop();
            timer.Dispose();
        }

        if (proceed)
        {
            Spotnet.Mac.Platform.MacPowerActions.ShutdownNow();
        }
    }

    private async Task<(bool confirmed, bool deleteFiles)> ShowConfirmRemoveDownloadDialog(Models.DownloadItem item)
    {
        long bytes = Models.DownloadItem.GetDiskSizeBytes(item);
        string sizeStr = Models.DownloadItem.FormatBytes(bytes);

        var titleBlock = new TextBlock
        {
            Text = "Download verwijderen",
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var messageBlock = new TextBlock
        {
            Text = $"Weet u zeker dat u '{item.Title}' uit de downloadlijst wilt verwijderen?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var deleteFilesCheck = new CheckBox
        {
            Content = bytes > 0
                ? $"Verwijder ook de opgeslagen bestanden van schijf ({sizeStr})"
                : "Verwijder ook de opgeslagen bestanden van schijf",
            IsChecked = false,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var cancelBtn = new Button { Content = "Annuleren" };
        var deleteBtn = new Button
        {
            Content = "Verwijderen",
            Classes = { "accent" }
        };

        var dialog = new Window
        {
            Title = "Download verwijderen",
            Width = 460,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    titleBlock,
                    messageBlock,
                    deleteFilesCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, deleteBtn }
                    }
                }
            }
        };

        bool confirmed = false;
        deleteBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);

        return (confirmed, deleteFilesCheck.IsChecked == true);
    }

    private async Task<(bool confirmed, bool deleteFiles)> ShowConfirmClearDownloadsDialog(int count, long totalBytes)
    {
        string sizeStr = Models.DownloadItem.FormatBytes(totalBytes);

        var titleBlock = new TextBlock
        {
            Text = "Downloadlijst leegmaken",
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var messageBlock = new TextBlock
        {
            Text = $"Weet u zeker dat u alle {count} downloads uit de lijst wilt verwijderen?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var deleteFilesCheck = new CheckBox
        {
            Content = totalBytes > 0
                ? $"Verwijder ook alle opgeslagen bestanden van schijf ({sizeStr})"
                : "Verwijder ook alle opgeslagen bestanden van schijf",
            IsChecked = false,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var cancelBtn = new Button { Content = "Annuleren" };
        var clearBtn = new Button
        {
            Content = "Lijst leegmaken",
            Classes = { "accent" }
        };

        var dialog = new Window
        {
            Title = "Downloadlijst leegmaken",
            Width = 460,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    titleBlock,
                    messageBlock,
                    deleteFilesCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, clearBtn }
                    }
                }
            }
        };

        bool confirmed = false;
        clearBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);

        return (confirmed, deleteFilesCheck.IsChecked == true);
    }
}
