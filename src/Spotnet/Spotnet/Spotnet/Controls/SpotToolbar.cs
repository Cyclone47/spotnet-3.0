using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Markup;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Utilities;
using Spotnet.ViewModel;

namespace Spotnet.Controls;
public partial class SpotToolbar : System.Windows.Controls.UserControl, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private MainToolbarViewModel ViewModel => (MainToolbarViewModel)base.DataContext;

    public SpotToolbar()
    {
        InitializeComponent();
        base.DataContext = new MainToolbarViewModel();
    }

    public void InitializeWithViewModel(SpotEx spotEx)
    {
        SpotRowChild spot = default(SpotRowChild);
        spot.Title = spotEx.Title;
        spot.Poster = spotEx.Poster;
        spot.Modulus = spotEx.User.Modulus;
        spot.MessageId = spotEx.MessageId;
        spot.NumberOfSpamReports = spotEx.NumberOfSpamReports;
        spot.Stamp = spotEx.Stamp;
        spot.Cat = spotEx.Category;
        SpotRowViewModel spotRowViewModel = SpotRowViewModel.InitializeNewSpotRow(spot);
        spotRowViewModel.PosterIdent = spotEx.PosterIdent;
        ViewModel.InitializeWithRow(spotRowViewModel);
        if (spotRowViewModel.IsMySpot && spotRowViewModel.IsDeleteSafePeriodIsNotReached)
        {
            ComplainImg.Visibility = Visibility.Collapsed;
            DeleteImg.Visibility = Visibility.Visible;
        }
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        System.Windows.Controls.ToolBar toolBar = sender as System.Windows.Controls.ToolBar;
        if (toolBar != null && toolBar.Template.FindName("OverflowGrid", toolBar)is FrameworkElement frameworkElement)
        {
            frameworkElement.Visibility = Visibility.Collapsed;
        }

        if (toolBar != null && toolBar.Template.FindName("MainPanelBorder", toolBar)is FrameworkElement frameworkElement2)
        {
            frameworkElement2.Margin = new Thickness(0.0);
        }
    }

    private void Complain_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && ViewModel.Row != null)
        {
            Sys.MainWindow.AddComplainReportToTheSpot(ViewModel.Row);
        }
    }

    private void Delete_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && ViewModel.Row != null)
        {
            Sys.MainWindow.DeleteArticle(ViewModel.Row.SpotMessageId, ViewModel.Row.Titel);
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

    private void CopyTitle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(ViewModel.Row.Titel, copy: false, 10, 100);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

            AppHelper.ShowPopupMessage(Words.TitleCopiedToClipboard, inTheCenter: false, TimeSpan.FromSeconds(3.0));
        }
    }

    private void CopyMessageId_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(SpotParser.GenerateSpotUrl(ViewModel.Row.SpotMessageId, ViewModel.Row.Titel), copy: false, 10, 100);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

            AppHelper.ShowPopupMessage(Words.SpotLinkCopiedToClipboard, inTheCenter: false, TimeSpan.FromSeconds(3.0));
        }
    }

    private void CopyImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && ViewModel.Image != null)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetImage(ViewModel.Image);
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }

            AppHelper.ShowPopupMessage(Words.ImageCopiedToClipboard, inTheCenter: false, TimeSpan.FromSeconds(3.0));
        }
    }

    internal void SetImageAsync(System.Drawing.Image image)
    {
        DispatcherHelper.CheckBeginInvokeOnUI(delegate
        {
            if (ViewModel != null)
            {
                ViewModel.Image = image;
            }
        });
    }

    public void Dispose()
    {
        ViewModel.Dispose();
    }

    private void Favorites_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel != null && !(ViewModel.OpacityFavorites < 1.0))
        {
            SpotRowViewModel row = ViewModel.Row;
            if (Favorites.ContainsMessageId(row.SpotMessageId))
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

            ViewModel.RaisePropertiesChanged();
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
}