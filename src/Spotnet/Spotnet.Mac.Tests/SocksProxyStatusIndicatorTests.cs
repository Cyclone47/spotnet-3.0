using System;
using System.Collections.Generic;
using Spotnet.Mac.DAL;
using Spotnet.Mac.Services;
using Spotnet.Mac.ViewModels;
using Spotnet.Platform;
using Xunit;

namespace Spotnet.Mac.Tests;

public class SocksProxyStatusIndicatorTests : IDisposable
{
    private readonly TempAppPaths _paths;
    private readonly FakeSecretStore _secretStore;
    private readonly UserPreferencesService _prefsService;
    private readonly MacSqliteDb _db;
    private readonly SpotDatabaseService _dbService;

    public SocksProxyStatusIndicatorTests()
    {
        _paths = new TempAppPaths();
        _secretStore = new FakeSecretStore();
        _prefsService = new UserPreferencesService(_paths);
        _db = new MacSqliteDb(System.IO.Path.Combine(_paths.DataFolder, "test.db"));
        _dbService = new SpotDatabaseService(_db);
    }

    public void Dispose()
    {
        _paths.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StatusIndicatorShowsDisabledByDefault()
    {
        using var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);

        Assert.False(vm.UseSocksProxy);
        Assert.Equal("🔓", vm.SocksProxyIcon);
        Assert.Equal("#888888", vm.SocksProxyForeground);
        Assert.Contains("uitgeschakeld", vm.SocksProxyToolTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToggleWhenProxyConfiguredTogglesStateAndSavesPreferences()
    {
        var prefs = _prefsService.Current;
        prefs.SocksProxyHost = "127.0.0.1";
        prefs.SocksProxyPort = 1080;
        prefs.UseSocksProxy = false;
        _prefsService.Save(prefs);

        using var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);

        var changedProperties = new List<string>();
        vm.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProperties.Add(e.PropertyName); };

        vm.ToggleSocksProxyCommand.Execute(null);

        Assert.True(vm.UseSocksProxy);
        Assert.Equal("🔒", vm.SocksProxyIcon);
        Assert.Equal("#39A633", vm.SocksProxyForeground);
        Assert.Contains("ingeschakeld", vm.SocksProxyToolTip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("127.0.0.1:1080", vm.SocksProxyToolTip);

        Assert.Contains(nameof(vm.UseSocksProxy), changedProperties);
        Assert.Contains(nameof(vm.SocksProxyIcon), changedProperties);
        Assert.Contains(nameof(vm.SocksProxyForeground), changedProperties);
        Assert.Contains(nameof(vm.SocksProxyToolTip), changedProperties);

        // Check persisted to disk
        var reloaded = new UserPreferencesService(_paths);
        Assert.True(reloaded.Current.UseSocksProxy);
    }

    [Fact]
    public void ToggleWhenProxyNotConfiguredRequestsOpenSettings()
    {
        var prefs = _prefsService.Current;
        prefs.SocksProxyHost = "";
        prefs.UseSocksProxy = false;
        _prefsService.Save(prefs);

        using var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);

        bool openSettingsRequested = false;
        vm.RequestOpenSettings += () => openSettingsRequested = true;

        vm.ToggleSocksProxyCommand.Execute(null);

        Assert.True(openSettingsRequested);
        Assert.False(vm.UseSocksProxy); // Should not enable without host
    }

    [Fact]
    public async System.Threading.Tasks.Task OnSettingsSavedNotifiesProxyIndicatorChange()
    {
        await _dbService.EnsureCreatedAsync();
        using var vm = new MainWindowViewModel(_paths, _secretStore, _dbService, _prefsService);

        var changedProperties = new List<string>();
        vm.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProperties.Add(e.PropertyName); };

        vm.OnSettingsSaved();

        Assert.Contains(nameof(vm.UseSocksProxy), changedProperties);
        Assert.Contains(nameof(vm.SocksProxyIcon), changedProperties);
        Assert.Contains(nameof(vm.SocksProxyForeground), changedProperties);
        Assert.Contains(nameof(vm.SocksProxyToolTip), changedProperties);
    }
}
