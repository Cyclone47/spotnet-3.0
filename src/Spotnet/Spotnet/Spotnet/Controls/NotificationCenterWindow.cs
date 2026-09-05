using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using Spotnet.Model;
using Spotnet.Notifications;
using Spotnet.Properties;
using Spotnet.Remote;
using Spotnet.ViewModel;

namespace Spotnet.Controls;

public partial class NotificationCenterWindow : MetroWindow
{
    private List<FilterDto> _availableFilters = new List<FilterDto>();
    private string _editingRuleId = null;

    public NotificationCenterWindow(int initialTabIndex = 0)
    {
        InitializeComponent();

        if (initialTabIndex > 0 && initialTabIndex < MainTabControl.Items.Count)
        {
            MainTabControl.SelectedIndex = initialTabIndex;
        }

        Loaded += (s, e) =>
        {
            if (initialTabIndex > 0 && initialTabIndex < MainTabControl.Items.Count)
            {
                MainTabControl.SelectedIndex = initialTabIndex;
            }

            NotificationManager.Instance.NotificationsUpdated += RefreshNotifications;
            NotificationManager.Instance.RulesUpdated += RefreshRules;

            LoadSettings();
            LoadFilters();
            RefreshNotifications();
            RefreshRules();
        };

        Unloaded += (s, e) =>
        {
            NotificationManager.Instance.NotificationsUpdated -= RefreshNotifications;
            NotificationManager.Instance.RulesUpdated -= RefreshRules;
        };
    }

