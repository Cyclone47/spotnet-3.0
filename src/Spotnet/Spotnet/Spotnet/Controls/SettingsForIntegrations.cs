using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using NLog;
using Spotnet.Community;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Controls;

/// <summary>
/// Instellingen voor koppelingen met diensten van derden: de Newznab-indexer en OMDb.
/// Deze stonden eerder tussen de community-instellingen, met ingevulde standaardwaarden die
/// naar een onbereikbare server en een in de broncode meegeleverde sleutel wezen. Ze horen
/// niet bij de community en staan nu standaard leeg: een lege waarde betekent uit.
/// Net als bij de community-pagina wordt er tegen een werkkopie bewerkt, zodat Annuleren
/// ook echt annuleert.
/// </summary>
public partial class SettingsForIntegrations : UserControl, IAdvancedSettingsControl
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Wordt getoond in plaats van een sleutel tot de gebruiker hem vervangt.</summary>
    private const string MaskedKeyPlaceholder = "••••••••";

    private static string NotSetPlaceholder => Words.IntegrNotSet;

    private CommunityConfig _working;

    /// <summary>
    /// De sleutels zoals ingeladen. Zolang het bijbehorende tekstvak alleen-lezen is, staat
    /// daar een masker en geldt de opgeslagen waarde in plaats van wat het masker leest.
    /// </summary>
    private string _originalNewznabKey = "";

    private string _originalOmdbKey = "";

    public SettingsForIntegrations()
    {
        base.Initialized += OnInitialized;
        InitializeComponent();
    }

    private void OnInitialized(object sender, EventArgs e)
    {
        _working = CloneCurrent();
        LoadToUi(_working);
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
        NewznabUrlTextBox.Text = c.Integrations.NewznabBaseUrl;
        _originalNewznabKey = c.Integrations.NewznabApiKey ?? "";
        ShowMasked(NewznabKeyTextBox, ReplaceNewznabKeyButton, _originalNewznabKey);

        _originalOmdbKey = c.Integrations.OmdbApiKey ?? "";
        ShowMasked(OmdbKeyTextBox, ReplaceOmdbKeyButton, _originalOmdbKey);

        UpdateStatusLabels();
    }

    private static void ShowMasked(TextBox box, Button replaceButton, string key)
    {
        box.IsReadOnly = true;
        replaceButton.IsEnabled = true;
        box.Text = key.Length == 0
            ? NotSetPlaceholder
            : (key.Length <= 4 ? MaskedKeyPlaceholder : MaskedKeyPlaceholder + key.Substring(key.Length - 4));
    }

    /// <summary>De sleutel die geldt: het masker telt niet mee, een bewerkt vak wel.</summary>
    private static string EffectiveKey(TextBox box, string original)
    {
        return box.IsReadOnly ? original : box.Text.Trim();
    }

    private void CollectFromUi(CommunityConfig c)
    {
        c.Integrations.NewznabBaseUrl = NewznabUrlTextBox.Text.Trim();
        c.Integrations.NewznabApiKey = EffectiveKey(NewznabKeyTextBox, _originalNewznabKey);
        c.Integrations.OmdbApiKey = EffectiveKey(OmdbKeyTextBox, _originalOmdbKey);
    }

    /// <summary>
    /// Laat per integratie zien of hij aan staat. Newznab vraagt om beide velden, dus een
    /// half ingevulde configuratie moet zichtbaar "uit" blijven in plaats van te lijken te werken.
    /// </summary>
    private void UpdateStatusLabels()
    {
        string url = NewznabUrlTextBox.Text.Trim();
        string newznabKey = EffectiveKey(NewznabKeyTextBox, _originalNewznabKey);

        if (url.IsNullOrWhiteSpace() && newznabKey.IsNullOrWhiteSpace())
        {
            NewznabStatusTextBlock.Text = Words.IntegrDisabled;
        }
        else if (url.IsNullOrWhiteSpace() || newznabKey.IsNullOrWhiteSpace())
        {
            NewznabStatusTextBlock.Text = url.IsNullOrWhiteSpace()
                ? Words.IntegrDisabledNoServer
                : Words.IntegrDisabledNoKey;
        }
        else
        {
            NewznabStatusTextBlock.Text = "Ingeschakeld";
        }

        OmdbStatusTextBlock.Text = EffectiveKey(OmdbKeyTextBox, _originalOmdbKey).IsNullOrWhiteSpace()
            ? Words.IntegrDisabled
            : "Ingeschakeld";
    }

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        // Wordt tijdens InitializeComponent al afgevuurd, voordat de andere velden bestaan.
        if (NewznabStatusTextBlock == null || OmdbStatusTextBlock == null || OmdbKeyTextBox == null)
        {
            return;
        }

        UpdateStatusLabels();
    }

    private void ReplaceNewznabKeyButton_Click(object sender, RoutedEventArgs e)
    {
        BeginKeyEdit(NewznabKeyTextBox, ReplaceNewznabKeyButton);
    }

    private void ReplaceOmdbKeyButton_Click(object sender, RoutedEventArgs e)
    {
        BeginKeyEdit(OmdbKeyTextBox, ReplaceOmdbKeyButton);
    }

    private void BeginKeyEdit(TextBox box, Button replaceButton)
    {
        box.IsReadOnly = false;
        box.Text = "";
        box.Focus();
        replaceButton.IsEnabled = false;
        UpdateStatusLabels();
    }

    public bool VerifyFields()
    {
        try
        {
            CollectFromUi(_working);
            IList<string> errors = _working.Validate();
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

            if (!_working.Save())
            {
                return false;
            }

            CommunityConfig.Replace(_working);

            // Er wordt tegen een kopie bewerkt, dus na opslaan een verse kopie pakken.
            _working = CloneCurrent();
            LoadToUi(_working);
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception(ex, showToClient: true);
            return false;
        }
    }
}
