using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using MahApps.Metro.Controls;
using NLog;
using Spotnet.Extensions;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;
public partial class AdvancedMessageBox : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private bool _closing;
    private string _newServer;
    private readonly Dictionary<string, int> _defaultPorts = new Dictionary<string, int>();
    private readonly int[] _ports;
    private readonly List<ServerInfo> _servers;
    private readonly Func<List<ServerInfo>, bool> _testConnectionFunc;
    public MessageBoxResult MessageBoxResult { get; set; }
    public TextBlock TextBlock => ContentLabel;

    public AdvancedMessageBox(Func<List<ServerInfo>, bool> testConnectionFunc = null, List<ServerInfo> servers = null, int[] ports = null)
    {
        base.Closing += OnClosing;
        base.Loaded += OnLoaded;
        InitializeComponent();
        ContentLabel.FontSize = (int)Settings.Default.FontSize;
        if (testConnectionFunc != null && servers != null && ports != null)
        {
            _testConnectionFunc = testConnectionFunc;
            _servers = servers;
            _ports = ports;
            base.Height += 25 * ports.Length * servers.Count + 40;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (_testConnectionFunc != null && _servers != null && _ports != null)
        {
            Task.Run(delegate
            {
                CheckServers();
            });
        }
    }

    private void OnClosing(object sender, CancelEventArgs cancelEventArgs)
    {
        _closing = true;
    }

    public new string ShowDialog()
    {
        base.ShowDialog();
        return _newServer;
    }

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CheckServers()
    {
        SavePorts();
        try
        {
            ContentLabelAppendText(Environment.NewLine + Environment.NewLine + Words.ScanningOtherPorts + ":");
            foreach (ServerInfo server in _servers)
            {
                int[] ports = _ports;
                for (int i = 0; i < ports.Length; i++)
                {
                    int num = (server.Port = ports[i]);
                    UIElement serverStatus = UpdateServerCheckingStatus(server.Server, num);
                    bool connected = (_servers.IndexOf(server) != 0 || !_defaultPorts.ContainsKey(server.Server.ToLowerInvariant()) || num != _defaultPorts[server.Server.ToLowerInvariant()]) && _testConnectionFunc(new List<ServerInfo> { server });
                    if (_closing)
                    {
                        return;
                    }

                    UpdateServerCheckingStatus(serverStatus, server.Server, num, connected);
                }
            }
        }
        finally
        {
            if (!_closing)
            {
                RestorePorts();
            }
        }
    }

    private void ContentLabelAppendText(string msg)
    {
        base.Dispatcher.Invoke(delegate
        {
            ContentLabel.Text += msg;
        });
    }

    private UIElement UpdateServerCheckingStatus(string server, int port)
    {
        return base.Dispatcher.Invoke((Func<UIElement>)delegate
        {
            Grid grid = (Grid)FindResource("ServerStatusInProgress");
            grid.FindChildByType<TextBlock>().Text = $"{server}:{port}";
            int count = ServersStatus.Children.Count;
            ServersStatus.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ServersStatus.Children.Add(grid);
            Grid.SetRow(grid, count);
            return grid;
        });
    }

    private void UpdateServerCheckingStatus(UIElement serverStatus, string server, int port, bool connected)
    {
        if (serverStatus != null)
        {
            base.Dispatcher.Invoke(delegate
            {
                ServersStatus.Children.Remove(serverStatus);
                Grid element = (connected ? ((Grid)FindResource("ServerStatusAvailable")) : ((Grid)FindResource("ServerStatusNotAvailable")));
                element.FindChildByType<TextBlock>().Text = $"{server}:{port}";
                int count = ServersStatus.Children.Count;
                ServersStatus.Children.Add(element);
                Grid.SetRow(element, count);
            });
        }
    }

    private void SavePorts()
    {
        foreach (ServerInfo server in _servers)
        {
            _defaultPorts[server.Server.ToLowerInvariant()] = server.Port;
        }
    }

    private void RestorePorts()
    {
        foreach (ServerInfo server in _servers)
        {
            if (_defaultPorts.ContainsKey(server.Server.ToLowerInvariant()))
            {
                server.Port = _defaultPorts[server.Server.ToLowerInvariant()];
            }
        }
    }

    private void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        TextBlock textBlock = ((UIElement)sender).ParentOfType<Grid>().FindChildByType<TextBlock>();
        _newServer = textBlock.Text;
        Close();
    }
}