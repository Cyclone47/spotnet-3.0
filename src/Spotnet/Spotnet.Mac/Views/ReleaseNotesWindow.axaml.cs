using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spotnet.Mac.Views;

public partial class ReleaseNotesWindow : Window
{
    public ReleaseNotesWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
