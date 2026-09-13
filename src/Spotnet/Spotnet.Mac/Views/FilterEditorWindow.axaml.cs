using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spotnet.Mac.Models;

namespace Spotnet.Mac.Views;

/// <summary>
/// The filter editor, replacing the imperative dialog that hardcoded eight categories
/// and dropped the subcategory tag. Category ids follow the SQLite categories the
/// Windows picker uses: 1 Beeld, 2 Muziek, 3 Spellen, 4 Applicaties, 5 Boeken,
/// 6 Series, 9 Erotiek.
/// </summary>
public partial class FilterEditorWindow : Window
{
    public bool Confirmed { get; private set; }
    public string FilterName => NameBox.Text?.Trim() ?? "";
    public string Icon => string.IsNullOrWhiteSpace(IconBox.Text) ? "🔖" : IconBox.Text.Trim();
    public string? SubcatTag => string.IsNullOrWhiteSpace(SubcatBox.Text) ? null : SubcatBox.Text.Trim();
    public string? Keyword => string.IsNullOrWhiteSpace(KeywordBox.Text) ? null : KeywordBox.Text.Trim();

    public int? MaxAgeHours =>
        int.TryParse(AgeBox.Text, out int hours) && hours > 0 ? hours : null;

    public List<int> CategoryIds
    {
        get
        {
            var ids = new List<int>();
            if (CatBeeld.IsChecked == true) ids.Add(1);
            if (CatMuziek.IsChecked == true) ids.Add(2);
            if (CatSpellen.IsChecked == true) ids.Add(3);
            if (CatApplicaties.IsChecked == true) ids.Add(4);
            if (CatBoeken.IsChecked == true) ids.Add(5);
            if (CatSeries.IsChecked == true) ids.Add(6);
            if (CatErotiek.IsChecked == true) ids.Add(9);
            return ids;
        }
    }

    public FilterEditorWindow()
    {
        InitializeComponent();
        OkButton.Click += OnOk;
        CancelButton.Click += (_, _) => Close();
    }

    /// <summary>Opens the editor prefilled from an existing filter, for "Bewerken".</summary>
    public FilterEditorWindow(FilterItem existing) : this()
    {
        HeadingLabel.Text = "Filter bewerken";
        Title = "Filter bewerken";
        NameBox.Text = existing.Name;
        IconBox.Text = existing.Icon;
        SubcatBox.Text = existing.SubcatTag ?? "";
        KeywordBox.Text = existing.KeywordFilter ?? "";
        AgeBox.Text = existing.MaxAgeHours?.ToString() ?? "";

        switch (existing.CategoryId)
        {
            case 1: CatBeeld.IsChecked = true; break;
            case 2: CatMuziek.IsChecked = true; break;
            case 3: CatSpellen.IsChecked = true; break;
            case 4: CatApplicaties.IsChecked = true; break;
            case 5: CatBoeken.IsChecked = true; break;
            case 6: CatSeries.IsChecked = true; break;
            case 9: CatErotiek.IsChecked = true; break;
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus();
            return;
        }

        Confirmed = true;
        Close();
    }
}
