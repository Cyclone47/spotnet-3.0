using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using MahApps.Metro.Controls;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.Controls;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Views;
public partial class SelectProviderWindow : MetroWindow
{
    [Flags]
    public enum HighlightControl
    {
        None = 0,
        Login = 1,
        HeaderAddress = 2,
        DownloadAddress = 4,
        UploadAddress = 8,
        HeaderPort = 0x10,
        DownloadPort = 0x20,
        UploadPort = 0x40,
        All = 0x7F
    }

    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly Color FieldValidColor = Colors.Transparent;
    // A border colour, not a LemonChiffon fill: the fill was unreadable under the ModernDark theme.
    private static readonly Color FieldInvalidColor = Color.FromRgb(0xD9, 0x53, 0x4F);
    private readonly HashSet<Control> _invalidFields = new HashSet<Control>();
    private readonly object _lockRoot = new object();
    public bool BSuc;
    private bool _initializationFinished;
    private string _lastSettingsString;
    private const string AutoConnectionsString = "Auto";

    private ListCollectionView _providerView;
    private string _providerFilter = string.Empty;
    /// <summary>
    /// Only genuine keystrokes narrow the list. The editable ComboBox also raises TextChanged when
    /// its template is applied and whenever a row is selected, and treating those as a search left
    /// the dropdown filtered down to the one entry whose name happened to be in the box.
    /// </summary>
    private bool _userIsSearching;
    /// <summary>The provider whose servers are currently in the fields, so re-applying is a no-op.</summary>
    private ProviderItem _appliedProvider;
    /// <summary>Set when the user picks a row, so only a deliberate choice commits a search.</summary>
    private bool _providerChosen;
    /// <summary>Suppresses routed ComboBox text events caused by our own view refresh.</summary>
    private bool _updatingProviderView;
    /// <summary>Coalesces rapid keystrokes and stale close events queued on the Dispatcher.</summary>
    private int _providerUpdateRevision;

    public static bool IsRunning => DispatcherHelper.UIDispatcher.Invoke(() => Application.Current.Windows.OfType<SelectProviderWindow>().Any());
    private string CurrentSettingsString => HeaderServerTextBox.Text + HeaderServerPortComboBox.Text.Split()[0] + DownloadServerTextBox.Text + DownloadServerPortComboBox.Text.Split()[0] + UploadServerTextBox.Text + UploadServerPortComboBox.Text.Split()[0] + ConnectionsCombo.Text + UserNameTextBox.Text + PasswordTextBox.Password;
    private Storyboard FieldAnimation => (Storyboard)base.Resources["FieldAnimationStoryboard"];
    public HighlightControl HighlightedControl { get; set; }
    private bool AreSettingsChanged => _lastSettingsString != CurrentSettingsString;

    /// <summary>Every field that carries a validation border, paired with the border that shows it.</summary>
    private IEnumerable<KeyValuePair<Control, Border>> ValidatedFields => new[]
    {
        new KeyValuePair<Control, Border>(HeaderServerTextBox, HeaderServerTextBoxBorder),
        new KeyValuePair<Control, Border>(DownloadServerTextBox, DownloadServerTextBoxBorder),
        new KeyValuePair<Control, Border>(UploadServerTextBox, UploadServerTextBoxBorder),
        new KeyValuePair<Control, Border>(HeaderServerPortComboBox, HeaderServerPortComboBoxBorder),
        new KeyValuePair<Control, Border>(DownloadServerPortComboBox, DownloadServerPortComboBoxBorder),
        new KeyValuePair<Control, Border>(UploadServerPortComboBox, UploadServerPortComboBoxBorder),
        new KeyValuePair<Control, Border>(UserNameTextBox, UserNameTextBoxBorder),
        new KeyValuePair<Control, Border>(PasswordTextBox, PasswordTextBoxBorder)
    };

    public SelectProviderWindow()
    {
        base.Closing += ProviderSelectie_Closing;
        base.Initialized += ProviderSelectie_Initialized;
        InitializeComponent();
    }

