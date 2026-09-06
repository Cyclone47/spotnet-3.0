using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spotnet.Mvvm.Threading;
using Spotnet.Helpers;
using Spotnet.Notifications;
using Spotnet.Views;
using Spotnet.Properties;

namespace Spotnet.Controls;

public partial class NotificationBellTitleBarControl : UserControl
{
    public NotificationBellTitleBarControl()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            NotificationManager.Instance.Initialize();
            NotificationManager.Instance.UnreadCountChanged += OnUnreadCountChanged;
            // The tooltip is composed in code, so it does not follow the resource bindings.
            UserLanguageHelper.LanguageChanged += UpdateUi;
            UpdateUi();
        };

        Unloaded += (s, e) =>
        {
            NotificationManager.Instance.UnreadCountChanged -= OnUnreadCountChanged;
            UserLanguageHelper.LanguageChanged -= UpdateUi;
        };
    }

    private void OnUnreadCountChanged()
    {
        DispatcherHelper.CheckBeginInvokeOnUI(UpdateUi);
    }

    public void UpdateUi()
    {
        int unread = NotificationManager.Instance.UnreadCount;

        if (unread <= 0)
        {
            BellPath.Fill = new SolidColorBrush(Color.FromRgb(156, 163, 175)); // Gray #9CA3AF
            BellGlow.Opacity = 0;
            UnreadBadge.Visibility = Visibility.Collapsed;
            TooltipStatus.Text = Words.NcBellNoUnread;
        }
        else
        {
            BellPath.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Gold #F59E0B
            BellGlow.Opacity = 0.4;
            UnreadBadge.Visibility = Visibility.Visible;
            UnreadCountTextBlock.Text = unread > 99 ? "99+" : unread.ToString();
            TooltipStatus.Text = unread == 1 ? Words.NcBellUnreadOne : string.Format(Words.NcBellUnreadMany, unread);
        }
    }

    private void BellButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new NotificationCenterWindow();
            win.Owner = Window.GetWindow(this);
            win.ShowDialog();
            UpdateUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Words.NcBellOpenFailed + ex.Message, Words.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
