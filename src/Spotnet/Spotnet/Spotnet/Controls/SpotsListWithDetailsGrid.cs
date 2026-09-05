using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using MahApps.Metro.Controls;
using Microsoft.VisualBasic;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.TaskSchedulers;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SpotsListWithDetailsGrid : SpotsContainer
{
    private string _lastCol;
    private bool _isColumnWidthChangedEventAssignedAlready;
    private readonly System.Timers.Timer _timerToSaveColWidth;
    private object _itemToRestoreSavedScrollPosition;
    public override Selector Spots => MainDataGrid;

    public SpotsListWithDetailsGrid(SpotsListViewModel vm, bool delaySpotsLoadForTheFirstTime = false) : base(vm, delaySpotsLoadForTheFirstTime)
    {
        _lastCol = Settings.Default.SortColumn;
        InitializeComponent();
        MainDataGrid.SelectionChanged += base.OnSelectionChanged;
        _timerToSaveColWidth = new System.Timers.Timer();
        _timerToSaveColWidth.AutoReset = false;
        _timerToSaveColWidth.Elapsed += SaveColsWidth;
        _timerToSaveColWidth.Interval = 1000.0;
    }

    public override void UpdateContainer()
    {
        foreach (DataGridColumn column in ((DataGrid)Spots).Columns)
        {
            if (AppHelper.TranslateColToId(column.Header.ToString()).Equals(Settings.Default.SortColumn))
            {
                column.SortDirection = ((!Settings.Default.SortDirection.ToUpper().Equals("ASC")) ? ListSortDirection.Descending : ListSortDirection.Ascending);
                return;
            }
        }

        AppHelper.Error("Column " + Settings.Default.SortColumn + " not found");
    }

    public override void RestoreFocus()
    {
        try
        {
            if (MainDataGrid.SelectedItem == null || !Sys.MainWindow.IsSpotsTabSelectedAndVisible || !MainDataGrid.SelectedCells.Any())
            {
                return;
            }

            DispatcherHelper.UIDispatcher.BeginInvoke(DispatcherPriority.Background, (ThreadStart)delegate
            {
                if (MainDataGrid.SelectedItem != null && MainDataGrid.SelectedCells.Any() && Sys.MainWindow.IsSpotsTabSelectedAndVisible)
                {
                    Keyboard.Focus(GetDataGridCell(MainDataGrid.SelectedCells[0]));
                }
            });
        }
        catch (Exception ex)
        {
            SpotsContainer.Log.Exception(ex);
        }
    }

    private DataGridCell GetDataGridCell(DataGridCellInfo cellInfo)
    {
        FrameworkElement cellContent = cellInfo.Column.GetCellContent(cellInfo.Item);
        if (cellContent == null)
        {
            return null;
        }

        return (DataGridCell)cellContent.Parent;
    }

    public override void SaveCols()
    {
        string text = "";
        foreach (DataGridColumn column in ((DataGrid)Spots).Columns)
        {
            text = ((column.Visibility != 0) ? (text + "00") : (text + $"{column.DisplayIndex + 1:D2}"));
        }

        Settings.Default.Columns = text;
        SaveColsWidth();
    }

    private void SaveColsWidth(object state = null, ElapsedEventArgs elapsedEventArgs = null)
    {
        DispatcherHelper.RunAsync(delegate
        {
            string text = "";
            foreach (DataGridColumn column in ((DataGrid)Spots).Columns)
            {
                int num = (int)column.Width.UnitType;
                if (num == 0)
                {
                    num = 1;
                }

                text += $"{column.Width.DisplayValue},{num};";
            }

            string text2 = text.Substring(0, text.Length - 1);
            if (!Settings.Default.ColumnsSize.Equals(text2))
            {
                Settings.Default.ColumnsSize = text2;
                Settings.Default.Save();
            }
        });
    }

    protected override void OnInitialized(EventArgs e)
    {
        ShowAndHideColumns();
        RestoreColumnsSize();
        ColumnWidthChangedEventAssignOneTime();
        UpdateContainer();
        base.OnInitialized(e);
    }

    private void ColumnWidthChangedEventAssignOneTime()
    {
        if (_isColumnWidthChangedEventAssignedAlready)
        {
            return;
        }

        _isColumnWidthChangedEventAssignedAlready = true;
        PropertyDescriptor propertyDescriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
        foreach (DataGridColumn column in ((DataGrid)Spots).Columns)
        {
            propertyDescriptor.AddValueChanged(column, ColumnWidthPropertyChanged);
        }
    }

    private void RestoreColumnsSize()
    {
        int count = ((DataGrid)Spots).Columns.Count;
        string[] array = Settings.Default.ColumnsSize.Split(';');
        if (array.Length != count)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            DataGridColumn dataGridColumn = ((DataGrid)Spots).Columns[i];
            string[] array2 = array[i].Split(',');
            if (array2.Length == 2)
            {
                DataGridLengthUnitType type = (DataGridLengthUnitType)Convert.ToInt32(array2[1]);
                dataGridColumn.Width = new DataGridLength(Convert.ToDouble(array2[0]), type);
            }
        }
    }

    private void ShowAndHideColumns()
    {
        int num = ((DataGrid)Spots).Columns.Count * 2;
        string text = Settings.Default.Columns;
        if (text.Length < 16)
        {
            text = text.Aggregate("", (string current, char c) => current + "0" + c);
        }

        List<char> source = text.Take(num).ToList();
        if (!source.Any((char n) => n > '0'))
        {
            text = "01020304000005080000";
        }

        text = Strings.Left(text + '0'.Repeat(num), num);
        source = text.Take(num).ToList();
        for (int i = 0; i < source.Count / 2; i++)
        {
            string text2 = $"{text[i * 2]}{text[i * 2 + 1]}";
            if (!text2.Equals("00"))
            {
                ((DataGrid)Spots).Columns[i].Visibility = Visibility.Visible;
                ((DataGrid)Spots).Columns[i].DisplayIndex = Convert.ToInt32(text2) - 1;
            }
            else
            {
                ((DataGrid)Spots).Columns[i].Visibility = Visibility.Hidden;
            }
        }
    }

    private void Spots_ColumnReordered(object sender, DataGridColumnEventArgs e)
    {
        SaveCols();
    }

    protected override bool ObjectIsContainerItem(DependencyObject obj)
    {
        if (((FrameworkElement)obj).Parent is FrameworkElement frameworkElement)
        {
            return frameworkElement.DataContext is VirtualListItem<ISpotRow>;
        }

        return false;
    }

    private void Spots_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        VirtualListItem<ISpotRow> virtualListItem;
        try
        {
            virtualListItem = (VirtualListItem<ISpotRow>)e.Row.Item;
            if (virtualListItem == null || virtualListItem.IsProcessed)
            {
                return;
            }

            virtualListItem.Load();
        }
        catch (Exception ex)
        {
            SpotsContainer.Log.Exception(ex, showToClient: true);
            return;
        }

        try
        {
            UpdateItemStyle(virtualListItem.Data);
            virtualListItem.IsProcessed = true;
        }
        catch (Exception ex2)
        {
            SpotsContainer.Log.Exception(ex2);
        }
    }

    private void Spots_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && ((e.OriginalSource is TextBlock && ((FrameworkElement)e.OriginalSource).Parent is DataGridCell) || ((FrameworkElement)e.OriginalSource).GetParent<ContentControl>() is DataGridCell || ((FrameworkElement)e.OriginalSource).GetParent<ContentControl>().Name.Equals("SpotContent")))
        {
            e.Handled = true;
            Sys.MainWindow.OpenSpot(base.SelectedSpot);
        }
    }

    private void Spots_Sorting(object sender, DataGridSortingEventArgs e)
    {
        DataGridColumn column = e.Column;
        Sys.MainWindow.SpotProvider.SortOrder = ((!_lastCol.Equals(AppHelper.TranslateColToId(column.Header.ToStringSafely()))) ? "DESC" : ((!Sys.MainWindow.SpotProvider.SortOrder.EqualsIgnoreCase("DESC")) ? "DESC" : "ASC"));
        _lastCol = AppHelper.TranslateColToId(column.Header.ToStringSafely());
        Settings.Default.SortColumn = _lastCol;
        Settings.Default.Save();
        Sys.LeftPanel.ReloadFilter(bResetCount: false, Words.Sorting);
        column.SortDirection = ((!Sys.MainWindow.SpotProvider.SortOrder.ToUpper().Trim().Equals("ASC")) ? ListSortDirection.Descending : ListSortDirection.Ascending);
        e.Handled = true;
    }

    private void RowDetails_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if ((bool)args.NewValue)
        {
            StartSpotLoad((ContentControl)sender);
        }
    }

    private void RowDetails_Loaded(object sender, RoutedEventArgs e)
    {
        StartSpotLoad((ContentControl)sender);
    }

    private void StartSpotLoad(ContentControl cc)
    {
        ((ITaskSchedulerExtentions)SpotRowViewModel.GetTaskSchedulerForLoadFromNet()).CancelAllTasks();
        ((VirtualListItem<ISpotRow>)cc.Content).Data.LoadSpotAsync(SpotsListTypeEnum.WithDetails);
    }

    private void MainDataGrid_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool flag = false;
        e.Handled = true;
        Spots.ContextMenu = null;
        if (!(e.OriginalSource is ScrollViewer))
        {
            if (!(e.OriginalSource is Border border) || !(border.TemplatedParent is DataGridColumnHeader))
            {
                if (e.OriginalSource is TextBlock textBlock2)
                {
                    TextBlock textBlock = textBlock2;
                    if (((DataGrid)Spots).Columns.Any((DataGridColumn current) => current.Header.ToStringSafely().ToLower().Equals(textBlock.DataContext.ToStringSafely().ToLower())))
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
            Sys.MainWindow.LoadHeaderMenu();
            base.ContextMenu = Sys.MainWindow.HeaderMenu;
            base.ContextMenu.IsOpen = true;
            base.ContextMenu = null;
            return;
        }

        DataGridRow dataGridRow = (e.OriginalSource as DependencyObject).TryFindParent<DataGridRow>();
        if (dataGridRow != null)
        {
            OpenContextMenu(e, (SpotRowViewModel)((VirtualListItem<ISpotRow>)dataGridRow.DataContext).Data);
            dataGridRow.IsSelected = true;
        }
    }

    private void OnMouseEnterItem(object sender, MouseEventArgs e)
    {
        if (((DataGridRow)sender).Item is VirtualListItem<ISpotRow> virtualListItem && ((DataGridRow)sender).GetChildByName("SpotsListToolbar")is SpotsListToolbar spotsListToolbar)
        {
            spotsListToolbar.InitializeWithViewModel((SpotRowViewModel)virtualListItem.Data);
        }
    }

    private void ColumnWidthPropertyChanged(object sender, EventArgs e)
    {
        _timerToSaveColWidth.Stop();
        _timerToSaveColWidth.Start();
    }

    protected override void RefreshMainToolbar(VirtualListItem<ISpotRow> vli)
    {
        if (((DataGridRow)Spots.ItemContainerGenerator.ContainerFromItem(vli)).GetChildByName("SpotsListToolbar")is SpotsListToolbar spotsListToolbar)
        {
            spotsListToolbar.Refresh();
        }
    }

    private void WarningTip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Sys.LeftPanel.ReloadFilter(bResetCount: true);
    }

    public override void SaveScrollPosition()
    {
        _itemToRestoreSavedScrollPosition = Spots.SelectedItem;
    }

    public override void RestoreScrollPosition()
    {
        if (_itemToRestoreSavedScrollPosition != null)
        {
            MainDataGrid.SelectedItem = _itemToRestoreSavedScrollPosition;
            MainDataGrid.ScrollIntoView(_itemToRestoreSavedScrollPosition);
        }
    }
}