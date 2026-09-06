using System;
using System.IO;
using System.Threading.Tasks;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// Tests voor fase 6, deel 2: zoeksuggesties (geschiedenis + Google-parse),
/// tabbladen onthouden (tabs.dat, Windows' msgid\ttitle-formaat), het
/// miniatuur-message-id (Windows' md5-scheme) en de nieuwe voorkeuren.
/// </summary>
public class SearchSuggestionsTabsAndThumbsTests : IDisposable
{
    private readonly TempAppPaths _paths;
    private readonly FakeSecretStore _secretStore;
    private readonly UserPreferencesService _prefsService;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _dbService;

    public SearchSuggestionsTabsAndThumbsTests()
    {
        _paths = new TempAppPaths();
        _secretStore = new FakeSecretStore();
        _prefsService = new UserPreferencesService(_paths);
        _db = new MacSqliteDb(Path.Combine(_paths.DataFolder, "test.db"));
        _dbService = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        _paths.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Zoekgeschiedenis (history.dat) ────────────────────────────────────────

    [Fact]
    public void SearchHistorySavesAndLoadsTerms()
    {
        var history = new SearchHistoryService(_paths);
        history.SaveHistory("linux iso");
        history.SaveHistory("ubuntu");
        history.SaveHistory("linux iso"); // dubbel: wordt genegeerd, zoals Windows

        var reloaded = new SearchHistoryService(_paths);
        Assert.Contains("linux iso", reloaded.HistoryItems);
        Assert.Contains("ubuntu", reloaded.HistoryItems);
        Assert.Equal(2, reloaded.HistoryItems.Count);
    }

    [Fact]
    public void SearchHistoryIgnoresEmptyTerm()
    {
        var history = new SearchHistoryService(_paths);
        history.SaveHistory("");
        Assert.Empty(history.HistoryItems);
    }

    [Fact]
    public void SearchHistoryClearDeletesFile()
    {
        var history = new SearchHistoryService(_paths);
        history.SaveHistory("testterm");
        Assert.True(history.ClearHistory());

        var reloaded = new SearchHistoryService(_paths);
        Assert.Empty(reloaded.HistoryItems);
    }

    // ── Google-suggestie-parser ───────────────────────────────────────────────

    [Fact]
    public void GoogleSuggestParserExtractsSuggestions()
    {
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <toplevel>
              <CompleteSuggestion><suggestion data="linux mint"/></CompleteSuggestion>
              <CompleteSuggestion><suggestion data="linux ubuntu"/></CompleteSuggestion>
            </toplevel>
            """;

        var suggestions = GoogleSuggestParser.Parse(xml);
        Assert.Equal(2, suggestions.Count);
        Assert.Contains("linux mint", suggestions);
        Assert.Contains("linux ubuntu", suggestions);
    }

    [Fact]
    public void GoogleSuggestParserHandlesGarbage()
    {
        Assert.Empty(GoogleSuggestParser.Parse(null));
        Assert.Empty(GoogleSuggestParser.Parse(""));
        Assert.Empty(GoogleSuggestParser.Parse("<<niet xml>>"));
    }

    // ── Tabbladen onthouden (tabs.dat) ────────────────────────────────────────

    [Fact]
    public void TabPersistenceRoundTripsMsgIdAndTitle()
    {
        var tabs = new TabPersistenceService(_paths);
        tabs.SaveTabs(new[]
        {
            new SavedTab("abc123@free.pt", "Mijn spot\tmet tab"),
            new SavedTab("def456@free.pt", "Tweede spot")
        });

        var loaded = tabs.LoadTabs();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("abc123@free.pt", loaded[0].MessageId);
        // Tabs worden uit de titel gehaald, zoals Windows' Replace("\t", "")
        Assert.Equal("Mijn spotmet tab", loaded[0].Title);
        Assert.Equal("def456@free.pt", loaded[1].MessageId);
    }

    [Fact]
    public void TabPersistenceSkipsEmptyMsgIds()
    {
        var tabs = new TabPersistenceService(_paths);
        tabs.SaveTabs(new[] { new SavedTab("", "Geen id") });
        Assert.Empty(tabs.LoadTabs());
    }

    [Fact]
    public void TabPersistenceClearRemovesFile()
    {
        var tabs = new TabPersistenceService(_paths);
        tabs.SaveTabs(new[] { new SavedTab("x@y", "T") });
        tabs.ClearTabs();
        Assert.Empty(tabs.LoadTabs());
    }

    [Fact]
    public async Task MainWindowViewModelSavesAndReopensTabs()
    {
        await _dbService.EnsureCreatedAsync();
        await _dbService.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "spot1@free.pt", Subject = "Eerste spot", Category = 0 }
        });

        var vm1 = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        var spot = await _dbService.GetSpotByMsgIdAsync("spot1@free.pt");
        Assert.NotNull(spot);
        vm1.OpenSpot(spot);
        Assert.Contains(vm1.Tabs, t => t is SpotTabViewModel);
        vm1.Dispose();

        // Nieuwe VM (opstartscenario): de tab wordt heropend.
        using var vm2 = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        await vm2.InitializeAsync();
        Assert.Contains(vm2.Tabs, t => t is SpotTabViewModel st && st.Spot.MsgId == "spot1@free.pt");
    }

    [Fact]
    public async Task MainWindowViewModelSkipsReopenWhenSaveTabsOff()
    {
        await _dbService.EnsureCreatedAsync();
        await _dbService.InsertSpotsAsync(new[]
        {
            new SpotItem { MsgId = "spot2@free.pt", Subject = "Tweede spot", Category = 0 }
        });

        _prefsService.Current.SaveTabs = false;
        _prefsService.Save(_prefsService.Current);

        var vm1 = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        var spot = await _dbService.GetSpotByMsgIdAsync("spot2@free.pt");
        vm1.OpenSpot(spot);
        vm1.Dispose();

        using var vm2 = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        await vm2.InitializeAsync();
        Assert.DoesNotContain(vm2.Tabs, t => t is SpotTabViewModel);
    }

    // ── Miniatuur-message-id (Windows-pariteit) ──────────────────────────────

    [Fact]
    public void ThumbMessageIdMatchesWindowsScheme()
    {
        // Windows: md5("<msgid>sup.secure") in HOOFDLETTERS, tekens 2..12,
        // gevolgd door het id: "<hash10>.<msgid>".
        string thumbId = SpotThumbService.GetThumbMessageId("abcd1234@free.pt");

        int dot = thumbId.IndexOf('.');
        Assert.True(dot == 10, $"Verwacht 10 hash-tekens vóór de punt, gekregen: {thumbId}");
        Assert.Equal("abcd1234@free.pt", thumbId[(dot + 1)..]);
        Assert.Equal(10, dot);

        // Hash-deel is hoofdletter-hex (BitConverter.ToString-pariteit).
        foreach (char c in thumbId[..dot])
        {
            Assert.True(Uri.IsHexDigit(c), $"Geen hex-teken in hash: {c}");
        }
    }

    [Fact]
    public void ThumbMessageIdStripsBrackets()
    {
        string withBrackets = SpotThumbService.GetThumbMessageId("<abcd1234@free.pt>");
        string without = SpotThumbService.GetThumbMessageId("abcd1234@free.pt");
        Assert.Equal(without, withBrackets);
    }

    // ── Voorkeuren ────────────────────────────────────────────────────────────

    [Fact]
    public void NewPreferencesMatchWindowsDefaults()
    {
        var prefs = new UserPreferences();
        Assert.True(prefs.GoogleSuggest);      // Windows: GoogleSuggest default True
        Assert.True(prefs.SaveTabs);           // Windows: SaveTabs default True
        Assert.Equal(0, prefs.SpotsListType);  // Windows: SpotsListType default 0 (lijst)
        Assert.Equal(12, prefs.SpotsFontSize);
        Assert.Equal("free.at", prefs.ThumbsGroup); // Windows: ThumbsGroup default free.at
    }

    [Fact]
    public async Task ViewModelViewTypeAndFontSizePersist()
    {
        var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        Assert.True(vm.IsListView);
        Assert.False(vm.IsThumbView);

        vm.SpotsListType = 3;
        vm.SpotsFontSize = 16;
        vm.Dispose();

        var reloaded = new UserPreferencesService(_paths);
        Assert.Equal(3, reloaded.Current.SpotsListType);
        Assert.Equal(16, reloaded.Current.SpotsFontSize);

        using var vm2 = new MainWindowViewModel(_paths, _secretStore, _dbService, reloaded);
        Assert.False(vm2.IsListView);
        Assert.True(vm2.IsThumbView);
        Assert.Equal(16, vm2.SpotsFontSize);
    }

    [Fact]
    public void FontSizeIsClamped()
    {
        var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        vm.SpotsFontSize = 100;
        Assert.Equal(24, vm.SpotsFontSize);
        vm.SpotsFontSize = 1;
        Assert.Equal(8, vm.SpotsFontSize);
        vm.Dispose();
    }

    // ── Suggesties in de VM ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSuggestionsUsesHistoryWithoutNetwork()
    {
        // GoogleSuggest uit: dan alleen de geschiedenis (deterministisch, geen netwerk).
        _prefsService.Current.GoogleSuggest = false;
        _prefsService.Save(_prefsService.Current);

        var history = new SearchHistoryService(_paths);
        history.SaveHistory("linux mint tips");
        history.SaveHistory("linux debian");

        using var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        vm.SearchText = "linux";
        await vm.UpdateSuggestionsAsync();

        Assert.True(vm.IsSuggestionsOpen);
        Assert.Contains("linux mint tips", vm.Suggestions);
        Assert.Contains("linux debian", vm.Suggestions);
    }

    [Fact]
    public async Task EmptySearchClosesSuggestions()
    {
        using var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);
        vm.SearchText = "";
        await vm.UpdateSuggestionsAsync();
        Assert.False(vm.IsSuggestionsOpen);
        Assert.Empty(vm.Suggestions);
    }
}
