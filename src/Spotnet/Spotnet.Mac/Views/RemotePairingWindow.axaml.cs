using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Spotnet.Mac.Remote;
using Spotnet.Mac.ViewModels;
using Spotnet.Remote;

namespace Spotnet.Mac.Views;

/// <summary>
/// Het koppelvenster van Spotnet Remote: toont de QR (zelfde token-URL als Windows'
/// RemotePairingWindow), de 6-cijferige PIN met ingangstijd en de lijst gekoppelde
/// apparaten met een ontkoppelknop — het Mac-antwoord op RemotePairingWindow +
/// het apparaatgedeelte van SettingsForRemote.
/// </summary>
public partial class RemotePairingWindow : Window
{
    public RemotePairingWindow()
    {
        InitializeComponent();
    }

    public RemotePairingWindow(MacRemoteHost host) : this()
    {
        var vm = new RemotePairingViewModel(host);
        DataContext = vm;
        vm.RefreshDevices();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}

/// <summary>
/// Bevat de QR, PIN en apparaatlijst. Een nieuwe koppelsessie ontstaat telkens het
/// venster opent — net zo lang geldig als de vijf minuten van RemoteAuthManager.
/// </summary>
public sealed class RemotePairingViewModel : ViewModelBase
{
    private readonly MacRemoteHost _host;

    public RemotePairingViewModel(MacRemoteHost host)
    {
        _host = host;
        RevokeDeviceCommand = new RelayCommand(id =>
        {
            if (id is string deviceId)
            {
                _host.RevokeDevice(deviceId);
                RefreshDevices();
            }
        });
        var (url, pin, _) = host.CreatePairing();
        Pin = pin;
        ExpiresText = "Geldig voor 5 minuten";

        var png = RemoteQrCode.GeneratePng(url, pixelsPerModule: 6);
        if (png != null)
        {
            using var stream = new MemoryStream(png);
            QrImage = new Bitmap(stream);
        }
    }

    public Avalonia.Media.Imaging.Bitmap? QrImage { get; }

    public string Pin { get; }

    public string ExpiresText { get; }

    public ObservableCollection<RemoteDeviceRow> Devices { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    public string EmptyDevicesText => "Nog geen apparaten gekoppeld.";

    public System.Windows.Input.ICommand RevokeDeviceCommand { get; }

    public void RefreshDevices()
    {
        var devices = _host.PairedDevices
            .Select(d => new RemoteDeviceRow(
                d.Id,
                d.Name,
                string.Join(" · ", new[] { d.IpAddress, d.LastSeenAt.ToLocalTime().ToString("dd-MM HH:mm") }.Where(s => !string.IsNullOrEmpty(s)))))
            .ToList();

        Devices.Clear();
        foreach (var row in devices)
        {
            Devices.Add(row);
        }
        OnPropertyChanged(nameof(HasDevices));
    }
}

/// <summary>Één rij in de apparaatlijst.</summary>
public sealed class RemoteDeviceRow
{
    public RemoteDeviceRow(string id, string name, string detail)
    {
        Id = id;
        Name = name;
        Detail = detail;
    }

    public string Id { get; }
    public string Name { get; }
    public string Detail { get; }
}
