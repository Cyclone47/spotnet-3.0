using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SpotsListToolbar : UserControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private MainToolbarViewModel ViewModel => (MainToolbarViewModel)base.DataContext;
    private static SpotsListViewModel SpotsListVm => ((ViewModelLocator)Application.Current.Resources["Locator"]).SpotsList;

    public SpotsListToolbar()
    {
        InitializeComponent();
        base.DataContext = new MainToolbarViewModel();
    }

    public void InitializeWithViewModel(SpotRowViewModel row)
    {
        ViewModel.InitializeWithRow(row);
        if (row.IsMySpot && row.IsDeleteSafePeriodIsNotReached)
        {
            ComplainImg.Visibility = Visibility.Collapsed;
            DeleteImg.Visibility = Visibility.Visible;
        }
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        ToolBar toolBar = sender as ToolBar;
        if (toolBar?.Template.FindName("OverflowGrid", toolBar)is FrameworkElement frameworkElement)
        {
            frameworkElement.Visibility = Visibility.Collapsed;
        }

        if (toolBar?.Template.FindName("MainPanelBorder", toolBar)is FrameworkElement frameworkElement2)
        {
            frameworkElement2.Margin = new Thickness(0.0);
        }
    }

    private void Complain_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.Row != null)
        {
            Sys.MainWindow.AddComplainReportToTheSpot(ViewModel.Row);
        }
    }

    private void Delete_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.Row != null)
        {
            Sys.MainWindow.DeleteArticle(ViewModel.Row.SpotMessageId, ViewModel.Row.Titel);
        }
    }

    private void WhiteList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && !(ViewModel.OpacityWhiteList < 1.0))
        {
            SpotRowViewModel row = ViewModel.Row;
            if (BlackAndWhite.WhiteList().Contains(row.Modulus))
            {
                BlackAndWhite.RemoveWhite(row.Modulus);
            }
            else
            {
                BlackAndWhite.AddWhite(AppHelper.StripNonAlphaNumericCharacters(row.Afzender), row.Modulus);
            }

            SpotsListVm.SpotsContainer.RefreshAllItemsStyle();
        }
    }

    public void Refresh()
    {
        ViewModel.RaisePropertiesChanged();
    }

    private void BlackList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && !(ViewModel.OpacityBlackList < 1.0))
        {
            SpotRowViewModel row = ViewModel.Row;
            if (BlackAndWhite.BlackList().Contains(row.Modulus))
            {
                BlackAndWhite.RemoveBlack(row.Modulus);
                AppHelper.ShowPopupMessage(Words.BlackListYouWillReceiveFromSender, inTheCenter: false, TimeSpan.FromSeconds(3.0));
            }
            else
            {
                BlackAndWhite.AddBlack(AppHelper.StripNonAlphaNumericCharacters(row.Afzender), row.Modulus);
                AppHelper.ShowPopupMessage(Words.BlackListYouWillNotReceiveFromSender, inTheCenter: false, TimeSpan.FromSeconds(3.0));
            }

            SpotsListVm.SpotsContainer.RefreshAllItemsStyle();
        }
    }

    private void DownloadNzb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null || ViewModel.OpacityDownloadNzb < 1.0)
        {
            return;
        }

        try
        {
            ViewModel.ScheduleDownloadAsync();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void Play_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel == null || ViewModel.OpacityDownloadNzb < 1.0)
        {
            return;
        }

        try
        {
            Sys.DownloadsPlayer.PlayerFullStop();
            ViewModel.ScheduleDownloadAsync(showTooltip: false);
            Sys.MainWindow.TabControl1.SelectedIndex = 1;
            ViewModel.SchedulePlayAsync();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private void Favorites_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && !(ViewModel.OpacityFavorites < 1.0))
        {
            SpotRowViewModel row = ViewModel.Row;
            if (row.IsInFavorites)
            {
                Favorites.Remove(row.SpotMessageId);
                AppHelper.ShowPopupMessage(Words.FavoritesRemoved + "\r\n" + row.Titel, inTheCenter: false, TimeSpan.FromSeconds(3.0));
                row.IsInFavorites = false;
            }
            else
            {
                Favorites.Add(row.SpotMessageId);
                AppHelper.ShowPopupMessage(Words.FavoritesAdded + "\r\n" + row.Titel, inTheCenter: false, TimeSpan.FromSeconds(3.0));
                row.IsInFavorites = true;
            }

            SpotsListVm.SpotsContainer.UpdateItemStyle(row);
        }
    }
}