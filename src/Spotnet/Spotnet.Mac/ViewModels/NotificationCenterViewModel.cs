using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Spotnet.Notifications;

namespace Spotnet.Mac.ViewModels;

/// <summary>
/// One row in the meldingcentrum's notification list — the wrapped engine item
/// plus the display fields the list binds to.
/// </summary>
public sealed class NotificationRow
{
    private readonly SpotNotificationItem _item;

    public NotificationRow(SpotNotificationItem item)
    {
        _item = item;
    }

    public SpotNotificationItem Item => _item;
    public string Id => _item.Id;
    public string Title => _item.Title;
    public string Body => _item.Body;
    public string RuleName => string.IsNullOrWhiteSpace(_item.RuleName) ? "" : _item.RuleName;
    public string TimeAgo => _item.TimeAgo;
    public bool IsUnread => !_item.IsRead;
    public string UnreadDot => _item.IsRead ? "" : "●";

    public void Refresh()
    {
        // TimeAgo en de ongelezen-stip veranderen met de tijd en het markeren.
        // De rij heeft geen PropertyChanged; de lijst wordt ververst door de
        // collectie leeg te maken en opnieuw te vullen (net als de regellijst).
    }
}

/// <summary>One row in the meldingcentrum's rule list.</summary>
public sealed class RuleRow
{
    private readonly NotificationRule _rule;

    public RuleRow(NotificationRule rule)
    {
        _rule = rule;
    }

    public NotificationRule Rule => _rule;
    public string Id => _rule.Id;
    public string Name => string.IsNullOrWhiteSpace(_rule.Name) ? "(naamloos)" : _rule.Name;
    public string TypeLabel => _rule.Type == NotificationRuleType.Filter ? "Filter" : "Trefwoord";
    public string Description => _rule.Type == NotificationRuleType.Filter
        ? (string.IsNullOrWhiteSpace(_rule.FilterName) ? _rule.FilterQuery : _rule.FilterName)
        : _rule.Keywords;
    public string IntervalDescription => _rule.IntervalDescription;
    public bool Enabled => _rule.Enabled;
}

/// <summary>
/// Het meldingcentrum-venster: de meldingenlijst en de regels van de gedeelde
/// engine (Spotnet.Core) — de Avalonia-tegenhanger van Windows'
/// NotificationCenterWindow.
/// </summary>
public sealed class NotificationCenterViewModel : ViewModelBase
{
    private readonly NotificationManager _engine;

    public ObservableCollection<NotificationRow> Notifications { get; } = new();
    public ObservableCollection<RuleRow> Rules { get; } = new();

    private NotificationRow? _selectedNotification;
    public NotificationRow? SelectedNotification
    {
        get => _selectedNotification;
        set => SetProperty(ref _selectedNotification, value);
    }

