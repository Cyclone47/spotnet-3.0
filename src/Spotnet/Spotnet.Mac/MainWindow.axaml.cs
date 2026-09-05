using System;
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

    public MainWindow()
    {
        InitializeComponent();

        _appPaths = new StandardAppPaths();
        _secretStore = new MacKeychainSecretStore();

        string dbPath = _appPaths.GetDatabasePath("spots");
        var sqliteDb = new MacSqliteDb(dbPath);
        _dbService = new SpotDatabaseService(sqliteDb);

        _viewModel = new MainWindowViewModel(_appPaths, _secretStore, _dbService);
        _viewModel.RequestOpenSettings += ShowSettingsWindow;
        _viewModel.RequestOpenOnboarding += ShowOnboardingWindow;
        _viewModel.RequestOpenReleaseNotes += ShowReleaseNotesWindow;
        _viewModel.RequestAddCustomFilter += ShowAddCustomFilterDialog;
        _viewModel.RequestOpenSpotWindow += detail => new SpotDetailWindow(detail).Show(this);
        _viewModel.RequestPickDownloadFolder += ShowPickDownloadFolderDialog;
        _viewModel.RequestOpenComplaintDialog += ShowComplaintDialog;
        _viewModel.RequestSetDownloadPassword += ShowSetPasswordDialog;
        _viewModel.RequestConfirmRemoveDownload = ShowConfirmRemoveDownloadDialog;
        _viewModel.RequestConfirmClearDownloads = ShowConfirmClearDownloadsDialog;

        DataContext = _viewModel;
        Loaded += OnWindowLoaded;
        Closed += (s, e) => _viewModel.Dispose();
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            _ = _viewModel.RefreshSpotsAsync();
        }
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
        var window = new SettingsWindow(settingsVm);
        await window.ShowDialog(this);
        _viewModel.OnSettingsSaved();
    }

    private async void ShowReleaseNotesWindow()
    {
        var window = new ReleaseNotesWindow();
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
                await _viewModel.SpotDetail.ReloadSpamReportsAsync();
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

    private async void ShowAddCustomFilterDialog()
    {
        // Simple inline dialog: name, icon, optional category, optional keyword, optional max age
        var nameBox = new TextBox { Watermark = "Filternaam (bijv. Mijn HD Films)", Width = 280 };
        var iconBox = new TextBox { Watermark = "Pictogram (bijv. ⭐ 🎯 🎞️)", Width = 80, Text = "🔖" };
        var keywordBox = new TextBox { Watermark = "Zoekwoord filter (optioneel)", Width = 280 };
        var ageBox = new TextBox { Watermark = "Max leeftijd uren (optioneel, bijv. 48)", Width = 280 };

        var catCombo = new ComboBox
        {
            Width = 280,
            PlaceholderText = "Categorie (optioneel)"
        };
        catCombo.Items.Add("(Alle categorieën)");
        catCombo.Items.Add("Beeld");
        catCombo.Items.Add("Muziek");
        catCombo.Items.Add("Spellen");
        catCombo.Items.Add("Applicaties");
        catCombo.Items.Add("Boeken");
        catCombo.Items.Add("Beeld - TV Series");
        catCombo.Items.Add("Erotiek");
        catCombo.SelectedIndex = 0;

        var okButton = new Button { Content = "Filter aanmaken", Classes = { "accent" }, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "Annuleren" };

        var dialog = new Window
        {
            Title = "Eigen filter toevoegen",
            Width = 380,
            Height = 370,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Nieuw filter", FontSize = 16, FontWeight = FontWeight.Bold },
                    new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Naam", FontSize = 12 },
                        nameBox
                    }},
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                    {
                        new StackPanel { Spacing = 4, Children =
                        {
                            new TextBlock { Text = "Pictogram", FontSize = 12 },
                            iconBox
                        }},
                    }},
                    new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Categorie beperken", FontSize = 12 },
                        catCombo
                    }},
                    new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Zoekwoord filter", FontSize = 12 },
                        keywordBox
                    }},
                    new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = "Maximale leeftijd (uren)", FontSize = 12 },
                        ageBox
                    }},
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children =
                    {
                        cancelButton,
                        okButton
                    }}
                }
            }
        };

        bool confirmed = false;
        okButton.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);

        if (!confirmed || string.IsNullOrWhiteSpace(nameBox.Text))
            return;

        // Map combo index to category id
        int? catId = catCombo.SelectedIndex switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 9,
            _ => null
        };

        int? maxAge = null;
        if (int.TryParse(ageBox.Text, out int parsedAge) && parsedAge > 0)
            maxAge = parsedAge;

        string? keyword = string.IsNullOrWhiteSpace(keywordBox.Text) ? null : keywordBox.Text.Trim();

        _viewModel.AddCustomFilter(
            name: nameBox.Text!.Trim(),
            icon: string.IsNullOrWhiteSpace(iconBox.Text) ? "🔖" : iconBox.Text.Trim(),
            categoryId: catId,
            subcatTag: null,
            maxAgeHours: maxAge,
            keyword: keyword);
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
