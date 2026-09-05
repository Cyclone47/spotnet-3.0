using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Spotnet.Mac.Models;

/// <summary>
/// Defines the kind of filter node in the sidebar.
/// </summary>
public enum FilterKind
{
    /// <summary>System preset (Nieuw, Overzicht, Laatste 24 uur, etc.)</summary>
    Preset,
    /// <summary>Main category (Beeld, Geluid, Spellen, ...)</summary>
    Category,
    /// <summary>Sub-category of a parent (Beeld › TV Series, etc.)</summary>
    SubCategory,
    /// <summary>User-created custom filter</summary>
    Custom
}

/// <summary>
/// Represents a single node in the hierarchical filter/category sidebar tree.
/// Implements INotifyPropertyChanged so the tree updates reactively.
/// </summary>
public sealed class FilterItem : INotifyPropertyChanged
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public FilterKind Kind { get; init; }

    // ── Display ───────────────────────────────────────────────────────────────
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
    }

    public string Icon { get; init; } = string.Empty;

    private int _count;
    public int Count
    {
        get => _count;
        set { _count = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(HasCount)); }
    }

    public bool HasCount => Count > 0;

    public string DisplayName => Count > 0 ? $"{Name} ({Count})" : Name;

    // ── Filter logic ──────────────────────────────────────────────────────────
    /// <summary>
    /// The filter expression in Spotnet's filter mini-language, e.g. "cat=1" or
    /// "cats MATCH '1a6'". Empty means "everything". Bundled filters get this straight
    /// from the shared FiltersAdvanced XML; custom filters get one composed from the
    /// category/subcat/age/keyword fields below.
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>SQLite category id (1=Beeld, 2=Geluid, etc.); null = no category filter</summary>
    public int? CategoryId { get; init; }

    /// <summary>cats column prefix to LIKE filter on (e.g. "1a3"); null = no subcat filter</summary>
    public string? SubcatTag { get; init; }

    /// <summary>Maximum age in hours; null = no age filter</summary>
    public int? MaxAgeHours { get; init; }

    /// <summary>FTS search query fragment to AND in; null = no extra keyword filter</summary>
    public string? KeywordFilter { get; init; }

    /// <summary>If true, only spots from the last sync run are shown</summary>
    public bool NewOnly { get; init; }

    // ── Tree state ────────────────────────────────────────────────────────────
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsCustom => Kind == FilterKind.Custom;
    public bool CanDelete => Kind == FilterKind.Custom;
    public bool HasChildren => Children.Count > 0;

    public ObservableCollection<FilterItem> Children { get; } = [];

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
