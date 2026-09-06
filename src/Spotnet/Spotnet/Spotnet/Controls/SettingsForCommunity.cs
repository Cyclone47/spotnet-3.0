using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NLog;
using Spotnet.Community;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Controls;

/// <summary>
/// Settings pane for everything that binds this client to a particular Spotnet community.
/// Edits are made against a working copy and only reach <see cref="CommunityConfig.Current"/>
/// when the settings window is saved, so Cancel really cancels.
/// </summary>
public partial class SettingsForCommunity : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private CommunityConfig _working;

    public SettingsForCommunity()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    private void OnInitialized(object sender, EventArgs e)
    {
        _working = CloneCurrent();
        LoadToUi(_working);
        UpdateListStatus();
    }

    private static CommunityConfig CloneCurrent()
    {
        try
        {
            return CommunityConfig.Deserialize(CommunityConfig.Current.Serialize()) ?? new CommunityConfig();
        }
        catch (Exception ex)
        {
            Log.Exception(ex);
            return new CommunityConfig();
        }
    }

    private void LoadToUi(CommunityConfig c)
    {
        CommunityNameTextBlock.Text = c.Name.IsNullOrWhiteSpace() ? "(naamloos)" : c.Name;

        SpotsGroupTextBox.Text = c.Newsgroups.Spots;
        CommentsGroupTextBox.Text = c.Newsgroups.Comments;
        ReportsGroupTextBox.Text = c.Newsgroups.Reports;
        NzbGroupTextBox.Text = c.Newsgroups.Nzb;

        ModerationEnabledCheckBox.IsChecked = c.Moderation.Enabled;
        IntervalTextBox.Text = c.Moderation.UpdateIntervalMinutes.ToString();

        WhitelistUrlTextBox.Text = c.Moderation.WhitelistUrl;
        BlacklistUrlTextBox.Text = c.Moderation.BlacklistUrl;
        SpotWhitelistUrlTextBox.Text = c.Moderation.SpotWhitelistUrl;
        SpotBlacklistUrlTextBox.Text = c.Moderation.SpotBlacklistUrl;
        ModeratorKeysUrlTextBox.Text = c.Moderation.ModeratorKeysUrl;
        RequireSignedListsCheckBox.IsChecked = c.Moderation.RequireSignedLists;
        SignatureKeyTextBox.Text = c.Moderation.SignaturePublicKeyXml;

        UseClassicListsCheckBox.IsChecked = c.Moderation.UseClassicLists;
        ClassicWhitelistUrlTextBox.Text = c.Moderation.ClassicWhitelistUrl;
        ClassicBlacklistUrlTextBox.Text = c.Moderation.ClassicBlacklistUrl;

        ResponseSiteUrlTextBox.Text = c.Services.ResponseSiteUrl;
        LogUploadUrlTextBox.Text = c.Services.LogUploadUrl;
        UpgradeFailuresUrlTextBox.Text = c.Services.UpgradeFailuresUrl;
        PromoFolderUrlTextBox.Text = c.Services.PromoFolderUrl;


        ApplyModerationEnabledState();
    }

    /// <summary>Reads the pane back into <paramref name="c"/>.</summary>
    private void CollectFromUi(CommunityConfig c)
    {
        c.Newsgroups.Spots = SpotsGroupTextBox.Text.Trim();
        c.Newsgroups.Comments = CommentsGroupTextBox.Text.Trim();
        c.Newsgroups.Reports = ReportsGroupTextBox.Text.Trim();
        c.Newsgroups.Nzb = NzbGroupTextBox.Text.Trim();

        c.Moderation.Enabled = ModerationEnabledCheckBox.IsChecked.GetValueOrDefault();
        c.Moderation.UpdateIntervalMinutes = ParseInterval(IntervalTextBox.Text, c.Moderation.UpdateIntervalMinutes);

        c.Moderation.UseClassicLists = UseClassicListsCheckBox.IsChecked.GetValueOrDefault();
        c.Moderation.ClassicWhitelistUrl = ClassicWhitelistUrlTextBox.Text.Trim();
        c.Moderation.ClassicBlacklistUrl = ClassicBlacklistUrlTextBox.Text.Trim();

        c.Moderation.WhitelistUrl = WhitelistUrlTextBox.Text.Trim();
        c.Moderation.BlacklistUrl = BlacklistUrlTextBox.Text.Trim();
        c.Moderation.SpotWhitelistUrl = SpotWhitelistUrlTextBox.Text.Trim();
        c.Moderation.SpotBlacklistUrl = SpotBlacklistUrlTextBox.Text.Trim();
        c.Moderation.ModeratorKeysUrl = ModeratorKeysUrlTextBox.Text.Trim();
        c.Moderation.RequireSignedLists = RequireSignedListsCheckBox.IsChecked.GetValueOrDefault();
        c.Moderation.SignaturePublicKeyXml = SignatureKeyTextBox.Text.Trim();

        c.Services.ResponseSiteUrl = ResponseSiteUrlTextBox.Text.Trim();
        c.Services.LogUploadUrl = LogUploadUrlTextBox.Text.Trim();
        c.Services.UpgradeFailuresUrl = UpgradeFailuresUrlTextBox.Text.Trim();
        c.Services.PromoFolderUrl = PromoFolderUrlTextBox.Text.Trim();

    }

    private static int ParseInterval(string text, int fallback)
    {
        return int.TryParse(text?.Trim(), out int minutes) ? minutes : fallback;
    }

    public bool VerifyFields()
    {
        try
        {
            CollectFromUi(_working);
            IList<string> errors = _working.Validate();
            if (!int.TryParse(IntervalTextBox.Text?.Trim(), out int _))
            {
                errors.Add("Het bijwerkinterval moet een heel getal zijn.");
            }

            ValidationTextBlock.Text = string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }

    public bool Save()
    {
        try
        {
            if (!VerifyFields())
            {
                return false;
            }

            bool moderationWasEnabled = CommunityConfig.Current.Moderation.Enabled;

            if (!_working.Save())
            {
                return false;
            }

            CommunityConfig.Replace(_working);
            _working.ApplyNewsgroupsToSettings();
            BlackAndWhite.RescheduleExternalListUpdates();

            // Turning moderation back on should not wait for the next tick.
            if (_working.Moderation.Enabled && !moderationWasEnabled)
            {
                BlackAndWhite.UpdateExternalListsAsync();
            }

            // Editing is done against a copy, so hand the pane a fresh one to work on.
            _working = CloneCurrent();
            UpdateListStatus();
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }

    private void ApplyModerationEnabledState()
    {
        bool on = ModerationEnabledCheckBox.IsChecked.GetValueOrDefault();
        IntervalTextBox.IsEnabled = on;
        RefreshListsButton.IsEnabled = on;
        UseClassicListsCheckBox.IsEnabled = on;
        ApplyClassicListsEnabledState();
    }

    private void ApplyClassicListsEnabledState()
    {
        bool on = ModerationEnabledCheckBox.IsChecked.GetValueOrDefault() &&
                  UseClassicListsCheckBox.IsChecked.GetValueOrDefault();
        ClassicListsPanel.IsEnabled = on;
    }

    private void ModerationEnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ApplyModerationEnabledState();
    }

    private void UseClassicListsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ApplyClassicListsEnabledState();
    }

    private void RefreshListsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BlackAndWhite.UpdateExternalListsAsync();
            ListStatusTextBlock.Text = Words.CommRefreshStarted;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    /// <summary>Reports what the client currently holds, so the pane says something true even offline.</summary>
    private void UpdateListStatus()
    {
        try
        {
            if (!CommunityConfig.Current.Moderation.Enabled)
            {
                ListStatusTextBlock.Text = "Moderatielijsten staan uit.";
                return;
            }

            string newest = NewestListTimestamp();
            ListStatusTextBlock.Text = string.Format(
                Words.CommListSummary,
                BlackAndWhite.WhiteList().Count,
                BlackAndWhite.BlackList().Count,
                newest == null ? "" : " · laatst bijgewerkt " + newest);
        }
        catch (Exception ex)
        {
            Log.Debug("Kon lijststatus niet bepalen: {0}", ex.Message);
            ListStatusTextBlock.Text = "";
        }
    }

    private static string NewestListTimestamp()
    {
        string[] files =
        {
            "whitelist.srv.csv", "blacklist.srv.csv", "spot_whitelist.srv.csv", "spot_blacklist.srv.csv",
            "whitelist.classic.srv.xml", "blacklist.classic.srv.xml"
        };

        DateTime? newest = null;
        foreach (string name in files)
        {
            string path = Path.Combine(AppHelper.SettingsFolder, name);
            if (!File.Exists(path))
            {
                continue;
            }

            DateTime stamp = File.GetLastWriteTime(path);
            if (newest == null || stamp > newest.Value)
            {
                newest = stamp;
            }
        }

        return newest?.ToString("dd-MM-yyyy HH:mm");
    }

    private void ToggleRawButton_Click(object sender, RoutedEventArgs e)
    {
        if (RawPanel.Visibility == Visibility.Visible)
        {
            RawPanel.Visibility = Visibility.Collapsed;
            ToggleRawButton.Content = Words.CommShowRaw;
            return;
        }

        CollectFromUi(_working);
        RawJsonTextBox.Text = _working.Serialize();
        RawStatusTextBlock.Text = "";
        RawPanel.Visibility = Visibility.Visible;
        ToggleRawButton.Content = Words.CommHideRaw;
    }

    private void ApplyRawButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommunityConfig parsed = CommunityConfig.Deserialize(RawJsonTextBox.Text);
            if (parsed == null)
            {
                RawStatusTextBlock.Text = "Leeg of onleesbaar.";
                return;
            }

            IList<string> errors = parsed.Validate();
            if (errors.Count > 0)
            {
                RawStatusTextBlock.Text = string.Join(Environment.NewLine, errors);
                return;
            }

            _working = parsed;
            LoadToUi(_working);
            RawStatusTextBlock.Text = Words.CommRawApplied;
            ValidationTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            RawStatusTextBlock.Text = "Ongeldige JSON: " + ex.Message;
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                Words.CommResetQuestion,
                Words.CommResetDefaults, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _working = new CommunityConfig();
        LoadToUi(_working);
        ValidationTextBlock.Text = "";
        if (RawPanel.Visibility == Visibility.Visible)
        {
            RawJsonTextBox.Text = _working.Serialize();
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CollectFromUi(_working);
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Community-profiel exporteren",
                Filter = "Spotnet community-profiel (*.json)|*.json|Alle bestanden (*.*)|*.*",
                FileName = SuggestProfileFileName(_working.Name),
                AddExtension = true,
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            File.WriteAllText(dialog.FileName, _working.Serialize());
            ValidationTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
        }
    }

    private static string SuggestProfileFileName(string name)
    {
        string cleaned = new string((name ?? "community")
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray()).Trim('-');
        return (cleaned.IsNullOrWhiteSpace() ? "community" : cleaned.ToLowerInvariant()) + "-profiel.json";
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Community-profiel importeren",
                Filter = "Spotnet community-profiel (*.json)|*.json|Alle bestanden (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            CommunityConfig imported = CommunityConfig.Deserialize(File.ReadAllText(dialog.FileName));
            if (imported == null)
            {
                ValidationTextBlock.Text = Words.CommImportNotAProfile;
                return;
            }

            IList<string> errors = imported.Validate();
            if (errors.Count > 0)
            {
                ValidationTextBlock.Text = Words.CommImportInvalid + Environment.NewLine +
                                           string.Join(Environment.NewLine, errors);
                return;
            }

            _working = imported;
            LoadToUi(_working);
            ValidationTextBlock.Text = Words.CommImportLoaded;
            if (RawPanel.Visibility == Visibility.Visible)
            {
                RawJsonTextBox.Text = _working.Serialize();
            }
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            ValidationTextBlock.Text = Words.CommImportFailed + ex.Message;
        }
    }
}
