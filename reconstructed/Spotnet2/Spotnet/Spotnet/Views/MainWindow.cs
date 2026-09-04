using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Resources;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using MahApps.Metro.Controls;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Microsoft.Win32;
using NLog;
using Newtonsoft.Json;
using Spotnet.Browser;
using Spotnet.Controls;
using Spotnet.DAL;
using Spotnet.DataVirtualization;
using Spotnet.Deployment;
using Spotnet.Downloader;
using Spotnet.Downloader.Controls;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Model.Newznab;
using Spotnet.Phuse;
using Spotnet.Properties;
using Spotnet.TaskSchedulers;
using Spotnet.Utilities;
using Spotnet.ViewModel;

namespace Spotnet.Views;
public partial class MainWindow : MetroWindow
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly object _lockAssignSpotToTab = new object ();
    private readonly object _lockDownloadScheduling = new object ();
    private readonly Tabs _tabDb;
    private readonly TaskScheduler _taskSchedulerCurrentContext;
    private readonly NotifyIcon _trayNotify;
    private readonly ManualResetEventSlim _waitForMainWindowLoaded = new ManualResetEventSlim();
    private readonly ManualResetEventSlim _waitForProviderSelectedEvent = new ManualResetEventSlim(initialState: false);
    private long _beforeUpdateLastRow;
    private DateTime _dbUpdatePauseStartTime = DateTime.MinValue;
    private DateTime _dbUpdateStartTime = DateTime.MinValue;
    private int _lastTab;
    private MainToolBarControl _mainToolBar;
    private SaveSpotsRow _newSpotsCount;
    private NamedPipeServerStream _pipe;
    private bool _stateChangedDidOnce;

    private bool _updatePromptOpen;
    internal System.Windows.Controls.ContextMenu HeaderMenu;
    internal Action OnWindowPrepared;
    internal SpotProvider SpotProvider;
    public string WaitString;
    private readonly object _lockUpdateNewCats = new object ();
    private bool _shouldUpdateNewCatsBeRepeated;
    private int[] _lastNewCatsResult;
    private readonly DownloadsStatusBar _downloadsStatusBar = new DownloadsStatusBar();
    private static StatusBarViewModel StatusBarVm => ((ViewModelLocator)System.Windows.Application.Current.Resources["Locator"]).StatusBar;
    private static SpotsListViewModel SpotsListVm => ((ViewModelLocator)System.Windows.Application.Current.Resources["Locator"]).SpotsList;
    private static VisibilityViewModel VisibilityVm => ((ViewModelLocator)System.Windows.Application.Current.Resources["Locator"]).Visibility;
    private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)System.Windows.Application.Current.Resources["Locator"]).MainWindow;

    internal bool IsDownloadsTabSelectedAndVisible
    {
        get
        {
            if (base.WindowState == WindowState.Minimized || base.Visibility != 0 || TabControl1.SelectedItem == null)
            {
                return false;
            }

            if (TabControl1.SelectedItem is UnCloseableTabItem unCloseableTabItem)
            {
                return unCloseableTabItem.IsDownloadTab;
            }

            return false;
        }
    }

    internal bool IsSpotsTabSelectedAndVisible
    {
        get
        {
            if (base.WindowState == WindowState.Minimized || base.Visibility != 0 || TabControl1.SelectedItem == null)
            {
                return false;
            }

            return TabControl1.SelectedIndex == 0;
        }
    }

    public event Action TabSelectionChanged;
    public static event Action ColoringForSpotsChanged;
    public static event Action ColoringForFiltersChanged;
    public MainWindow()
    {
        try
        {
            if (!Sys.IsShutdownRequested)
            {
                Sys.MainWindow = this;
                _taskSchedulerCurrentContext = TaskScheduler.FromCurrentSynchronizationContext();
                _tabDb = new Tabs();
                _trayNotify = new NotifyIcon();
                base.StateChanged += MainWindow_StateChanged;
                base.Closing += MainWindow_Closing;
                _trayNotify.DoubleClick += TrayNotify_DoubleClick;
                _trayNotify.Click += TrayNotify_Click;
                // One notification icon for both jobs: minimise-to-tray and desktop
                // notifications. A second one would show up as a second tray entry.
                NotificationHelper.AttachTrayIcon(_trayNotify);
                base.Activated += MainWindow_OnActivated;
                InitializeDownloader();
                SpotProvider = new SpotProvider();
                InitializeComponent();
                UpdateTitle("Spotnet");
                CollectionViewSource.GetDefaultView(TabControl1.Items).CollectionChanged += delegate
                {
                    RefreshTabItemsCount();
                };
                MainWindowVm.FiltersDb.FiltersLoaded += delegate
                {
                    UpdateNewCats(null, restoreTheLastResult: true);
                };
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            Sys.Shutdown();
        }
    }

    private void MainWindow_OnActivated(object sender, EventArgs eventArgs)
    {
        _lastTab = 1;
        TabControl1_SelectionChanged(null, null);
    }

    private void UpdateTitle(string title)
    {
        if (SquirrelStuff.UpdateChannel != "release")
        {
            title = title + " [" + SquirrelStuff.UpdateChannel + "]";
        }

        base.Title = title;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Sys.IsShutdownRequested = true;
        if (Sys.DownloadsPlayer != null)
        {
            Sys.DownloadsPlayer.Dispose();
        }

        Task<bool> task = Sys.StatsReporter.ReportOnStopAsync();
        Task task2 = null;
        if (Sys.Downloader != null)
        {
            Sys.Downloader.DownloadsProgressChanged -= DownloaderProgressChanged;
            task2 = Sys.Downloader.ShutdownProcessAsync();
        }

        NotificationHelper.Shutdown();
        SpotRowViewModel.DisposeTaskScheduler();
        DbUpdater.DbUpdateTimerStop();
        DbUpdater.Stop();
        PagesFactory.DisposeAllPages();
        task?.Wait(TimeSpan.FromSeconds(3.0));
        task2?.Wait(TimeSpan.FromSeconds(3.0));
        System.Windows.Application.Current.Shutdown();
        Log.Debug("Shutdown complete");

        // Handing over to Setup: end the process now. Everything that must be finished is
        // finished above - downloads stopped, the database closed, statistics reported -
        // but download and browser threads outlive the window by around half a minute,
        // and Setup can do nothing but wait for them behind a window with no progress on
        // it. Only on this path; an ordinary exit is left exactly as it was.
        if (AppUpdater.HandoverInProgress)
        {
            LogManager.Flush();
            LogManager.Shutdown();
            Environment.Exit(0);
        }
    }

    public void InitializeDownloader()
    {
        if (Sys.Downloader != null)
        {
            Sys.Downloader.DownloadsProgressChanged -= DownloaderProgressChanged;
            Sys.Downloader.OnDownloaderLoadedFirstTime -= EnsureDownloadsContentIsSet;
            Sys.Downloader.Dispose();
            if (DownloadsTab.Content is DownloadsControl downloadsControl)
            {
                downloadsControl.Dispose();
                DownloadsTab.Content = new Spotnet.Controls.ProgressRing
                {
                    IsActive = true
                };
            }
        }

        SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NzbGet);
        if (Settings.Default.ExternalNzbGet)
        {
            Sys.Downloader = new NzbGetDownloader();
        }
        else
        {
            Sys.Downloader = new SpotnetDownloader();
        }

        Sys.Downloader.DownloadsProgressChanged += DownloaderProgressChanged;
        Sys.Downloader.OnDownloaderLoadedFirstTime += EnsureDownloadsContentIsSet;
        Task.Run(delegate
        {
            Sys.Downloader.UpdateDownloadSpeedLimit(Settings.Default.SpeedLimit);
        });
    }

    private void EnsureDownloadsContentIsSet()
    {
        if (Sys.IsShutdownRequested)
        {
            return;
        }

        DispatcherHelper.UIDispatcher.Invoke(delegate
        {
            if (!(DownloadsTab.Content is DownloadsControl))
            {
                DownloadsTab.Content = new DownloadsControl();
            }
        }, DispatcherPriority.Background);
    }

    internal void UpdateMainMenuVisibility()
    {
        if (VisibilityVm.IsVisibleLeftPanel)
        {
            if (!MainToolBarDockInLeftPanel.Children.Contains(_mainToolBar))
            {
                MainToolBarDockInTop.Children.Remove(_mainToolBar);
                MainToolBarDockInLeftPanel.Children.Add(_mainToolBar);
            }
        }
        else if (!MainToolBarDockInTop.Children.Contains(_mainToolBar))
        {
            MainToolBarDockInLeftPanel.Children.Remove(_mainToolBar);
            MainToolBarDockInTop.Children.Add(_mainToolBar);
        }

        _mainToolBar.UpdateVisibility();
    }

    internal bool OnKeyDown(PreviewKeyDownEventArgs e, bool updateDownloadFolder = true)
    {
        return DispatcherHelper.UIDispatcher.Invoke(delegate
        {
            if (!e.Control)
            {
                return false;
            }

            switch (e.KeyCode)
            {
                case Keys.U:
                    ResetNewSpotsCountAndStartDbUpdateTimer(null, null);
                    return true;
                case Keys.N:
                    ExecuteAddNewSpot(null, null);
                    return true;
                case Keys.O:
                    ExecuteOpenNzb(null, null);
                    return true;
                case Keys.L:
                    ExecuteOpenSpotlink(null, null);
                    return true;
                case Keys.P:
                    ExecuteSelectProvider(null, null);
                    return true;
                case Keys.D:
                    ExecuteDownloadFolderChange(updateDownloadFolder);
                    return true;
                case Keys.W:
                case Keys.F4:
                    ExecuteCloseActiveTab(null, null);
                    return true;
                default:
                    return false;
            }
        });
    }

    private void ExecuteCloseActiveTab(object sender, RoutedEventArgs e)
    {
        (TabControl1.SelectedItem as CloseableTabItem)?.CloseMe();
    }

    private void GotoTab1(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 0;
    }

    private void GotoTab2(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 1;
    }

    private void GotoTab3(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 2;
    }

    private void GotoTab4(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 3;
    }

    private void GotoTab5(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 4;
    }

    private void GotoTab6(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 5;
    }

    private void GotoTab7(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 6;
    }

    private void GotoTab8(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 7;
    }

    private void GotoTab9(object sender, RoutedEventArgs e)
    {
        TabControl1.SelectedIndex = 8;
    }

    internal void AddComplainReportToTheSpot(SpotEx spotEx)
    {
        SpotRowChild spot = default(SpotRowChild);
        spot.Title = spotEx.Title;
        spot.Poster = spotEx.Poster;
        spot.Modulus = spotEx.User.Modulus;
        spot.MessageId = spotEx.MessageId;
        spot.NumberOfSpamReports = spotEx.NumberOfSpamReports;
        spot.Stamp = spotEx.Stamp;
        spot.Cat = spotEx.Category;
        SpotRowViewModel row = SpotRowViewModel.InitializeNewSpotRow(spot);
        AddComplainReportToTheSpot(row);
    }

    internal void AddComplainReportToTheSpot(SpotRowViewModel row)
    {
        if (row.Id == 0L && row.SpotMessageId.IsNullOrEmpty())
        {
            Log.Warn("Neither MessageId nor row.id are not specified");
            return;
        }

        string text = row.SpotMessageId;
        if (text.IsNullOrEmpty())
        {
            text = SpotHelper.MakeMsg(SpotProvider.GetMessageId(row.Id));
        }

        string zErr = "";
        if (text.IsNullOrEmpty() || text.Length < 3)
        {
            Log.Warn("MessageId is too short: " + text + ". Row: " + row.Id);
            return;
        }

        ComplainToTheSpot complainToTheSpot = new ComplainToTheSpot(row.Titel, row.Afzender, row.AfzenderId, isItToRemove: false);
        complainToTheSpot.ShowDialog();
        string result = complainToTheSpot.Result;
        if (result.IsNullOrWhiteSpace())
        {
            return;
        }

        try
        {
            if (complainToTheSpot.AddToBlacklist)
            {
                BlackAndWhite.AddBlack(AppHelper.StripNonAlphaNumericCharacters(row.Afzender), row.Modulus);
                SpotsListVm.SpotsContainer.RefreshAllItemsStyle();
            }

            DoWait(Words.ReportSending);
            if (Spots.CreatReport(AppHelper.UploadPhuse, AppHelper.StripNonAlphaNumericCharacters(Settings.Default.Nickname), result, Settings.Default.ReportGroup, text, row.Titel, ref zErr))
            {
                AppHelper.ShowPopupMessage(Words.MessageIsSend);
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            zErr = ex.Message;
        }
        finally
        {
            EndWait();
        }

        AppHelper.Error(zErr);
    }

    public void UpdateTabItemHeaderAsync(string sHeader, PageTypeEnum sIcon, TabItem tabItem = null)
    {
        DispatcherHelper.UIDispatcher.InvokeAsync(delegate
        {
            UpdateTabItemHeader(sHeader, sIcon, tabItem);
        });
    }

    public void UpdateTabItemHeader(string sHeader, PageTypeEnum sIcon, TabItem tabItem = null)
    {
        if (tabItem == null)
        {
            tabItem = (TabItem)TabControl1.Items[0];
        }

        tabItem.Header = MainWindowVm.GenerateTabItemHeader(sHeader, sIcon);
    }

    internal void DeleteArticle(string spotMsgId, string sTitle)
    {
        if (spotMsgId.IsNullOrEmpty())
        {
            return;
        }

        ComplainToTheSpot complainToTheSpot = new ComplainToTheSpot(sTitle, "", "", isItToRemove: true);
        complainToTheSpot.ShowDialog();
        string result = complainToTheSpot.Result;
        if (result.IsNullOrWhiteSpace())
        {
            return;
        }

        try
        {
            DoWait(Words.SpotRemoving);
            Engine uploadPhuse = AppHelper.UploadPhuse;
            string nickname = Settings.Default.Nickname;
            string headerGroup = Settings.Default.HeaderGroup;
            if (!Spots.DeleteSpot(uploadPhuse, nickname, sTitle, result, headerGroup, spotMsgId, out var zErr))
            {
                string text = "Failed to remove the spot: " + spotMsgId + ". " + zErr + " ";
                Log.Error(text);
                AppHelper.Error(text);
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
        finally
        {
            EndWait();
            AppHelper.ShowPopupMessage(string.Format(Words.SpotRemoved, sTitle), inTheCenter: false, TimeSpan.FromSeconds(3.0));
        }
    }

    internal void OnDbUpdateEnd(Task task)
    {
        try
        {
            if (task == null || Sys.IsShutdownRequested)
            {
                StatusBarVm.SetDbUpdateProgressStatus(Words.DbUpdatePaused, -1);
                return;
            }

            _newSpotsCount.Add(DbUpdater.LastHeaderResults);
            DbUpdater.LastHeaderResults = new SaveSpotsRow();
            OnSpotsUpdate(null);
            if (task.IsCanceled || DbUpdater.IsCancellationRequested)
            {
                Log.Debug("Database update cancelled/paused");
                while (DateTime.Now - _dbUpdatePauseStartTime < TimeSpan.FromMilliseconds(500.0))
                {
                    Thread.Sleep(50);
                }

                string message = ((DbUpdater.LastHeaderResults != null) ? (Words.DbUpdatePaused + " (" + SpotHelper.FormatLong(_newSpotsCount.SpotsAdded) + " " + Words.newWord + " " + Words.Spots + " " + Words.found + ")") : Words.DbUpdatePaused);
                StatusBarVm.SetDbUpdateProgressStatus(message, -1);
                return;
            }

            if (task.Exception != null)
            {
                string message2 = task.Exception.TheMostInnerException().Message;
                message2 = ExtendDbUpdateErrorMessage(message2);
                Log.Debug("Database update not finished: " + message2);
                while (DateTime.Now - _dbUpdateStartTime < TimeSpan.FromMilliseconds(500.0))
                {
                    Thread.Sleep(50);
                }

                StatusBarVm.SetDbUpdateProgressStatus(Words.DbUpdateFailed, -1);
                StatusBarVm.SetStatusBarProgressTooltip(message2);
                DbUpdater.Stop();
                return;
            }

            SetDbUpToDateStatus(spotsAreNotUpToDate: false, commentsAreNotUpToDate: false);
            SaveSpotsRow lastHeaderResults = DbUpdater.LastHeaderResults;
            string message3;
            if (lastHeaderResults != null)
            {
                Log.Debug("Database update finished. New spots: " + lastHeaderResults.SpotsAdded + " deleted spots: " + lastHeaderResults.SpotsDeleted);
                message3 = "[" + DateTime.Now.ToLocalTime().ToShortTimeString() + "] " + Words.DbIsUpToDate + " (" + SpotHelper.FormatLong(_newSpotsCount.SpotsAdded) + " " + Words.newWord + " " + Words.Spots + " " + Words.found + ")";
            }
            else
            {
                message3 = Words.DbUpdatePaused;
            }

            StatusBarVm.SetDbUpdateProgressStatus(message3, -1);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
        finally
        {
            EndWait();
            this.DispatchAsync(delegate
            {
                _mainToolBar.EnableUpdate();
            });
            StatusBarVm.DbUpdateImageStarted = false;
            StatusBarVm.DbUpdateImageEnabled = true;
        }
    }

    private string ExtendDbUpdateErrorMessage(string errorMsg)
    {
        if (errorMsg.Contains("Too many connections"))
        {
            errorMsg = errorMsg + ". " + Words.ConnectionsLimitHowToSolve;
        }

        return errorMsg;
    }

    internal void LoadHeaderMenu()
    {
        if (!(SpotsListVm.SpotsContainer.Spots is System.Windows.Controls.DataGrid dataGrid))
        {
            return;
        }

        HeaderMenu = new System.Windows.Controls.ContextMenu
        {
            FontFamily = base.FontFamily,
            FontSize = (double)System.Windows.Application.Current.Resources["ContextMenuFontSize"],
            FontStyle = base.FontStyle,
            Resources = AppHelper.GetMenuResourceDictionary
        };
        HeaderMenu.PreviewMouseUp += HeaderMenu_PreviewMouseUp;
        HeaderMenu.PreviewMouseDown += HeaderMenu_PreviewMouseDown;
        System.Windows.Controls.MenuItem[] array = new System.Windows.Controls.MenuItem[dataGrid.Columns.Count];
        foreach (DataGridColumn column in dataGrid.Columns)
        {
            System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
            {
                Header = RuntimeHelpers.GetObjectValue(column.Header),
                IsChecked = (column.Visibility == Visibility.Visible)
            };
            if (array[column.DisplayIndex] == null)
            {
                array[column.DisplayIndex] = menuItem;
            }
            else
            {
                AppHelper.Error("ColErr");
            }
        }

        foreach (System.Windows.Controls.MenuItem item in array.Where((System.Windows.Controls.MenuItem t) => t != null))
        {
            HeaderMenu.Items.Add(item);
        }
    }

    internal void CloseAllSpotsTab()
    {
        foreach (CloseableTabItem item in
            from tab in TabControl1.Items.OfType<CloseableTabItem>().ToList()
            where tab.Tag is SpotEx
            select tab)
        {
            item.CloseMe();
        }
    }

    private void OnLoad()
    {
        try
        {
            // Step 6: Interface ready â€” animate to 100% then fade splash
            Views.SplashWindow.SetProgress(6);
            System.Threading.Thread.Sleep(200); // brief pause so user sees the step
            // Ask about a new version while the splash is still up, so an update is
            // offered as Spotnet opens instead of a minute into using it. Bounded, so an
            // unreachable server delays the start by a moment and nothing more.
            AppUpdater.CheckOnStartup(Views.SplashWindow.SetMessage);
            Views.SplashWindow.SetProgress(7); // "Ready!"
            System.Threading.Thread.Sleep(300); // let the bar fill animate
            App.CloseSplash();

            if (AppHelper.ServersDb.ODown.Server.Trim().IsNullOrEmpty())
            {
                if (SelectProvider())
                {
                    _waitForProviderSelectedEvent.Set();
                }
                else
                {
                    Log.Debug("Provider is not selected");
                    Close();
                }
            }

            _waitForMainWindowLoaded.Set();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            Close();
        }
    }

    private void RefreshTabItemsCount()
    {
        MainWindowVm.TabItemsCount = TabControl1.Items.Count;
    }

    internal void OpenAbout()
    {
        // About is a dialog now, not a tab: it is read once and dismissed, so it has no
        // business holding a slot in the tab strip next to Spots and Downloads.
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private TabItem FindTabByMessageId(string sMes)
    {
        foreach (TabItem item in (IEnumerable)TabControl1.Items)
        {
            if (!(item.Tag is SpotEx spotEx))
            {
                continue;
            }

            try
            {
                if (SpotHelper.MakeMsg(spotEx.MessageId).Equals(SpotHelper.MakeMsg(sMes)))
                {
                    return item;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        return null;
    }

    internal void OpenSpot(ISpotRow spotRow, CloseableTabItem exTab = null, bool saveParrentTab = true, bool isPreview = false)
    {
        try
        {
            string text = SpotHelper.MakeMsg(spotRow.SpotMessageId);
            bool flag = Keyboard.Modifiers != ModifierKeys.Control;
            if (exTab == null)
            {
                TabItem tabItem = FindTabByMessageId(text);
                TabItem parentTab = TabControl1.SelectedItem as TabItem;
                if (tabItem != null)
                {
                    tabItem.IsSelected = flag;
                    exTab = tabItem as CloseableTabItem;
                }
                else
                {
                    exTab = PrepareTab(spotRow.Titel, text, flag, spotRow.Modulus, isPreview);
                }

                if (flag)
                {
                    if (exTab != null && saveParrentTab)
                    {
                        exTab.SetParentTab(parentTab);
                    }

                    return;
                }
            }

            Task.Run(delegate
            {
                string errorMsg = "";
                SpotEx spotOut = null;
                bool flag2;
                try
                {
                    SpotEx spotEx = FileCacheManager.Get(spotRow.SpotMessageId);
                    if (spotEx != null && !spotEx.Body.IsNullOrEmpty())
                    {
                        Thread.Sleep(250);
                        spotOut = spotEx;
                        flag2 = true;
                    }
                    else
                    {
                        UpdateTabItemHeaderAsync(WebUtility.HtmlDecode(spotRow.Titel), PageTypeEnum.Loading, exTab);
                        flag2 = Spots.GetSpot(AppHelper.HeaderPhuse, Settings.Default.HeaderGroup, spotRow.Id, spotRow.SpotMessageId, ref spotOut, AppHelper.HeaderSettings(bIncludePosition: false), ref errorMsg);
                    }

                    if (flag2)
                    {
                        spotOut.NumberOfSpamReports = SpotProvider.GetTheNumberOfSpamReports(spotOut.MessageId);
                    }
                }
                catch (Exception ex2)
                {
                    Log.Exception(ex2);
                    flag2 = false;
                    errorMsg = "GetSpot: " + ex2.Message;
                }

                if (exTab != null)
                {
                    if (!flag2)
                    {
                        DispatcherHelper.CheckBeginInvokeOnUI(delegate
                        {
                            exTab.CloseMe();
                        });
                        if (!Sys.IsShutdownRequested)
                        {
                            if (spotOut != null)
                            {
                                errorMsg = errorMsg + ". " + spotOut.MessageId + ": " + spotOut.Title;
                            }

                            Log.Debug("Failed to get spot: " + errorMsg);
                            AppHelper.Error(errorMsg);
                        }
                    }
                    else
                    {
                        AssignSpotToTab(spotOut, exTab);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            if (spotRow != null)
            {
                Log.Error("Failed to open spot: " + spotRow.SpotMessageId);
            }
            else
            {
                Log.Error("spotRow is null");
            }

            Log.Exception(ex, showToClient: true);
        }
    }

    private void AssignSpotToTab(SpotEx spotEx, CloseableTabItem closeableTabItem)
    {
        lock (_lockAssignSpotToTab)
        {
            try
            {
                if (closeableTabItem == null)
                {
                    throw new ArgumentNullException("closeableTabItem");
                }

                if (AppHelper.ShiftKeyDown)
                {
                    spotEx.DoNotLoadImageAutomatically = true;
                }

                if (spotEx.ImageWidth > 0 || spotEx.ImageHeight > 0)
                {
                    if (spotEx.ImageWidth > 350)
                    {
                        spotEx.ImageWidth = 350;
                    }

                    if (spotEx.ImageHeight < 32)
                    {
                        spotEx.ImageWidth = 32;
                    }

                    if (spotEx.ImageHeight > 350)
                    {
                        spotEx.ImageHeight = 350;
                    }

                    if (spotEx.ImageHeight < 32)
                    {
                        spotEx.ImageHeight = 32;
                    }
                }

                Sys.StatsReporter.ReportOnSpotOpenAsync(spotEx.MessageId);
                OpenPage(PageTypeEnum.SpotLoaded, spotEx.Title, saveParrentTab: false, closeableTabItem, spotEx).Forget();
            }
            catch (Exception ex)
            {
                Log.Exception(ex, showToClient: true);
                if (closeableTabItem != null)
                {
                    DispatcherHelper.CheckBeginInvokeOnUI(delegate
                    {
                        TabControl1.Items.Remove(closeableTabItem);
                    });
                }
            }
        }
    }

    internal async Task<IPage> OpenPage(PageTypeEnum pageType, string address = "", bool saveParrentTab = false, TabItem oldTab = null, SpotEx spotEx = null)
    {
        try
        {
            return await DispatcherHelper.UIDispatcher.Invoke(async delegate
            {
                bool flag = Keyboard.Modifiers != ModifierKeys.Control;
                if (pageType != PageTypeEnum.WebPage || address.Contains("spotcloud.spotnet.wf"))
                {
                    IPage page2 = PagesFactory.GetPage(pageType, address, spotEx);
                    if (page2 != null)
                    {
                        foreach (object item in (IEnumerable)TabControl1.Items)
                        {
                            object content = ((TabItem)item).Content;
                            if (content != null && content.Equals(page2))
                            {
                                if (flag || address.Contains("spotcloud.spotnet.wf"))
                                {
                                    TabControl1.SelectedItem = item;
                                }

                                return page2;
                            }
                        }
                    }
                }

                TabItem newTab = oldTab ?? new CloseableTabItem
                {
                    AutoSelect = flag
                };
                bool isPromo = newTab.Tag is UrlInfo urlInfo && urlInfo.IsPromo;
                if (!isPromo && saveParrentTab)
                {
                    ((CloseableTabItem)newTab).SetParentTab(TabControl1.SelectedItem as TabItem);
                }

                IPage newPage = await PagesFactory.NewPage(pageType, newTab, address, spotEx);
                if (!isPromo)
                {
                    UpdateTabItemHeader(newPage.Title, newPage.PageType, newTab);
                    newPage.TitleChangedEvent += delegate (object page)
                    {
                        WebPageOnTitleOrTypeChanged(page);
                        SaveTabs();
                    };
                    newPage.TypeChangedEvent += delegate (object page)
                    {
                        WebPageOnTitleOrTypeChanged(page);
                    };
                }
                else
                {
                    newPage.TitleChangedEvent += delegate (object page)
                    {
                        WebPageOnTitleOrTypeChanged(page, isPromo: true);
                    };
                    newPage.TypeChangedEvent += delegate (object page)
                    {
                        WebPageOnTitleOrTypeChanged(page, isPromo: true);
                    };
                }

                newPage.DocumentReadyEvent += delegate (object o, PageReadyEventArgs e)
                {
                    if (e == null || e.ReadyState == PageReadyState.Ready)
                    {
                        newTab.Content = newPage;
                    }
                };
                string title = ((!isPromo) ? newPage.Title : ((UrlInfo)newTab.Tag).Title);
                if (pageType == PageTypeEnum.WebPage)
                {
                    newTab.Tag = new UrlInfo
                    {
                        Title = title,
                        Url = newPage.Uri.AbsoluteUri,
                        TabLoaded = true,
                        IsPromo = isPromo
                    };
                }
                else if (spotEx != null)
                {
                    newTab.Tag = spotEx;
                }

                if (oldTab == null)
                {
                    TabControl1.Items.Add(newTab);
                    SaveTabs();
                }

                DispatcherHelper.UIDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
                {
                    ((System.Windows.Controls.UserControl)newPage).ApplyTemplate();
                    // Any engine-backed page shows the spinner while it loads; the native
                    // spot renderer does not, because it paints from the local database.
                    // WebView2Page has to be named here too now that it backs the release
                    // notes, feedback and downloads tabs - without it those tabs sat with
                    // no loading indicator at all.
                    if (pageType != PageTypeEnum.SpotLoaded && newPage is WebView2Page)
                    {
                        title = ((!isPromo) ? newPage.Title : ((UrlInfo)newTab.Tag).Title);
                        UpdateTabItemHeaderAsync(title, PageTypeEnum.Loading, newTab);
                    }
                });
                return newPage;
            });
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return null;
        }
    }

    public void ReloadAllSpotPages()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            foreach (TabItem item in (IEnumerable)TabControl1.Items)
            {
                if (item.Content is IPage page)
                {
                    string title = page.Title;
                    item.Content = null;
                    page.Dispose();
                    UpdateTabItemHeader(title, PageTypeEnum.SpotNotLoaded, item);
                }
            }

            _lastTab = 1;
            TabControl1_SelectionChanged(null, null);
        });
    }

    internal CloseableTabItem PrepareTab(string sTitle, string msg, bool autoSelect = true, string modulus = "", bool isPreview = false)
    {
        bool flag = msg.StartsWith("<");
        CloseableTabItem closeableTabItem = new CloseableTabItem
        {
            AutoSelect = autoSelect
        };
        string sHeader = WebUtility.HtmlDecode(sTitle);
        UpdateTabItemHeaderAsync(sHeader, autoSelect ? PageTypeEnum.Loading : (flag ? PageTypeEnum.SpotNotLoaded : PageTypeEnum.WebPage), closeableTabItem);
        if (flag)
        {
            closeableTabItem.Tag = new SpotEx
            {
                Title = sTitle,
                MessageId = SpotHelper.MakeMsg(msg, tag: false),
                Modulus = modulus,
                IsPreview = isPreview
            };
        }
        else
        {
            closeableTabItem.Tag = new UrlInfo
            {
                Title = sTitle,
                Url = msg,
                TabLoaded = false
            };
        }

        TabControl1.Items.Add(closeableTabItem);
        SaveTabs();
        return closeableTabItem;
    }

    private void PrepareWindow()
    {
        SpotsListVm.UpdateSpotsListType((SpotsListTypeEnum)Settings.Default.SpotsListType, force: true, delaySpotsLoad: true);
        ShowDownloads(Settings.Default.DownloadAction < 2);
        if (Settings.Default.SaveTabs)
        {
            _tabDb.LoadTabs();
            ReopenTabs();
        }

        WebRequest.DefaultWebProxy = null;
        StreamResourceInfo resourceStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Spotnet;component/Resources/ImagesInternal/smallspotnet.ico"));
        if (resourceStream != null)
        {
            _trayNotify.Icon = new Icon(resourceStream.Stream);
        }

        _trayNotify.Text = base.Title;
        Dock.ColumnDefinitions[0].Width = new GridLength(Settings.Default.LeftPanelWidth);
        OnWindowPrepared?.Invoke();
    }

    private void ReopenTabs()
    {
        foreach (string tab in _tabDb.TabList)
        {
            if (tab.Contains("\t"))
            {
                string text = Strings.Split(tab, "\t")[0];
                PrepareTab(tab.Substring(text.Length + 1), text, autoSelect: false);
            }
        }
    }

    private void DownloaderProgressChanged(int lVal)
    {
        StatusBarVm.SetTaskBarProgressStatus(null, lVal);
    }

    private bool SelectProvider()
    {
        try
        {
            SelectProviderWindow selectProviderWindow = new SelectProviderWindow
            {
                Owner = this
            };
            selectProviderWindow.ShowDialog();
            return !AppHelper.ServersDb.ODown.Server.Trim().IsNullOrEmpty() && selectProviderWindow.BSuc;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }

        return false;
    }

    internal void SelectTab(TabItem tabItem)
    {
        TabControl1.SelectedItem = tabItem;
    }

    internal void RefreshSpotsList(bool force = false)
    {
        var container = SpotsListVm.SpotsContainer;
        // Startup and view changes can run before the background provider publishes
        // an ItemsSource. A refresh is not a reason to close the application.
        if (container?.Spots?.ItemsSource is not VirtualList<ISpotRow> virtualList) return;
        if (container.IsSpotKeyboardFocused && !force)
        {
            return;
        }

        bool flag = Settings.Default.SpotsListType == 3;
        container.Spots.SelectedItem = null;
        if (!flag || virtualList.Count <= 50 || force)
        {
            virtualList.Clear();
            if (flag)
            {
                SpotsListVm.SpotsContainer.UpdateContainer();
            }
        }
    }

    internal Task<bool> ShowDownloads(bool bVisible)
    {
        DispatcherHelper.UIDispatcher.Invoke(delegate
        {
            if (bVisible)
            {
                if (DownloadsTab.Content == null)
                {
                    DownloadsTab.Content = new Spotnet.Controls.ProgressRing
                    {
                        IsActive = true
                    };
                }

                DownloadsTab.Visibility = Visibility.Visible;
                ((FrameworkElement)DownloadsTab.Content).Focus();
            }
            else
            {
                DownloadsTab.Visibility = Visibility.Collapsed;
                (DownloadsTab.Content as DownloadsControl)?.Dispose();
                DownloadsTab.Content = null;
                if (TabControl1.SelectedItem is UnCloseableTabItem { IsDownloadTab: not false })
                {
                    TabControl1.SelectedIndex = 0;
                }
            }
        });
        if (!bVisible)
        {
            return Sys.Downloader.ShutdownProcessAsync();
        }

        return Sys.Downloader.StartProcessAsync();
    }

    private void SpotAdd()
    {
        if (TabControl1.SelectedItem == null || !(NewLateBinding.LateGet(TabControl1.SelectedItem, null, "Content", new object[0], null, null, null) is Toevoegen))
        {
            CloseableTabItem closeableTabItem = new CloseableTabItem();
            UpdateTabItemHeaderAsync(Words.NewSpot, PageTypeEnum.AddNewSpot, closeableTabItem);
            Toevoegen content = new Toevoegen
            {
                HeaderSettings = AppHelper.HeaderSettings(bIncludePosition: false)
            };
            closeableTabItem.Content = content;
            TabControl1.Items.Add(closeableTabItem);
        }
    }

    internal void StopWait()
    {
        FirstTabHeaderUpdate();
        EndWait();
    }

    internal void FirstTabHeaderUpdate()
    {
        PageTypeEnum sIcon = PageTypeEnum.SpotsFilter;
        if (SpotProvider.RowFilter.ToLower().Equals("cat < 9") || SpotProvider.RowFilter.IsNullOrEmpty())
        {
            sIcon = PageTypeEnum.SpotsNoFilter;
        }

        if (SpotProvider.QueryName.StartsWith(Words.Search + ": "))
        {
            sIcon = PageTypeEnum.SpotsSearch;
        }

        UpdateTabItemHeaderAsync(SpotProvider.QueryName, sIcon);
    }

    internal string TabSearchText()
    {
        if (TabControl1 == null || TabControl1.Items.Count == 0)
        {
            return "";
        }

        TabItem tabItem = (TabItem)TabControl1.Items[0];
        if (AppHelper.GetHeader(RuntimeHelpers.GetObjectValue(tabItem.Header)).StartsWith(Words.Search + ": "))
        {
            return AppHelper.GetHeader(RuntimeHelpers.GetObjectValue(tabItem.Header)).Substring((Words.Search + ": ").Length);
        }

        return string.Empty;
    }

    private void UpdateNewCats(int[] nc = null, bool restoreTheLastResult = false)
    {
        if (Sys.MainWindow.SpotProvider.RowNew <= 1)
        {
            return;
        }

        Task.Run(delegate
        {
            _shouldUpdateNewCatsBeRepeated = true;
            if (!Monitor.TryEnter(_lockUpdateNewCats))
            {
                return;
            }

            try
            {
                while (_shouldUpdateNewCatsBeRepeated)
                {
                    _shouldUpdateNewCatsBeRepeated = false;
                    Dictionary<string, int> dictionary = new Dictionary<string, int>();
                    if (nc == null)
                    {
                        nc = new int[11];
                    }

                    if (restoreTheLastResult)
                    {
                        nc = _lastNewCatsResult;
                    }
                    else
                    {
                        _lastNewCatsResult = nc;
                    }

                    foreach (FilterViewModel completeFilters in MainWindowVm.GetCompleteFiltersList())
                    {
                        if (!completeFilters.Query.IsNullOrWhiteSpace())
                        {
                            string text = Filters.SimplifyQuery(completeFilters.Query).Replace(" ", "").Replace("(", "").Replace(")", "");
                            Match match = Regex.Match(text, "^cat=([1-6,9])$", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                int num = Convert.ToInt32(match.Groups[1].ToString());
                                completeFilters.NewCount = nc[num];
                            }
                            else if (text.Equals("cat!=9") || text.Equals("cat!=0") || text.Equals("rowid>[SN:NEW]ANDcat!=9") || text.Equals("rowid>[SN:NEW]") || text.Equals("date>[SN:DATE]-86400ANDcat!=9") || text.Equals("date>[SN:DATE]-86400"))
                            {
                                if (dictionary.ContainsKey(text))
                                {
                                    completeFilters.NewCount = dictionary[text];
                                }
                                else
                                {
                                    completeFilters.NewCount = SpotProvider.GetNewCounts(completeFilters.Query);
                                    dictionary[text] = completeFilters.NewCount;
                                    Thread.Sleep(20);
                                }
                            }
                        }
                    }

                    if (_shouldUpdateNewCatsBeRepeated)
                    {
                        Thread.Sleep(200);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lockUpdateNewCats);
            }
        });
    }

    private void WaitedConnection(IAsyncResult ar)
    {
        try
        {
            _pipe.EndWaitForConnection(ar);
            using (BinaryReader binaryReader = new BinaryReader(_pipe))
            {
                bool isNzbDownload = false;
                try
                {
                    while (true)
                    {
                        string f = binaryReader.ReadString();
                        BringToTheTopForce();
                        RunPipeParameterProcessingAsync(f, out isNzbDownload);
                    }
                }
                catch (IOException)
                {
                }

                if (isNzbDownload)
                {
                    DispatcherHelper.RunAsync(delegate
                    {
                        TabControl1.SelectedIndex = 1;
                    });
                }
            }

            _pipe = new NamedPipeServerStream("Pipe\\Spotnet", PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            _pipe.BeginWaitForConnection(WaitedConnection, null);
        }
        catch (Exception ex2)
        {
            Log.Exception(ex2);
        }
    }

    private void RunPipeParameterProcessingAsync(string f, out bool isNzbDownload)
    {
        isNzbDownload = !f.ToLower().StartsWith("spotnet://");
        if (f.Equals("--exitOnUninstall"))
        {
            Log.Debug("Exit on uninstall signal received. Shutdown...");
            Sys.Shutdown();
            return;
        }

        Task.Run(delegate
        {
            if (f.ToLower().StartsWith("spotnet://"))
            {
                ProcessSpotnetProtocol(f, saveParrentTab: false);
            }
            else
            {
                Log.Debug("New downloads from file association: " + f);
                ScheduleNzbDownload(f, DownloaderItemFactory.New(System.IO.Path.GetFileNameWithoutExtension(f)));
            }
        });
    }

    internal void ProcessSpotnetProtocol(string link, bool saveParrentTab)
    {
        if (link.Length <= 200)
        {
            string input = link.Substring("spotnet://".Length);
            input = Regex.Replace(input, "[^\\u0020-\\u007E]", string.Empty);
            input = Regex.Replace(input, "[\\\\/<>\"]", string.Empty);
            SpotEx spotEx = new SpotEx
            {
                MessageId = input,
                Title = input
            };
            DispatcherHelper.RunAsync(delegate
            {
                OpenSpot(SpotRowViewModel.InitializeNewSpotRow(spotEx), null, saveParrentTab);
            });
        }
    }

    internal void ExecuteUpdateDb_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!StatusBarVm.DbUpdateImageEnabled)
        {
            return;
        }

        StatusBarVm.DbUpdateImageEnabled = false;
        if (StatusBarVm.DbUpdateImageStarted)
        {
            DispatcherHelper.UIDispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
            {
                StatusBarVm.SetDbUpdateProgressStatus(Words.DbUpdatePausing + "...", -1);
                _dbUpdatePauseStartTime = DateTime.Now;
                DbUpdater.DbUpdateTimerStop();
                DbUpdater.Stop();
                AppHelper.ClearHeaderPhuse();
                if (Settings.Default.SpotsListType == 3)
                {
                    SpotsListVm.SpotsContainer.UpdateContainer();
                }
            });
        }
        else
        {
            ResetNewSpotsCountAndStartDbUpdateTimer(null, null);
        }
    }

    internal void HeaderMenu_PreviewMouseDown(object sender, RoutedEventArgs e)
    {
        if (!(SpotsListVm.SpotsContainer.Spots is System.Windows.Controls.DataGrid dataGrid))
        {
            return;
        }

        e.Handled = true;
        NewLateBinding.LateSetComplex(e.Source, null, "isChecked", new object[1] { Operators.NotObject(NewLateBinding.LateGet(e.Source, null, "isChecked", new object[0], null, null, null)) }, null, null, OptimisticSet: false, RValueBase: true);
        string right = NewLateBinding.LateGet(e.Source, null, "Header", new object[0], null, null, null).ToStringSafely().ToLower();
        foreach (DataGridColumn column in dataGrid.Columns)
        {
            if (!Operators.ConditionalCompareObjectEqual(NewLateBinding.LateGet(column.Header, null, "ToLower", new object[0], null, null, null), right, TextCompare: false))
            {
                continue;
            }

            if (column.Visibility == Visibility.Visible)
            {
                foreach (DataGridColumn column2 in dataGrid.Columns)
                {
                    if (column2.Visibility == Visibility.Visible && !column2.Header.ToStringSafely().Equals(column.Header.ToStringSafely()))
                    {
                        column.Visibility = Visibility.Hidden;
                    }
                }
            }
            else
            {
                column.Visibility = Visibility.Visible;
            }

            SpotsListVm.SpotsContainer.SaveCols();
        }
    }

    private void HeaderMenu_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SpotsListVm.SpotsContainer.SaveCols();
    }

    private void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        Sys.IsShutdownRequested = true;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        OnLoad();
    }

    private void MainWindow_StateChanged(object sender, EventArgs e)
    {
        if (Settings.Default.SystemTray && base.WindowState == WindowState.Minimized)
        {
            base.Title = "Spotnet :: Tray";
            _trayNotify.Text = "Spotnet :: Tray";
            _trayNotify.Visible = true;
            base.ShowInTaskbar = false;
            if (!_stateChangedDidOnce)
            {
                _stateChangedDidOnce = true;
            }

            return;
        }

        if (!base.ShowInTaskbar)
        {
            base.ShowInTaskbar = true;
        }

        if (_trayNotify.Visible)
        {
            _trayNotify.Visible = false;
        }

        _lastTab = -1;
        TabControl1_SelectionChanged(null, null);
    }

    private void ExecuteDownloadFolderChange(object sender, RoutedEventArgs e)
    {
        ExecuteDownloadFolderChange();
    }

    private void ExecuteDownloadFolderChange(bool updateDownloadFolder = true)
    {
        if (Sys.Downloader.IsAnyActiveDownloads())
        {
            Interaction.MsgBox(Words.CannotChangeDirActiveDownloads, MsgBoxStyle.Information, Words.Error);
            return;
        }

        FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
        {
            Description = Words.SelectDownloadsFolder,
            ShowNewFolderButton = true,
            SelectedPath = DownloaderProps.MainDir
        };
        if (folderBrowserDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            string selectedPath = folderBrowserDialog.SelectedPath;
            if (selectedPath.Length > 200)
            {
                throw new PathTooLongException();
            }

            if (!DownloaderProps.MainDir.EqualsIgnoreCase(selectedPath))
            {
                Settings.Default.DownloadFolder = selectedPath;
                if (updateDownloadFolder)
                {
                    AppHelper.EnsureDirectoryExist(selectedPath);
                    Settings.Default.Save();
                    Sys.Downloader.RestartProcessAsync();
                }
            }
        }
        catch (PathTooLongException)
        {
            AppHelper.Error("Path is too long. Please use another one.");
            ExecuteDownloadFolderChange(null, null);
        }
    }

    private void ExecuteOpenNzb(object sender, RoutedEventArgs e)
    {
        try
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Words.NZBFiles,
                AddExtension = true,
                CheckFileExists = true,
                CheckPathExists = true,
                InitialDirectory = ((!Settings.Default.LastFolder.Trim().IsNullOrEmpty()) ? Settings.Default.LastFolder : AppHelper.DesktopDirectory),
                RestoreDirectory = true,
                Title = Words.NZBOpen
            };
            if (openFileDialog.ShowDialog() != true || openFileDialog.FileName.IsNullOrEmpty())
            {
                return;
            }

            Settings.Default.LastFolder = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
            Settings.Default.Save();
            DispatcherHelper.CheckBeginInvokeOnUI(delegate
            {
                if (Settings.Default.DownloadAction <= 1)
                {
                    TabControl1.SelectedIndex = 1;
                }
            });
            SpotHelper.DoDownload(openFileDialog.FileName, DownloaderItemFactory.New(System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName)));
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void ExecuteOpenSpotlink(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenSpotlinkWindow openSpotlinkWindow = new OpenSpotlinkWindow
            {
                Owner = Sys.MainWindow
            };
            openSpotlinkWindow.ShowDialog();
            OpenSpotlink(openSpotlinkWindow.Link);
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void OpenSpotlink(string link)
    {
        if (link.IsNullOrEmpty())
        {
            return;
        }

        Match match = new Regex("(spotnet://)?([A-Za-z0-9]+@[\\.-A-Za-z0-9]+)").Match(link);
        if (!match.Success)
        {
            return;
        }

        string value = match.Groups[2].Value;
        if (value.Length <= 200)
        {
            SpotEx spotEx = new SpotEx
            {
                MessageId = value,
                Title = value
            };
            DispatcherHelper.RunAsync(delegate
            {
                OpenSpot(SpotRowViewModel.InitializeNewSpotRow(spotEx));
            });
        }
    }

    private void ExecuteSelectProvider(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StatusBarUpdateIcon.IsEnabled)
            {
                ((ITaskSchedulerExtentions)SpotRowViewModel.GetTaskSchedulerForLoadFromNet()).CancelAllTasks();
                if (!SelectProvider())
                {
                    SpotsListVm.SpotsContainer.UpdateContainer();
                    return;
                }

                if (!SpotProvider.OpenDb())
                {
                    AppHelper.Error("Cannot open database");
                    Close();
                    return;
                }

                SpotsListVm.SpotsContainer.UpdateContainer();
                Sys.LeftPanel.NoFilter(bForce: true);
                UpdateNewCats(new int[11]);
                UpdateLayout();
                ResetNewSpotsCount();
                Headers.ResetHeaders();
                Comments.ResetComments();
                SpamReports.ResetReports();
                Sys.LeftPanel.ReloadFilter(bResetCount: true);
                ScheduleDbUpdate();
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            Close();
        }
    }

    internal void ClearSavedTabs()
    {
        _tabDb.ClearTabs();
    }

    private void TabControl1_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabControl1.SelectedIndex == _lastTab)
        {
            return;
        }

        _lastTab = TabControl1.SelectedIndex;
        this.TabSelectionChanged?.Invoke();
        InitStatusBar();
        if (TabControl1.SelectedItem is UnCloseableTabItem unCloseableTabItem)
        {
            if (TabControl1.SelectedIndex == 0)
            {
                if (SpotsListVm.SpotsContainer != null)
                {
                    SpotsListVm.SpotsContainer.Spots.Focus();
                    SpotsListVm.SpotsContainer.RestoreFocus();
                }
            }
            else if (unCloseableTabItem.IsDownloadTab)
            {
                ShowDownloads(bVisible: true);
            }
            else if (unCloseableTabItem.Tag is UrlInfo urlInfo)
            {
                UrlInfo urlInfo2 = urlInfo;
                if (!urlInfo2.TabLoaded)
                {
                    OpenPage(PageTypeEnum.WebPage, urlInfo2.Url, saveParrentTab: false, unCloseableTabItem).Forget();
                    urlInfo2.TabLoaded = true;
                }
            }

            if (!base.Title.Equals("Spotnet"))
            {
                UpdateTitle("Spotnet");
                _trayNotify.Text = base.Title;
            }
        }
        else
        {
            if (!(TabControl1.SelectedItem is CloseableTabItem closeableTabItem))
            {
                return;
            }

            UpdateTitle(AppHelper.GetHeader(RuntimeHelpers.GetObjectValue(closeableTabItem.Header)) + " - Spotnet");
            _trayNotify.Text = Strings.Left(base.Title, 60);
            if (closeableTabItem.Tag is UrlInfo urlInfo3)
            {
                UrlInfo urlInfo4 = urlInfo3;
                if (!urlInfo4.TabLoaded)
                {
                    OpenPage(PageTypeEnum.WebPage, urlInfo4.Url, saveParrentTab: false, closeableTabItem).Forget();
                    urlInfo4.TabLoaded = true;
                }
            }

            if (closeableTabItem.Tag is SpotEx spot && closeableTabItem.Content == null)
            {
                OpenSpot(SpotRowViewModel.InitializeNewSpotRow(spot), closeableTabItem);
            }
            else if (closeableTabItem.Content is ICloseableView closeableView)
            {
                closeableView.FocusDocument();
            }
        }
    }

    private void ExecuteAddNewSpot(object sender, RoutedEventArgs e)
    {
        SpotAdd();
    }

    private void ExecuteFillAddToExtWhitelist(object sender, RoutedEventArgs e)
    {
        SpotRowViewModel selectedSpot = SpotsListVm.SpotsContainer.SelectedSpot;
        if (selectedSpot != null)
        {
            string afzender = selectedSpot.Afzender;
            string modulus = selectedSpot.Modulus;
            ExecuteFillAddToList("switchToWhite", "rwl_username", afzender, "rwl_modulus", modulus, "whitelistAddBtn");
        }
    }

    private void ExecuteFillAddToExtBlacklist(object sender, RoutedEventArgs e)
    {
        SpotRowViewModel selectedSpot = SpotsListVm.SpotsContainer.SelectedSpot;
        if (selectedSpot != null)
        {
            string afzender = selectedSpot.Afzender;
            string modulus = selectedSpot.Modulus;
            ExecuteFillAddToList("switchToBlack", "rbl_username", afzender, "rbl_modulus", modulus, "blacklistAddBtn");
        }
    }

    private void ExecuteFillAddSpotToExtWhitelist(object sender, RoutedEventArgs e)
    {
        SpotRowViewModel selectedSpot = SpotsListVm.SpotsContainer.SelectedSpot;
        if (selectedSpot != null)
        {
            string titel = selectedSpot.Titel;
            string spotMessageId = selectedSpot.SpotMessageId;
            ExecuteFillAddToList("switchToSpotWhite", "rswl_title", titel, "rswl_messageid", spotMessageId, "spotWhitelistAddBtn");
        }
    }

    private void ExecuteFillAddSpotToExtBlacklist(object sender, RoutedEventArgs e)
    {
        SpotRowViewModel selectedSpot = SpotsListVm.SpotsContainer.SelectedSpot;
        if (selectedSpot != null)
        {
            string titel = selectedSpot.Titel;
            string spotMessageId = selectedSpot.SpotMessageId;
            ExecuteFillAddToList("switchToSpotBlack", "rsbl_title", titel, "rsbl_messageid", spotMessageId, "spotBlacklistAddBtn");
        }
    }

    private async void ExecuteFillAddToList(string switchClassName, string fieldId1, string fieldValue1, string fieldId2, string fieldValue2, string addButtonId)
    {
        string path = System.IO.Path.Combine(System.IO.Directory.GetParent(AppHelper.AppPath()).FullName, "lists.url");
        if (!System.IO.File.Exists(path))
        {
            return;
        }

        string text = System.IO.File.ReadAllText(path);
        if (text.IsNullOrWhiteSpace() || TabControl1.SelectedIndex != 0)
        {
            return;
        }

        IPage op = await OpenPage(PageTypeEnum.WebPage, text.Trim());
        if (op == null)
        {
            return;
        }

        while (!op.IsDomReady)
        {
            await Task.Delay(500);
        }

        if (op is WebView2Page webPage)
        {
            string script = string.Format(
                "(() => {{ const switcher = document.getElementsByClassName({0})[0]; if (switcher) switcher.click(); " +
                "const first = document.getElementById({1}); if (first) first.value = {2}; " +
                "const second = document.getElementById({3}); if (second) second.value = {4}; " +
                "const add = document.getElementById({5}); if (add) add.click(); }})()",
                JsonConvert.SerializeObject(switchClassName),
                JsonConvert.SerializeObject(fieldId1),
                JsonConvert.SerializeObject(fieldValue1),
                JsonConvert.SerializeObject(fieldId2),
                JsonConvert.SerializeObject(fieldValue2),
                JsonConvert.SerializeObject(addButtonId));
            await webPage.ExecuteJavascriptWithResultAsync(script);
        }
    }

    private void TrayNotify_Click(object sender, EventArgs e)
    {
        if (((System.Windows.Forms.MouseEventArgs)e).Button != MouseButtons.Left)
        {
            base.WindowState = WindowState.Normal;
        }
    }

    private void TrayNotify_DoubleClick(object sender, EventArgs e)
    {
        base.WindowState = WindowState.Normal;
    }

    private void InitStatusBar()
    {
        if (!IsDownloadsTabSelectedAndVisible)
        {
            TextBlock textBlock = new TextBlock();
            System.Windows.Data.Binding binding = new System.Windows.Data.Binding();
            binding.Path = new PropertyPath("SpotsListStatusMessage");
            binding.Source = StatusBarVm;
            System.Windows.Data.Binding binding2 = binding;
            BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, binding2);
            StatusBarFirstContainer.Content = textBlock;
        }
        else
        {
            StatusBarFirstContainer.Content = _downloadsStatusBar;
        }
    }

    protected override void OnInitialized(EventArgs e)
    {
        if (Sys.IsShutdownRequested)
        {
            return;
        }

        base.OnInitialized(e);
        InitStatusBar();
        SettingsForSpotsList.ColoringForSpotsChanged += delegate
        {
            MainWindow.ColoringForSpotsChanged?.Invoke();
        };
        SettingsForSpotsList.ColoringForFiltersChanged += delegate
        {
            MainWindow.ColoringForFiltersChanged?.Invoke();
        };
        _mainToolBar = new MainToolBarControl();
        UpdateMainMenuVisibility();
        base.Visibility = Visibility.Hidden;
        Task.Run(delegate
        {
            try
            {
                // Step 2: Loading servers (30s timeout guards against infinite hang)
                Views.SplashWindow.SetProgress(2);
                var serversTask = Task.Run(() => AppHelper.ServersDb.LoadServers());
                bool serversTimedOut = !serversTask.Wait(TimeSpan.FromSeconds(30));
                bool serversLoaded = !serversTimedOut && serversTask.Result;

                if (serversTimedOut || !serversLoaded)
                {
                    Log.Error(serversTimedOut ? "LoadServers timed out (30s)." : "LoadServers returned false.");
                    AppHelper.Error(Words.CannotLoad + " " + System.IO.Path.Combine(AppHelper.SettingsFolder, "servers.xml"));
                    Sys.Shutdown();
                    return;
                }

                // Step 3: Loading filters
                Views.SplashWindow.SetProgress(3);
                if (!MainWindowVm.FiltersDb.LoadFilters())
                {
                    AppHelper.Error(Words.CannotLoad + " filters");
                    Sys.Shutdown();
                    return;
                }

                if (AppHelper.ServersDb.ODown.Server.IsNullOrEmpty())
                {
                    Log.Info("Provider is not selected");
                    System.Windows.Application.Current.Dispatcher.Invoke(delegate
                    {
                        base.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    _waitForProviderSelectedEvent.Set();
                }

                _pipe = new NamedPipeServerStream("Pipe\\Spotnet", PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                _pipe.BeginWaitForConnection(WaitedConnection, null);
                _waitForProviderSelectedEvent.Wait();
                Log.Debug("Provider selected. Server {0}:{1}", AppHelper.ServersDb.ODown.Server, AppHelper.ServersDb.ODown.Port);
                if (!Sys.IsShutdownRequested)
                {
                    InitializeDatabase();
                    UserKeyHelper.GetModulus();
                }
            }
            catch (Exception ex2)
            {
                Log.Exception(ex2, showToClient: true);
                Sys.Shutdown();
            }
        }).ContinueWith(delegate
        {
            try
            {
                if (!Sys.IsShutdownRequested)
                {
                    SpotsListVm.IsSpotsListLoading = true;
                    PrepareWindow();
                    EndWait();
                    base.Visibility = Visibility.Visible;
                    BringToTheTopForce();
                    _mainToolBar.EnableUpdate();
                    StatusBarVm.DbUpdateImageStarted = false;
                    StatusBarVm.DbUpdateImageEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, showToClient: true);
                Sys.Shutdown();
            }
        }, _taskSchedulerCurrentContext).ContinueWith(delegate
        {
            if (!Sys.IsShutdownRequested)
            {
                ProcessExternalArgs();
                _waitForMainWindowLoaded.Wait();
                RunAfterStartActions();
            }
        }, CancellationToken.None, TaskContinuationOptions.LongRunning, TaskScheduler.Default);
    }

    private void InitializeDatabase()
    {
        if (Settings.Default.SpotsDbFileMalformed || Settings.Default.CommentsDbFileMalformed || Settings.Default.RecreateDbScheduled)
        {
            var action = DbRecoveryWindow.Prompt(this, "Spotnet detected a previous malformed database flag.");
            if (action == DbRecoveryAction.Closed)
            {
                Sys.Shutdown();
                return;
            }
        }

        // Step 4: Connecting to database
        Views.SplashWindow.SetProgress(4);
        // A schema migration says what it is doing on the splash, because it is the one
        // startup step that can run long enough to look like a hang.
        DAL.SpotProvider.OnSchemaUpgradeMessage = Views.SplashWindow.SetMessage;

        bool opened = false;
        while (!opened)
        {
            Task<bool> openTask = Task.Run(() => SpotProvider.OpenDb());
            // Twenty seconds is the limit for a database that will not open. It is not the
            // limit for one that is being rebuilt: a search index migration on a large
            // database legitimately takes minutes, and timing it out here would offer the
            // recovery window over healthy data and start a second, competing open.
            while (!openTask.Wait(TimeSpan.FromSeconds(20.0)) && DAL.SpotProvider.SchemaUpgradeInProgress)
            {
                Log.Info("Waiting for the database schema upgrade to finish.");
            }

            if (openTask.IsCompleted)
            {
                opened = openTask.Result;
            }
            else
            {
                Log.Warn("Database startup timed out after 20 seconds.");
            }

            if (!opened)
            {
                string reason = SpotProvider.Corrupted
                    ? "Spots database reported a corruption or locking issue."
                    : "Database startup took longer than 20 seconds or failed to connect.";

                Log.Error("Database open issue: {0}", reason);
                var action = DbRecoveryWindow.Prompt(this, reason);
                if (action == DbRecoveryAction.Closed)
                {
                    Sys.Shutdown();
                    return;
                }
            }
        }

        // Step 5: Verifying database
        Views.SplashWindow.SetProgress(5);
        SpotSaver.InitializeCommentsDb();
        SpotProvider.QueryName = "cat < 9";
        SpotProvider.RowFilter = "cat < 9";
    }

    private void ProcessExternalArgs()
    {
        bool isNzbDownload = false;
        if (App.Args != null && App.Args.Count > 0)
        {
            foreach (string arg in App.Args)
            {
                RunPipeParameterProcessingAsync(arg, out isNzbDownload);
            }
        }

        if (isNzbDownload)
        {
            base.Dispatcher.Invoke(delegate
            {
                TabControl1.SelectedIndex = 1;
            });
        }
    }

    private void BringToTheTopForce()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            if (base.WindowState != WindowState.Maximized)
            {
                base.WindowState = WindowState.Normal;
            }

            Activate();
            base.Topmost = true;
            base.Topmost = false;
            Focus();
        });
    }

    private void RunAfterStartActions()
    {
        try
        {
            if (SquirrelStuff.IsNewVersion)
            {
                OpenPage(PageTypeEnum.ReleaseNotes).Wait();
            }

            PromotionHelper.OpenTabsAsync();
            Favorites.MigrateFromFileToDatabase();
            MainWindowVm.CheckShowTrustedOnlyModeShouldBeTemporaryDisabled();
            SpotsListVm.SpotsContainer.LoadContentForTheFirstTime();
            SystemStateChecker.NntpServerCheck(tryToSwitchToOtherPorts: true);
            SystemStateChecker.Start();
            ResetNewSpotsCount();
            if (Settings.Default.DbAutoUpdateIntervalMin > 0 && Settings.Default.DbAutoUpdateEnabled)
            {
                DbUpdater.DbUpdateTimerStart();
                ScheduleDbUpdate();
            }

            SquirrelStuff.StartNewVersionCheckTimer();
            StartAutoUpdates();
            Sys.StatsReporter.ReportOnStartAsync();
            CheckFreeSpaceOnTheDisk();
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    /// <summary>
    /// Starts watching the project's repository for a released build. Installed copies
    /// only: a development build has no setup that could replace it.
    /// </summary>
    private void StartAutoUpdates()
    {
        if (!AppUpdater.IsSupported) return;
        AppUpdater.CleanupOldDownloads();
        AppUpdater.UpdateOffered += OnUpdateOffered;
        AppUpdater.StartPeriodicCheck();
        // What the splash-screen check found, now that there is a window to own the dialog.
        if (AppUpdater.TryTakePendingOffer(out UpdateManifest pending, out UpdateDecision decision))
        {
            base.Dispatcher.BeginInvoke(new Action(() => ShowUpdateWindow(pending, decision)));
        }
    }

    private void OnUpdateOffered(UpdateManifest manifest, UpdateDecision decision)
    {
        // The check runs on the pool; the window has to be built on the dispatcher.
        base.Dispatcher.BeginInvoke(new Action(() => ShowUpdateWindow(manifest, decision)));
    }

    /// <summary>
    /// Shows the update prompt. One at a time, never during shutdown, and never on top of
    /// itself when a later check finds the same release again.
    /// </summary>
    internal void ShowUpdateWindow(UpdateManifest manifest, UpdateDecision decision)
    {
        if (manifest == null || Sys.IsShutdownRequested || _updatePromptOpen) return;
        try
        {
            _updatePromptOpen = true;
            new UpdateWindow(manifest, decision) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
        finally
        {
            _updatePromptOpen = false;
        }
    }

    /// <summary>Help &gt; Check for updates. Says something either way, unlike the timer.</summary>
    internal async Task CheckForUpdatesInteractivelyAsync()
    {
        if (!AppUpdater.IsSupported)
        {
            AppHelper.ShowPopupMessage(Words.UpdateUpToDate);
            return;
        }
        try
        {
            (UpdateManifest manifest, UpdateDecision decision, string error) =
                await AppUpdater.CheckAsync(CancellationToken.None).ConfigureAwait(true);
            if (error != null)
            {
                AppHelper.ShowPopupMessage(string.Format(Words.UpdateCheckFailed, error));
                return;
            }
            Log.Info("Manual update check: {0}", decision.Reason);
            if (decision.ShouldPrompt)
            {
                ShowUpdateWindow(manifest, decision);
                return;
            }
            AppHelper.ShowPopupMessage(Words.UpdateUpToDate);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            AppHelper.ShowPopupMessage(string.Format(Words.UpdateCheckFailed, ex.Message));
        }
    }

    private void CheckFreeSpaceOnTheDisk()
    {
        try
        {
            if (AppHelper.GetDiskSpace(System.IO.Path.GetDirectoryName(AppHelper.AppPath())) < 104857600)
            {
                string text = "Please check the disk space for " + System.IO.Path.GetPathRoot(AppHelper.AppPath()) + ". It's less than 100MB that can lead to problems with Spotnet.";
                Log.Warn(text);
                AppHelper.Error(text);
            }
        }
        catch (Exception)
        {
        }
    }

    internal void ResetNewSpotsCount()
    {
        _beforeUpdateLastRow = Settings.Default.DatabaseMax;
        _newSpotsCount = new SaveSpotsRow();
    }

    private void ResetNewSpotsCountAndStartDbUpdateTimer(object sender, RoutedEventArgs e)
    {
        ResetNewSpotsCount();
        ScheduleDbUpdate();
        DbUpdater.DbUpdateTimerStart();
    }

    public async void ActionsAfterChangeRetention()
    {
        await Task.Run(async delegate
        {
            ResetNewSpotsCount();
            Headers.ResetHeaders();
            Comments.ResetComments();
            SpamReports.ResetReports();
            DbUpdater.Stop();
            while (DbUpdater.IsDbUpdateInProgress)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200.0));
            }

            Sys.MainWindow.ScheduleDbUpdate();
        });
    }

    internal async void ScheduleDbUpdate()
    {
        if (DbUpdater.IsDbUpdateInProgress || SelectProviderWindow.IsRunning)
        {
            return;
        }

        await Task.Run(delegate
        {
            try
            {
                NewznabHelper.Cache.Clear();
                _dbUpdateStartTime = DateTime.Now;
                SetDbUpdateStartedStatus();
                StatusBarVm.SetDbUpdateProgressStatus(Words.LookingFor + " " + Words.newWord + " " + Words.Spots + "...", -1);
                NntpSettings headerSettings = AppHelper.HeaderSettings(bIncludePosition: true);
                Task task = null;
                try
                {
                    task = DbUpdater.StartTaskAsync(headerSettings, CommentSettings(bIncludeLast: true), SpamReportSettings(bIncludeLast: true), StatusBarVm.SetDbUpdateProgressStatus, OnSpotsUpdate, SetDbUpToDateStatus);
                    task.Wait();
                }
                finally
                {
                    OnDbUpdateEnd(task);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        });
    }

    internal async Task ScheduleCommentsDbUpdate()
    {
        if (DbUpdater.IsDbUpdateInProgress || SelectProviderWindow.IsRunning)
        {
            return;
        }

        await Task.Run(delegate
        {
            try
            {
                _dbUpdateStartTime = DateTime.Now;
                SetDbUpdateStartedStatus();
                StatusBarVm.SetDbUpdateProgressStatus(Words.CommentsUpdating, -1);
                SetDbUpdateStartedStatus();
                SetDbUpToDateStatus(spotsAreNotUpToDate: false, commentsAreNotUpToDate: true);
                DbUpdater.LastHeaderResults = null;
                Task task = null;
                try
                {
                    task = DbUpdater.UpdateComments(Sys.MainWindow.CommentSettings(bIncludeLast: true), StatusBarVm.SetDbUpdateProgressStatus, Sys.MainWindow.SetDbUpToDateStatus);
                    task.Wait();
                }
                finally
                {
                    OnDbUpdateEnd(task);
                }
            }
            catch (Exception ex)
            {
                if (!DbUpdater.IsCancellationRequested)
                {
                    Log.Exception(ex);
                }
            }
        });
    }

    internal void SetDbUpdateStartedStatus()
    {
        StatusBarVm.DbUpdateImageStarted = true;
        StatusBarVm.DbUpdateImageEnabled = true;
        this.DispatchAsync(delegate
        {
            _mainToolBar.DisableUpdate();
        });
    }

    internal void SetDbUpToDateStatus(bool spotsAreNotUpToDate, bool commentsAreNotUpToDate)
    {
        if (spotsAreNotUpToDate || commentsAreNotUpToDate)
        {
            StatusBarVm.SetDbUpdateProgressStatus(Words.DatabaseUpdating);
        }

        SpotsListVm.IsSpotsDbUpToDate = !spotsAreNotUpToDate;
        SpotsListVm.IsCommentsDbUpToDate = !commentsAreNotUpToDate;
    }

    private void OnSpotsUpdate(SaveSpotsRow savedSpots)
    {
        try
        {
            SpotProvider.ResetCache();
            SpotProvider.RowNew = _beforeUpdateLastRow;
            UpdateNewCats((_newSpotsCount + savedSpots).NewCats);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    internal NntpSettings CommentSettings(bool bIncludeLast)
    {
        NntpSettings nntpSettings = new NntpSettings
        {
            BlackList = BlackAndWhite.BlackList(),
            WhiteList = BlackAndWhite.WhiteList(),
            TrustedKeys = AppHelper.LoadKeys(),
            GroupName = Settings.Default.ReplyGroup,
            CheckSignatures = Settings.Default.CheckSignatures
        };
        if (bIncludeLast)
        {
            if (System.IO.File.Exists(AppHelper.GetDbFilename("dbc")))
            {
                using (ISqlDb db = SqlDbFactory.CreateSqlDbComments(isReadOnly: true))
                {
                    nntpSettings.Position = AppHelper.GetIdPosition(db, "comments");
                }

                if (nntpSettings.Position.First > 0)
                {
                    Comment comment = new Comment
                    {
                        Article = nntpSettings.Position.First
                    };
                    if (comment.GetCommentDateFromTheNet(AppHelper.HeaderPhuse, nntpSettings, out var _))
                    {
                        nntpSettings.Position.FirstDateTime = comment.Created;
                    }
                }
            }
            else
            {
                nntpSettings.Position = new IdPosition();
            }
        }

        return nntpSettings;
    }

    internal NntpSettings SpamReportSettings(bool bIncludeLast)
    {
        NntpSettings nntpSettings = new NntpSettings
        {
            BlackList = BlackAndWhite.BlackList(),
            WhiteList = BlackAndWhite.WhiteList(),
            TrustedKeys = AppHelper.LoadKeys(),
            GroupName = Settings.Default.ReportGroup,
            CheckSignatures = false
        };
        if (bIncludeLast)
        {
            if (System.IO.File.Exists(AppHelper.GetDbFilename("dbs")))
            {
                using ISqlDb db = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
                nntpSettings.Position = AppHelper.GetIdPosition(db, "spamreports");
                if (nntpSettings.Position.First > 0)
                {
                    SpamReport spamReport = new SpamReport
                    {
                        ReportId = nntpSettings.Position.First
                    };
                    spamReport.GetReportDateFromDb(db);
                    nntpSettings.Position.FirstDateTime = spamReport.Date;
                }
            }
            else
            {
                nntpSettings.Position = new IdPosition();
            }
        }

        return nntpSettings;
    }

    /// <summary>
    /// Reports a finished download: in the window while the user is in it, on the desktop
    /// while they are not.
    /// </summary>
    /// <remarks>
    /// The balloon used to be raised and the tray icon hidden again in the same statement.
    /// Hiding a notification icon takes its balloon with it, so on Windows 10 and 11 the
    /// notification usually never reached the screen at all. NotificationHelper keeps the
    /// icon alive for the life of the notification and restores its visibility afterwards.
    /// </remarks>
    internal void DisplayTooltip(string sTooltip, bool success = true)
    {
        if (!WindowActivatedHelper.ApplicationIsActivated())
        {
            if (Settings.Default.NotifyAboutDownloadComplete)
            {
                // The notification says which of the two happened. It used to announce
                // every finished item as complete, including the ones that failed to
                // unpack or repair.
                NotificationHelper.Show(
                    success ? Words.NotificationDownloadFinished : Words.NotificationDownloadProblem,
                    sTooltip);
            }
            else
            {
                Log.Info("Download notification suppressed by the NotifyAboutDownloadComplete setting.");
            }
        }
        else
        {
            // Spotnet is the window in front, so this belongs in the window rather than on
            // the desktop.
            Log.Debug("Spotnet is in the foreground; reporting the finished download in-app.");
            AppHelper.ShowPopupMessage(sTooltip, inTheCenter: false, TimeSpan.FromSeconds(3.0));
        }
    }

    internal void DoWait(string sMsg, bool blockUi = false)
    {
        if (blockUi)
        {
            DispatcherHelper.CheckBeginInvokeOnUI(delegate
            {
                Dock.IsHitTestVisible = true;
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                base.Cursor = System.Windows.Input.Cursors.Wait;
            });
        }

        StatusBarVm.SpotsListStatusMessage = sMsg ?? WaitString;
    }

    internal void EndWait()
    {
        DispatcherHelper.UIDispatcher.InvokeAsync(delegate
        {
            Dock.IsHitTestVisible = true;
            Mouse.OverrideCursor = null;
            base.Cursor = null;
        });
        StatusBarVm.SetDefaultSpotsListStatusMessage();
    }

    internal bool ScheduleNzbDownload(string pathToNzb, DownloaderItemViewModel item)
    {
        try
        {
            lock (_lockDownloadScheduling)
            {
                if (!ShowDownloads(bVisible: true).Result)
                {
                    AppHelper.Error(Words.ErrorOnNzbGetStart);
                    return false;
                }

                if (!Sys.Downloader.AddToDownloadQueue(pathToNzb, item))
                {
                    string nZBDownloadError = Words.NZBDownloadError;
                    Log.Warn(nZBDownloadError);
                    AppHelper.Error(nZBDownloadError);
                    return false;
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return false;
        }
    }

    public void SaveTabs(string zExtra = "")
    {
        if (!Settings.Default.SaveTabs)
        {
            return;
        }

        List<string> list = new List<string>();
        foreach (TabItem item in (IEnumerable)TabControl1.Items)
        {
            if (item.Tag is SpotEx { IsPreview: false } spotEx)
            {
                try
                {
                    if (!spotEx.MessageId.IsNullOrEmpty())
                    {
                        list.Add(SpotHelper.MakeMsg(spotEx.MessageId) + "\t" + spotEx.Title.Replace("\t", ""));
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
            }

            if (!(item.Tag is UrlInfo { IsPromo: false } urlInfo))
            {
                continue;
            }

            try
            {
                UrlInfo urlInfo2 = urlInfo;
                if (!urlInfo2.Url.IsNullOrEmpty())
                {
                    list.Add(urlInfo2.Url.Replace("\t", "") + "\t" + urlInfo2.Title.Replace("\t", ""));
                }
            }
            catch (Exception ex2)
            {
                Log.Exception(ex2);
            }
        }

        if (!zExtra.IsNullOrEmpty())
        {
            list.Add(zExtra);
        }

        _tabDb.SaveTabs(list);
    }

    private void WebPageOnTitleOrTypeChanged(object p, bool isPromo = false)
    {
        IPage page = (IPage)p;
        PageTypeEnum pageType = page.PageType;
        DispatcherHelper.RunAsync(delegate
        {
            try
            {
                TabItem tabItem = page.TabItem;
                if (tabItem != null)
                {
                    if (pageType == PageTypeEnum.WebPage && !isPromo)
                    {
                        if (tabItem.Tag is UrlInfo urlInfo)
                        {
                            urlInfo.Title = page.Title;
                            urlInfo.Url = page.Uri.AbsoluteUri;
                        }
                    }
                    else if (pageType == PageTypeEnum.SpotLoaded && page is ISpotPage spotPage)
                    {
                        tabItem.Tag = spotPage.SpotEx;
                    }

                    string sHeader = ((!isPromo) ? page.Title : ((UrlInfo)tabItem.Tag).Title);
                    UpdateTabItemHeader(sHeader, pageType, tabItem);
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex, showToClient: true);
            }
        });
    }

    /// <summary>Switches the spots list to a view mode and puts the user back in it.</summary>
    internal void ShowSpotsListAs(SpotsListTypeEnum type)
    {
        SpotsListVm.UpdateSpotsListType(type);
        TabControl1.SelectedIndex = 0;
        // The thumbnail view has no rows to select.
        if (type != SpotsListTypeEnum.Thumbs
            && SpotsListVm.SpotsContainer.Spots is System.Windows.Controls.DataGrid { SelectedItem: null } dataGrid)
        {
            dataGrid.SelectedIndex = 0;
            if (dataGrid.SelectedItem != null)
            {
                dataGrid.ScrollIntoView(dataGrid.SelectedItem, dataGrid.Columns[0]);
            }
        }
    }

    private void StatusBarSystemStateImage_OnIsMouseDirectlyOverChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue)
        {
            StatusBarSystemStateTooltip.ScheduleHide(TimeSpan.FromSeconds(1.0));
        }
    }

    private void StatusBarSocksProxy_OnIsMouseDirectlyOverChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue)
        {
            StatusBarSocksProxyTooltip.ScheduleHide();
        }
    }

    private void LeftPanelSplitter_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Dock.ColumnDefinitions[0].Width = GridLength.Auto;
    }

    private void LeftPanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double actualWidth = Dock.ColumnDefinitions[0].ActualWidth;
        Dock.ColumnDefinitions[0].Width = new GridLength(actualWidth);
        Settings.Default.LeftPanelWidth = (int)actualWidth;
        Settings.Default.Save();
    }

    public TabItem GetPromoTab(string url)
    {
        foreach (object item in (IEnumerable)TabControl1.Items)
        {
            TabItem tabItem = item as TabItem;
            if (tabItem?.Tag is UrlInfo && ((UrlInfo)tabItem.Tag).Url.Equals(url))
            {
                return tabItem;
            }
        }

        return null;
    }

    public void OpenPromo(PromotionHelper.PromotionTabInfo promoTab)
    {
        if (GetPromoTab(promoTab.Url) == null)
        {
            TabItem tabItem = (promoTab.IsTabClosable ? ((TabItem)new CloseableTabItem
            {
                AutoSelect = false
            }

            ) : ((TabItem)new UnCloseableTabItem
            {
                AutoSelect = false
            }

            ));
            tabItem.Tag = new UrlInfo
            {
                Title = promoTab.Title,
                Url = promoTab.Url,
                TabLoaded = false,
                IsPromo = true
            };
            UpdateTabItemHeader(promoTab.Title, PageTypeEnum.WebPage, tabItem);
            TabControl1.Items.Add(tabItem);
        }
    }

    private void ProxyIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StatusBarSocksProxyTooltip.TooltipIsVisible = true;
        if (e.ClickCount == 2)
        {
            SocksProxy.ChangeState(!Settings.Default.UseSocksProxy);
        }
    }

    private void TestCommandOnClick(object sender, ExecutedRoutedEventArgs e)
    {
    }

    private void SystemStateImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StatusBarSystemStateTooltip.TooltipIsVisible = true;
    }

    static MainWindow()
    {
        MainWindow.ColoringForSpotsChanged = delegate
        {
        };
        MainWindow.ColoringForFiltersChanged = delegate
        {
        };
    }
}
