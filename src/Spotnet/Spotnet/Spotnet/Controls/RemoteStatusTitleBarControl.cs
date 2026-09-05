using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Spotnet.Remote;

namespace Spotnet.Controls;

public partial class RemoteStatusTitleBarControl : UserControl
{
    private DispatcherTimer _timer;

    private static readonly SolidColorBrush GreenBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981
    private static readonly SolidColorBrush BlueBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));  // #38BDF8
    private static readonly SolidColorBrush GrayBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128)); // #6B7280

    public RemoteStatusTitleBarControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateState();

        RemoteServer.Instance.StatusChanged += OnServerStatusChanged;

        if (_timer == null)
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _timer.Tick += (s, ev) => UpdateState();
        }
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        RemoteServer.Instance.StatusChanged -= OnServerStatusChanged;
    }

    private void OnServerStatusChanged()
    {
        if (Dispatcher.CheckAccess())
        {
            UpdateState();
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(UpdateState));
        }
    }

    public void UpdateState()
    {
        try
        {
            var server = RemoteServer.Instance;
            var config = RemoteConfig.Load();

            bool isRunning = server.IsRunning;
            bool isActive = server.IsClientActive;
            bool keepAwake = config.KeepAwake;

            if (!isRunning)
            {
                // Status: Uit (Grijs lampje)
                StatusLed.Fill = GrayBrush;
                StatusGlow.Opacity = 0;
                StatusLabel.Text = "Remote: Uit";
                StatusLabel.Opacity = 0.65;
                InUseBadge.Visibility = Visibility.Collapsed;
                KeepAwakeBadge.Visibility = Visibility.Collapsed;

                TooltipTitle.Text = "Spotnet Remote: Uit";
                TooltipStatus.Text = "De server is momenteel uitgeschakeld.";
                TooltipClient.Visibility = Visibility.Collapsed;
                TooltipKeepAwake.Text = "Slaapstand: Normaal Windows energiebeheer";
            }
            else if (isActive)
            {
                // Status: In gebruik (Blauw lampje)
                StatusLed.Fill = BlueBrush;
                StatusGlow.Fill = BlueBrush;
                StatusGlow.Opacity = 0.7;
                StatusLabel.Text = "Remote:";
                StatusLabel.Opacity = 1.0;
                InUseBadge.Visibility = Visibility.Visible;

                KeepAwakeBadge.Visibility = keepAwake ? Visibility.Visible : Visibility.Collapsed;

                string clientName = server.LastActiveClientName;
                if (string.IsNullOrWhiteSpace(clientName)) clientName = "Verbonden mobiel";

                TooltipTitle.Text = "Spotnet Remote: In gebruik";
                TooltipStatus.Text = $"Status: Actief verbonden op poort {server.ActivePort}";
                TooltipClient.Text = $"Actief apparaat: {clientName}";
                TooltipClient.Visibility = Visibility.Visible;

                TooltipKeepAwake.Text = keepAwake
                    ? "Houd PC wakker: Actief (slaapstand geblokkeerd)"
                    : "Houd PC wakker: Uitgeschakeld";
            }
            else
            {
                // Status: Aan (Groen lampje)
                StatusLed.Fill = GreenBrush;
                StatusGlow.Fill = GreenBrush;
                StatusGlow.Opacity = 0.45;
                StatusLabel.Text = "Remote: Aan";
                StatusLabel.Opacity = 1.0;
                InUseBadge.Visibility = Visibility.Collapsed;

                KeepAwakeBadge.Visibility = keepAwake ? Visibility.Visible : Visibility.Collapsed;

                TooltipTitle.Text = "Spotnet Remote: Aan";
                TooltipStatus.Text = $"Status: Actief en wacht op verbinding (poort {server.ActivePort})";
                TooltipClient.Visibility = Visibility.Collapsed;

                TooltipKeepAwake.Text = keepAwake
                    ? "Houd PC wakker: Actief (slaapstand geblokkeerd)"
                    : "Houd PC wakker: Uitgeschakeld";
            }
        }
        catch
        {
            // Fail-safe
        }
    }

    private void TitleBarButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var advancedSettings = new AdvancedSettings
            {
                Owner = Window.GetWindow(this),
                HeaderItemIndex = 6
            };
            advancedSettings.ShowDialog();
            UpdateState();
        }
        catch
        {
            // Ignore if window is closing
        }
    }
}
