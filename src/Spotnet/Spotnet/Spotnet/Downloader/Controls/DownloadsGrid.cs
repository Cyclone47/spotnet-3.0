using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using Spotnet.Mvvm.Threading;
using Microsoft.VisualBasic.CompilerServices;
using NLog;
using Spotnet.Controls;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Downloader.Controls;
public partial class DownloadsGrid : UserControl, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private int _lockColumnUpdated;
    private bool _columnShouldBeUpdatedOneMoreTime;
    private static readonly string ColumnsWidthDefault = "32,1;724,4;94,1;75,1;97,1;77,1;77,1;20,1;20,1";
    internal ContextMenu HeaderMenu;
    public DownloadsGrid()
    {
        if (!Sys.IsShutdownRequested)
        {
            InitializeComponent();
            CollectionViewSource collectionViewSource = new CollectionViewSource
            {
                Source = Sys.Downloader.Items
            };
            Downloads.ItemsSource = collectionViewSource.View;
            Sys.Downloader.ItemsOrderChanged += DownloaderOnItemsOrderChanged;
            SetDefaultSortOrder();
        }
    }

    private void DownloaderOnItemsOrderChanged()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            ((ICollectionView)Downloads.ItemsSource).Refresh();
        });
    }

    public void SetDefaultSortOrder()
    {
        DataGridColumn dataGridColumn = Downloads.Columns.First();
        Downloads.Items.SortDescriptions.Clear();
        Downloads.Items.SortDescriptions.Add(new SortDescription(dataGridColumn.SortMemberPath, ListSortDirection.Ascending));
        foreach (DataGridColumn column in Downloads.Columns)
        {
            column.SortDirection = null;
        }

        dataGridColumn.SortDirection = ListSortDirection.Ascending;
        Downloads.Items.Refresh();
    }

    internal DownloaderItemViewModel GetDownloaderItemVmBySubject(string subject)
    {
        return Sys.Downloader.Items.ItemsDict.Values.FirstOrDefault((DownloaderItemViewModel vm) => vm.Titel.EqualsIgnoreCase(subject));
    }

    internal bool IsDownloadExists(DownloaderItemViewModel download)
    {
        return Sys.Downloader.Items.ItemsDict.Values.Contains(download);
    }

    private void Downloads_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (Downloads.SelectedItems.Count == 0 || e.OriginalSource is ScrollViewer)
        {
            return;
        }

        List<DownloaderItemViewModel> list = new List<DownloaderItemViewModel>();
        try
        {
            list.AddRange(
                from i in Downloads.SelectedItems.OfType<DownloaderItemViewModel>()
                where !i.IsNzbDownload
                select i);
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return;
        }

        if (list.Count != 0)
        {
            ContextMenu contextMenu = ((list.Count > 1) ? GetMultiMenu(list) : GetMenu(list.First()));
            FrameworkElement obj = (FrameworkElement)e.Source;
            obj.ContextMenu = contextMenu;
            obj.ContextMenu.IsOpen = true;
        }
    }

    private void OpenSpot(DownloaderItemViewModel item)
    {
        if (item != null && !item.MessageId.IsNullOrWhiteSpace())
        {
            Sys.MainWindow.OpenSpot(SpotRowViewModel.InitializeNewSpotRow(new Spot { MessageId = item.MessageId, Title = item.Titel }));
        }
    }

    private void Downloads_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Downloads.SelectedItems.Count == 1)
        {
            DownloaderItemViewModel downloaderItemViewModel = (DownloaderItemViewModel)Downloads.SelectedItems[0];
            if (downloaderItemViewModel.IsHistory)
            {
                downloaderItemViewModel.OpenCompleteDir();
            }
            else
            {
                OpenSpot(downloaderItemViewModel);
            }

            e.Handled = true;
        }
    }

    private void Downloads_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (Downloads.SelectedItems.Count != 0)
            {
                DownloaderItemViewModel[] items = new DownloaderItemViewModel[Downloads.SelectedItems.Count];
                Downloads.SelectedItems.CopyTo(items, 0);
                Task.Run(delegate
                {
                    Sys.Downloader.RemoveItemsAsync(items);
                });
            }
        }
        else
        {
            e.Handled = false;
        }
    }

    private void Downloads_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Downloads.IsFocused)
        {
            Downloads.Focus();
        }
    }

    private ContextMenu GetMultiMenu(List<DownloaderItemViewModel> items)
    {
        if (items == null || !items.Any())
        {
            return null;
        }

        ContextMenu contextMenu = new ContextMenu
        {
            FontFamily = base.FontFamily,
            FontSize = (double)Application.Current.Resources["ContextMenuFontSize"],
            FontStyle = base.FontStyle,
            Tag = items,
            Resources = AppHelper.GetMenuResourceDictionary
        };
        bool flag = false;
        bool flag2 = false;
        bool flag3 = true;
        bool flag4 = true;
        foreach (DownloaderItemViewModel item in items.Where((DownloaderItemViewModel current) => !current.IsHistory))
        {
            if (!item.IsHistory)
            {
                if (item.IsPaused)
                {
                    flag2 = true;
                }
                else
                {
                    flag = true;
                }
            }

            if (item.IsHistory)
            {
                flag4 = false;
            }
            else
            {
                flag3 = false;
            }
        }

        MenuItem menuItem = new MenuItem
        {
            Header = Words.Up,
            Tag = "UP",
            IsEnabled = Sys.Downloader.CanMoveUp(items),
            Icon = AppHelper.GetIcon("up")
        };
        if (!menuItem.IsEnabled)
        {
            menuItem.Opacity = 0.5;
        }

        menuItem.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.MoveUp(items);
        });
        MenuItem menuItem2 = new MenuItem
        {
            Header = Words.Down,
            Tag = "DOWN",
            IsEnabled = Sys.Downloader.CanMoveDown(items),
            Icon = AppHelper.GetIcon("down")
        };
        if (!menuItem2.IsEnabled)
        {
            menuItem2.Opacity = 0.5;
        }

        menuItem2.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.MoveDown(items);
        });
        MenuItem menuItem3 = new MenuItem
        {
            Header = Words.Delete,
            Tag = "DELETE",
            Icon = AppHelper.GetIcon("delete")
        };
        if (!menuItem3.IsEnabled)
        {
            menuItem3.Opacity = 0.5;
        }

        menuItem3.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.RemoveItemsAsync(items);
        });
        MenuItem menuItem4 = new MenuItem
        {
            Header = Words.PauseText,
            Tag = "PAUSE",
            IsEnabled = flag
        };
        Image icon = AppHelper.GetIcon("pause");
        icon.Width = 18.0;
        menuItem4.Icon = icon;
        if (!menuItem4.IsEnabled)
        {
            menuItem4.Opacity = 0.5;
        }

        menuItem4.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.PauseItemsAsync(items);
        });
        MenuItem menuItem5 = new MenuItem
        {
            Header = Words.ResumeText,
            Tag = "RESUME",
            IsEnabled = flag2,
            Icon = AppHelper.GetIcon("resume")
        };
        if (!menuItem5.IsEnabled)
        {
            menuItem5.Opacity = 0.5;
        }

        menuItem5.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.ResumeItemsAsync(items);
        });
        if ((flag3 || flag4) && (menuItem.IsEnabled || menuItem2.IsEnabled))
        {
            if (menuItem.IsEnabled)
            {
                contextMenu.Items.Add(menuItem);
            }

            if (menuItem2.IsEnabled)
            {
                contextMenu.Items.Add(menuItem2);
            }

            contextMenu.Items.Add(new Separator());
        }

        if (flag || flag2)
        {
            if (flag)
            {
                contextMenu.Items.Add(menuItem4);
            }

            if (flag2)
            {
                contextMenu.Items.Add(menuItem5);
            }

            contextMenu.Items.Add(new Separator());
        }

        contextMenu.Items.Add(menuItem3);
        if (Settings.Default.ExternalNzbGet)
        {
            contextMenu.Items.Add(new Separator());
            TextBlock icon2 = new TextBlock
            {
                Style = (Style)Application.Current.FindResource("FontAwesomeTabs"),
                Text = "\uf019"
            };
            MenuItem menuItem6 = new MenuItem
            {
                Header = Words.DownloadsAdvanced,
                Tag = "ADVANCED",
                IsEnabled = true,
                Icon = icon2
            };
            menuItem6.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
            {
                Sys.MainWindow.OpenPage(PageTypeEnum.AdvancedDownloads, "", saveParrentTab: true).Forget();
            });
            contextMenu.Items.Add(menuItem6);
        }

        return contextMenu;
    }

    private ContextMenu GetMenu(DownloaderItemViewModel item)
    {
        ContextMenu contextMenu = new ContextMenu
        {
            FontFamily = base.FontFamily,
            FontSize = (double)Application.Current.Resources["ContextMenuFontSize"],
            FontStyle = base.FontStyle,
            Tag = item,
            Resources = AppHelper.GetMenuResourceDictionary
        };
        if (item.IsHistory)
        {
            MenuItem menuItem = new MenuItem
            {
                Header = Words.Open,
                Tag = "OPEN",
                IsEnabled = item.CanOpen,
                Icon = AppHelper.GetIcon("open"),
                FontWeight = FontWeights.Bold
            };
            if (!menuItem.IsEnabled)
            {
                menuItem.Opacity = 0.5;
            }

            menuItem.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
            {
                item.OpenCompleteDir();
            });
            contextMenu.Items.Add(menuItem);
        }

        TextBlock icon = new TextBlock
        {
            Style = (Style)Application.Current.FindResource("FontAwesomeTabs"),
            Text = "\uf005"
        };
        MenuItem menuItem2 = new MenuItem
        {
            Header = Words.OpenSpotTab,
            Tag = "OPENSPOT",
            IsEnabled = !item.MessageId.IsNullOrEmpty(),
            Icon = icon,
            Opacity = (item.MessageId.IsNullOrEmpty() ? 0.5 : 1.0)
        };
        if (!item.IsHistory)
        {
            menuItem2.FontWeight = FontWeights.Bold;
        }

        menuItem2.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            OpenSpot(item);
        });
        contextMenu.Items.Add(menuItem2);
        SpotnetDownloaderItemViewModel sItem = item as SpotnetDownloaderItemViewModel;
        if (sItem != null)
        {
            MenuItem menuItem3 = new MenuItem
            {
                Header = Words.ShowLog,
                Tag = "SHOWLOG",
                IsEnabled = File.Exists(sItem.LogQueue.LogPath),
                Icon = AppHelper.GetIcon("showlog")
            };
            if (!menuItem3.IsEnabled)
            {
                menuItem3.Opacity = 0.5;
            }

            menuItem3.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
            {
                ((SpotnetDownloader)Sys.Downloader).ShowLog(sItem);
            });
            contextMenu.Items.Add(menuItem3);
        }

        contextMenu.Items.Add(new Separator());
        MenuItem menuItem4 = new MenuItem();
        menuItem4.Header = Words.Up;
        menuItem4.Tag = "UP";
        menuItem4.IsEnabled = Sys.Downloader.CanMoveUp(new DownloaderItemViewModel[1] { item });
        menuItem4.Icon = AppHelper.GetIcon("up");
        MenuItem menuItem5 = menuItem4;
        if (!menuItem5.IsEnabled)
        {
            menuItem5.Opacity = 0.5;
        }

        menuItem5.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.MoveUp(new DownloaderItemViewModel[1] { item });
        });
        menuItem4 = new MenuItem();
        menuItem4.Header = Words.Down;
        menuItem4.Tag = "DOWN";
        menuItem4.IsEnabled = Sys.Downloader.CanMoveDown(new DownloaderItemViewModel[1] { item });
        menuItem4.Icon = AppHelper.GetIcon("down");
        MenuItem menuItem6 = menuItem4;
        if (!menuItem6.IsEnabled)
        {
            menuItem6.Opacity = 0.5;
        }

        menuItem6.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.MoveDown(new DownloaderItemViewModel[1] { item });
        });
        MenuItem menuItem7 = new MenuItem();
        if (!item.IsPaused)
        {
            menuItem7.Header = Words.PauseText;
            menuItem7.Tag = "PAUSE";
            Image icon2 = AppHelper.GetIcon("pause");
            icon2.Width = 18.0;
            menuItem7.Icon = icon2;
        }
        else
        {
            menuItem7.Header = Words.ResumeText;
            menuItem7.Tag = "RESUME";
            menuItem7.Icon = AppHelper.GetIcon("resume");
        }

        menuItem7.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            if (item.IsPaused)
            {
                Sys.Downloader.ResumeItemsAsync(new DownloaderItemViewModel[1] { item });
            }
            else
            {
                Sys.Downloader.PauseItemsAsync(new DownloaderItemViewModel[1] { item });
            }
        });
        MenuItem menuItem8 = new MenuItem
        {
            Header = Words.Delete,
            Tag = "DELETE",
            Icon = AppHelper.GetIcon("delete")
        };
        if (!menuItem8.IsEnabled)
        {
            menuItem8.Opacity = 0.5;
        }

        menuItem8.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
        {
            Sys.Downloader.RemoveItemsAsync(new DownloaderItemViewModel[1] { item });
        });
        if (menuItem5.IsEnabled || menuItem6.IsEnabled)
        {
            if (menuItem5.IsEnabled)
            {
                contextMenu.Items.Add(menuItem5);
            }

            if (menuItem6.IsEnabled)
            {
                contextMenu.Items.Add(menuItem6);
            }

            contextMenu.Items.Add(new Separator());
        }

        if (!item.IsHistory)
        {
            contextMenu.Items.Add(menuItem7);
            contextMenu.Items.Add(new Separator());
        }

        contextMenu.Items.Add(menuItem8);
        if (Settings.Default.ExternalNzbGet)
        {
            contextMenu.Items.Add(new Separator());
            TextBlock icon3 = new TextBlock
            {
                Style = (Style)Application.Current.FindResource("FontAwesomeTabs"),
                Text = "\uf019"
            };
            MenuItem menuItem9 = new MenuItem
            {
                Header = Words.DownloadsAdvanced,
                Tag = "ADVANCED",
                IsEnabled = true,
                Icon = icon3
            };
            menuItem9.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
            {
                Sys.MainWindow.OpenPage(PageTypeEnum.AdvancedDownloads, "", saveParrentTab: true).Forget();
            });
            contextMenu.Items.Add(menuItem9);
        }
        else if (!item.IsHistory && !item.IsPostProcess)
        {
            contextMenu.Items.Add(new Separator());
            TextBlock icon4 = new TextBlock
            {
                Style = (Style)Application.Current.FindResource("FontAwesomeTabs"),
                Text = "\uf019"
            };
            MenuItem menuItem10 = new MenuItem
            {
                Header = Words.SetUnpackPassword,
                Tag = "PASSWORD",
                IsEnabled = true,
                Icon = icon4
            };
            menuItem10.AddHandler(MenuItem.ClickEvent, (RoutedEventHandler)delegate
            {
                SetUnpackPassword(item);
            });
            contextMenu.Items.Add(menuItem10);
        }

        return contextMenu;
    }

    private void SetUnpackPassword(DownloaderItemViewModel item)
    {
        ChangeUnpackPasswordWindow changeUnpackPasswordWindow = new ChangeUnpackPasswordWindow(item.UnpackPassword)
        {
            Owner = Sys.MainWindow
        };
        changeUnpackPasswordWindow.ShowDialog();
        if (changeUnpackPasswordWindow.BSuc)
        {
            item.UnpackPassword = changeUnpackPasswordWindow.Password;
        }
    }

    private void Downloads_ColumnsUpdated(object sender, DataGridColumnEventArgs args)
    {
        UpdateColumns().Forget();
    }

    private async Task UpdateColumns(bool force = false)
    {
        if (!force && Interlocked.CompareExchange(ref _lockColumnUpdated, 1, 0) != 0)
        {
            _columnShouldBeUpdatedOneMoreTime = true;
            return;
        }

        try
        {
            _columnShouldBeUpdatedOneMoreTime = false;
            for (int i = 0; i < Downloads.Columns.Count && i < DownloadTotals.Columns.Count; i++)
            {
                if (Downloads.Columns[i].DisplayIndex != -1)
                {
                    DownloadTotals.Columns[i].DisplayIndex = Downloads.Columns[i].DisplayIndex;
                }

                DownloadTotals.Columns[i].Width = Downloads.Columns[i].ActualWidth;
            }

            await Task.Delay(100);
            if (_columnShouldBeUpdatedOneMoreTime)
            {
                await UpdateColumns(force: true);
            }
        }
        finally
        {
            if (!force)
            {
                SaveCols();
            }

            Interlocked.Exchange(ref _lockColumnUpdated, 0);
        }
    }

    private void Play_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ((DownloaderItemViewModel)((FrameworkElement)e.Source).GetParent<Grid>().DataContext).SchedulePlayOrPause();
    }

    public void Dispose()
    {
        Sys.Downloader.ItemsOrderChanged -= DownloaderOnItemsOrderChanged;
    }

    protected override void OnInitialized(EventArgs e)
    {
        ShowAndHideColumns();
        RestoreColsSize();
        Task.Run(delegate
        {
            Thread.Sleep(1000);
            DependencyPropertyDescriptor dependencyPropertyDescriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
            foreach (DataGridColumn column in Downloads.Columns)
            {
                dependencyPropertyDescriptor.AddValueChanged(column, delegate
                {
                    UpdateColumns().Forget();
                });
            }
        });
        UpdateContainer();
        Downloads.PreviewMouseRightButtonUp += Downloads_OnPreviewMouseRightButtonUp;
        base.OnInitialized(e);
    }

    public void SaveCols()
    {
        string text = "";
        foreach (DataGridColumn column in Downloads.Columns)
        {
            text = ((column.Visibility != 0) ? (text + "00") : (text + $"{column.DisplayIndex + 1:D2}"));
        }

        Settings.Default.ColumnsDownloads = text;
        SaveColsWidth();
    }

    private void RestoreColsSize()
    {
        int count = Downloads.Columns.Count;
        string text = Settings.Default.ColumnsDownloadsSize;
        if (text.IsNullOrEmpty())
        {
            text = ColumnsWidthDefault;
            Settings.Default.ColumnsSize = ColumnsWidthDefault;
            Settings.Default.Save();
        }

        string[] array = text.Split(';');
        if (array.Length != count)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            DataGridColumn dataGridColumn = Downloads.Columns[i];
            DataGridColumn dataGridColumn2 = DownloadTotals.Columns[i];
            string[] array2 = array[i].Split(',');
            if (array2.Length == 2)
            {
                int num = Convert.ToInt32(array2[1]);
                if (i == 0 && num == 0)
                {
                    Settings.Default.ColumnsSize = ColumnsWidthDefault;
                    Settings.Default.Save();
                    RestoreColsSize();
                    break;
                }

                DataGridLengthUnitType type = (DataGridLengthUnitType)num;
                dataGridColumn.Width = new DataGridLength(Convert.ToDouble(array2[0]), type);
                dataGridColumn2.Width = new DataGridLength(Convert.ToDouble(array2[0]), type);
            }
        }
    }

    private void SaveColsWidth()
    {
        string text = "";
        foreach (DataGridColumn column in Downloads.Columns)
        {
            int num = (int)column.Width.UnitType;
            if (num == 0)
            {
                num = 1;
            }

            text += $"{column.Width.DisplayValue},{num};";
        }

        string text2 = text.Substring(0, text.Length - 1);
        if (!Settings.Default.ColumnsDownloadsSize.Equals(text2))
        {
            Settings.Default.ColumnsDownloadsSize = text2;
            Settings.Default.Save();
        }
    }

    private void ShowAndHideColumns()
    {
        int count = Downloads.Columns.Count * 2;
        string text = Settings.Default.ColumnsDownloads;
        List<char> source = text.Take(count).ToList();
        if (!source.Any((char n) => n > '0'))
        {
            text = "010203040506070000";
        }

        source = text.Take(count).ToList();
        for (int i = 0; i < source.Count / 2; i++)
        {
            string text2 = $"{text[i * 2]}{text[i * 2 + 1]}";
            bool flag = Settings.Default.ExternalNzbGet && ((string)Downloads.Columns[i].Header).Equals("Added");
            if (!text2.Equals("00") && !flag)
            {
                Downloads.Columns[i].Visibility = Visibility.Visible;
                Downloads.Columns[i].DisplayIndex = Convert.ToInt32(text2) - 1;
                DownloadTotals.Columns[i].Visibility = Visibility.Visible;
                DownloadTotals.Columns[i].DisplayIndex = Convert.ToInt32(text2) - 1;
            }
            else
            {
                Downloads.Columns[i].Visibility = Visibility.Hidden;
                DownloadTotals.Columns[i].Visibility = Visibility.Hidden;
            }
        }
    }

    public void UpdateContainer()
    {
        foreach (DataGridColumn column in Downloads.Columns)
        {
            if (AppHelper.TranslateColToId(column.Header.ToString()).Equals(Settings.Default.SortColumn))
            {
                column.SortDirection = ((!Settings.Default.SortDownloadsDirection.ToUpper().Equals("ASC")) ? ListSortDirection.Descending : ListSortDirection.Ascending);
                return;
            }
        }

        AppHelper.Error("Column " + Settings.Default.SortDownloadsColumn + " not found");
    }

    internal void LoadHeaderMenu()
    {
        HeaderMenu = new ContextMenu
        {
            FontFamily = base.FontFamily,
            FontSize = (double)Application.Current.Resources["ContextMenuFontSize"],
            FontStyle = base.FontStyle,
            Resources = AppHelper.GetMenuResourceDictionary
        };
        HeaderMenu.PreviewMouseDown += HeaderMenu_PreviewMouseDown;
        MenuItem[] array = new MenuItem[Downloads.Columns.Count];
        foreach (DataGridColumn column in Downloads.Columns)
        {
            MenuItem menuItem = new MenuItem
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

        foreach (MenuItem item in array.Where((MenuItem t) => t != null))
        {
            if (!Settings.Default.ExternalNzbGet || !((string)item.Header).Equals("Added"))
            {
                HeaderMenu.Items.Add(item);
            }
        }
    }

    internal void HeaderMenu_PreviewMouseDown(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        NewLateBinding.LateSetComplex(e.Source, null, "isChecked", new object[1] { Operators.NotObject(NewLateBinding.LateGet(e.Source, null, "isChecked", new object[0], null, null, null)) }, null, null, OptimisticSet: false, RValueBase: true);
        string right = NewLateBinding.LateGet(e.Source, null, "Header", new object[0], null, null, null).ToStringSafely().ToLower();
        for (int i = 0; i < Downloads.Columns.Count; i++)
        {
            DataGridColumn dataGridColumn = Downloads.Columns[i];
            DataGridColumn dataGridColumn2 = DownloadTotals.Columns[i];
            if (!Operators.ConditionalCompareObjectEqual(NewLateBinding.LateGet(dataGridColumn.Header, null, "ToLower", new object[0], null, null, null), right, TextCompare: false))
            {
                continue;
            }

            if (dataGridColumn.Visibility == Visibility.Visible)
            {
                foreach (DataGridColumn column in Downloads.Columns)
                {
                    if (column.Visibility == Visibility.Visible && column != dataGridColumn)
                    {
                        dataGridColumn.Visibility = Visibility.Hidden;
                        dataGridColumn2.Visibility = Visibility.Hidden;
                    }
                }
            }
            else
            {
                dataGridColumn.Visibility = Visibility.Visible;
                dataGridColumn2.Visibility = Visibility.Visible;
            }
        }
    }

    private void Downloads_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool flag = false;
        Downloads.ContextMenu = null;
        if (!(e.OriginalSource is ScrollViewer))
        {
            if (!(e.OriginalSource is Border border) || !(border.TemplatedParent is DataGridColumnHeader))
            {
                if (e.OriginalSource is TextBlock textBlock2)
                {
                    TextBlock textBlock = textBlock2;
                    if (Downloads.Columns.Any((DataGridColumn current) => current.Header.ToStringSafely().ToLower().Equals(textBlock.DataContext.ToStringSafely().ToLower())))
                    {
                        flag = true;
                    }
                }
            }
            else
            {
                flag = true;
            }
        }

        if (flag)
        {
            LoadHeaderMenu();
            base.ContextMenu = HeaderMenu;
            base.ContextMenu.IsOpen = true;
            base.ContextMenu = null;
            e.Handled = true;
        }
    }

    private void StatusLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Hyperlink hyperlink = (Hyperlink)e.Source;
        DownloaderItemViewModel vm = (DownloaderItemViewModel)hyperlink.DataContext;
        if (vm.RawStatus == DownloadStatus.WrongPassword)
        {
            DispatcherHelper.UIDispatcher.InvokeAsync(delegate
            {
                SetUnpackPassword(vm);
                vm.DownloadResume();
            });
        }
        else
        {
            Process.Start(e.Uri.AbsoluteUri);
        }
    }
}