    private void DoButton()
    {
        string sError1 = "";
        ServerInfo serverForHeaders = new ServerInfo();
        ServerInfo serverForDownload = new ServerInfo();
        ServerInfo serverForUpload = new ServerInfo();
        Task.Factory.StartNew(delegate
        {
            string text2 = DownloadServerTextBox.Text.Trim().ToLower();
            string text3 = UploadServerTextBox.Text.Trim().ToLower();
            string text4 = HeaderServerTextBox.Text.Trim().ToLower();
            serverForHeaders.Server = text4;
            serverForUpload.Server = ((!text3.IsNullOrEmpty()) ? text3 : text4);
            serverForDownload.Server = ((!text2.IsNullOrEmpty()) ? text2 : text4);
            serverForHeaders.Port = Conversions.ToInteger(RemoveStrings(HeaderServerPortComboBox.Text));
            serverForUpload.Port = Conversions.ToInteger(RemoveStrings(UploadServerPortComboBox.Text));
            serverForDownload.Port = Conversions.ToInteger(RemoveStrings(DownloadServerPortComboBox.Text));
            serverForDownload.SSL = serverForDownload.DoesProviderUseSsl();
            serverForHeaders.SSL = serverForHeaders.DoesProviderUseSsl();
            serverForUpload.SSL = serverForUpload.DoesProviderUseSsl();
            if (ConnectionsCombo.Text.Equals(AutoConnectionsString))
            {
                serverForDownload.Connections = 0;
            }
            else
            {
                serverForDownload.Connections = Conversions.ToInteger(RemoveStrings(ConnectionsCombo.Text)) - 2;
                if (serverForDownload.Connections < 1)
                {
                    serverForDownload.Connections = 1;
                }
            }

            serverForHeaders.Connections = 2;
            serverForUpload.Connections = 1;
            serverForHeaders.Username = UserNameTextBox.Text;
            serverForUpload.Username = serverForHeaders.Username;
            serverForDownload.Username = serverForHeaders.Username;
            serverForHeaders.Password = PasswordTextBox.Password;
            serverForUpload.Password = serverForHeaders.Password;
            serverForDownload.Password = serverForHeaders.Password;
        }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()).ContinueWith(delegate
        {
            DownloadQueue.StopDownloadQueue();
            return AppHelper.TestConnections(new List<ServerInfo> { serverForDownload, serverForHeaders }, Settings.Default.UseSocksProxy, out sError1);
        }, CancellationToken.None, TaskContinuationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate (Task<bool> t)
        {
            try
            {
                base.Cursor = null;
                Mouse.OverrideCursor = null;
                ConnectProgressRing.IsActive = false;
                StatusLabel.Text = string.Empty;
                Sys.EuroUsenetRetention = 0;
                if (t.Result)
                {
                    BSuc = true;
                    AppHelper.ServersDb.OHeader = serverForHeaders;
                    AppHelper.ServersDb.OUp = serverForUpload;
                    AppHelper.ServersDb.ODown = serverForDownload;
                    AppHelper.ResetProviderDetermination();
                    AppHelper.ServersDb.InitCacheServers();
                    string sError2 = "";
                    AppHelper.ServersDb.SaveServers(ref sError2);
                    Log.Info($"Provider changed to {serverForHeaders.Server}: {serverForHeaders.Port}");
                    Log.Debug("Socks5 proxy is used: " + (Settings.Default.UseSocksProxy ? "yes" : "no"));
                    Close();
                }
                else
                {
                    int errorCode = GetErrorCode(sError1);
                    HighlightControl highlightControl = GetHighlightControl(errorCode, sError1);
                    bool num = errorCode == 931 || errorCode == 932 || errorCode == 941 || errorCode == 950;
                    List<ServerInfo> list = new List<ServerInfo>
                    {
                        serverForHeaders
                    };
                    if (serverForHeaders.Server.ToLower().EndsWith(".snelnl.com"))
                    {
                        ServerInfo serverInfo = (ServerInfo)serverForHeaders.Clone();
                        serverInfo.Server = "news.sslusenet.com";
                        list.Add(serverInfo);
                    }

                    AdvancedMessageBox obj = (num ? new AdvancedMessageBox((List<ServerInfo> i) => AppHelper.TestConnections(i, Settings.Default.UseSocksProxy, out sError1), list, new int[4] { 119, 443, 563, 80 }) : new AdvancedMessageBox());
                    obj.Title = Words.Error;
                    obj.TextBlock.Text = GetErrorMessage(errorCode, sError1);
                    obj.Owner = this;
                    string text = obj.ShowDialog();
                    if (text.IsNullOrEmpty())
                    {
                        StartAnimation(highlightControl);
                        EnableVal(bVal: true);
                    }
                    else
                    {
                        string[] serverArr = text.Split(':');
                        base.Dispatcher.BeginInvoke((Action)delegate
                        {
                            HeaderServerTextBox.Text = serverArr[0];
                            HeaderServerPortComboBox.Text = serverArr[1];
                            UploadServerCheckBox.IsEnabled = false;
                            DownloadServerCheckBox.IsEnabled = false;
                            base.Dispatcher.BeginInvoke((Action)delegate
                            {
                                ConnectButton_Click(null, null);
                            });
                        });
                    }
                }
            }
            finally
            {
                Sys.Downloader.RestartProcessAsync();
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void EnableVal(bool bVal)
    {
        ProviderBox.IsEnabled = bVal;
        HeaderServerTextBox.IsEnabled = bVal;
        HeaderServerPortComboBox.IsEnabled = bVal;
        UploadServerTextBox.IsEnabled = UploadServerCheckBox.IsChecked.GetValueOrDefault() && bVal;
        UploadServerPortComboBox.IsEnabled = UploadServerCheckBox.IsChecked.GetValueOrDefault() && bVal;
        DownloadServerTextBox.IsEnabled = DownloadServerCheckBox.IsChecked.GetValueOrDefault() && bVal;
        DownloadServerPortComboBox.IsEnabled = DownloadServerCheckBox.IsChecked.GetValueOrDefault() && bVal;
        ConnectionsCombo.IsEnabled = bVal;
        UserNameTextBox.IsEnabled = bVal;
        PasswordTextBox.IsEnabled = bVal;
        CancelButton.IsEnabled = bVal;
        if (bVal)
        {
            UpdateConnectButtonState();
        }
        else
        {
            ConnectButton.IsEnabled = false;
        }
    }

    private static int GetErrorCode(string errorText)
    {
        if (errorText == "Gebruikersnaam en/of wachtwoord is onjuist.")
        {
            errorText = Words.UsernamePasswordWrong + "(502)";
        }

        if (!int.TryParse(Regex.Match(errorText, ".*\\((?<code>\\d+)\\)").Groups["code"].Value, out var result))
        {
            return -1;
        }

        return result;
    }

    private static string GetErrorMessage(int errorCode, string errorText = "")
    {
        switch (errorCode)
        {
            case 381:
            case 400:
            case 450:
            case 452:
            case 480:
            case 481:
            case 482:
            case 502:
                if (errorText.ToLower().Contains("connection") || errorText.ToLower().Contains("too many"))
                {
                    return errorText;
                }

                return Words.UsernamePasswordWrong + Environment.NewLine + Words.CheckUsernamePasswordAndTryAgain;
            case 941:
            case 950:
                return Words.HostIsUnknown + Environment.NewLine + Words.CheckServerOrPortAndTryAgain;
            case 932:
                return Words.CannotConnectToPort + Environment.NewLine + Words.CheckPortIsCorrect;
            case 931:
                return errorText;
            default:
                return errorText;
        }
    }

    private static HighlightControl GetHighlightControl(int errorCode, string errorText = "")
    {
        switch (errorCode)
        {
            case 381:
            case 400:
            case 450:
            case 452:
            case 480:
            case 481:
            case 482:
            case 502:
                if (!errorText.ToLower().Contains("connection") && !errorText.ToLower().Contains("too many"))
                {
                    return HighlightControl.Login;
                }

                return HighlightControl.None;
            case 941:
            case 950:
                return HighlightControl.HeaderAddress;
            case 931:
                if (!errorText.StartsWith("The connection attempt timed out"))
                {
                    return HighlightControl.None;
                }

                return HighlightControl.HeaderAddress;
            case 932:
                return HighlightControl.HeaderPort;
            default:
                return HighlightControl.None;
        }
    }

    private string RemoveStrings(string msg)
    {
        string empty = string.Empty;
        empty = Regex.Matches(msg, "[0-9.-]").Cast<Match>().Aggregate(empty, (string c, Match m) => c + m.ToStringSafely());
        if (!empty.Contains("-"))
        {
            empty = Regex.Matches(msg, "[0-9.+]").Cast<Match>().Aggregate("", (string c, Match m) => c + m.ToStringSafely());
        }

        if (empty.IsNullOrEmpty())
        {
            empty = "0";
        }

        if (msg.IsNullOrEmpty())
        {
            empty = "0";
        }

        return Strings.Replace(empty, "-", "");
    }

    /// <summary>The borders each highlight flag points at.</summary>
    private IEnumerable<Border> BordersFor(HighlightControl control)
    {
        if (control.HasFlag(HighlightControl.Login))
        {
            yield return UserNameTextBoxBorder;
            yield return PasswordTextBoxBorder;
        }
        if (control.HasFlag(HighlightControl.HeaderAddress)) yield return HeaderServerTextBoxBorder;
        if (control.HasFlag(HighlightControl.HeaderPort)) yield return HeaderServerPortComboBoxBorder;
        if (control.HasFlag(HighlightControl.DownloadAddress)) yield return DownloadServerTextBoxBorder;
        if (control.HasFlag(HighlightControl.DownloadPort)) yield return DownloadServerPortComboBoxBorder;
        if (control.HasFlag(HighlightControl.UploadAddress)) yield return UploadServerTextBoxBorder;
        if (control.HasFlag(HighlightControl.UploadPort)) yield return UploadServerPortComboBoxBorder;
    }

    private void StartAnimation(HighlightControl control)
    {
        if (control == HighlightControl.None)
        {
            Log.Warn(Words.WrongValueCheckHighlighted);
            return;
        }
        // The header address and port are reported together, as they were in 2.0.
        if (control.HasFlag(HighlightControl.HeaderAddress)) control |= HighlightControl.HeaderPort;
        foreach (Border border in BordersFor(control))
        {
            border.BeginStoryboard(FieldAnimation, HandoffBehavior.SnapshotAndReplace, isControllable: true);
            border.Tag = true;
        }
        HighlightedControl = control;
    }

    private void StopAnimation(HighlightControl control = HighlightControl.All)
    {
        foreach (Border border in BordersFor(control))
        {
            if (!(border.Tag is bool running) || !running) continue;
            FieldAnimation.Stop(border);
            border.Tag = false;
            // Stopping the storyboard leaves the animated colour behind; restore the validation state.
            border.BorderBrush = new SolidColorBrush(FieldValidColor);
        }
        HighlightedControl &= ~control;
        RefreshFieldBorders();
    }

    private void HeaderServerTextBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.HeaderAddress | HighlightControl.HeaderPort);

    private void UploadServerTextBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.UploadAddress);

    private void DownloadServerTextBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.DownloadAddress);

    private void HeaderServerPortComboBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.HeaderPort);