    private void LoadSettings()
    {
        var cfg = NotificationManager.Instance.Config;
        WindowsNotificationsCheckBox.IsChecked = cfg.WindowsNotificationsEnabled;

        int currentSync = Math.Max(5, Settings.Default.DbAutoUpdateIntervalMin);
        foreach (ComboBoxItem item in AutoSyncIntervalComboBox.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == currentSync)
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private void LoadFilters()
    {
        try
        {
            _availableFilters = RemoteCatalogService.Instance.GetFilters();
            // Flatten hierarchical filters for dropdown
            var flatList = new List<FilterDto>();
            foreach (var f in _availableFilters)
            {
                flatList.Add(f);
                if (f.Children != null)
                {
                    foreach (var child in f.Children)
                    {
                        flatList.Add(new FilterDto
                        {
                            Id = child.Id,
                            Name = $"  ↳ {child.Name}",
                            Query = child.Query
                        });
                    }
                }
            }

            FilterComboBox.ItemsSource = flatList;
            if (flatList.Count > 0)
            {
                FilterComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn("Failed to load filters for notifications: {0}", ex.Message);
        }
    }

    private void RefreshNotifications()
    {
        var notifs = NotificationManager.Instance.Config.Notifications.OrderByDescending(n => n.CreatedAtUtc).ToList();
        NotificationsListBox.ItemsSource = null;
        NotificationsListBox.ItemsSource = notifs;

        int unread = notifs.Count(n => !n.IsRead);
        NotificationsTab.Header = unread > 0 ? $"🔔 Meldingen ({unread})" : "🔔 Meldingen";
        NotificationsHeaderTextBlock.Text = $"Recente Meldingen ({notifs.Count})";

        EmptyNotificationsPanel.Visibility = notifs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsListBox.Visibility = notifs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshRules()
    {
        var rules = NotificationManager.Instance.Config.Rules.OrderBy(r => r.Name).ToList();
        RulesListBox.ItemsSource = null;
        RulesListBox.ItemsSource = rules;
    }

    private void RuleTypeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (FilterOptionsPanel == null || KeywordOptionsPanel == null) return;
        bool isFilter = RuleTypeFilterRadio.IsChecked == true;
        FilterOptionsPanel.Visibility = isFilter ? Visibility.Visible : Visibility.Collapsed;
        KeywordOptionsPanel.Visibility = isFilter ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FilterIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomIntervalTextBox == null || CustomIntervalMinLabel == null) return;
        if (FilterIntervalComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int tag))
        {
            bool isCustom = tag == -1;
            CustomIntervalTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            CustomIntervalMinLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AutoSyncIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoSyncIntervalComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int tag))
        {
            NotificationManager.Instance.SetAutoSyncInterval(tag);
        }
    }

    private void WindowsNotificationsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        NotificationManager.Instance.Config.WindowsNotificationsEnabled = WindowsNotificationsCheckBox.IsChecked == true;
    }

    private void SaveRuleButton_Click(object sender, RoutedEventArgs e)
    {
        bool isFilter = RuleTypeFilterRadio.IsChecked == true;
        string ruleName = RuleNameTextBox.Text.Trim();

        NotificationRule rule;
        bool isEditing = !string.IsNullOrEmpty(_editingRuleId);

        if (isEditing)
        {
            rule = NotificationManager.Instance.Config.Rules.FirstOrDefault(r => r.Id == _editingRuleId);
            if (rule == null)
            {
                rule = new NotificationRule
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Enabled = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                isEditing = false;
            }
        }
        else
        {
            rule = new NotificationRule
            {
                Id = Guid.NewGuid().ToString("N"),
                Enabled = true,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        rule.Type = isFilter ? NotificationRuleType.Filter : NotificationRuleType.Keyword;

        if (isFilter)
        {
            if (FilterComboBox.SelectedItem is not FilterDto filter)
            {
                MessageBox.Show("Selecteer een filter uit de lijst.", "Geen filter geselecteerd", MessageBoxButton.OK, MessageBoxImage.Warning);
                FilterComboBox.Focus();
                return;
            }

            rule.FilterId = filter.Id;
            rule.FilterName = filter.Name.Replace("↳", "").Trim();
            rule.FilterQuery = filter.Query;
            rule.Keywords = null;
            rule.Category = null;

            if (string.IsNullOrWhiteSpace(ruleName))
            {
                ruleName = $"Nieuw in {rule.FilterName}";
            }

            // Get interval
            if (FilterIntervalComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int mins))
            {
                if (mins == -1)
                {
                    if (!int.TryParse(CustomIntervalTextBox.Text.Trim(), out int customMins) || customMins < 5)
                    {
                        MessageBox.Show("Vul een geldig interval in van minimaal 5 minuten.", "Ongeldig interval", MessageBoxButton.OK, MessageBoxImage.Warning);
                        CustomIntervalTextBox.Focus();
                        return;
                    }
                    rule.CheckIntervalMinutes = customMins;
                }
                else
                {
                    rule.CheckIntervalMinutes = mins;
                }
            }
        }
        else // Keyword
        {
            string keywords = KeywordsTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(keywords))
            {
                MessageBox.Show("Voer één of meerdere trefwoorden in (bijv. F1, Formule 1).", "Trefwoorden vereist", MessageBoxButton.OK, MessageBoxImage.Warning);
                KeywordsTextBox.Focus();
                return;
            }

            rule.Keywords = keywords;
            rule.FilterId = null;
            rule.FilterName = null;
            rule.FilterQuery = null;

            if (string.IsNullOrWhiteSpace(ruleName))
            {
                ruleName = $"Alert: {keywords}";
            }

            if (KeywordCategoryComboBox.SelectedItem is ComboBoxItem catItem && int.TryParse(catItem.Tag?.ToString(), out int catId))
            {
                rule.Category = catId > 0 ? catId : null;
            }

            if (KeywordDirectRadio.IsChecked == true)
            {
                rule.CheckIntervalMinutes = 0; // Direct on sync
            }
            else
            {
                rule.CheckIntervalMinutes = 15;
            }
        }

        rule.Name = ruleName;

        NotificationManager.Instance.AddOrUpdateRule(rule);

        ResetRuleForm();

        string msg = isEditing
            ? $"Melding '{rule.Name}' is succesvol bijgewerkt!"
            : $"Melding '{rule.Name}' is succesvol opgeslagen en actief!";
        string title = isEditing ? "Melding Gewijzigd" : "Melding Opgeslagen";

        MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshRules();
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var rule = NotificationManager.Instance.Config.Rules.FirstOrDefault(r => r.Id == id);
            if (rule == null) return;

            _editingRuleId = rule.Id;
            RuleFormHeaderTextBlock.Text = "Melding Aanpassen";
            SaveRuleButton.Content = "💾 Wijzigingen Opslaan";
            CancelEditRuleButton.Visibility = Visibility.Visible;

            RuleNameTextBox.Text = rule.Name ?? "";

            if (rule.Type == NotificationRuleType.Filter)
            {
                RuleTypeFilterRadio.IsChecked = true;
                RuleTypeRadio_Checked(this, null);

                if (FilterComboBox.ItemsSource is List<FilterDto> filters)
                {
                    var match = filters.FirstOrDefault(f => f.Id == rule.FilterId || f.Name.Trim().TrimStart('↳').Trim() == rule.FilterName);
                    if (match != null) FilterComboBox.SelectedItem = match;
                }

                int interval = rule.CheckIntervalMinutes;
                bool matched = false;
                foreach (ComboBoxItem item in FilterIntervalComboBox.Items)
                {
                    if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == interval)
                    {
                        item.IsSelected = true;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    foreach (ComboBoxItem item in FilterIntervalComboBox.Items)
                    {
                        if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == -1)
                        {
                            item.IsSelected = true;
                            CustomIntervalTextBox.Text = interval.ToString();
                            CustomIntervalTextBox.Visibility = Visibility.Visible;
                            CustomIntervalMinLabel.Visibility = Visibility.Visible;
                            break;
                        }
                    }
                }
            }
            else
            {
                RuleTypeKeywordRadio.IsChecked = true;
                RuleTypeRadio_Checked(this, null);

                KeywordsTextBox.Text = rule.Keywords ?? "";

                int cat = rule.Category ?? 0;
                foreach (ComboBoxItem item in KeywordCategoryComboBox.Items)
                {
                    if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == cat)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }

                if (rule.CheckIntervalMinutes == 0)
                {
                    KeywordDirectRadio.IsChecked = true;
                }
                else
                {
                    KeywordPeriodicRadio.IsChecked = true;
                }
            }

            RulesTabScrollViewer?.ScrollToTop();
        }
    }

    private void CancelEditRuleButton_Click(object sender, RoutedEventArgs e)
    {
        ResetRuleForm();
    }

    private void ResetRuleForm()
    {
        _editingRuleId = null;
        RuleFormHeaderTextBlock.Text = "Nieuwe Melding Aanmaken";
        SaveRuleButton.Content = "➕ Melding Opslaan";
        CancelEditRuleButton.Visibility = Visibility.Collapsed;

        RuleNameTextBox.Text = "";
        KeywordsTextBox.Text = "";
        CustomIntervalTextBox.Text = "45";
        RuleTypeFilterRadio.IsChecked = true;
        RuleTypeRadio_Checked(this, null);
        if (FilterComboBox.Items.Count > 0) FilterComboBox.SelectedIndex = 0;
        if (FilterIntervalComboBox.Items.Count > 0) FilterIntervalComboBox.SelectedIndex = 0;
        if (KeywordCategoryComboBox.Items.Count > 0) KeywordCategoryComboBox.SelectedIndex = 0;
        KeywordDirectRadio.IsChecked = true;
    }

    private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
    {
        NotificationManager.Instance.MarkAllAsRead();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Weet je zeker dat je alle meldingen wilt wissen?", "Meldingen wissen", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            NotificationManager.Instance.ClearAllNotifications();
        }
    }

    private void DeleteNotification_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            NotificationManager.Instance.DeleteNotification(id);
        }
    }

    private void OpenSpotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SpotSummaryItem spot)
        {
            try
            {
                var spotObj = new Spot
                {
                    Article = spot.Id,
                    MessageId = spot.MessageId,
                    Title = spot.Title,
                    Category = spot.Category
                };
                var spotRow = SpotRowViewModel.InitializeNewSpotRow(spotObj);

                Sys.MainWindow?.OpenSpot(spotRow);
                Sys.MainWindow?.Activate();

                // Close dialog and mark as read
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kon spot niet openen: " + ex.Message, "Fout", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ToggleRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string id)
        {
            NotificationManager.Instance.ToggleRule(id);
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (MessageBox.Show("Weet je zeker dat je deze melding wilt verwijderen?", "Melding verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (id == _editingRuleId)
                {
                    ResetRuleForm();
                }
                NotificationManager.Instance.DeleteRule(id);
            }
        }
    }

    private void TestRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var notif = NotificationManager.Instance.TestRuleNow(id);
            if (notif != null)
            {
                MessageBox.Show($"Test geslaagd! Er zijn {notif.SpotCount} spots gevonden en een melding is getoond.", "Test Resultaat", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshNotifications();
                MainTabControl.SelectedIndex = 0; // Switch to notifications tab
            }
            else
            {
                MessageBox.Show("Er zijn momenteel geen recente spots gevonden die matchen met deze regel.", "Geen resultaten", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
