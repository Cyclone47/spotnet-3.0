using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Spotnet.Mac.Models;
using Spotnet.Platform;

namespace Spotnet.Mac.DataVirtualization;

/// <summary>
/// Fetches a window of spots. <paramref name="skip"/> and <paramref name="take"/> map
/// straight onto SQL OFFSET and LIMIT.
/// </summary>
public delegate Task<IReadOnlyList<SpotItem>> SpotPageLoader(int skip, int take, CancellationToken cancellationToken);

/// <summary>
/// A read-only list that reports the full result count but only holds the pages that
/// have actually been looked at.
///
/// This is the macOS counterpart of the Windows client's
/// <c>Spotnet.DataVirtualization.VirtualList</c>. Before it existed the spot list ran
/// one query with <c>LIMIT 100</c>, so a filter matching two hundred thousand spots
/// showed the newest hundred and silently dropped the rest.
///
/// Rows are handed out as <see cref="SpotItem"/> placeholders and filled in place once
/// their page arrives, rather than being replaced. The grid therefore never sees a
/// collection change while scrolling, which is what keeps selection and scroll position
/// steady. The only <see cref="INotifyCollectionChanged"/> event this class raises is
/// the Reset in <see cref="Clear"/>.
/// </summary>
public sealed class VirtualSpotCollection : IList<SpotItem>, IList, INotifyCollectionChanged, IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Rows per fetch. Comfortably more than fits on screen, so a slow scroll rarely waits.</summary>
    public const int DefaultPageSize = 200;

    /// <summary>
    /// Pages kept in memory. At the default page size that is 24 × 200 = 4.800 spots,
    /// roughly a couple of megabytes, after which the least recently touched page is
    /// dropped back to placeholders.
    /// </summary>
    public const int DefaultMaxCachedPages = 24;

    private readonly SpotPageLoader _loader;
    private readonly IUiDispatcher _dispatcher;
    private readonly int _pageSize;
    private readonly int _maxCachedPages;

    /// <summary>Placeholder rows, allocated per page on first touch and never re-created.</summary>
    private readonly Dictionary<int, SpotItem[]> _pages = new();

    /// <summary>Pages whose data has arrived, most recently touched last.</summary>
    private readonly LinkedList<int> _recency = new();

    private readonly HashSet<int> _loaded = new();
    private readonly HashSet<int> _inFlight = new();

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public VirtualSpotCollection(
        SpotPageLoader loader,
        int count,
        IUiDispatcher dispatcher,
        int pageSize = DefaultPageSize,
        int maxCachedPages = DefaultMaxCachedPages)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCachedPages, 1);

        _loader = loader;
        _dispatcher = dispatcher;
        _pageSize = pageSize;
        _maxCachedPages = maxCachedPages;
        Count = count;
    }

    /// <summary>The number of spots the filter matches — not the number fetched.</summary>
    public int Count { get; }

    public int PageSize => _pageSize;

    /// <summary>Pages currently holding real data. Exposed for tests and diagnostics.</summary>
    public int LoadedPageCount => _loaded.Count;

    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    /// <summary>
    /// Only ever a Reset, from <see cref="Invalidate"/>. Scrolling raises nothing: rows
    /// are filled in place and announce themselves through their own PropertyChanged.
    /// <see cref="Count"/> is fixed for the lifetime of an instance — a new filter, sort
    /// or search builds a new one — so there is no INotifyPropertyChanged here.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Raised after a page is filled in. Nothing in the UI needs it — the rows announce
    /// themselves — but a test can await it, and the status bar uses it to stop showing
    /// a spinner.
    /// </summary>
    public event Action<int>? PageLoaded;

    public SpotItem this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));

            int pageIndex = index / _pageSize;
            var page = GetOrCreatePage(pageIndex);
            EnsurePageRequested(pageIndex);
            return page[index % _pageSize];
        }
        set => throw new NotSupportedException("The spot list is read-only; it is a view over the database.");
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("The spot list is read-only; it is a view over the database.");
    }

    private SpotItem[] GetOrCreatePage(int pageIndex)
    {
        if (_pages.TryGetValue(pageIndex, out var existing)) return existing;

        // The last page is short whenever Count is not a whole number of pages.
        int size = Math.Min(_pageSize, Count - (pageIndex * _pageSize));
        var page = new SpotItem[size];
        for (int i = 0; i < size; i++) page[i] = SpotItem.Placeholder();

        _pages[pageIndex] = page;
        return page;
    }

    private void EnsurePageRequested(int pageIndex)
    {
        if (_disposed) return;

        if (_loaded.Contains(pageIndex))
        {
            Touch(pageIndex);
            return;
        }

        if (!_inFlight.Add(pageIndex)) return;

        _ = LoadPageAsync(pageIndex);
    }

    private async Task LoadPageAsync(int pageIndex)
    {
        try
        {
            var rows = await _loader(pageIndex * _pageSize, _pageSize, _cts.Token).ConfigureAwait(false);
            if (_cts.IsCancellationRequested) return;

            // Filling rows touches bindings, so it belongs on the UI thread.
            await _dispatcher.InvokeAsync(() => ApplyPage(pageIndex, rows)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A new filter or sort replaced this collection; the rows are gone anyway.
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not load spot page {0}: {1}", pageIndex, ex.Message);
            _inFlight.Remove(pageIndex);
        }
    }

    private void ApplyPage(int pageIndex, IReadOnlyList<SpotItem> rows)
    {
        if (_disposed) return;

        _inFlight.Remove(pageIndex);

        var page = GetOrCreatePage(pageIndex);
        int fill = Math.Min(page.Length, rows.Count);
        for (int i = 0; i < fill; i++)
        {
            page[i].Fill(rows[i]);
        }

        // Fewer rows came back than the page holds. That means the table shrank under
        // us — a sync ran, or the user deleted spots. The remaining placeholders stay
        // placeholders rather than showing stale data from another page.
        if (rows.Count < page.Length)
        {
            Log.Debug("Page {0} returned {1} of {2} rows; the result set moved underneath.",
                      pageIndex, rows.Count, page.Length);
        }

        _loaded.Add(pageIndex);
        Touch(pageIndex);
        TrimCache();

        PageLoaded?.Invoke(pageIndex);
    }

    private void Touch(int pageIndex)
    {
        var node = _recency.Find(pageIndex);
        if (node != null) _recency.Remove(node);
        _recency.AddLast(pageIndex);
    }

    /// <summary>
    /// Drops the least recently touched pages once the cache is over its limit. A
    /// dropped page keeps its <see cref="SpotItem"/> instances — anything still bound to
    /// them, a selected row for instance, stays valid — but is re-fetched next time it
    /// is scrolled into view.
    /// </summary>
    private void TrimCache()
    {
        while (_recency.Count > _maxCachedPages)
        {
            int oldest = _recency.First!.Value;
            _recency.RemoveFirst();
            _loaded.Remove(oldest);
            _pages.Remove(oldest);
        }
    }

    /// <summary>
    /// Forgets every fetched page so the next scroll re-reads the database. Used after a
    /// sync adds spots without changing which filter is showing.
    /// </summary>
    public void Invalidate()
    {
        _pages.Clear();
        _recency.Clear();
        _loaded.Clear();
        _inFlight.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    // ── IEnumerable ───────────────────────────────────────────────────────────
    // Enumerating the whole thing defeats the point, but ToList() on a small result and
    // the grid's own bookkeeping both go through here, so it works and simply pulls the
    // pages it walks.

    public IEnumerator<SpotItem> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Read-only list surface ────────────────────────────────────────────────

    public int IndexOf(SpotItem item)
    {
        if (item == null) return -1;

        // Only rows already in memory can be found; a linear scan would fetch the whole
        // table. The grid asks this about the selected row, which is loaded by
        // definition.
        foreach (var (pageIndex, page) in _pages)
        {
            int offset = Array.IndexOf(page, item);
            if (offset >= 0) return (pageIndex * _pageSize) + offset;
        }
        return -1;
    }

    int IList.IndexOf(object? value) => value is SpotItem item ? IndexOf(item) : -1;

    public bool Contains(SpotItem item) => IndexOf(item) >= 0;

    bool IList.Contains(object? value) => value is SpotItem item && Contains(item);

    public void CopyTo(SpotItem[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int i = 0; i < Count; i++) array[arrayIndex + i] = this[i];
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
    }

    private static NotSupportedException ReadOnly() =>
        new("The spot list is a read-only view over the database.");

    public void Add(SpotItem item) => throw ReadOnly();
    int IList.Add(object? value) => throw ReadOnly();
    public void Insert(int index, SpotItem item) => throw ReadOnly();
    void IList.Insert(int index, object? value) => throw ReadOnly();
    public bool Remove(SpotItem item) => throw ReadOnly();
    void IList.Remove(object? value) => throw ReadOnly();
    public void RemoveAt(int index) => throw ReadOnly();

    public void Clear() => Invalidate();
}
