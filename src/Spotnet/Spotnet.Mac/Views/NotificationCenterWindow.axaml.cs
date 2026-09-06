using Avalonia.Controls;
using Avalonia.Interactivity;
using Spotnet.Mac.ViewModels;

namespace Spotnet.Mac.Views;

/// <summary>
/// Het meldingcentrum-venster — de Avalonia-tegenhanger van Windows'
/// NotificationCenterWindow: meldingen met mark-as-read, regels met toggle,
/// test en verwijderen, en het regelformulier.
/// </summary>
public partial class NotificationCenterWindow : Window
{
    private NotificationCenterViewModel? _viewModel;

    public NotificationCenterWindow()
    {
        InitializeComponent();
    }

    public NotificationCenterWindow(Spotnet.Notifications.NotificationManager engine) : this()
    {
        _viewModel = new NotificationCenterViewModel(engine);
        DataContext = _viewModel;
        Closed += (s, e) => _viewModel.DetachEngineEvents();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
