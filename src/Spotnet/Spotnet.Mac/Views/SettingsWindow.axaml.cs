using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose += Close;
        vm.RequestPickFolder += async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Downloadmap selecteren",
                AllowMultiple = false
            });

            if (folders.Count > 0 && folders[0].TryGetLocalPath() is string path && !string.IsNullOrWhiteSpace(path))
            {
                vm.DownloadFolder = path;
            }
        };
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
