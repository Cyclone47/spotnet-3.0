using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NLog;
using Spotnet.Mac.DAL;
using Spotnet.Mac.DataVirtualization;
using Spotnet.Mac.Models;
using Spotnet.Mac.Network;
using Spotnet.Mac.Platform;
using Spotnet.Mac.Services;
using Spotnet.Platform;

namespace Spotnet.Mac.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly IAppPaths _appPaths;
    private readonly ISecretStore _secretStore;
    private readonly SpotDatabaseService _dbService;
    private readonly UserPreferencesService _prefsService;
    private readonly SpotSyncService _syncService;
    private readonly NzbService _nzbService;
    private readonly CustomFilterService _customFilterService;
    private readonly CommentService _commentService;
    private readonly SpotBodyService _bodyService;
    private readonly IUiDispatcher _dispatcher;
    private readonly TrustService _trustService;

    public UserPreferencesService PreferencesService => _prefsService;
    public TrustService TrustService => _trustService;
    private System.Threading.Timer? _autoSyncTimer;

    // ── State ─────────────────────────────────────────────────────────────────
    private FilterItem? _selectedFilter;
    private SpotItem? _selectedSpot;
    private string _searchText = "";
    private bool _isLoading;
    private string _statusText = "Gereed";
    private int _totalSpotsCount;
    private bool _isSyncing;
    private int _syncProgress;

    // ── Collections ───────────────────────────────────────────────────────────

    private VirtualSpotCollection? _spots;

    /// <summary>
    /// The spot list. Reports the full number of matches but only holds the pages the
    /// grid has actually scrolled through — see <see cref="VirtualSpotCollection"/>.
    /// A new filter, search or sort order replaces the instance rather than refilling it.
    /// </summary>
    public VirtualSpotCollection? Spots
    {
        get => _spots;
        private set => SetProperty(ref _spots, value);
    }

    /// <summary>Fetches one page for the current query. Passed to the virtual list as its loader.</summary>
    private async Task<IReadOnlyList<SpotItem>> LoadSpotPageAsync(
        string? filterQuery, string? keyword, int skip, int take, string sortColumn, string sortDirection)
    {
        try
        {
            var prefs = _prefsService.Current;
            return await _dbService.QueryByFilterAsync(
                filterQuery: filterQuery,
                searchText: keyword,
                skip: skip,
                take: take,
                sortDirection: sortDirection,
                sortColumn: sortColumn,
                hideBlacklisted: prefs.HideBlacklistedSpots,
                showTrustedOnly: prefs.ShowTrustedOnlyMode,
                showErotica: prefs.ShowEroticaInSearchResults,
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide);
        }
        catch (Exception ex)
        {
            // One unreadable page must not take the window down; the rows stay
            // placeholders and scrolling past and back retries.
            Log.Error(ex, "Could not load spots {0}..{1}: {2}", skip, skip + take, ex.Message);
            return Array.Empty<SpotItem>();
        }
    }

    // ── Sorting ───────────────────────────────────────────────────────────────

    /// <summary>Which column the list is ordered by; persisted between sessions.</summary>
    public string SortColumn => SpotSort.NormalizeColumn(_prefsService.Current.SortColumn);

    /// <summary>"ASC" or "DESC"; persisted between sessions.</summary>
    public string SortDirection => SpotSort.NormalizeDirection(_prefsService.Current.SortDirection);

    /// <summary>
    /// Orders the list by a grid column. Clicking the column that is already active
    /// flips the direction, which is what both the Windows client and the platform
    /// convention do. The order is applied in SQL, so it covers the whole result set and
    /// not just the pages in memory.
    /// </summary>
    public async Task ApplySortAsync(string? sortMemberPath)
    {
        string column = SpotSort.NormalizeColumn(sortMemberPath);
        string direction = column.Equals(SortColumn, StringComparison.OrdinalIgnoreCase)
            ? (SortDirection == "ASC" ? "DESC" : "ASC")
            : SpotSort.DefaultDirection;

        var prefs = _prefsService.Current;
        prefs.SortColumn = column;
        prefs.SortDirection = direction;
        _prefsService.Save(prefs);

        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDirection));

        await RefreshSpotsAsync();
    }

    /// <summary>The tab strip: the overview, plus one tab per opened spot.</summary>
    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = new();

    private WorkspaceTabViewModel? _selectedTab;
    public WorkspaceTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    /// <summary>Tab or separate window, as chosen in Weergave › Spots openen in.</summary>
    public SpotOpenMode SpotOpenMode
    {
        get => _prefsService.Current.SpotOpenMode;
        set
        {
            if (_prefsService.Current.SpotOpenMode == value) return;
            var prefs = _prefsService.Current;
            prefs.SpotOpenMode = value;
            _prefsService.Save(prefs);
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpensInTabs));
        }
    }

    public bool OpensInTabs => SpotOpenMode == SpotOpenMode.Tab;

    /// <summary>Downloadknop mode, as chosen in Bewerken › Downloadknop.</summary>
    public DownloadMode DownloadMode
    {
        get => _prefsService.Current.DownloadMode;
        set
        {
            if (_prefsService.Current.DownloadMode == value) return;
            var prefs = _prefsService.Current;
            prefs.DownloadMode = value;
            _prefsService.Save(prefs);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDownloadModeIntegrated));
            OnPropertyChanged(nameof(IsDownloadModeOpenNzb));
            OnPropertyChanged(nameof(IsDownloadModeSaveNzb));
        }
    }

    public bool IsDownloadModeIntegrated => DownloadMode == DownloadMode.Integrated;
    public bool IsDownloadModeOpenNzb => DownloadMode == DownloadMode.OpenNzb;
    public bool IsDownloadModeSaveNzb => DownloadMode == DownloadMode.SaveNzb;

    public string DownloadFolder
    {
        get => string.IsNullOrWhiteSpace(_prefsService.Current.DownloadFolder)
            ? _appPaths.DownloadsFolder
            : _prefsService.Current.DownloadFolder;
        set
        {
            var prefs = _prefsService.Current;
            prefs.DownloadFolder = value ?? "";
            _prefsService.Save(prefs);
            OnPropertyChanged();
        }
    }

    public ObservableCollection<FilterItem> FilterTree { get; } = new();

    // Flat list of custom filter items for easy save/load
    private readonly ObservableCollection<FilterItem> _customFilters = new();

    // ── Sub-view-models ───────────────────────────────────────────────────────
    public SpotDetailViewModel SpotDetail { get; }

    /// <summary>The Downloads tab, kept as a field so spot tabs can report into it.</summary>
    public DownloadsTabViewModel DownloadsTab { get; }

    // ── Properties ────────────────────────────────────────────────────────────
    public FilterItem? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            var previous = _selectedFilter;
            if (SetProperty(ref _selectedFilter, value))
            {
                // IsSelected drives the highlight in the sidebar, the way the Windows
                // tree marks the active filter.
                if (previous != null) previous.IsSelected = false;
                if (value != null) value.IsSelected = true;

                _ = RefreshSpotsAsync();
            }
        }
    }

    public SpotItem? SelectedSpot
    {
        get => _selectedSpot;
        set
        {
            if (SetProperty(ref _selectedSpot, value))
            {
                SpotDetail.Spot = value;
                OnPropertyChanged(nameof(IsDetailOpen));
            }
        }
    }

    public bool IsDetailOpen => SelectedSpot != null;

    public string ArchitectureInfo => $"Spotnet 3.0 • macOS ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})";

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public int TotalSpotsCount
    {
        get => _totalSpotsCount;
        set => SetProperty(ref _totalSpotsCount, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    public int SyncProgress
    {
        get => _syncProgress;
        set => SetProperty(ref _syncProgress, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CloseDetailCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenOnboardingCommand { get; }
    public ICommand SetThemeCommand { get; }
    public ICommand AddCustomFilterCommand { get; }
    public ICommand DeleteFilterCommand { get; }
    public ICommand ToggleFilterExpandCommand { get; }
    public ICommand SelectFilterCommand { get; }
    public ICommand OpenSpotCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand SetSpotOpenModeCommand { get; }
    public ICommand SetDownloadModeCommand { get; }
    public ICommand PickDownloadFolderCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }
    public ICommand QuickRepairDbCommand { get; }
    public ICommand ToggleSocksProxyCommand { get; }

    public ICommand AddSenderToBlacklistCommand { get; }
    public ICommand RemoveSenderFromBlacklistCommand { get; }
    public ICommand AddSenderToWhitelistCommand { get; }
    public ICommand RemoveSenderFromWhitelistCommand { get; }
    public ICommand AddSpotToBlacklistCommand { get; }
    public ICommand RemoveSpotFromBlacklistCommand { get; }
    public ICommand AddSpotToWhitelistCommand { get; }
    public ICommand RemoveSpotFromWhitelistCommand { get; }
    public ICommand DownloadExternalListsCommand { get; }

    public bool UseSocksProxy => _prefsService.Current.UseSocksProxy;
    public string SocksProxyIcon => UseSocksProxy ? "🔒" : "🔓";
    public string SocksProxyForeground => UseSocksProxy ? "#39A633" : "#888888";
    public string SocksProxyToolTip
    {
        get
        {
            var prefs = _prefsService.Current;
            if (!prefs.UseSocksProxy)
            {
                return "SOCKS5-proxy is uitgeschakeld (klik om in te schakelen)";
            }
            string host = !string.IsNullOrWhiteSpace(prefs.SocksProxyHost)
                ? $"{prefs.SocksProxyHost}:{prefs.SocksProxyPort}"
                : "geen host ingesteld";
            return $"SOCKS5-proxy is ingeschakeld ({host}) — klik om uit te schakelen";
        }
    }

    /// <summary>Raised when a spot should open in its own window rather than a tab.</summary>
    public event Action<SpotDetailViewModel>? RequestOpenSpotWindow;

    public event Action? RequestOpenSettings;
    public event Action? RequestOpenOnboarding;
    public event Action? RequestOpenReleaseNotes;
    public event Action? RequestAddCustomFilter;
    public event Action? RequestPickDownloadFolder;
    public event Action<DownloadItem>? RequestSetDownloadPassword;
    public Func<DownloadItem, Task<(bool confirmed, bool deleteFiles)>>? RequestConfirmRemoveDownload;
    public Func<int, long, Task<(bool confirmed, bool deleteFiles)>>? RequestConfirmClearDownloads;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindowViewModel(IAppPaths appPaths, ISecretStore secretStore, SpotDatabaseService dbService,
                               UserPreferencesService? prefsService = null, IUiDispatcher? dispatcher = null,
                               TrustService? trustService = null)
    {
        _appPaths = appPaths;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        _secretStore = secretStore;
        _dbService = dbService;
        _prefsService = prefsService ?? new UserPreferencesService(_appPaths);
        _customFilterService = new CustomFilterService(_appPaths);
        _trustService = trustService ?? new TrustService(_appPaths, _prefsService);

        _trustService.ListsChanged += () =>
        {
            _ = _dispatcher.InvokeAsync(async () =>
            {
                await _trustService.SyncToDatabaseAsync(_dbService);
                await RefreshSpotsAsync();
            });
        };

        _nzbService = new NzbService(_appPaths, _secretStore, _prefsService);
        _syncService = new SpotSyncService(_appPaths, _secretStore, _dbService, _prefsService, _trustService);
        _commentService = new CommentService(_appPaths, _secretStore, _dbService);
        _bodyService = new SpotBodyService(_appPaths, _secretStore);

        _syncService.ProgressChanged += (current, total, msg) =>
        {
            StatusText = msg;
            SyncProgress = current;
            IsSyncing = _syncService.IsSyncing;
        };

        SpotDetail = new SpotDetailViewModel(_dbService, _nzbService, _commentService, _bodyService);

        DownloadsTab = new DownloadsTabViewModel(new DownloadHistoryService(_appPaths), _prefsService);
        SpotDetail.NzbFetched += OnNzbFetched;
        SpotDetail.RequestClose += () => SelectedSpot = null;

        OpenReleaseNotesCommand = new RelayCommand(() => RequestOpenReleaseNotes?.Invoke());
        QuickRepairDbCommand = new RelayCommand(async () =>
        {
            StatusText = "Database herstellen en optimaliseren...";
            var (success, msg) = await _dbService.QuickRepairAsync();
            StatusText = msg;
            var notifier = new Platform.MacNotificationService(_prefsService);
            notifier.NotifyDatabaseRepairFinished(success, msg);
            await RefreshSpotsAsync();
        });

        // Commands
        SearchCommand = new RelayCommand(async () => await RefreshSpotsAsync());
        ClearSearchCommand = new RelayCommand(async () =>
        {
            SearchText = "";
            await RefreshSpotsAsync();
        });

        RefreshCommand = new RelayCommand(async () =>
        {
            IsSyncing = true;
            // Everything already in the table is "seen"; whatever the sync adds above
            // this watermark is what the Nieuw filter ([SN:NEW]) shows.
            await _dbService.MarkSpotsSeenAsync();
            await _syncService.SyncSpotsAsync();
            IsSyncing = false;
            await RefreshSpotsAsync();
            await UpdateFilterCountsAsync();
        });

        CloseDetailCommand = new RelayCommand(() => SelectedSpot = null);
        OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());
        OpenOnboardingCommand = new RelayCommand(() => RequestOpenOnboarding?.Invoke());
        ToggleSocksProxyCommand = new RelayCommand(ToggleSocksProxy);

        SetThemeCommand = new RelayCommand(param =>
        {
            if (param is AppThemeStyle style)
            {
                ThemeService.Instance.ApplyTheme(style);
                var prefs = _prefsService.Current;
                prefs.ThemeStyle = style;
                _prefsService.Save(prefs);
            }
        });

        AddCustomFilterCommand = new RelayCommand(() => RequestAddCustomFilter?.Invoke());

        DeleteFilterCommand = new RelayCommand(param =>
        {
            if (param is FilterItem item && item.IsCustom)
                RemoveCustomFilter(item);
        });

        ToggleFilterExpandCommand = new RelayCommand(param =>
        {
            if (param is FilterItem item && item.HasChildren)
                item.IsExpanded = !item.IsExpanded;
        });

        SelectFilterCommand = new RelayCommand(param =>
        {
            if (param is FilterItem item)
                SelectedFilter = item;
        });

        OpenSpotCommand = new RelayCommand(param => OpenSpot(param as SpotItem ?? SelectedSpot));

        CloseTabCommand = new RelayCommand(param =>
        {
            if (param is WorkspaceTabViewModel tab && tab.CanClose)
            {
                int index = Tabs.IndexOf(tab);
                Tabs.Remove(tab);
                SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Max(0, Math.Min(index - 1, Tabs.Count - 1))];
            }
        });

        SetSpotOpenModeCommand = new RelayCommand(param =>
        {
            if (param is SpotOpenMode mode) SpotOpenMode = mode;
        });

        SetDownloadModeCommand = new RelayCommand(param =>
        {
            if (param is DownloadMode mode) DownloadMode = mode;
        });

        PickDownloadFolderCommand = new RelayCommand(() => RequestPickDownloadFolder?.Invoke());

        DeleteSelectedCommand = new RelayCommand(async () =>
        {
            if (SelectedTab == DownloadsTab && DownloadsTab.Selected != null)
            {
                DownloadsTab.RemoveCommand.Execute(DownloadsTab.Selected);
            }
            else if (SelectedSpot != null && SelectedTab == null)
            {
                // Delete on the spot list adds the spot to the blacklist, exactly like Windows
                if (!string.IsNullOrWhiteSpace(SelectedSpot.MsgId))
                {
                    _trustService.AddSpotBlack(SelectedSpot.MsgId);
                    await _trustService.SyncToDatabaseAsync(_dbService);
                    await RefreshSpotsAsync();
                }
            }
        });

        AddSenderToBlacklistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.Modulus)) return;
            _trustService.AddBlack(spot.SenderName, spot.Modulus);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        RemoveSenderFromBlacklistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.Modulus)) return;
            _trustService.RemoveBlack(spot.Modulus);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        AddSenderToWhitelistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.Modulus)) return;
            _trustService.AddWhite(spot.SenderName, spot.Modulus);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        RemoveSenderFromWhitelistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.Modulus)) return;
            _trustService.RemoveWhite(spot.Modulus);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        AddSpotToBlacklistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId)) return;
            _trustService.AddSpotBlack(spot.MsgId);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        RemoveSpotFromBlacklistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId)) return;
            _trustService.RemoveSpotBlack(spot.MsgId);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        AddSpotToWhitelistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId)) return;
            _trustService.AddSpotWhite(spot.MsgId);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        RemoveSpotFromWhitelistCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId)) return;
            _trustService.RemoveSpotWhite(spot.MsgId);
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
        });

        DownloadExternalListsCommand = new RelayCommand(async () =>
        {
            StatusText = "Externe lijsten downloaden...";
            await _trustService.UpdateExternalListsAsync();
            await _trustService.SyncToDatabaseAsync(_dbService);
            await RefreshSpotsAsync();
            StatusText = "Externe lijsten bijgewerkt";
        });

        DownloadsTab.RequestOpenSpotInfo += async msgId =>
        {
            var spot = await _dbService.GetSpotByMsgIdAsync(msgId);
            if (spot != null)
            {
                OpenSpot(spot);
            }
        };

        DownloadsTab.RequestSetPassword += item =>
        {
            RequestSetDownloadPassword?.Invoke(item);
        };

        DownloadsTab.RequestConfirmRemove = item =>
            RequestConfirmRemoveDownload != null
                ? RequestConfirmRemoveDownload(item)
                : Task.FromResult((true, false));

        DownloadsTab.RequestConfirmClear = (count, bytes) =>
            RequestConfirmClearDownloads != null
                ? RequestConfirmClearDownloads(count, bytes)
                : Task.FromResult((true, false));

        // Overzicht and Downloads are permanent, in that order, as on Windows.
        Tabs.Add(new OverviewTabViewModel());
        Tabs.Add(DownloadsTab);
        SelectedTab = Tabs[0];

        // Build the filter tree
        BuildFilterTree();
    }

    /// <summary>
    /// Opens a spot the way the preference says: a new tab next to the overview, like
    /// Windows, or a separate window. Re-opening a spot that already has a tab just
    /// selects it instead of adding a second one.
    /// </summary>
    public void OpenSpot(SpotItem? spot)
    {
        if (spot == null) return;

        if (SpotOpenMode == SpotOpenMode.Window)
        {
            SpotDetail.Spot = spot;
            RequestOpenSpotWindow?.Invoke(SpotDetail);
            return;
        }

        var existing = Tabs.OfType<SpotTabViewModel>().FirstOrDefault(t => t.Spot.MsgId == spot.MsgId);
        if (existing != null)
        {
            SelectedTab = existing;
            return;
        }

        var detail = new SpotDetailViewModel(_dbService, _nzbService, _commentService, _bodyService);
        var tab = new SpotTabViewModel(spot, detail);
        detail.RequestClose += () => CloseTabCommand.Execute(tab);
        detail.NzbFetched += OnNzbFetched;

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    /// <summary>Records an NZB fetch in the Downloads tab and brings that tab forward.</summary>
    private void OnNzbFetched(SpotItem spot, bool success, string? path, string message, Network.NzbDownloadJob? job, string? description = null)
    {
        DownloadsTab.Add(spot, success, path, message, job, description: description);
        SelectedTab = DownloadsTab;
    }

    // ── Filter Tree ───────────────────────────────────────────────────────────

    private FilterItem _customGroup = null!;
    private FilterItem _defaultFilter = null!;

    private void BuildFilterTree()
    {
        FilterTree.Clear();

        // ── Bundled advanced filters ──────────────────────────────────────────
        // Same tree the Windows client ships (Nieuw, Overzicht, Laatste 24 uur,
        // Beeld, Beeld - Genres, Beeld - TV Series, Boeken, Muziek, Muziek - Genres,
        // Spellen, Spellen - Console, Spellen - Mobile, Applicaties,
        // Applicaties - Mobile, Erotiek), loaded from the shared XML.
        foreach (var item in DefaultFilterProvider.Load())
        {
            FilterTree.Add(item);
        }

        // ── Custom filters group ──────────────────────────────────────────────
        _customGroup = new FilterItem
        {
            Id = "custom",
            Kind = FilterKind.Custom,
            Name = "Eigen filters",
            Icon = "🔖",
            IsExpanded = true
        };

        // Load persisted custom filters
        var saved = _customFilterService.Load();
        foreach (var def in saved)
        {
            var customItem = new FilterItem
            {
                Id = def.Id,
                Kind = FilterKind.Custom,
                Name = def.Name,
                Icon = def.Icon,
                CategoryId = def.CategoryId,
                SubcatTag = def.SubcatTag,
                MaxAgeHours = def.MaxAgeHours,
                KeywordFilter = def.KeywordFilter,
                Query = ComposeQuery(def.CategoryId, def.SubcatTag, def.MaxAgeHours)
            };
            _customGroup.Children.Add(customItem);
            _customFilters.Add(customItem);
        }

        FilterTree.Add(_customGroup);

        // Default selection: "Overzicht", as on Windows.
        _defaultFilter = FilterTree.FirstOrDefault(f => f.Id == "def_Overzicht") ?? FilterTree.First();
        _selectedFilter = _defaultFilter;
        _defaultFilter.IsSelected = true;
    }

    /// <summary>
    /// Builds a filter expression for a user-created filter out of the fields the
    /// "filter toevoegen" dialog collects. Keyword matching stays out of the
    /// expression: it is applied as a free-text search alongside it.
    /// </summary>
    private static string ComposeQuery(int? categoryId, string? subcatTag, int? maxAgeHours)
    {
        var parts = new List<string>();
        if (categoryId is > 0)
        {
            parts.Add($"cat = {categoryId.Value}");
        }
        if (!string.IsNullOrWhiteSpace(subcatTag))
        {
            // LIKE rather than MATCH: a MATCH term would send the whole expression to
            // the FTS table, which has no cat or date column to combine it with.
            parts.Add($"cats LIKE '%{subcatTag.Replace("'", "''")}%'");
        }
        if (maxAgeHours is > 0)
        {
            parts.Add($"date > ( [SN:DATE] - {maxAgeHours.Value * 3600} )");
        }
        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// Adds a new user-created filter to the custom group and persists it.
    /// </summary>
    public void AddCustomFilter(string name, string icon, int? categoryId, string? subcatTag, int? maxAgeHours, string? keyword)
    {
        var item = new FilterItem
        {
            Kind = FilterKind.Custom,
            Name = name,
            Icon = icon,
            CategoryId = categoryId,
            SubcatTag = subcatTag,
            MaxAgeHours = maxAgeHours,
            KeywordFilter = keyword,
            Query = ComposeQuery(categoryId, subcatTag, maxAgeHours)
        };

        _customGroup.Children.Add(item);
        _customFilters.Add(item);
        PersistCustomFilters();
    }

    private void RemoveCustomFilter(FilterItem item)
    {
        _customGroup.Children.Remove(item);
        _customFilters.Remove(item);
        PersistCustomFilters();

        if (SelectedFilter == item)
            SelectedFilter = _defaultFilter;
    }

    private void PersistCustomFilters()
    {
        var defs = _customFilters.Select(f => new CustomFilterDefinition
        {
            Id = f.Id,
            Name = f.Name,
            Icon = f.Icon,
            CategoryId = f.CategoryId,
            SubcatTag = f.SubcatTag,
            MaxAgeHours = f.MaxAgeHours,
            KeywordFilter = f.KeywordFilter
        }).ToList();

        _customFilterService.Save(defs);
    }

    // ── Initialise ────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusText = "Database initialiseren...";
        try
        {
            await _dbService.EnsureCreatedAsync();
            await _dbService.LoadRowNewAsync();
            await _dbService.UpdateDatabaseStatsAsync(_prefsService);
            await _trustService.SyncToDatabaseAsync(_dbService);

            await RefreshSpotsAsync();
            await UpdateFilterCountsAsync();
            StartAutoSyncTimer();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database: {0}", ex.Message);
            StatusText = $"Databasefout: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Spots Query ───────────────────────────────────────────────────────────

    public async Task RefreshSpotsAsync()
    {
        IsLoading = true;
        StatusText = "Spots ophalen...";
        try
        {
            var filter = _selectedFilter;

            // A filter's own keyword is only used when the search box is empty, so
            // typing in the box narrows the filter rather than replacing it.
            string? keyword = string.IsNullOrWhiteSpace(SearchText) ? filter?.KeywordFilter : SearchText;

            string? filterQuery = filter?.Query;
            string sortColumn = SortColumn;
            string sortDirection = SortDirection;

            var prefs = _prefsService.Current;
            // The count comes first: it is what the virtual list reports as its size, so
            // the grid can size its scrollbar to the whole result rather than to the one
            // page that happens to be in memory.
            TotalSpotsCount = await _dbService.CountByFilterAsync(
                filterQuery,
                keyword,
                hideBlacklisted: prefs.HideBlacklistedSpots,
                showTrustedOnly: prefs.ShowTrustedOnlyMode,
                showErotica: prefs.ShowEroticaInSearchResults,
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide);

            var previous = Spots;
            Spots = new VirtualSpotCollection(
                (skip, take, _) => LoadSpotPageAsync(filterQuery, keyword, skip, take, sortColumn, sortDirection),
                TotalSpotsCount,
                _dispatcher);
            previous?.Dispose();

            SelectedSpot = null;

            UpdateSpotsListStatusMessage();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to query spots: {0}", ex.Message);
            StatusText = $"Zoekfout: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Formats the status bar spot count message to match Windows StatusBarViewModel.SetDefaultSpotsListStatusMessage.
    /// Uses Dutch thousand-separator dots and reports total in database vs filtered query count.
    /// </summary>
    private void UpdateSpotsListStatusMessage()
    {
        long databaseCount = _prefsService.Current.DatabaseCount;
        var nfi = new System.Globalization.CultureInfo("nl-NL");
        string formattedDbCount = databaseCount.ToString("#,##0", nfi);

        if (databaseCount < 1)
        {
            StatusText = "Geen spots in de database";
            return;
        }

        if (TotalSpotsCount < 1)
        {
            StatusText = "Geen spots gevonden";
            return;
        }

        bool isAllSpots = _selectedFilter == null
            || string.IsNullOrWhiteSpace(_selectedFilter.Query)
            || _selectedFilter.Id == "def_Overzicht";

        if (isAllSpots && string.IsNullOrWhiteSpace(SearchText))
        {
            StatusText = databaseCount != 1
                ? $"{formattedDbCount} spots in de database"
                : "1 spot in de database";
        }
        else
        {
            string formattedQueryCount = TotalSpotsCount.ToString("#,##0", nfi);
            StatusText = TotalSpotsCount != 1
                ? $"{formattedQueryCount} spots (van de {formattedDbCount})"
                : $"1 spot (van de {formattedDbCount})";
        }
    }

    /// <summary>
    /// Starts the automatic background synchronization timer if enabled (Windows: DbUpdateTimerStart).
    /// </summary>
    public void StartAutoSyncTimer()
    {
        StopAutoSyncTimer();

        var prefs = _prefsService.Current;
        if (!prefs.DbAutoUpdateEnabled || prefs.DbAutoUpdateIntervalMin <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, prefs.DbAutoUpdateIntervalMin));
        _autoSyncTimer = new System.Threading.Timer(async _ =>
        {
            await OnAutoSyncTimerElapsedAsync();
        }, null, interval, interval);
    }

    /// <summary>
    /// Stops the automatic background synchronization timer (Windows: DbUpdateTimerStop).
    /// </summary>
    public void StopAutoSyncTimer()
    {
        _autoSyncTimer?.Dispose();
        _autoSyncTimer = null;
    }

    private async Task OnAutoSyncTimerElapsedAsync()
    {
        var prefs = _prefsService.Current;
        if (!prefs.DbAutoUpdateEnabled || prefs.DbAutoUpdateIntervalMin <= 0)
        {
            StopAutoSyncTimer();
            return;
        }

        if (IsSyncing || IsLoading)
        {
            Log.Debug("Auto-sync skipped: a sync or load is already in progress.");
            return;
        }

        await _dispatcher.InvokeAsync(async () =>
        {
            if (IsSyncing || IsLoading) return;
            try
            {
                Log.Info("Auto-sync timer elapsed; starting automated sync.");
                IsSyncing = true;
                await _syncService.SyncSpotsAsync();
                IsSyncing = false;
                await RefreshSpotsAsync();
                await UpdateFilterCountsAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-sync failed: {0}", ex.Message);
            }
            finally
            {
                IsSyncing = false;
            }
        });
    }

    /// <summary>
    /// Invoked when settings are saved to refresh auto-sync timer, database statistics, and views.
    /// </summary>
    public async void OnSettingsSaved()
    {
        NotifySocksProxyChanged();
        StartAutoSyncTimer();
        await _dbService.UpdateDatabaseStatsAsync(_prefsService);
        await RefreshSpotsAsync();
        await UpdateFilterCountsAsync();
    }

    private void ToggleSocksProxy()
    {
        var prefs = _prefsService.Current;
        if (!prefs.UseSocksProxy && string.IsNullOrWhiteSpace(prefs.SocksProxyHost))
        {
            RequestOpenSettings?.Invoke();
            return;
        }

        prefs.UseSocksProxy = !prefs.UseSocksProxy;
        _prefsService.Save(prefs);
        NotifySocksProxyChanged();
    }

    public void NotifySocksProxyChanged()
    {
        OnPropertyChanged(nameof(UseSocksProxy));
        OnPropertyChanged(nameof(SocksProxyIcon));
        OnPropertyChanged(nameof(SocksProxyForeground));
        OnPropertyChanged(nameof(SocksProxyToolTip));
    }

    // ── Filter Counts ─────────────────────────────────────────────────────────

    private async Task UpdateFilterCountsAsync()
    {
        try
        {
            // Update counts on all leaf filter nodes
            foreach (var group in FilterTree)
            {
                await UpdateCountsForGroupAsync(group);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to update filter counts: {0}", ex.Message);
        }
    }

    private async Task UpdateCountsForGroupAsync(FilterItem group)
    {
        if (!string.IsNullOrWhiteSpace(group.Query))
        {
            var prefs = _prefsService.Current;
            // The badge is a "new since the last sync" count, as on Windows — not the
            // total the filter holds.
            group.Count = await _dbService.CountNewByFilterAsync(
                group.Query,
                hideBlacklisted: prefs.HideBlacklistedSpots,
                showTrustedOnly: prefs.ShowTrustedOnlyMode,
                showErotica: prefs.ShowEroticaInSearchResults,
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide);
        }

        foreach (var child in group.Children)
        {
            await UpdateCountsForGroupAsync(child);
        }
    }

    public void Dispose()
    {
        StopAutoSyncTimer();
        _trustService.Dispose();
    }
}