    private void UploadServerPortComboBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.UploadPort);

    private void DownloadServerPortComboBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.DownloadPort);

    private void UserNameTextBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.Login);

    private void PasswordTextBox_GotFocus(object sender, RoutedEventArgs e) => StopAnimation(HighlightControl.Login);

    /// <summary>
    /// While the user types, WPF keeps re-selecting whatever survived the filter. Applying those
    /// interim selections would rewrite the server fields and clear the credentials on every
    /// keystroke, so a search only commits when the dropdown closes.
    /// </summary>
    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A search commits through CommitProviderChoice instead, never from an interim selection.
        if (_userIsSearching) return;
        ApplySelectedProvider();
    }

    private void ProviderBox_DropDownClosed(object sender, EventArgs e) => ScheduleProviderChoiceCommit();

    private void ScheduleProviderChoiceCommit()
    {
        int revision = ++_providerUpdateRevision;
        base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
        {
            if (revision == _providerUpdateRevision) CommitProviderChoice();
        });
    }

    /// <summary>
    /// Ends a search. Only a real pick applies a provider: a dropdown that closes for any other
    /// reason - losing focus, a click elsewhere - must leave the configured provider alone.
    /// </summary>
    private void CommitProviderChoice()
    {
        // Capture before clearing: refreshing the view resets the ComboBox selection.
        ProviderItem chosen = _providerChosen ? ProviderBox.SelectedItem as ProviderItem : null;
        _providerChosen = false;
        ClearProviderFilter();
        if (chosen != null)
        {
            if (!ReferenceEquals(ProviderBox.SelectedItem, chosen)) ProviderBox.SelectedItem = chosen;
            ApplySelectedProvider();
        }
        RestoreProviderText();
    }

    /// <summary>A click that lands on a row of the dropdown, as opposed to anywhere else.</summary>
    private void ProviderBox_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject node = e.OriginalSource as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ComboBoxItem)
            {
                _providerChosen = true;
                return;
            }
        }
    }

    private void ApplySelectedProvider()
    {
        if (!(ProviderBox.SelectedItem is ProviderItem providerItem)) return;
        // Re-applying the provider already in the fields would throw away typed credentials.
        if (ReferenceEquals(providerItem, _appliedProvider)) return;
        _appliedProvider = providerItem;

        if (providerItem.IsManual)
        {
            AdvancedSettingsExpander.IsExpanded = true;
            HeaderServerTextBox.Focus();
            return;
        }

        HeaderServerTextBox.Text = providerItem.Headers;
        UploadServerTextBox.Text = providerItem.Upload;
        DownloadServerTextBox.Text = providerItem.Download;
        SetServerPort(providerItem.HeadersPort, HeaderServerPortComboBox);
        SetServerPort(providerItem.UploadPort, UploadServerPortComboBox);
        SetServerPort(providerItem.DownloadPort, DownloadServerPortComboBox);
        SyncCheckBoxesWithHeaderServer();
        EnableVal(bVal: true);
        UserNameTextBox.Clear();
        PasswordTextBox.Clear();
        ConnectionsCombo.Text = AutoConnectionsString;
        UserNameTextBox.Focus();
    }

    private void ProviderSelectie_Closing(object sender, CancelEventArgs e)
    {
        if (e.Cancel)
        {
            return;
        }

        try
        {
            base.Owner.Activate();
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    private void SetServerPort(int port, ComboBox portCombo)
    {
        switch (port)
        {
            case 563:
                portCombo.SelectedIndex = 0;
                break;
            case 443:
                portCombo.SelectedIndex = 1;
                break;
            case 0:
            case 119:
                portCombo.SelectedIndex = 2;
                break;
            case 80:
                portCombo.SelectedIndex = 3;
                break;
            default:
                portCombo.Text = port.ToString(CultureInfo.InvariantCulture);
                break;
        }
    }

    private void ProviderSelectie_Initialized(object sender, EventArgs e)
    {
        try
        {
            FitToWorkingArea();
            base.FontSize = (int)Settings.Default.FontSize;

            // Each border needs its own unbound brush; the storyboard animates BorderBrush.Color,
            // which cannot touch the frozen brush a Style setter would hand out.
            foreach (KeyValuePair<Control, Border> field in ValidatedFields)
            {
                field.Value.BorderBrush = new SolidColorBrush(FieldValidColor);
                field.Value.Tag = false;
            }

            BuildProviderView();
            ProviderBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(ProviderBox_OnTextChanged));
            // Dropdown rows live in the popup; their mouse events still route through the ComboBox.
            ProviderBox.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(ProviderBox_PreviewMouseUp), handledEventsToo: true);

            ServerInfo oDown = AppHelper.ServersDb.ODown;
            ServerInfo oHeader = AppHelper.ServersDb.OHeader;
            ServerInfo oUp = AppHelper.ServersDb.OUp;
            DownloadServerTextBox.Text = (oDown.Server.IsNullOrEmpty() ? "" : oDown.Server);
            HeaderServerTextBox.Text = (oHeader.Server.IsNullOrEmpty() ? "" : oHeader.Server);
            UploadServerTextBox.Text = (oUp.Server.IsNullOrEmpty() ? "" : oUp.Server);
            SetServerPort(oDown.Port, DownloadServerPortComboBox);
            SetServerPort(oHeader.Port, HeaderServerPortComboBox);
            SetServerPort(oUp.Port, UploadServerPortComboBox);
            SyncCheckBoxesWithHeaderServer();
            ConnectionsCombo.Text = (oDown.Connections + oHeader.Connections).ToString(CultureInfo.InvariantCulture);
            UserNameTextBox.Text = oDown.Username;
            PasswordTextBox.Password = ((!oDown.Password.IsNullOrEmpty()) ? oDown.Password : "");
            _lastSettingsString = CurrentSettingsString;
            _initializationFinished = true;
            ProviderBox.SelectionChanged += ProviderBox_SelectionChanged;
            UpdateProviderBoxSelection();
            UpdateProviderCountLabel();
            if ((ProviderBox.SelectedItem as ProviderItem)?.IsManual != false)
            {
                ProviderBox.Focus();
            }
            else
            {
                UserNameTextBox.Focus();
            }

            ValidateAll();
            UpdateConnectButtonState();
            Activate();
            StartCatalogueRefresh();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            Close();
        }
    }

    /// <summary>
    /// WPF sizes are device-independent pixels. A 700-DIP dialog is taller than a 720p desktop at
    /// 125% scaling, so cap the initial and maximum size to the actual working area and let the
    /// central ScrollViewer handle the remaining content.
    /// </summary>
    private void FitToWorkingArea()
    {
        const double margin = 24;
        Rect workArea = SystemParameters.WorkArea;
        double availableWidth = Math.Max(320, workArea.Width - margin);
        double availableHeight = Math.Max(280, workArea.Height - margin);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    private void BuildProviderView()
    {
        _providerView = new ListCollectionView(UsenetProviders.All.ToList());
        _providerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProviderItem.GroupDisplayName)));
        _providerView.Filter = candidate => ((ProviderItem)candidate).Matches(_providerFilter);
        ProviderBox.ItemsSource = _providerView;
    }

    /// <summary>
    /// Picks up a newly published catalogue without a restart. The dialog opens on whatever list is
    /// already known, so this never delays it; if the fetch changes anything the list is rebuilt
    /// around the provider currently selected.
    /// </summary>
    private void StartCatalogueRefresh()
    {
        // Marshalled with the Dispatcher rather than a captured TaskScheduler: this runs from
        // Initialized, which can fire before a SynchronizationContext exists, and
        // FromCurrentSynchronizationContext throws there.
        ProviderCatalogueSource.RefreshAsync().ContinueWith(task =>
        {
            if (!task.Result) return;
            base.Dispatcher.BeginInvoke((Action)delegate
            {
                // Leave the list alone while the user is busy in it; a list that reorders mid-search
                // is worse than one that updates the next time the dialog opens.
                if (_userIsSearching || ProviderBox.IsDropDownOpen || !IsLoaded) return;
                lock (_lockRoot)
                {
                    ProviderBox.SelectionChanged -= ProviderBox_SelectionChanged;
                    try
                    {
                        BuildProviderView();
                        _appliedProvider = null;
                    }
                    finally
                    {
                        ProviderBox.SelectionChanged += ProviderBox_SelectionChanged;
                    }
                }
                UpdateProviderBoxSelection();
                UpdateProviderCountLabel();
            });
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void SyncCheckBoxesWithHeaderServer()
    {
        string value = HeaderServerTextBox.Text + ":" + HeaderServerPortComboBox.Text;
        string text = DownloadServerTextBox.Text + ":" + DownloadServerPortComboBox.Text;
        string text2 = UploadServerTextBox.Text + ":" + UploadServerPortComboBox.Text;
        DownloadServerCheckBox.IsChecked = !text.Equals(value);
        UploadServerCheckBox.IsChecked = !text2.Equals(value);
    }

    /// <summary>Narrows the dropdown to what the user has typed, matching name as well as hostname.</summary>
    private void ProviderBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingProviderView || !_userIsSearching || !_initializationFinished) return;

        TextBox editBox = ProviderEditBox;
        string text = editBox?.Text ?? ProviderBox.Text ?? string.Empty;
        int caretIndex = editBox?.CaretIndex ?? text.Length;
        int revision = ++_providerUpdateRevision;

        // Refreshing a ListCollectionView synchronously from the editable ComboBox's routed
        // TextChanged event invalidates the index WPF is still using for that same keystroke.
        // Coalesce input and refresh after ComboBox has completed its internal edit operation.
        base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
        {
            if (revision != _providerUpdateRevision || !_userIsSearching) return;
            RefreshProviderFilter(text, caretIndex);
        });
    }

    private void RefreshProviderFilter(string text, int caretIndex)
    {
        _updatingProviderView = true;
        try
        {
            _providerFilter = text;
            // A selection that disappears during Refresh is what made the editable ComboBox use a
            // stale collection index. Clear it before the reset and restore only the edit text.
            ProviderBox.SelectedItem = null;
            _providerView.Refresh();
            _providerView.MoveCurrentToPosition(-1);
            ProviderBox.SelectedItem = null;

            TextBox editBox = ProviderEditBox;
            if (editBox != null)
            {
                editBox.Text = text;
                editBox.CaretIndex = Math.Min(caretIndex, text.Length);
                editBox.SelectionLength = 0;
            }
            else
            {
                ProviderBox.Text = text;
            }
        }
        finally
        {
            _updatingProviderView = false;
        }

        UpdateProviderCountLabel();
        if (!ProviderBox.IsDropDownOpen && text.Length > 0) ProviderBox.IsDropDownOpen = true;
    }

    /// <summary>A printable character in the edit box is the one unambiguous "user is searching" signal.</summary>
    private void ProviderBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // The box still holds the selected provider's name. Select it so the TextBox replaces it
        // with this keystroke; without that the first character appends, giving "Eweka" + "n" and
        // an empty list. Selecting rather than clearing keeps this independent of when the
        // TextBox does its own insert.
        if (!_userIsSearching) ProviderEditBox?.SelectAll();
        _userIsSearching = true;
    }

    /// <summary>Selecting the text on focus lets the next keystroke replace it, as a search box should.</summary>
    private void ProviderBox_GotFocus(object sender, RoutedEventArgs e)
    {
        StopAnimation();
        ProviderEditBox?.SelectAll();
    }

    private TextBox ProviderEditBox => ProviderBox.Template?.FindName("PART_EditableTextBox", ProviderBox) as TextBox;

    private void ProviderBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back:
            case Key.Delete:
                _userIsSearching = true;
                break;
            // Enter would otherwise reach the Connect button while the user is still searching.
            case Key.Enter when _userIsSearching || ProviderBox.IsDropDownOpen:
                if (ProviderBox.SelectedItem == null) SelectFirstMatch();
                _providerChosen = ProviderBox.SelectedItem != null;
                if (ProviderBox.IsDropDownOpen) ProviderBox.IsDropDownOpen = false;
                else ScheduleProviderChoiceCommit();
                e.Handled = true;
                break;
            case Key.Escape when _userIsSearching || ProviderBox.IsDropDownOpen:
                _providerChosen = false;
                if (ProviderBox.IsDropDownOpen) ProviderBox.IsDropDownOpen = false;
                else ScheduleProviderChoiceCommit();
                e.Handled = true;
                break;
            case Key.Down when !ProviderBox.IsDropDownOpen:
                ProviderBox.IsDropDownOpen = true;
                e.Handled = true;
                break;
        }
    }

    private void SelectFirstMatch()
    {
        ProviderItem first = _providerView.Cast<ProviderItem>().FirstOrDefault();
        if (first != null) ProviderBox.SelectedItem = first;
    }

    private void ClearProviderFilter()
    {
        _userIsSearching = false;
        if (_providerFilter.Length == 0) return;
        _updatingProviderView = true;
        try
        {
            _providerFilter = string.Empty;
            _providerView.Refresh();
        }
        finally
        {
            _updatingProviderView = false;
        }
        UpdateProviderCountLabel();
    }

    /// <summary>Puts the selected provider's name back in the edit box after an abandoned search.</summary>
    private void RestoreProviderText()
    {
        _userIsSearching = false;
        _updatingProviderView = true;
        try
        {
            if (_appliedProvider != null && !ReferenceEquals(ProviderBox.SelectedItem, _appliedProvider))
                ProviderBox.SelectedItem = _appliedProvider;
            ProviderBox.Text = _appliedProvider?.Name ?? string.Empty;
        }
        finally
        {
            _updatingProviderView = false;
        }
    }

    private void UpdateProviderCountLabel()
    {
        ProviderCountLabel.Text = string.Format(CultureInfo.CurrentCulture, Words.ProviderCount, _providerView.Count, UsenetProviders.All.Count);
    }

    private void UpdateProviderBoxSelection()
    {
        if (!_initializationFinished)
        {
            return;
        }

        lock (_lockRoot)
        {
            ProviderBox.SelectionChanged -= ProviderBox_SelectionChanged;
            _userIsSearching = false;
            try
            {
                ProviderItem match = UsenetProviders.Match(UsenetProviders.All, HeaderServerTextBox.Text)
                    ?? UsenetProviders.All.First(p => p.IsManual);
                if (!ReferenceEquals(ProviderBox.SelectedItem, match))
                {
                    ProviderBox.SelectedItem = match;
                }
                ProviderBox.Text = match.Name;
                _appliedProvider = match;
            }
            finally
            {
                ProviderBox.SelectionChanged += ProviderBox_SelectionChanged;
            }
        }
    }

    private void UpdateConnectButtonState()
    {
        if (_initializationFinished)
        {
            ConnectButton.IsEnabled = AreSettingsChanged && _invalidFields.Count == 0;
        }
    }

    private bool ValidateAll()
    {
        return ValidateAddress(HeaderServerTextBox) & ValidateAddress(DownloadServerTextBox) & ValidateAddress(UploadServerTextBox) & ValidatePort(HeaderServerPortComboBox) & ValidatePort(DownloadServerPortComboBox) & ValidatePort(UploadServerPortComboBox) & ValidateConnections() & ValidateUsername() & ValidatePassword();
    }

    /// <summary>Records the outcome for one field and paints its border accordingly.</summary>
    private bool Record(Control field, bool valid)
    {
        if (valid) _invalidFields.Remove(field);
        else _invalidFields.Add(field);
        RefreshFieldBorder(field);
        return valid;
    }

    private void RefreshFieldBorders()
    {
        foreach (KeyValuePair<Control, Border> field in ValidatedFields) RefreshFieldBorder(field.Key);
    }

    private void RefreshFieldBorder(Control field)
    {
        Border border = ValidatedFields.FirstOrDefault(f => ReferenceEquals(f.Key, field)).Value;
        // Leave a field alone while its "check this" storyboard is running.
        if (border == null || (border.Tag is bool running && running)) return;
        border.BorderBrush = new SolidColorBrush(_invalidFields.Contains(field) ? FieldInvalidColor : FieldValidColor);
    }

    private bool ValidateAddress(TextBox addrBox)
    {
        return Record(addrBox, !addrBox.Text.Trim().IsNullOrEmpty() && (AppHelper.IsDomainName(addrBox.Text) || AppHelper.IsIp(addrBox.Text)));
    }

    private bool ValidatePort(ComboBox portCombo)
    {
        if (portCombo.Text.IsNullOrEmpty())
        {
            return Record(portCombo, valid: false);
        }

        return Record(portCombo, int.TryParse(portCombo.Text.Split()[0], out int result) && result > 0 && result < 65535);
    }

    private bool ValidateConnections()
    {
        if (ConnectionsCombo.Text.IsNullOrEmpty())
        {
            return Record(ConnectionsCombo, valid: false);
        }

        return Record(ConnectionsCombo, ConnectionsCombo.Text.Equals(AutoConnectionsString) || (int.TryParse(ConnectionsCombo.Text, out int result) && result >= 3 && result <= 100));
    }

    private bool ValidateUsername() => Record(UserNameTextBox, UserNameTextBox.Text.Length < 200);

    private bool ValidatePassword() => Record(PasswordTextBox, PasswordTextBox.Password.Length < 200);

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        StopAnimation();
        ConnectButton.Focus();
        EnableVal(bVal: false);
        base.Cursor = Cursors.Wait;
        Mouse.OverrideCursor = Cursors.Wait;
        ConnectProgressRing.IsActive = true;
        StatusLabel.Text = Words.Testing;
        UpdateLayout();
        DoButton();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HeaderServerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateAddress(HeaderServerTextBox);
        UpdateConnectButtonState();
        UpdateProviderBoxSelection();
        SyncFieldsWithHeaderServer();
    }

    private void SyncFieldsWithHeaderServer()
    {
        if (!DownloadServerCheckBox.IsChecked.GetValueOrDefault())
        {
            DownloadServerTextBox.Text = HeaderServerTextBox.Text;
            DownloadServerPortComboBox.Text = HeaderServerPortComboBox.Text;
        }

        if (!UploadServerCheckBox.IsChecked.GetValueOrDefault())
        {
            UploadServerTextBox.Text = HeaderServerTextBox.Text;
            UploadServerPortComboBox.Text = HeaderServerPortComboBox.Text;
        }
    }

    private void DownloadServerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateAddress(DownloadServerTextBox);
        UpdateConnectButtonState();
    }

    private void UploadServerTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateAddress(UploadServerTextBox);
        UpdateConnectButtonState();
    }

    private void UserNameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateUsername();
        UpdateConnectButtonState();
    }

    private void PasswordTextBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ValidatePassword();
        UpdateConnectButtonState();
    }

    private void HiddenPortTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidatePort(HeaderServerPortComboBox);
        UpdateConnectButtonState();
    }

    private void HiddenConnectionsTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateConnections();
        UpdateConnectButtonState();
    }

    private void HeaderServerPortComboBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SyncFieldsWithHeaderServer();
    }

    private void DownloadServerCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        DownloadServerTextBox.IsEnabled = false;
        DownloadServerPortComboBox.IsEnabled = false;
        if (!DownloadServerCheckBox.IsChecked.GetValueOrDefault())
        {
            DownloadServerTextBox.Text = HeaderServerTextBox.Text;
            DownloadServerPortComboBox.Text = HeaderServerPortComboBox.Text;
        }
    }

    private void DownloadServerCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        DownloadServerTextBox.IsEnabled = true;
        DownloadServerPortComboBox.IsEnabled = true;
    }

    private void UploadServerCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        UploadServerTextBox.IsEnabled = false;
        UploadServerPortComboBox.IsEnabled = false;
        if (!UploadServerCheckBox.IsChecked.GetValueOrDefault())
        {
            UploadServerTextBox.Text = HeaderServerTextBox.Text;
            UploadServerPortComboBox.Text = HeaderServerPortComboBox.Text;
        }
    }

    private void UploadServerCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        UploadServerTextBox.IsEnabled = true;
        UploadServerPortComboBox.IsEnabled = true;
    }
}
