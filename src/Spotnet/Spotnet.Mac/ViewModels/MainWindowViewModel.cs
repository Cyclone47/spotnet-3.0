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
    private readonly ComplaintService _complaintService;
    private readonly UserKeyService _userKeyService;
    private readonly SpotBodyService _bodyService;
    private readonly IUiDispatcher _dispatcher;
    private readonly TrustService _trustService;
    private readonly Spotnet.Notifications.NotificationManager _notifications;
    private readonly Services.SearchHistoryService _searchHistory;
    private readonly Services.TabPersistenceService _tabPersistence;
    private readonly System.Net.Http.HttpClient _suggestClient = new();
    private readonly Services.SpotThumbService? _thumbService;
    private System.Threading.Timer? _notificationEvalTimer;

    public UserPreferencesService PreferencesService => _prefsService;
    public TrustService TrustService => _trustService;
    public ComplaintService ComplaintService => _complaintService;

    /// <summary>
    /// The shared notification engine (Spotnet.Core): rules over de spots-database,
    /// ongelezen meldingen en de configuratie in notifications_config.json.
    /// </summary>
    public Spotnet.Notifications.NotificationManager Notifications => _notifications;

    private int _unreadNotificationCount;

    /// <summary>Ongelezen meldingen voor de bel in de statusbalk.</summary>
    public int UnreadNotificationCount
    {
        get => _unreadNotificationCount;
        private set
        {
            if (SetProperty(ref _unreadNotificationCount, value))
            {
                OnPropertyChanged(nameof(HasUnreadNotifications));
            }
        }
    }

    /// <summary>Toont de gelezen/ongelezen-badge op de bel.</summary>
    public bool HasUnreadNotifications => UnreadNotificationCount > 0;

    /// <summary>View-event: opent het meldingcentrum-venster.</summary>
    public event Action? RequestOpenNotificationCenter;

    private System.Threading.Timer? _autoSyncTimer;

    // ── State ─────────────────────────────────────────────────────────────────
    private FilterItem? _selectedFilter;
    private SpotItem? _selectedSpot;
    private string _searchText = "";
    private string _searchField = "subject";
    private bool _extensiveSearch = true;
    private bool _favoritesOnly;
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
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide,
                searchField: _searchField,
                extensiveSearch: _extensiveSearch,
                favoritesOnly: _favoritesOnly);
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

                OnPropertyChanged(nameof(SelectedFilterLabel));
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

    /// <summary>
    /// Waar het ZOEKEN-paneel in zoekt: "subject" (Titel), "sender" (Afzender) of
    /// "tag" (Label) — de FTS-kolommen, zoals Windows' zoekradioknoppen.
    /// </summary>
    public string SearchField
    {
        get => _searchField;
        set
        {
            if (SetProperty(ref _searchField, value))
            {
                _ = RefreshSpotsAsync();
            }
        }
    }

    /// <summary>Windows' "Uitgebreid"-vinkje: prefix-zoekopdracht per term.</summary>
    public bool ExtensiveSearch
    {
        get => _extensiveSearch;
        set
        {
            if (SetProperty(ref _extensiveSearch, value))
            {
                var prefs = _prefsService.Current;
                prefs.AdvancedSearch = value;
                _prefsService.Save(prefs);
                _ = RefreshSpotsAsync();
            }
        }
    }

    /// <summary>Windows' "Favorieten"-vinkje: alleen favoriete spots tonen.</summary>
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetProperty(ref _favoritesOnly, value))
            {
                _ = RefreshSpotsAsync();
            }
        }
    }

    // ── Weergave (fase 6): lijst- of thumbnailweergave, lettergrootte ────────

    /// <summary>De spotslijst als tabel (Windows' SpotsListTypeEnum.Default).</summary>
    public bool IsListView => _prefsService.Current.SpotsListType != 3;

    /// <summary>De spotslijst als miniatuurrooster (Windows' SpotsListTypeEnum.Thumbs).</summary>
    public bool IsThumbView => !IsListView;

    /// <summary>Schakelt tussen de lijst- en de thumbnailweergave, zoals Windows' SpotsListType.</summary>
    public int SpotsListType
    {
        get => _prefsService.Current.SpotsListType;
        set
        {
            if (_prefsService.Current.SpotsListType != value)
            {
                var prefs = _prefsService.Current;
                prefs.SpotsListType = value;
                _prefsService.Save(prefs);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsListView));
                OnPropertyChanged(nameof(IsThumbView));
            }
        }
    }

    /// <summary>Lettergrootte van de spotslijst, zoals Windows' FontSize (spotlijst).</summary>
    public int SpotsFontSize
    {
        get => _prefsService.Current.SpotsFontSize;
        set
        {
            int clamped = Math.Clamp(value, 8, 24);
            if (_prefsService.Current.SpotsFontSize != clamped)
            {
                var prefs = _prefsService.Current;
                prefs.SpotsFontSize = clamped;
                _prefsService.Save(prefs);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Windows' SaveTabs: geopende spot-tabbladen heropenen bij het opstarten.</summary>
    public bool SaveTabs
    {
        get => _prefsService.Current.SaveTabs;
        set
        {
            if (_prefsService.Current.SaveTabs != value)
            {
                var prefs = _prefsService.Current;
                prefs.SaveTabs = value;
                _prefsService.Save(prefs);
                OnPropertyChanged();
                if (!value)
                {
                    _tabPersistence.ClearTabs();
                }
            }
        }
    }

    // ── Zoeksuggesties (fase 6): geschiedenis + Google, zoals het ZOEKEN-paneel ─

    private readonly ObservableCollection<string> _suggestions = new();

    /// <summary>De suggesties onder de zoekbox: Google-suggesties + geschiedenis, zoals Windows.</summary>
    public ObservableCollection<string> Suggestions => _suggestions;

    private bool _isSuggestionsOpen;

    /// <summary>Of het suggestie-venster open staat; de view sluit het bij een klik buiten.</summary>
    public bool IsSuggestionsOpen
    {
        get => _isSuggestionsOpen;
        set => SetProperty(ref _isSuggestionsOpen, value);
    }

    private string _lastSuggestText = "";

    /// <summary>
    /// Verzamelt suggesties voor de huidige zoektekst: Google's volledige-zinnenlijst
    /// (wanneer GoogleSuggest aan staat) plus eigen zoektermen die beginnen met de
    /// tekst — dezelfde mix als Windows' UpdateSuggestions.
    /// </summary>
    public async System.Threading.Tasks.Task UpdateSuggestionsAsync()
    {
        string text = SearchText.Trim();
        if (text.Length == 0)
        {
            _suggestions.Clear();
            IsSuggestionsOpen = false;
            return;
        }

        if (text.Equals(_lastSuggestText, StringComparison.OrdinalIgnoreCase) && _suggestions.Count > 0)
        {
            return;
        }
        _lastSuggestText = text;

        var suggestions = new List<string>();
        if (_prefsService.Current.GoogleSuggest)
        {
            try
            {
                string url = "http://www.google.nl/complete/search?hl=nl&output=toolbar&q="
                    + System.Uri.EscapeDataString(text);
                using var response = await _suggestClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string xml = await response.Content.ReadAsStringAsync();
                    suggestions.AddRange(Services.GoogleSuggestParser.Parse(xml));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Google suggest failed for '{0}'", text);
            }
        }

        // Eigen zoekgeschiedenis eronder, zoals Windows: termen die met de tekst beginnen.
        foreach (string historyItem in _searchHistory.HistoryItems)
        {
            if (historyItem.StartsWith(text, StringComparison.OrdinalIgnoreCase) && !suggestions.Contains(historyItem))
            {
                suggestions.Add(historyItem);
            }
            if (suggestions.Count >= 12) break;
        }

        _suggestions.Clear();
        foreach (string suggestion in suggestions)
        {
            _suggestions.Add(suggestion);
        }
        IsSuggestionsOpen = _suggestions.Count > 0;
    }

    /// <summary>Voert de zoekactie uit: opslaan in de geschiedenis (Windows' DoSearch) en verversen.</summary>
    public async System.Threading.Tasks.Task SubmitSearchAsync()
    {
        string text = SearchText.Trim();
        IsSuggestionsOpen = false;
        if (text.Length > 0 && _prefsService.Current.GoogleSuggest)
        {
            _searchHistory.SaveHistory(text);
        }
        await RefreshSpotsAsync();
    }

    /// <summary>Zet de zoektekst op de gekozen suggestie en zoekt, zoals Windows' SearchBox-selectie.</summary>
    public async System.Threading.Tasks.Task ApplySuggestionAsync(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        SearchText = suggestion;
        IsSuggestionsOpen = false;
        await SubmitSearchAsync();
    }

    // ── Thumbnailweergave (fase 6) ────────────────────────────────────────────

    private readonly ObservableCollection<SpotItem> _thumbs = new();

    /// <summary>De zichtbare miniaturen: de eerste pagina's van de huidige query.</summary>
    public ObservableCollection<SpotItem> Thumbs => _thumbs;

    private int _thumbTotalCount;

    /// <summary>Er kunnen nog meer miniaturen geladen worden voor deze query.</summary>
    public bool CanLoadMoreThumbs => _thumbs.Count < _thumbTotalCount;

    public ICommand LoadMoreThumbsCommand { get; }

    private const int ThumbPageSize = 60;

    /// <summary>Laadt de volgende portie miniaturen uit de huidige query.</summary>
    public async System.Threading.Tasks.Task LoadMoreThumbsAsync()
    {
        try
        {
            var filter = _selectedFilter;
            string? keyword = string.IsNullOrWhiteSpace(SearchText) ? filter?.KeywordFilter : SearchText;
            var prefs = _prefsService.Current;

            int count = await _dbService.CountByFilterAsync(
                filter?.Query, keyword,
                hideBlacklisted: prefs.HideBlacklistedSpots,
                showTrustedOnly: prefs.ShowTrustedOnlyMode,
                showErotica: prefs.ShowEroticaInSearchResults,
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide,
                searchField: _searchField,
                extensiveSearch: _extensiveSearch,
                favoritesOnly: _favoritesOnly);
            _thumbTotalCount = count;

            var rows = await _dbService.QueryByFilterAsync(
                filterQuery: filter?.Query,
                searchText: keyword,
                skip: _thumbs.Count,
                take: ThumbPageSize,
                sortDirection: SortDirection,
                sortColumn: SortColumn,
                hideBlacklisted: prefs.HideBlacklistedSpots,
                showTrustedOnly: prefs.ShowTrustedOnlyMode,
                showErotica: prefs.ShowEroticaInSearchResults,
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide,
                searchField: _searchField,
                extensiveSearch: _extensiveSearch,
                favoritesOnly: _favoritesOnly);

            _thumbs.Clear();
            foreach (var row in rows)
            {
                _thumbs.Add(row);
            }
            OnPropertyChanged(nameof(CanLoadMoreThumbs));
            _ = LoadThumbImagesAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Failed to load thumbnails: {0}", ex.Message);
        }
    }

    /// <summary>Laadt de miniatuur-afbeeldingen voor de zichtbare rijen (server-cache voorkomt herhaling).</summary>
    private async System.Threading.Tasks.Task LoadThumbImagesAsync()
    {
        var thumbs = _thumbService;
        if (thumbs == null) return;
        foreach (var spot in _thumbs)
        {
            if (spot.ThumbImage != null) continue;
            try
            {
                var image = await thumbs.GetThumbAsync(spot);
                if (image != null && _thumbs.Contains(spot))
                {
                    spot.ThumbImage = image;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Thumb failed for {0}", spot.MsgId);
            }
        }
    }

    public bool IsSearchFieldTitle => _searchField == "subject";
    public bool IsSearchFieldSender => _searchField == "sender";
    public bool IsSearchFieldTag => _searchField == "tag";

    /// <summary>De naam van de gekozen filter, zoals in Windows' "FILTERS <naam> ▾"-kop.</summary>
    public string SelectedFilterLabel => SelectedFilter?.Name is { Length: > 0 } name ? name : "Aangepast";

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
    public ICommand SetSearchFieldCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ApplySuggestionCommand { get; }
    public ICommand SetSpotsListTypeCommand { get; }
    public ICommand SetFontSizeCommand { get; }
    public ICommand ToggleSaveTabsCommand { get; }
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
    public ICommand OpenSpotlinkCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }
    public ICommand OpenNotificationCenterCommand { get; }
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

    public ICommand ToggleShowTrustedOnlyCommand { get; }
    public ICommand ToggleHideBlacklistedSpotsCommand { get; }
    public ICommand ToggleShowEroticaCommand { get; }

    public ICommand ToggleSpotFavoriteCommand { get; }
    public ICommand AddSpotToFavoritesCommand { get; }
    public ICommand RemoveSpotFromFavoritesCommand { get; }
    public ICommand ComplainToSpotCommand { get; }

    public bool ShowTrustedOnlyMode
    {
        get => _prefsService.Current.ShowTrustedOnlyMode;
        set
        {
            if (_prefsService.Current.ShowTrustedOnlyMode != value)
            {
                var prefs = _prefsService.Current;
                prefs.ShowTrustedOnlyMode = value;
                _prefsService.Save(prefs);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowTrustedOnlyTooltip));
                _ = RefreshViewAndCountsAsync();
            }
        }
    }

    public string ShowTrustedOnlyTooltip => ShowTrustedOnlyMode
        ? "Toon spots van vertrouwde en onbetrouwbare afzenders"
        : "Toon alleen spots van vertrouwde afzenders";

    public bool HideBlacklistedSpots
    {
        get => _prefsService.Current.HideBlacklistedSpots;
        set
        {
            if (_prefsService.Current.HideBlacklistedSpots != value)
            {
                var prefs = _prefsService.Current;
                prefs.HideBlacklistedSpots = value;
                _prefsService.Save(prefs);
                OnPropertyChanged();
                _ = RefreshViewAndCountsAsync();
            }
        }
    }

    public bool ShowEroticaInSearchResults
    {
        get => _prefsService.Current.ShowEroticaInSearchResults;
        set
        {
            if (_prefsService.Current.ShowEroticaInSearchResults != value)
            {
                var prefs = _prefsService.Current;
                prefs.ShowEroticaInSearchResults = value;
                _prefsService.Save(prefs);
                OnPropertyChanged();
                _ = RefreshViewAndCountsAsync();
            }
        }
    }

    private async Task RefreshViewAndCountsAsync()
    {
        await RefreshSpotsAsync();
        await UpdateFilterCountsAsync();
    }

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

    public event Action? RequestOpenSpotlinkDialog;

    public event Action? RequestOpenSettings;
    public event Action? RequestOpenOnboarding;
    public event Action? RequestOpenReleaseNotes;
    public event Action? RequestAddCustomFilter;
    public event Action<SpotItem>? RequestOpenComplaintDialog;
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
        _extensiveSearch = _prefsService.Current.AdvancedSearch;
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

        _userKeyService = new UserKeyService(_dbService);
        _nzbService = new NzbService(_appPaths, _secretStore, _prefsService);
        _syncService = new SpotSyncService(_appPaths, _secretStore, _dbService, _prefsService, _trustService);
        _commentService = new CommentService(_appPaths, _secretStore, _dbService, _userKeyService);
        _complaintService = new ComplaintService(_appPaths, _secretStore, _dbService, _prefsService, _trustService, _userKeyService);
        _bodyService = new SpotBodyService(_appPaths, _secretStore);
        _searchHistory = new Services.SearchHistoryService(_appPaths);
        _tabPersistence = new Services.TabPersistenceService(_appPaths);
        try
        {
            _thumbService = new Services.SpotThumbService(_appPaths, _secretStore, _prefsService);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Thumbnail service unavailable: {0}", ex.Message);
        }

        _syncService.ProgressChanged += (current, total, msg) =>
        {
            StatusText = msg;
            SyncProgress = current;
            IsSyncing = _syncService.IsSyncing;
        };

        // Meldingen (fase 4): de gedeelde engine uit Spotnet.Core, gevoed door de
        // Mac-database en de macOS-meldingen. Windows doet dit via NotificationHost.
        _notifications = new Spotnet.Notifications.NotificationManager(
            new MacNotificationSpotQuery(dbService),
            new MacNotificationService(_prefsService),
            _appPaths.DataFolder);
        _notifications.AutoUpdateSettingsApplier = (intervalMinutes, force) =>
        {
            var prefs = _prefsService.Current;
            if (force)
            {
                prefs.DbAutoUpdateIntervalMin = intervalMinutes;
            }
            else if (prefs.DbAutoUpdateIntervalMin < 5)
            {
                prefs.DbAutoUpdateIntervalMin = Math.Max(5, intervalMinutes);
            }
            prefs.DbAutoUpdateEnabled = true;
            _prefsService.Save(prefs);
            StartAutoSyncTimer();
        };
        _notifications.UnreadCountChanged += () =>
        {
            void Update()
            {
                UnreadNotificationCount = _notifications.UnreadCount;
            }

            if (_dispatcher.CheckAccess())
            {
                Update();
            }
            else
            {
                _dispatcher.Invoke(Update);
            }
        };

        SpotDetail = new SpotDetailViewModel(_dbService, _nzbService, _commentService, _bodyService);

        DownloadsTab = new DownloadsTabViewModel(new DownloadHistoryService(_appPaths), _prefsService,
            downloadNotificationRecorder: (title, success) => _notifications.NotifyDownloadComplete(title, success));
        SpotDetail.NzbFetched += OnNzbFetched;
        SpotDetail.RequestClose += () => SelectedSpot = null;
        SpotDetail.RequestComplain += spot => RequestOpenComplaintDialog?.Invoke(spot);

        OpenReleaseNotesCommand = new RelayCommand(() => RequestOpenReleaseNotes?.Invoke());
        OpenNotificationCenterCommand = new RelayCommand(() => RequestOpenNotificationCenter?.Invoke());
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
        SearchCommand = new RelayCommand(async () => await SubmitSearchAsync());
        ApplySuggestionCommand = new RelayCommand(param => _ = ApplySuggestionAsync(param as string));
        SetSearchFieldCommand = new RelayCommand(param =>
        {
            if (param is string field)
            {
                SearchField = field;
                OnPropertyChanged(nameof(IsSearchFieldTitle));
                OnPropertyChanged(nameof(IsSearchFieldSender));
                OnPropertyChanged(nameof(IsSearchFieldTag));
            }
        });
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
            _notifications.OnSyncFinished();
        });

        CloseDetailCommand = new RelayCommand(() => SelectedSpot = null);
        OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());
        OpenOnboardingCommand = new RelayCommand(() => RequestOpenOnboarding?.Invoke());
        ToggleSocksProxyCommand = new RelayCommand(ToggleSocksProxy);

        // Windows: ExecuteOpenSpotlink → OpenSpotlinkWindow → OpenSpotlink(link).
        // Het venster zelf blijft in de view; hier staat de koppeling en de parsing.
        OpenSpotlinkCommand = new RelayCommand(() => RequestOpenSpotlinkDialog?.Invoke());

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
                SaveOpenTabs();
            }
        });

        SetSpotsListTypeCommand = new RelayCommand(param =>
        {
            if (param is int type) SpotsListType = type;
        });

        LoadMoreThumbsCommand = new RelayCommand(async () => await LoadMoreThumbsAsync());

        SetFontSizeCommand = new RelayCommand(param =>
        {
            if (param is string direction && direction == "+") SpotsFontSize++;
            else if (param is string dir2 && dir2 == "-") SpotsFontSize--;
        });

        ToggleSaveTabsCommand = new RelayCommand(() => SaveTabs = !SaveTabs);

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

        ToggleShowTrustedOnlyCommand = new RelayCommand(() => ShowTrustedOnlyMode = !ShowTrustedOnlyMode);
        ToggleHideBlacklistedSpotsCommand = new RelayCommand(() => HideBlacklistedSpots = !HideBlacklistedSpots);
        ToggleShowEroticaCommand = new RelayCommand(() => ShowEroticaInSearchResults = !ShowEroticaInSearchResults);

        ToggleSpotFavoriteCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId)) return;
            bool newState = !spot.IsFavorite;
            spot.IsFavorite = newState;
            if (newState)
            {
                await _dbService.AddFavoriteAsync(spot.MsgId);
            }
            else
            {
                await _dbService.RemoveFavoriteAsync(spot.MsgId);
            }
            await UpdateFilterCountsAsync();
        });

        AddSpotToFavoritesCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId) || spot.IsFavorite) return;
            spot.IsFavorite = true;
            await _dbService.AddFavoriteAsync(spot.MsgId);
            await UpdateFilterCountsAsync();
        });

        RemoveSpotFromFavoritesCommand = new RelayCommand(async param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot == null || string.IsNullOrWhiteSpace(spot.MsgId) || !spot.IsFavorite) return;
            spot.IsFavorite = false;
            await _dbService.RemoveFavoriteAsync(spot.MsgId);
            await UpdateFilterCountsAsync();
        });

        ComplainToSpotCommand = new RelayCommand(param =>
        {
            var spot = param as SpotItem ?? SelectedSpot;
            if (spot != null)
            {
                RequestOpenComplaintDialog?.Invoke(spot);
            }
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
        SaveOpenTabs();
    }

    /// <summary>
    /// Schrijft de open spot-tabbladen weg, zoals Windows' SaveTabs: alleen wanneer
    /// SaveTabs aan staat, één "msgid\ttitle"-regel per tabblad in tabs.dat.
    /// </summary>
    private void SaveOpenTabs()
    {
        if (!SaveTabs) return;
        var tabs = new List<Services.SavedTab>();
        foreach (var tab in Tabs.OfType<SpotTabViewModel>())
        {
            if (!string.IsNullOrWhiteSpace(tab.Spot.MsgId))
            {
                tabs.Add(new Services.SavedTab(tab.Spot.MsgId, tab.Spot.Subject));
            }
        }
        _tabPersistence.SaveTabs(tabs);
    }

    /// <summary>
    /// Heropent de opgeslagen spot-tabbladen bij het opstarten, zoals Windows'
    /// ReopenTabs: alleen wanneer SaveTabs aan staat; tabbladen waarvan de spot uit
    /// de database is verjaard worden overgeslagen.
    /// </summary>
    private async Task ReopenSavedTabsAsync()
    {
        if (!SaveTabs) return;
        foreach (var saved in _tabPersistence.LoadTabs())
        {
            try
            {
                var spot = await _dbService.GetSpotByMsgIdAsync(saved.MessageId);
                if (spot == null) continue;

                var detail = new SpotDetailViewModel(_dbService, _nzbService, _commentService, _bodyService);
                var tab = new SpotTabViewModel(spot, detail);
                detail.RequestClose += () => CloseTabCommand.Execute(tab);
                detail.NzbFetched += OnNzbFetched;
                Tabs.Add(tab);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not reopen saved tab {0}", saved.MessageId);
            }
        }
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

        // ── Favorieten filter node ────────────────────────────────────────────
        var favFilter = new FilterItem
        {
            Id = "def_Favorieten",
            Kind = FilterKind.Preset,
            Name = "Favorieten",
            Icon = "⭐",
            Query = "spots.msgid in favorieten"
        };
        FilterTree.Add(favFilter);

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

            // Meldingen (fase 4): engine initialiseren, de periodeke-regelcontrole
            // starten (Windows: een Timer van 60 s in NotificationManager.Initialize)
            // en direct de ongelezen teller vullen.
            _notifications.Initialize();
            _notificationEvalTimer = new System.Threading.Timer(
                _ => _notifications.OnPeriodicTimer(), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
            UnreadNotificationCount = _notifications.UnreadCount;

            // .nzb-bestanden en spotnet://-links van bij het opstarten (Open With,
            // URL-scheme), zoals Windows die uit de pipe-parameters haalt.
            await HandleStartupTargetsAsync(Program.StartupTargets);

            // Tabbladen onthouden (fase 6): de opgeslagen spot-tabbladen heropenen,
            // zoals Windows' ReopenTabs in PrepareWindow.
            await ReopenSavedTabsAsync();
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

    /// <summary>
    /// Opent een spotlink zoals Windows' OpenSpotlink: haal met de regex het
    /// message-id eruit (eventueel met spotnet://-prefix), zoek de spot in de
    /// database en open hem.
    /// </summary>
    public async Task OpenSpotlinkAsync(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return;

        // Windows: regex (spotnet://)?([A-Za-z0-9]+@[\\.-A-Za-z0-9]+)
        var match = System.Text.RegularExpressions.Regex.Match(
            link, "(spotnet://)?([A-Za-z0-9]+@[\\.-A-Za-z0-9]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return;

        string msgId = match.Groups[2].Value;
        if (msgId.Length > 200) return;

        var spot = await _dbService.GetSpotByMsgIdAsync(msgId);
        if (spot == null)
        {
            StatusText = $"Spot niet gevonden in de database: {msgId}";
            return;
        }
        OpenSpot(spot);
    }

    /// <summary>
    /// Verwerkt de doelwitten die met de app meekwamen: een spotnet://-link opent de
    /// spot (zoals Windows' ProcessSpotnetProtocol), een .nzb-pad downloadt de bestanden
    /// direct (zoals ScheduleNzbDownload).
    /// </summary>
    internal async Task HandleStartupTargetsAsync(System.Collections.Generic.IReadOnlyList<string> targets)
    {
        foreach (string target in targets ?? System.Array.Empty<string>())
        {
            try
            {
                if (target.StartsWith("spotnet://", StringComparison.OrdinalIgnoreCase))
                {
                    // Windows: regex (spotnet://)?([A-Za-z0-9]+@[\\.-A-Za-z0-9]+), max 200 tekens.
                    var match = System.Text.RegularExpressions.Regex.Match(
                        target, "(spotnet://)?([A-Za-z0-9]+@[\\.-A-Za-z0-9]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!match.Success || match.Groups[2].Value.Length > 200) continue;

                    string msgId = match.Groups[2].Value;
                    var spot = await _dbService.GetSpotByMsgIdAsync(msgId);
                    if (spot == null)
                    {
                        StatusText = $"Spot niet gevonden in de database: {msgId}";
                        continue;
                    }
                    OpenSpot(spot);
                }
                else if (File.Exists(target))
                {
                    await ImportNzbFileAsync(target);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Startup target kon niet worden verwerkt: {0}", target);
            }
            await Task.Yield();
        }
    }

    /// <summary>
    /// Importeert een .nzb-bestand van schijf: parseert het en zet een downloadjob klaar
    /// in de Downloads-tab, zoals Windows' ScheduleNzbDownload.
    /// </summary>
    internal async Task ImportNzbFileAsync(string nzbPath)
    {
        string xml = await File.ReadAllTextAsync(nzbPath);
        var files = NzbParser.Parse(xml);
        if (files.Count == 0)
        {
            StatusText = $"NZB bevat geen downloadbare bestanden: {Path.GetFileName(nzbPath)}";
            return;
        }

        var prefs = _prefsService.Current;
        string title = Path.GetFileNameWithoutExtension(nzbPath);
        string downloadDir = Path.Combine(
            string.IsNullOrEmpty(prefs.DownloadFolder)
                ? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Spotnet")
                : prefs.DownloadFolder,
            NzbService.SanitizeFileName(title));

        int maxConn = prefs.MaxDownloadConnections > 0 ? prefs.MaxDownloadConnections : 4;
        var connection = new UsenetConnection(_appPaths, _secretStore);
        var job = new NzbDownloadJob(connection, files, downloadDir, maxConn,
            NzbDownloadOptions.FromPreferences(prefs));

        DownloadsTab.Add(new SpotItem { MsgId = "nzb:" + title, Subject = title, Filesize = 0 },
            success: true, nzbPath: nzbPath, message: "NZB geïmporteerd", job: job);
        StatusText = $"NZB geïmporteerd: {title}";
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
                spamReportsThreshold: prefs.NumOfSpamReportsToSpotHide,
                searchField: _searchField,
                extensiveSearch: _extensiveSearch,
                favoritesOnly: _favoritesOnly);

            var previous = Spots;
            Spots = new VirtualSpotCollection(
                (skip, take, _) => LoadSpotPageAsync(filterQuery, keyword, skip, take, sortColumn, sortDirection),
                TotalSpotsCount,
                _dispatcher);
            previous?.Dispose();

            SelectedSpot = null;

            // Thumbnailweergave volgt dezelfde query: herlaad de zichtbare pagina.
            if (IsThumbView)
            {
                _ = LoadMoreThumbsAsync();
            }

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
                _notifications.OnSyncFinished();
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
        OnPropertyChanged(nameof(ShowTrustedOnlyMode));
        OnPropertyChanged(nameof(ShowTrustedOnlyTooltip));
        OnPropertyChanged(nameof(HideBlacklistedSpots));
        OnPropertyChanged(nameof(ShowEroticaInSearchResults));
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
            if (group.Id == "def_Favorieten")
            {
                group.Count = await _dbService.GetFavoritesCountAsync();
            }
            else
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
        }

        foreach (var child in group.Children)
        {
            await UpdateCountsForGroupAsync(child);
        }
    }

    public void Dispose()
    {
        // Tabbladen onthouden: bij het afsluiten de open spot-tabs wegschrijven,
        // zoals Windows dat doet bij het sluiten van het venster.
        SaveOpenTabs();
        StopAutoSyncTimer();
        _notificationEvalTimer?.Dispose();
        _notificationEvalTimer = null;
        _trustService.Dispose();
        _suggestClient.Dispose();
    }
}