    private RuleRow? _selectedRule;
    public RuleRow? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value) && value != null)
            {
                LoadRuleIntoForm(value.Rule);
            }
        }
    }

    // ── Regelformulier ────────────────────────────────────────────────────────

    private string _ruleFormName = "";
    public string RuleFormName
    {
        get => _ruleFormName;
        set => SetProperty(ref _ruleFormName, value);
    }

    private bool _ruleFormIsFilter;
    public bool RuleFormIsFilter
    {
        get => _ruleFormIsFilter;
        set => SetProperty(ref _ruleFormIsFilter, value);
    }

    private string _ruleFormFilterName = "";
    public string RuleFormFilterName
    {
        get => _ruleFormFilterName;
        set => SetProperty(ref _ruleFormFilterName, value);
    }

    private string _ruleFormFilterQuery = "";
    public string RuleFormFilterQuery
    {
        get => _ruleFormFilterQuery;
        set => SetProperty(ref _ruleFormFilterQuery, value);
    }

    private string _ruleFormKeywords = "";
    public string RuleFormKeywords
    {
        get => _ruleFormKeywords;
        set => SetProperty(ref _ruleFormKeywords, value);
    }

    private int _ruleFormCategoryIndex;
    public int RuleFormCategoryIndex
    {
        get => _ruleFormCategoryIndex;
        set => SetProperty(ref _ruleFormCategoryIndex, value);
    }

    private int _ruleFormIntervalIndex = 1;
    public int RuleFormIntervalIndex
    {
        get => _ruleFormIntervalIndex;
        set => SetProperty(ref _ruleFormIntervalIndex, value);
    }

    private string? _ruleFormEditingId;
    public string RuleFormHeaderText => string.IsNullOrEmpty(_ruleFormEditingId) ? "Nieuwe regel" : "Regel bewerken";

    // ── Opties ────────────────────────────────────────────────────────────────

    private bool _showDesktopNotifications;
    public bool ShowDesktopNotifications
    {
        get => _showDesktopNotifications;
        set
        {
            if (SetProperty(ref _showDesktopNotifications, value))
            {
                _engine.SetDesktopNotificationsEnabled(value);
            }
        }
    }

    private int _autoSyncIntervalIndex = 1;
    public int AutoSyncIntervalIndex
    {
        get => _autoSyncIntervalIndex;
        set
        {
            if (SetProperty(ref _autoSyncIntervalIndex, value))
            {
                _engine.SetAutoSyncInterval(AutoSyncIntervalValues[Math.Max(0, Math.Min(value, AutoSyncIntervalValues.Length - 1))]);
            }
        }
    }

    public int[] AutoSyncIntervalValues { get; } = { 5, 15, 30, 60, 480, 1440 };
    public string[] AutoSyncIntervalLabels { get; } = { "Elke 5 minuten", "Elke 15 minuten", "Elke 30 minuten", "Elk uur", "Elke 8 uur", "Elke 24 uur" };

    public string[] CategoryLabels { get; } = { "Alle categorieën", "Films", "Muziek", "Spellen", "Applicaties", "Boeken", "Series", "Erotiek" };
    public int[] CategoryValues { get; } = { 0, 1, 2, 3, 4, 5, 6, 9 };

    public int[] RuleIntervalValues { get; } = { 0, 15, 30, 60, 480, 1440 };
    public string[] RuleIntervalLabels { get; } = { "Direct bij elke sync", "Elke 15 minuten", "Elke 30 minuten", "Elk uur", "Elke 8 uur", "Elke 24 uur" };

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public RelayCommand MarkAllReadCommand { get; }
    public RelayCommand DeleteNotificationCommand { get; }
    public RelayCommand ClearAllNotificationsCommand { get; }
    public RelayCommand ToggleRuleCommand { get; }
    public RelayCommand DeleteRuleCommand { get; }
    public RelayCommand TestRuleCommand { get; }
    public RelayCommand NewRuleCommand { get; }
    public RelayCommand SaveRuleCommand { get; }

    public NotificationCenterViewModel(NotificationManager engine)
    {
        _engine = engine;

        MarkAllReadCommand = new RelayCommand(() => _engine.MarkAllAsRead());
        DeleteNotificationCommand = new RelayCommand(() =>
        {
            if (SelectedNotification is { } row)
            {
                _engine.DeleteNotification(row.Id);
            }
        });
        ClearAllNotificationsCommand = new RelayCommand(() => _engine.ClearAllNotifications());
        ToggleRuleCommand = new RelayCommand(param =>
        {
            if (param is RuleRow row)
            {
                _engine.ToggleRule(row.Id);
            }
        });
        DeleteRuleCommand = new RelayCommand(param =>
        {
            if (param is RuleRow row)
            {
                _engine.DeleteRule(row.Id);
                if (SelectedRule?.Id == row.Id)
                {
                    NewRule();
                }
            }
        });
        TestRuleCommand = new RelayCommand(param =>
        {
            if (param is RuleRow row)
            {
                _ = TestRuleAsync(row.Id);
            }
        });
        NewRuleCommand = new RelayCommand(NewRule);
        SaveRuleCommand = new RelayCommand(SaveRule);

        _engine.NotificationsUpdated += RefreshNotifications;
        _engine.RulesUpdated += RefreshRules;
        _engine.UnreadCountChanged += RefreshNotifications;

        ShowDesktopNotifications = engine.Config.WindowsNotificationsEnabled;
        int idx = Array.IndexOf(AutoSyncIntervalValues, engine.Config.AutoSyncIntervalMinutes);
        _autoSyncIntervalIndex = idx >= 0 ? idx : 1;

        RefreshNotifications();
        RefreshRules();
    }

    /// <summary>
    /// Koppelt de abonnees op de langlevende engine los — de taak van het venster
    /// bij het sluiten, anders blijft de engine een dood viewmodel bijhouden.
    /// </summary>
    public void DetachEngineEvents()
    {
        _engine.NotificationsUpdated -= RefreshNotifications;
        _engine.RulesUpdated -= RefreshRules;
        _engine.UnreadCountChanged -= RefreshNotifications;
    }

    private async Task TestRuleAsync(string ruleId)
    {
        StatusText = "Regel testen...";
        var result = await _engine.TestRuleNowAsync(ruleId);
        StatusText = result != null
            ? $"Test geslaagd: {result.SpotCount} spot(s) gevonden en een melding getoond."
            : "Geen recente spots gevonden die matchen met deze regel.";
    }

    private void NewRule()
    {
        _ruleFormEditingId = null;
        RuleFormName = "";
        RuleFormIsFilter = false;
        RuleFormFilterName = "";
        RuleFormFilterQuery = "";
        RuleFormKeywords = "";
        RuleFormCategoryIndex = 0;
        RuleFormIntervalIndex = 1;
        OnPropertyChanged(nameof(RuleFormHeaderText));
    }

    private void LoadRuleIntoForm(NotificationRule rule)
    {
        _ruleFormEditingId = rule.Id;
        RuleFormName = rule.Name;
        RuleFormIsFilter = rule.Type == NotificationRuleType.Filter;
        RuleFormFilterName = rule.FilterName;
        RuleFormFilterQuery = rule.FilterQuery;
        RuleFormKeywords = rule.Keywords;
        int catIdx = rule.Category.HasValue ? Array.IndexOf(CategoryValues, rule.Category.Value) : 0;
        RuleFormCategoryIndex = catIdx >= 0 ? catIdx : 0;
        int ivIdx = Array.IndexOf(RuleIntervalValues, rule.CheckIntervalMinutes);
        RuleFormIntervalIndex = ivIdx >= 0 ? ivIdx : 1;
        OnPropertyChanged(nameof(RuleFormHeaderText));
    }

    private void SaveRule()
    {
        if (string.IsNullOrWhiteSpace(RuleFormName))
        {
            StatusText = "Geef de regel een naam.";
            return;
        }

        var rule = new NotificationRule
        {
            Id = _ruleFormEditingId ?? Guid.NewGuid().ToString("N"),
            Name = RuleFormName.Trim(),
            Type = RuleFormIsFilter ? NotificationRuleType.Filter : NotificationRuleType.Keyword,
            CheckIntervalMinutes = RuleIntervalValues[Math.Max(0, Math.Min(RuleFormIntervalIndex, RuleIntervalValues.Length - 1))],
            Enabled = true
        };

        if (RuleFormIsFilter)
        {
            rule.FilterName = RuleFormFilterName.Trim();
            rule.FilterQuery = RuleFormFilterQuery.Trim();
            // Een bestaande query blijft staan; een lege query matcht alleen op cat_.
        }
        else
        {
            rule.Keywords = RuleFormKeywords.Trim();
            int cat = CategoryValues[Math.Max(0, Math.Min(RuleFormCategoryIndex, CategoryValues.Length - 1))];
            rule.Category = cat > 0 ? cat : null;
        }

        // Een nieuwe regel begint op het huidige maximum, zodat hij niet op al
        // de spots in de database springt — hetzelfde als Windows' AddOrUpdateRule.
        _engine.AddOrUpdateRule(rule);
        StatusText = $"Regel '{rule.Name}' opgeslagen.";
        NewRule();
    }

    private void RefreshNotifications()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshNotifications);
            return;
        }

        var selectedId = SelectedNotification?.Id;
        Notifications.Clear();
        foreach (var n in _engine.Config.Notifications.OrderByDescending(n => n.CreatedAtUtc))
        {
            Notifications.Add(new NotificationRow(n));
        }
        SelectedNotification = selectedId != null ? Notifications.FirstOrDefault(r => r.Id == selectedId) : null;
    }

    private void RefreshRules()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshRules);
            return;
        }

        Rules.Clear();
        foreach (var r in _engine.Config.Rules.OrderBy(r => r.Name))
        {
            Rules.Add(new RuleRow(r));
        }
    }
}
