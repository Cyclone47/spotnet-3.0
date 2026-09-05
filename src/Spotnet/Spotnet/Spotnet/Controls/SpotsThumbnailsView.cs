using System;
using System.CodeDom.Compiler;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using Spotnet.DataVirtualization;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.TaskSchedulers;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SpotsThumbnailsView : SpotsContainer
{
    private int _prevFirstVisibleItemIndex;
    private int _prevLastVisibleItemIndex;
    private object _itemToRestoreSavedScrollPosition;
    public override Selector Spots => MainListBox;

    public SpotsThumbnailsView(SpotsListViewModel vm, bool delaySpotsLoadForTheFirstTime = false) : base(vm, delaySpotsLoadForTheFirstTime)
    {
        ResetVisibleIndexes();
        InitializeComponent();
        MainListBox.SelectionChanged += base.OnSelectionChanged;
    }

    internal void VirtualListOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action.Equals(NotifyCollectionChangedAction.Add) && args.NewItems != null && args.NewItems.Count > 0)
        {
            VirtualListItem<ISpotRow> item = args.NewItems[0] as VirtualListItem<ISpotRow>;
            RefreshVisibilityFlags(item);
        }
    }

    private void HandleScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RefreshVisibilityFlags();
    }

    private void ResetVisibleIndexes()
    {
        _prevFirstVisibleItemIndex = -1;
        _prevLastVisibleItemIndex = -1;
    }

    private void RefreshVisibilityFlags(VirtualListItem<ISpotRow> item = null)
    {
        if (Spots.Items.Count == 0)
        {
            return;
        }

        VirtualizingWrapPanel virtualizingWrapPanel = Spots.FindChildByType<VirtualizingWrapPanel>();
        if (virtualizingWrapPanel == null)
        {
            return;
        }

        virtualizingWrapPanel.GetVerticalVisibleRange(out var firstVisibleItemIndex, out var lastVisibleItemIndex);
        if (_prevFirstVisibleItemIndex == firstVisibleItemIndex && _prevLastVisibleItemIndex == lastVisibleItemIndex)
        {
            return;
        }

        _prevFirstVisibleItemIndex = firstVisibleItemIndex;
        _prevLastVisibleItemIndex = lastVisibleItemIndex;
        Task.Run(delegate
        {
            try
            {
                if (item == null)
                {
                    ((ITaskSchedulerExtentions)SpotRowViewModel.GetTaskSchedulerForLoadFromNet()).CancelAllTasks();
                    UpdateItems(firstVisibleItemIndex, lastVisibleItemIndex);
                }
                else if (item.Index >= firstVisibleItemIndex && item.Index <= lastVisibleItemIndex)
                {
                    UpdateItemProperties(item);
                }
            }
            catch (Exception ex)
            {
                SpotsContainer.Log.Exception(ex);
            }
        });
    }

    private void UpdateItems(int startIndex, int endIndex)
    {
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        if (endIndex >= Spots.Items.Count)
        {
            endIndex = Spots.Items.Count - 1;
        }

        for (int i = startIndex; i <= endIndex; i++)
        {
            VirtualListItem<ISpotRow> virtualListItem = (VirtualListItem<ISpotRow>)Spots.Items[i];
            UpdateItemProperties(virtualListItem);
        }
    }

    public override void UpdateContainer()
    {
        ResetVisibleIndexes();
        RefreshVisibilityFlags();
    }

    public override void RestoreFocus()
    {
        try
        {
            if (Spots.SelectedItem == null || !Sys.MainWindow.IsSpotsTabSelectedAndVisible)
            {
                return;
            }

            DispatcherHelper.UIDispatcher.BeginInvoke(DispatcherPriority.Background, (ThreadStart)delegate
            {
                if (Spots.SelectedItem != null && Sys.MainWindow.IsSpotsTabSelectedAndVisible)
                {
                    Keyboard.Focus((ListBoxItem)Spots.ItemContainerGenerator.ContainerFromItem(Spots.SelectedItem));
                }
            });
        }
        catch (Exception ex)
        {
            SpotsContainer.Log.Exception(ex);
        }
    }

    private void UpdateItemProperties(VirtualListItem<ISpotRow> virtualListItem)
    {
        if (virtualListItem == null)
        {
            return;
        }

        bool flag = false;
        if (virtualListItem.Data == null)
        {
            DispatcherHelper.UIDispatcher.Invoke(delegate
            {
                flag = Mouse.OverrideCursor != Cursors.Wait;
                if (flag)
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                }
            }, DispatcherPriority.Input);
        }

        virtualListItem.Load();
        if (flag)
        {
            DispatcherHelper.UIDispatcher.Invoke(delegate
            {
                Mouse.OverrideCursor = null;
            }, DispatcherPriority.Input);
        }

        ISpotRow data = virtualListItem.Data;
        if (data != null && !data.SpotMessageId.IsNullOrEmpty())
        {
            if (!virtualListItem.IsProcessed)
            {
                UpdateItemStyle(data);
            }

            virtualListItem.IsProcessed = true;
            data.IsVisible = true;
            data.LoadSpotAsync(SpotsListTypeEnum.Thumbs);
        }
    }

    private void Spots_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && ObjectIsContainerItem((DependencyObject)e.OriginalSource))
        {
            e.Handled = true;
            Sys.MainWindow.OpenSpot(base.SelectedSpot);
        }
    }

    protected override bool ObjectIsContainerItem(DependencyObject obj)
    {
        if (!(obj is Border) && !(obj is TextBlock) && !(obj is Image))
        {
            return false;
        }

        ContentControl parent = obj.GetParent<ContentControl>();
        if (parent != null)
        {
            return !parent.Name.StartsWith("PART_");
        }

        return false;
    }

    private void Spots_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        OpenContextMenu(e);
    }

    private void ThumbTopGrid_OnMouseLeftButtonUp(object sender, MouseEventArgs e)
    {
        Popup popup = (Popup)((e.OriginalSource as FrameworkElement)?.GetChildByName("FloatingTip"));
        if (popup == null)
        {
            return;
        }

        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        if (e.GetPosition(null).Y < 260.0)
        {
            popup.Placement = PlacementMode.Mouse;
            popup.HorizontalOffset = 10.0;
            popup.VerticalOffset = 0.0;
        }
        else
        {
            popup.Placement = PlacementMode.Bottom;
            Point position = e.GetPosition(popup.PlacementTarget);
            popup.HorizontalOffset = position.X + 10.0;
            popup.VerticalOffset = position.Y;
        }

        popup.IsOpen = true;
    }

    private void ThumbTopGrid_OnMouseLeave(object sender, MouseEventArgs e)
    {
        Popup popup = (Popup)((e.OriginalSource as FrameworkElement)?.GetChildByName("FloatingTip"));
        if (popup != null && popup.IsOpen)
        {
            popup.IsOpen = false;
        }
    }

    private void OnMouseEnterItem(object sender, MouseEventArgs e)
    {
        if (((ListBoxItem)sender).Content is VirtualListItem<ISpotRow> virtualListItem && ((ListBoxItem)sender).GetChildByName("SpotsListToolbar")is SpotsListToolbar spotsListToolbar)
        {
            spotsListToolbar.InitializeWithViewModel((SpotRowViewModel)virtualListItem.Data);
        }
    }

    protected override void RefreshMainToolbar(VirtualListItem<ISpotRow> vli)
    {
        (((ListBoxItem)Spots.ItemContainerGenerator.ContainerFromItem(vli)).GetChildByName("SpotsListToolbar") as SpotsListToolbar)?.Refresh();
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
            MainListBox.SelectedItem = _itemToRestoreSavedScrollPosition;
            MainListBox.ScrollIntoView(_itemToRestoreSavedScrollPosition);
        }
    }

    private void OpenSpotButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Sys.MainWindow.OpenSpot(base.SelectedSpot);
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            ((MainToolbarViewModel)((SpotsListToolbar)((FrameworkElement)sender).GetAncestors<Grid>().First((Grid x) => x.Name.Equals("ThumbTopGrid")).GetChildByName("SpotsListToolbar")).DataContext).ScheduleDownloadAsync();
        }
        catch (Exception ex)
        {
            SpotsContainer.Log.Exception(ex, showToClient: true);
        }
    }
}