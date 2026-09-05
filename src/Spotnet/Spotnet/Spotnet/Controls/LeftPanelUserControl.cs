using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Spotnet.Mvvm.Threading;
using MahApps.Metro.Controls;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Utilities;
using Spotnet.ViewModel;
using Spotnet.Views;
using Spotnet.Notifications;

namespace Spotnet.Controls;
public partial class LeftPanelUserControl : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly WebClient _suggest;
    private readonly History _historyDb;
    private readonly List<string> _suggestList;
    private Popup _searchPopup;
    private TextBox _searchText;
    private string _lastSearch = "";
    private string _lastSelectedFilter;
    private static MainWindowViewModel MainWindowVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).MainWindow;
    private static SpotsListViewModel SpotsListVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).SpotsList;

    public LeftPanelUserControl()
    {
        Sys.LeftPanel = this;
        _suggestList = new List<string>();
        _suggest = new WebClient();
        _historyDb = new History();
        _suggest.DownloadStringCompleted += Suggest_DownloadStringCompleted;
        InitializeComponent();
        AddFilterTreeView.DataContext = FilterCatViewModel.RootCollection;
        if (Sys.MainWindow != null)
        {
            MainWindow mainWindow = Sys.MainWindow;
            mainWindow.OnWindowPrepared = (Action)Delegate.Combine(mainWindow.OnWindowPrepared, new Action(OnWindowPrepared));
        }
    }

    private void OnWindowPrepared()
    {
        UseFilterCheckBox.Visibility = Visibility.Hidden;
        UpdateUseFilterCheckbox();
        if (Settings.Default.GoogleSuggest)
        {
            _historyDb.LoadHistory();
        }

        ExtensiveSearchCheckBox.IsChecked = Settings.Default.AdvancedSearch;

        try
        {
            NotificationManager.Instance.UnreadCountChanged += NotificationManager_UnreadCountChanged;
            UpdateUnreadNotificationsCount(NotificationManager.Instance.UnreadCount);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to hook NotificationManager in LeftPanel");
        }
    }

    private void UpdateUseFilterCheckbox()
    {
        FilterViewModel filterViewModel = MainWindowVm.FiltersCollection.FirstOrDefault((FilterViewModel f) => !f.Name.IsNullOrWhiteSpace());
        if (filterViewModel != null)
        {
            UseFilterCheckBox.Content = filterViewModel.Name;
            UseFilterCheckBox.Tag = filterViewModel.Id;
            UseFilterCheckBox.Visibility = Visibility.Visible;
        }
    }

    internal void SearchFilter(string zQuery, string sName)
    {
        if (zQuery.Equals("cat = 1") && sName.Equals(Categories.CatImages))
        {
            zQuery = "cats MATCH '1a12 OR 1a13'";
        }

        this.DispatchAsync(delegate
        {
            SearchBox.Text = "";
            ClearFilterSelection();
            SetFilter(Words.Search + ": " + sName, zQuery, Words.LookingFor + ": " + sName + "...", bResetCount: true);
            Sys.MainWindow.TabControl1.SelectedItem = (TabItem)Sys.MainWindow.TabControl1.Items[0];
            base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
            {
                SpotsListVm.SpotsContainer.Spots.Focus();
                SpotsListVm.SpotsContainer.RestoreFocus();
            });
        });
    }

    internal ContextMenu GetFilterMenu(FilterViewModel filter, bool bFirst, bool bLast)
    {
        ContextMenu contextMenu = new ContextMenu
        {
            FontFamily = base.FontFamily,
            FontSize = (double)Application.Current.Resources["ContextMenuFontSize"],
            FontStyle = base.FontStyle,
            Resources = AppHelper.GetMenuResourceDictionary
        };
        MenuItem menuItem = new MenuItem
        {
            Header = Words.FilterEdit,
            Tag = "EDIT " + filter.Id,
            IsEnabled = (!filter.Name.IsNullOrEmpty() && filter.CanBeModified),
            Icon = AppHelper.GetIcon("settings")
        };
        menuItem.Opacity = (menuItem.IsEnabled ? 1.0 : 0.5);
        menuItem.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoFilterMenu));
        MenuItem menuItem2 = new MenuItem
        {
            Header = Words.Delete,
            Tag = "DELETE " + filter.Id,
            IsEnabled = !filter.Name.IsNullOrEmpty(),
            Icon = AppHelper.GetIcon("delete")
        };
        menuItem2.Opacity = (menuItem2.IsEnabled ? 1.0 : 0.5);
        menuItem2.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoFilterMenu));
        MenuItem menuItem3 = new MenuItem
        {
            Header = Words.Up,
            Tag = "UP " + filter.Id,
            Icon = AppHelper.GetIcon("up"),
            IsEnabled = !bFirst,
            Opacity = (bFirst ? 0.5 : 1.0)
        };
        menuItem3.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoFilterMenu));
        MenuItem menuItem4 = new MenuItem
        {
            Header = Words.Down,
            Tag = "DOWN " + filter.Id,
            Icon = AppHelper.GetIcon("down"),
            IsEnabled = !bLast,
            Opacity = (bLast ? 0.5 : 1.0)
        };
        menuItem4.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoFilterMenu));
        contextMenu.Items.Add(menuItem3);
        contextMenu.Items.Add(menuItem4);
        contextMenu.Items.Add(new Separator());
        if (!Favorites.IsFavoritesQuery(filter.Query))
        {
            contextMenu.Items.Add(menuItem);
            contextMenu.Items.Add(menuItem2);
            contextMenu.Items.Add(new Separator());
        }

        MenuItem menuItem5 = new MenuItem
        {
            Header = Words.Filters,
            Icon = AppHelper.GetIcon("filter")
        };
        foreach (object headerMenuItem in GetHeaderMenuItems())
        {
            menuItem5.Items.Add(headerMenuItem);
        }

        contextMenu.Items.Add(menuItem5);
        return contextMenu;
    }

    internal ContextMenu GetHeaderFilterMenu()
    {
        ContextMenu contextMenu = new ContextMenu
        {
            FontFamily = base.FontFamily,
            FontSize = (double)Application.Current.Resources["ContextMenuFontSize"],
            FontStyle = base.FontStyle,
            Resources = AppHelper.GetMenuResourceDictionary
        };
        foreach (object headerMenuItem in GetHeaderMenuItems())
        {
            contextMenu.Items.Add(headerMenuItem);
        }

        return contextMenu;
    }

    private List<object> GetHeaderMenuItems()
    {
        MenuItem menuItem = new MenuItem
        {
            Header = Words.FiltersSaveAs,
            Tag = "SAVE",
            Icon = AppHelper.GetIcon("gear")
        };
        menuItem.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoHeaderFilterMenu));
        MenuItem menuItem2 = null;
        if (!Filters.GetUnchangableFilterNamesList().Contains(Settings.Default.Filter))
        {
            menuItem2 = new MenuItem
            {
                Header = Words.Delete,
                Tag = "DELETE",
                Icon = AppHelper.GetIcon("delete")
            };
            menuItem2.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoHeaderFilterMenu));
        }

        List<object> list = new List<object>
        {
            menuItem
        };
        if (menuItem2 != null)
        {
            list.Add(menuItem2);
        }

        list.Add(new Separator());
        foreach (string unchangableFilterNames in Filters.GetUnchangableFilterNamesList())
        {
            MenuItem menuItem3 = new MenuItem
            {
                Header = unchangableFilterNames,
                Tag = "FILTER|" + unchangableFilterNames,
                IsChecked = unchangableFilterNames.Equals(Settings.Default.Filter),
                FontStyle = FontStyles.Italic
            };
            menuItem3.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoHeaderFilterMenu));
            list.Add(menuItem3);
        }

        foreach (string changableFilterNames in Filters.GetChangableFilterNamesList())
        {
            MenuItem menuItem4 = new MenuItem
            {
                Header = changableFilterNames,
                Tag = "FILTER|" + changableFilterNames,
                IsChecked = changableFilterNames.Equals(Settings.Default.Filter)
            };
            menuItem4.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler(DoHeaderFilterMenu));
            list.Add(menuItem4);
        }

        return list;
    }

    private void FilterList_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        Sys.MainWindow.SpotsTab.IsSelected = true;
        FilterViewModel filterViewModel = e.NewValue as FilterViewModel;
        FilterViewModel filterViewModel2 = e.OldValue as FilterViewModel;
        string text = ((filterViewModel != null) ? filterViewModel.Id : "");
        string value = ((filterViewModel2 != null) ? filterViewModel2.Id : "");
        if (!text.Equals(value) && filterViewModel != null && !filterViewModel.Name.IsNullOrWhiteSpace() && !filterViewModel.Query.IsNullOrWhiteSpace())
        {
            HasFilter(text);
        }
        else
        {
            NoFilter();
        }

        e.Handled = true;
    }

    private void FilterList_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DependencyObject dependencyObject = e.OriginalSource as DependencyObject;
            while (dependencyObject != null && !(dependencyObject is TreeViewItem))
            {
                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }

            TreeViewItem treeViewItem = dependencyObject as TreeViewItem;
            if (treeViewItem == null)
            {
                return;
            }

            FilterViewModel filterViewModel = (FilterViewModel)treeViewItem.DataContext;
            if (filterViewModel != null)
            {
                FilterViewModel filterViewModel2 = filterViewModel.Parent ?? MainWindowVm.FiltersDb.FiltersRoot;
                int num = filterViewModel2.Children.IndexOf(filterViewModel);
                bool bFirst = num == 0;
                bool bLast = num == filterViewModel2.Children.Count - 1;
                ContextMenu filterMenu = GetFilterMenu(filterViewModel, bFirst, bLast);
                FrameworkElement obj = (FrameworkElement)e.Source;
                Brush bg = null;
                filterMenu.Opened += delegate
                {
                    bg = treeViewItem.Background;
                    treeViewItem.Background = (SolidColorBrush)Application.Current.FindResource("AccentColorBrush3");
                };
                filterMenu.Closed += delegate
                {
                    treeViewItem.Background = bg;
                };
                obj.ContextMenu = filterMenu;
                obj.ContextMenu.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    private void FilterList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        TreeViewItem treeViewItem = (e.OriginalSource as DependencyObject).TryFindParent<TreeViewItem>();
        if (treeViewItem == null)
        {
            return;
        }

        FilterViewModel filterViewModel = (FilterViewModel)treeViewItem.DataContext;
        if (filterViewModel != null)
        {
            ToggleButton toggleButton = treeViewItem.FindChildByType<ToggleButton>();
            bool flag = false;
            if (toggleButton != null)
            {
                flag = e.GetPosition(toggleButton).X < toggleButton.Width;
            }

            if (flag)
            {
                filterViewModel.IsExpanded = !filterViewModel.IsExpanded;
                MainWindowVm.FiltersDb.FiltersExpandedStateSaveAsync();
            }
            else
            {
                filterViewModel.IsSelected = !filterViewModel.Id.Equals(_lastSelectedFilter);
            }

            e.Handled = true;
        }
    }

    private void StackPanelScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (double)e.Delta);
            e.Handled = true;
        }
    }

    private void ShowTrustedOnlyNowButton_OnMouseUp(object sender, RoutedEventArgs e)
    {
        MainWindowVm.ShowTrustedOnlyPressed = false;
        MainWindowVm.ShowTrustedOnlyMode = !MainWindowVm.ShowTrustedOnlyMode;
    }

    private void ShowTrustedOnlyNowButton_OnMouseDown(object sender, RoutedEventArgs e)
    {
        MainWindowVm.ShowTrustedOnlyPressed = true;
    }

    private void FilterList_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private string GetFilterString(List<string> theFilX)
    {
        if (theFilX == null)
        {
            return null;
        }

        if (theFilX.Count == 0)
        {
            return "cat!=0";
        }

        string text = "";
        foreach (string item in theFilX)
        {
            string text2 = item.ToStringSafely();
            if (text2.Length == 3 && text2.StartsWith("X", StringComparison.OrdinalIgnoreCase))
            {
                text = text + (text.IsNullOrEmpty() ? "" : " OR ") + (Conversion.Val(text2.Substring(1, 2)) + 1.0);
            }
        }

        string text3 = text + (text.IsNullOrEmpty() ? "" : " OR ");
        long num = 0L;
        do
        {
            text3 += LimCat2(theFilX, num);
            num++;
        }
        while (num <= 9);
        checked
        {
            if (string.Equals(Strings.Right(text3, 4), " OR ", StringComparison.OrdinalIgnoreCase))
            {
                text3 = text3.Substring(0, text3.Length - 4);
            }

            if (string.Equals(Strings.Right(text3, 5), " AND ", StringComparison.OrdinalIgnoreCase))
            {
                text3 = text3.Substring(0, text3.Length - 5);
            }

            return Filters.SimplifyQuery("cats MATCH '" + text3.Trim() + "'");
        }
    }

    internal bool IsSearching()
    {
        if (Sys.MainWindow.TabControl1 == null || Sys.MainWindow.TabControl1.Items.Count == 0)
        {
            return false;
        }

        return AppHelper.GetHeader(RuntimeHelpers.GetObjectValue(((HeaderedContentControl)Sys.MainWindow.TabControl1.Items[0]).Header)).StartsWith(Words.Search + ": ");
    }

    private string LimCat(IEnumerable<string> theFilX, long limitCat, string limitSub)
    {
        if (theFilX == null)
        {
            return null;
        }

        string text = "";
        long num = 0L;
        foreach (string item in theFilX)
        {
            string text2 = item.ToStringSafely();
            if (string.Equals(text2.Substring(0, 1), "X", StringComparison.OrdinalIgnoreCase) && text2.Length > 3 && Math.Abs(Conversion.Val(text2.Substring(1, 2)) - (double)limitCat) < AppHelper.Epsilon && string.Equals(text2.Substring(3, 1).ToLower(), limitSub, StringComparison.OrdinalIgnoreCase))
            {
                num++;
                if (num > 1)
                {
                    text += " OR ";
                }

                text = text + (Conversion.Val(text2.Substring(1, 2)) + 1.0).ToStringSafely() + limitSub + Conversion.Val(text2.Substring(4)).ToStringSafely();
            }
        }

        if (num > 1)
        {
            text = "(" + text + ")";
        }

        return text;
    }

    private string LimCat2(List<string> theFilX, long limitCat)
    {
        string text = LimCat(theFilX, limitCat, "a");
        if (!text.IsNullOrEmpty())
        {
            text += " AND ";
        }

        string text2 = LimCat(theFilX, limitCat, "b");
        if (!text2.IsNullOrEmpty())
        {
            text2 += " AND ";
        }

        string text3 = LimCat(theFilX, limitCat, "c");
        if (!text3.IsNullOrEmpty())
        {
            text3 += " AND ";
        }

        string text4 = LimCat(theFilX, limitCat, "d");
        if (!text4.IsNullOrEmpty())
        {
            text4 += " AND ";
        }

        string text5 = LimCat(theFilX, limitCat, "z");
        string text6 = text + text2 + text3 + text4 + text5;
        if (Strings.Right(text6, 5).Equals(" AND "))
        {
            text6 = text6.Substring(0, checked(text6.Length - 5));
        }

        if (!text6.IsNullOrEmpty())
        {
            text6 = ((!text6.Contains(" AND ")) ? (" " + text6 + " OR ") : (" (" + text6 + ") OR "));
        }

        return text6;
    }

    private void UseFilterCheckBox_Click(object sender, RoutedEventArgs e)
    {
        RefreshSearch();
    }

    private void AddFilterTreeView_MouseMove(object sender, MouseEventArgs e)
    {
        AddFilterButton.IsEnabled = !AddFilterTextBox.Text.IsNullOrWhiteSpace() || TreeChilds();
        CheckAddButton();
    }

    private void AddFilterTreeView_MouseUp(object sender, MouseButtonEventArgs e)
    {
        AddFilterButton.IsEnabled = !AddFilterTextBox.Text.IsNullOrWhiteSpace() || TreeChilds();
        CheckAddButton();
    }

    private void AddFilterTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        AddFilterButton.IsEnabled = !AddFilterTextBox.Text.IsNullOrWhiteSpace() || TreeChilds();
        CheckAddButton();
    }

    private void CancelSuggest()
    {
        _suggest.CancelAsync();
    }

    private void QueryGoogle(string sSearch)
    {
        try
        {
            if (sSearch.Length < 2)
            {
                if (SearchBox.IsDropDownOpen)
                {
                    SearchBox.IsDropDownOpen = false;
                }
            }
            else
            {
                if (!_lastSearch.IsNullOrEmpty() && _lastSearch.EqualsIgnoreCase(sSearch) && SearchBox.IsDropDownOpen)
                {
                    return;
                }

                _lastSearch = sSearch;
                if (!Settings.Default.GoogleSuggest)
                {
                    return;
                }

                try
                {
                    CancelSuggest();
                    UpdateSuggestions();
                    if (!_suggest.IsBusy)
                    {
                        _suggest.DownloadStringAsync(new Uri("http://www.google.nl/complete/search?hl=nl&output=toolbar&q=" + AppHelper.HtmlEncode(sSearch)));
                    }

                    return;
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                    return;
                }
            }
        }
        catch (Exception ex2)
        {
            Log.Exception(ex2);
        }
    }

    private void ShowTrustedOnlyBorder_OnMouseLeave(object sender, MouseEventArgs e)
    {
        MainWindowVm.ShowTrustedOnlyPressed = false;
    }

    private void ClickOnSearchClear(object sender, RoutedEventArgs e)
    {
        NoFilter();
        SearchBox.Text = "";
    }

    private void Panel_OnExpandChanged(object sender, RoutedEventArgs e)
    {
        UpdateLeftPanelSizes();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateLeftPanelSizes();
    }

    private void UpdateLeftPanelSizes()
    {
        if (FiltersExpander != null && AddFilterPanel != null && StackPanel2 != null)
        {
            int num = (SearchExpander.IsExpanded ? 178 : 35);
            int num2 = (FiltersExpander.IsExpanded ? 170 : 35);
            double num3 = Sys.MainWindow.ActualHeight - (double)num2 - (double)num - 75.0;
            if (num3 > 47.0)
            {
                AddFilterPanel.MaxHeight = num3;
            }
        }
    }

    private void CheckAddButton()
    {
        if (AddFilterButton.IsEnabled)
        {
            AddFilter.Opacity = 1.0;
            AddFilter.Cursor = Cursors.Hand;
            AddFilter.ToolTip = Words.AddFilterButtonToolTip;
        }
        else
        {
            AddFilter.Opacity = 0.3;
            AddFilter.Cursor = null;
            AddFilter.ToolTip = null;
        }
    }

    private void ClearFilterSelection(FilterViewModel root = null)
    {
        IEnumerable<FilterViewModel> source;
        if (root != null)
        {
            IEnumerable<FilterViewModel> children = root.Children;
            source = children;
        }
        else
        {
            source = FilterList.Items.Cast<FilterViewModel>();
        }

        List<FilterViewModel> list = source.ToList();
        if (!list.Any())
        {
            return;
        }

        foreach (FilterViewModel item in list)
        {
            item.IsSelected = false;
            ClearFilterSelection(item);
        }
    }

    private bool DoSearch()
    {
        try
        {
            Sys.MainWindow.TabControl1.SelectedIndex = 0;
            SearchBox.IsDropDownOpen = false;
            _searchText.SelectionStart = _searchText.Text.Length;
            string text = SearchBox.Text.Trim();
            if (text.IsNullOrEmpty())
            {
                NoFilter();
            }
            else
            {
                if (Settings.Default.GoogleSuggest)
                {
                    _historyDb.SaveHistory(text);
                }

                ClearFilterSelection();
                SetFilter(Words.Search + ": " + text, SearchString(text), Words.LookingFor + ": " + text + "...", bResetCount: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }

    private bool TreeChilds()
    {
        foreach (FilterCatViewModel item in AddFilterTreeView.Items.Cast<FilterCatViewModel>().ToList())
        {
            if (!item.IsChecked.HasValue)
            {
                if (item.Children.Any((FilterCatViewModel с2) => с2.Children.Any((FilterCatViewModel c) => c.IsChecked == true)))
                {
                    return true;
                }
            }
            else if (item.IsChecked == true)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateSuggestions()
    {
        try
        {
            string text = SearchBox.Text;
            int selectionStart = _searchText.SelectionStart;
            int selectionLength = _searchText.SelectionLength;
            SearchBox.Items.Clear();
            if (!string.Equals(text, SearchBox.Text, StringComparison.OrdinalIgnoreCase))
            {
                _searchText.Text = text;
                _searchText.SelectionStart = selectionStart;
                _searchText.SelectionLength = selectionLength;
            }

            if (_suggestList.Count == 0)
            {
                if (SearchBox.IsDropDownOpen)
                {
                    SearchBox.IsDropDownOpen = false;
                }

                return;
            }

            foreach (string suggest in _suggestList)
            {
                SearchBox.Items.Add(suggest);
            }

            if (!SearchBox.IsDropDownOpen && Settings.Default.GoogleSuggest)
            {
                SearchBox.IsDropDownOpen = true;
                _searchText.SelectionStart = selectionStart;
                _searchText.SelectionLength = selectionLength;
                if (!string.Equals(text, SearchBox.Text, StringComparison.OrdinalIgnoreCase))
                {
                    _searchText.Text = text;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
        }
    }

    private void AddFilter_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddFilterButton.IsEnabled || !AddFilter.IsEnabled)
        {
            return;
        }

        List<string> list = new List<string>();
        foreach (FilterCatViewModel item in AddFilterTreeView.Items.Cast<FilterCatViewModel>().ToList())
        {
            if (item.IsChecked.HasValue)
            {
                if (item.IsChecked == true)
                {
                    list.Add("X0" + item.CatLink.Tag);
                }

                continue;
            }

            foreach (FilterCatViewModel child in item.Children)
            {
                int num = child.Children.Count((FilterCatViewModel current3) => current3.IsChecked == true);
                if (num <= 0 || num >= child.Children.Count)
                {
                    continue;
                }

                foreach (FilterCatViewModel item2 in child.Children.Where((FilterCatViewModel current3) => current3.IsChecked == true))
                {
                    string text = item2.CatLink.Tag.ToUpperInvariant();
                    string text2 = ((Conversion.Val(text.Substring(1)) > 9.0) ? text : text.Insert(1, "0"));
                    list.Add("X0" + item.CatLink.Tag + text2);
                }
            }
        }

        if (AddFilterTextBox.Text.IsNullOrWhiteSpace())
        {
            AddFilterButton.IsEnabled = false;
            CheckAddButton();
            Interaction.MsgBox(Words.AddFilterErrorFilterEmpty, MsgBoxStyle.Information);
        }
        else if (SaveFilter(GetFilterString(list), AddFilterTextBox.Text.Trim()))
        {
            AddFilterTextBox.Text = "";
            AddFilterTreeView.ItemsSource = FilterCatViewModel.RootCollection;
            if (AddFilterExpander.IsExpanded && !FiltersExpander.IsExpanded)
            {
                AddFilterExpander.IsExpanded = false;
            }
        }
    }

    private bool NewFilter(string filterName, string filterQuery, string sImg, bool showErr)
    {
        if (filterName.IsNullOrWhiteSpace())
        {
            return false;
        }

        try
        {
            if (MainWindowVm.FiltersDb.AddFilter(filterName, filterQuery, sImg))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            if (showErr)
            {
                AppHelper.Error(Words.CannotAddFilter + ": " + ex.Message);
            }
        }

        return false;
    }

    internal bool SaveFilter(string filterQuery, string filterFullName)
    {
        filterQuery = filterQuery.Replace("cat<9 AND ", "").Replace("cats NOT LIKE '9 %' AND ", "");
        FilterViewModel filterByName = MainWindowVm.FiltersDb.GetFilterByName(filterFullName);
        if (filterByName != null)
        {
            if (AppHelper.AskYesNo(string.Format(Words.FilterNameAlreadyExists, filterFullName), $"{Words.Filter} {Words.AlreadyExists}") == MsgBoxResult.No)
            {
                return false;
            }

            MainWindowVm.FiltersDb.UpdateFilterQuery(filterByName, filterQuery);
            return true;
        }

        if (!NewFilter(filterFullName, filterQuery, "", showErr: false))
        {
            AppHelper.Error(Words.CannotAddFilter + " " + filterFullName);
            return false;
        }

        return true;
    }

    private void ExtensiveSearchCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensiveSearchCheckBox.IsChecked.HasValue)
        {
            Settings.Default.AdvancedSearch = ExtensiveSearchCheckBox.IsChecked.Value;
        }

        Settings.Default.Save();
        RefreshSearch();
    }

    private void AddFilterTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            AddFilter_MouseDown(null, null);
        }
    }

    private void AddFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddFilterButton.IsEnabled = !AddFilterTextBox.Text.IsNullOrWhiteSpace() || TreeChilds();
        CheckAddButton();
    }

    private void AddFilterExpander_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AddFilterExpander.IsExpanded && e.OriginalSource is TextBlock && Operators.ConditionalCompareObjectEqual(NewLateBinding.LateGet(e.OriginalSource, null, "text", new object[0], null, null, null), AddFilterExpander.Header, TextCompare: false))
        {
            AddFilterExpander.IsExpanded = !AddFilterExpander.IsExpanded;
        }
    }

    private void SearchBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        CancelSuggest();
        ShowSuggestions.IsChecked = Settings.Default.GoogleSuggest;
    }

    private void SearchBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CancelSuggest();
            DoSearch();
        }
        else if (e.Key != Key.End && e.Key != Key.Home && e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right && SearchByTitleRadio.IsChecked.GetValueOrDefault())
        {
            DispatcherHelper.UIDispatcher.BeginInvoke((Action)delegate
            {
                QueryGoogle(_searchText.Text);
            }, DispatcherPriority.Background);
        }
    }

    private void SearchBox_Loaded(object sender, RoutedEventArgs e)
    {
        _searchPopup = (Popup)SearchBox.Template.FindName("PART_Popup", SearchBox);
        if (_searchPopup != null)
        {
            _searchPopup.Closed += SearchPopup_Closed;
            _searchText = (TextBox)SearchBox.Template.FindName("PART_EditableTextBox", SearchBox);
            _searchText.ContextMenu = SearchBox.ContextMenu;
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CancelSuggest();
    }

    private void SearchBySenderRadio_Click(object sender, RoutedEventArgs e)
    {
        if (SearchBySenderRadio.IsChecked.GetValueOrDefault())
        {
            SearchBox.IsDropDownOpen = false;
            RefreshSearch();
        }
    }

    private void SearchByTagRadio_Click(object sender, RoutedEventArgs e)
    {
        if (SearchByTagRadio.IsChecked.GetValueOrDefault())
        {
            SearchBox.IsDropDownOpen = false;
            RefreshSearch();
        }
    }

    private void SearchByTitleRadio_Click(object sender, RoutedEventArgs e)
    {
        if (SearchByTitleRadio.IsChecked.GetValueOrDefault())
        {
            SearchBox.IsDropDownOpen = false;
            RefreshSearch();
        }
    }

    private void ClickOnSearch(object sender, RoutedEventArgs routedEventArgs)
    {
        DoSearch();
    }

    private void SearchPopup_Closed(object sender, EventArgs e)
    {
        if (SearchBox.SelectedItem != null)
        {
            _searchText.SelectionStart = _searchText.Text.Length;
        }
    }

    private void ShowSuggestions_Click(object sender, RoutedEventArgs e)
    {
        Settings.Default.GoogleSuggest = ShowSuggestions.IsChecked;
        Settings.Default.Save();
    }

    private void Suggest_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
    {
        if (!Settings.Default.GoogleSuggest || e.Cancelled || e.Error != null)
        {
            return;
        }

        _suggestList.Clear();
        XmlDocument xmlDocument = new XmlDocument();
        // e.Result is an HTTP response body; do not resolve external entities from it.
        xmlDocument.XmlResolver = null;
        xmlDocument.LoadXml(e.Result);
        foreach (XmlNode item in xmlDocument.SelectNodes("//CompleteSuggestion"))
        {
            XmlNode xmlNode = item.SelectSingleNode("suggestion/@data");
            if (xmlNode != null)
            {
                _suggestList.Add(xmlNode.InnerText);
            }
        }

        foreach (string historyItem in _historyDb.HistoryItems)
        {
            if (historyItem.StartsWith(_searchText.Text))
            {
                _suggestList.Add(historyItem);
            }
        }

        if (_searchText.IsFocused && SearchBox.IsDropDownOpen)
        {
            UpdateSuggestions();
        }
    }

    private void HasFilter(string filterId)
    {
        _lastSelectedFilter = filterId;
        FilterViewModel filter = MainWindowVm.FiltersDb.GetFilter(filterId);
        if (filter == null || filter.Name.IsNullOrEmpty() || filter.Query.IsNullOrEmpty())
        {
            AppHelper.Error("Filter Err2");
        }
        else if (!Sys.MainWindow.SpotProvider.RowFilter.EqualsIgnoreCase(filter.Query) && SetFilter(filter.Name, filter.Query, Words.SpotsFiltering, bResetCount: true))
        {
            UseFilterCheckBox.Content = Sys.MainWindow.SpotProvider.QueryName;
            UseFilterCheckBox.Tag = filter.Id;
        }
    }

    internal void NoFilter(bool bForce = false, string waitString = null)
    {
        if (waitString == null)
        {
            waitString = Words.SpotsLoading;
        }

        ClearFilterSelection();
        _lastSelectedFilter = null;
        if (bForce || !Sys.MainWindow.SpotProvider.RowFilter.EqualsIgnoreCase("cat < 9"))
        {
            SetFilter("cat < 9", "cat < 9", waitString, bResetCount: true);
        }
    }

    private void RefreshSearch()
    {
        if (IsSearching() && Sys.MainWindow.TabControl1 != null && Sys.MainWindow.TabControl1.Items.Count != 0 && string.Equals(AppHelper.GetHeader(RuntimeHelpers.GetObjectValue(((HeaderedContentControl)Sys.MainWindow.TabControl1.Items[0]).Header)).Trim(), Words.Search + ": " + SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            DoSearch();
        }
    }

    internal void ReloadFilter(bool bResetCount, string waitString = null)
    {
        if (waitString == null)
        {
            waitString = Words.SpotsLoading;
        }

        SetFilter(Sys.MainWindow.SpotProvider.QueryName, Sys.MainWindow.SpotProvider.RowFilter, waitString, bResetCount);
    }

    internal void ReloadFilters()
    {
        MainWindowVm.FiltersDb.LoadFilters();
        if (UseFilterCheckBox.Visibility == Visibility.Hidden)
        {
            UpdateUseFilterCheckbox();
        }

        NoFilter();
    }

    private void RemoveFilter(string filterId)
    {
        MainWindowVm.FiltersDb.RemoveFilter(filterId);
        if (!MainWindowVm.FiltersCollection.Any())
        {
            MainWindowVm.FiltersDb.ResetFilters();
        }
    }

    private void EditFilter(string filterId)
    {
        FilterViewModel filter = MainWindowVm.FiltersDb.GetFilter(filterId);
        AddFilterTextBox.Text = filter.FullPathString;
        FilterCatViewModel.ApplyQueryToRootCollection(filter.Query);
        AddFilterExpander.IsExpanded = true;
        AddFilterTextBox.Focus();
        AddFilterTextBox.CaretIndex = AddFilterTextBox.Text.Length;
    }

    private bool SetFilter(string sName, string sFilter, string waitString, bool bResetCount)
    {
        MainWindowVm.IsSearchResetVisible = IsSearchFilter(sName);
        if (SpotsListVm.SpotsContainer?.Spots == null)
        {
            return true;
        }

        Sys.MainWindow.WaitString = waitString;
        SpotsListVm.SpotsContainer.Spots.SelectedIndex = -1;
        if (bResetCount)
        {
            Sys.MainWindow.SpotProvider.ResetCount();
        }

        Sys.MainWindow.SpotProvider.QueryName = sName;
        Sys.MainWindow.SpotProvider.RowFilter = sFilter;
        SpotsListVm.ResetNewSpotsBar();
        SpotsListVm.SpotsContainer.IcStopWait = true;
        VirtualList<ISpotRow> virtualList = (VirtualList<ISpotRow>)SpotsListVm.SpotsContainer.Spots.ItemsSource;
        if (virtualList == null)
        {
            Log.Error("ItemsSource is null");
            Sys.MainWindow.Close();
            return false;
        }

        virtualList.Clear();
        return true;
    }

    private void DoFilterMenu(object sender, RoutedEventArgs e)
    {
        string text = ((MenuItem)e.OriginalSource).Tag.ToStringSafely();
        string text2 = Strings.Split(text)[0];
        string filterId = ((text2.Length != text.Length) ? text.Substring(text2.Length + 1) : null);
        if (text2.EqualsIgnoreCase("DELETE"))
        {
            RemoveFilter(filterId);
        }
        else if (text2.EqualsIgnoreCase("UP"))
        {
            MainWindowVm.FiltersDb.SwapFilter(filterId, bUp: true);
        }
        else if (text2.EqualsIgnoreCase("DOWN"))
        {
            MainWindowVm.FiltersDb.SwapFilter(filterId, bUp: false);
        }
        else if (text2.EqualsIgnoreCase("EDIT"))
        {
            EditFilter(filterId);
        }
    }

    private void DoHeaderFilterMenu(object sender, RoutedEventArgs e)
    {
        string text = ((MenuItem)e.OriginalSource).Tag.ToStringSafely();
        if (text.EqualsIgnoreCase("SAVE"))
        {
            try
            {
                FilterSaveAsWindow filterSaveAsWindow = new FilterSaveAsWindow
                {
                    Owner = Sys.MainWindow
                };
                filterSaveAsWindow.ShowDialog();
                if (!filterSaveAsWindow.NewName.IsNullOrEmpty())
                {
                    MainWindowVm.FiltersDb.SaveAs(filterSaveAsWindow.NewName);
                }

                return;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, showToClient: true);
                return;
            }
        }

        if (text.EqualsIgnoreCase("DELETE"))
        {
            try
            {
                if (!Filters.GetUnchangableFilterNamesList().Contains(Settings.Default.Filter) && AppHelper.AskYesNo(string.Format(Words.FiltersDeleteDialogQuestion, Settings.Default.Filter), string.Format(Words.FiltersDeleteDialogHeader, Settings.Default.Filter)) == MsgBoxResult.Yes)
                {
                    MainWindowVm.FiltersDb.RemoveFiltersList();
                    ReloadFilters();
                }

                return;
            }
            catch (Exception ex2)
            {
                Log.Exception(ex2, showToClient: true);
                return;
            }
        }

        if (text.StartsWith("FILTER|"))
        {
            string text2 = text.Substring("FILTER|".Length);
            if (!text2.Equals(Settings.Default.Filter))
            {
                MainWindowVm.FilterSelectedName = text2;
                ReloadFilters();
            }
        }
    }

    private bool IsSearchFilter(string filterName)
    {
        return filterName.StartsWith(Words.Search + ": ");
    }

    private static string RewriteQuery(string sIn)
    {
        if (sIn.IsNullOrEmpty())
        {
            return sIn;
        }

        string a = sIn.Replace(" ", "").Replace("(", "").Replace(")", "").ToLower();
        if (string.Equals(a, "cat=1") || string.Equals(a, "searchmatch'cats:1'"))
        {
            return "cats MATCH '1'";
        }

        if (string.Equals(a, "cat=2") || string.Equals(a, "searchmatch'cats:2'"))
        {
            return "cats MATCH '2'";
        }

        if (string.Equals(a, "cat=3") || string.Equals(a, "searchmatch'cats:3'"))
        {
            return "cats MATCH '3'";
        }

        if (string.Equals(a, "cat=4") || string.Equals(a, "searchmatch'cats:4'"))
        {
            return "cats MATCH '4'";
        }

        if (string.Equals(a, "cat=5") || string.Equals(a, "searchmatch'cats:5'"))
        {
            return "cats MATCH '5'";
        }

        if (string.Equals(a, "cat=6") || string.Equals(a, "searchmatch'cats:6'"))
        {
            return "cats MATCH '6'";
        }

        if (string.Equals(a, "cat=9") || string.Equals(a, "searchmatch'cats:9'"))
        {
            return "cats MATCH '9'";
        }

        return sIn;
    }

    private string SearchString(string xVal)
    {
        string text = "";
        string text2 = "";
        xVal = Strings.Replace(xVal, "  ", " ").Replace("  ", " ");
        xVal = Strings.Replace(xVal, "'", "''").Replace("-", " ");
        xVal = xVal.Trim();
        if (xVal.IsNullOrEmpty())
        {
            return null;
        }

        if (((UseFilterCheckBox.Visibility == Visibility.Visible) ? (UseFilterCheckBox.IsEnabled ? UseFilterCheckBox.IsChecked : new bool? (false)) : new bool? (false)).GetValueOrDefault())
        {
            FilterViewModel filter = MainWindowVm.FiltersDb.GetFilter(UseFilterCheckBox.Tag.ToStringSafely());
            if (filter != null)
            {
                text = RewriteQuery(filter.Query) + " AND rowid IN (SELECT rowid FROM search WHERE ";
                text2 = ")";
            }
            else
            {
                UseFilterCheckBox.IsChecked = false;
            }
        }

        bool? flag = (SearchBySenderRadio.IsEnabled ? SearchBySenderRadio.IsChecked : new bool? (false));
        bool? flag2 = flag;
        if (flag2.GetValueOrDefault())
        {
            if ((!(ExtensiveSearchCheckBox.IsEnabled ? ExtensiveSearchCheckBox.IsChecked : new bool? (false))).GetValueOrDefault())
            {
                return text + "sender MATCH '" + xVal.Replace(" ", "").ToLower() + "'" + text2;
            }

            return text + "sender MATCH '" + xVal.Replace(" ", "").ToLower() + "*'" + text2;
        }

        bool? isChecked = SearchByTitleRadio.IsChecked;
        if ((SearchByTitleRadio.IsEnabled & isChecked).GetValueOrDefault())
        {
            if ((!(ExtensiveSearchCheckBox.IsEnabled ? ExtensiveSearchCheckBox.IsChecked : new bool? (false))).GetValueOrDefault())
            {
                if (string.Equals(xVal, AppHelper.StripNonAlphaNumericCharacters(xVal), StringComparison.OrdinalIgnoreCase))
                {
                    return text + "subject MATCH '" + xVal.ToLower() + "'" + text2;
                }

                return text + "subject MATCH '\"" + xVal.ToLower().Replace("\"", "") + "\"'" + text2;
            }

            return text + "subject MATCH '" + xVal.ToLower().Replace(" ", "* ") + "*'" + text2;
        }

        if (!(SearchByTagRadio.IsEnabled ? SearchByTagRadio.IsChecked : new bool? (false)).GetValueOrDefault())
        {
            return null;
        }

        if ((!(ExtensiveSearchCheckBox.IsEnabled ? ExtensiveSearchCheckBox.IsChecked : new bool? (false))).GetValueOrDefault())
        {
            return text + "tag MATCH '" + xVal.Replace(" ", "").ToLower() + "'" + text2;
        }

        return text + "tag MATCH '" + xVal.Replace(" ", "").ToLower() + "*'" + text2;
    }

    private void Filters_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ContextMenu headerFilterMenu = GetHeaderFilterMenu();
        FrameworkElement obj = (FrameworkElement)e.Source;
        obj.ContextMenu = headerFilterMenu;
        obj.ContextMenu.IsOpen = true;
    }

    private void NotificationManager_UnreadCountChanged()
    {
        this.DispatchAsync(() => UpdateUnreadNotificationsCount(NotificationManager.Instance.UnreadCount));
    }

    private void UpdateUnreadNotificationsCount(int unreadCount)
    {
        if (LeftPanelUnreadBadge == null || LeftPanelUnreadText == null) return;
        if (unreadCount > 0)
        {
            LeftPanelUnreadText.Text = unreadCount > 99 ? "99+" : unreadCount.ToString();
            LeftPanelUnreadBadge.Visibility = Visibility.Visible;
        }
        else
        {
            LeftPanelUnreadBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NotificationCenterWindow window = new NotificationCenterWindow(initialTabIndex: 0);
            window.Owner = Sys.MainWindow;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open NotificationCenterWindow");
        }
    }

    private void CreateNotificationRuleBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NotificationCenterWindow window = new NotificationCenterWindow(initialTabIndex: 1);
            window.Owner = Sys.MainWindow;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open NotificationCenterWindow on rules tab");
        }
    }

    private void NotificationExpander_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBlock tb && string.Equals(tb.Text, "MELDINGEN", StringComparison.OrdinalIgnoreCase))
        {
            NotificationExpander.IsExpanded = !NotificationExpander.IsExpanded;
            e.Handled = true;
        }
    }

    private void NotificationExpander_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenNotificationsButton_Click(sender, e);
        e.Handled = true;
    }
}
