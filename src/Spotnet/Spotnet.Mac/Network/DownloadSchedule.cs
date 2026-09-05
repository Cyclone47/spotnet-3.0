using System;

namespace Spotnet.Mac.Network;

/// <summary>
/// The download schedule: a window of the day inside which downloads are allowed.
/// Port of <c>DownloadQueue.IsDownloaderActiveTime</c> from the Windows client, with
/// the settings read as parameters so the logic is testable.
///
/// Windows checks the queue before every segment and, while outside the window, puts
/// the queue in a pause state checked once a minute. The Mac downloader does the same
/// through <see cref="NzbDownloadJob"/>: a worker that is about to fetch a segment
/// waits inside <see cref="WaitUntilActiveAsync"/> for as long as the window is closed.
///
/// Rules, with the boundary behaviour the Windows branch structure produces:
/// - schedule off                    → always active
/// - start == end                    → always active (the user has not split the day)
/// - start &lt; end (daytime window)  → active inside [start, end]; before start and
///                                     at/after end the queue idles. (At exactly
///                                     now == end Windows' overnight quirk below does
///                                     not apply, so the window closes at end.)
/// - start &gt; end (overnight)        → active from start, through midnight, up to
///                                     and including end; inactive in the open gap
///                                     (end, start). This is the intuitive reading of
///                                     the window the settings dialog promises, and it
///                                     fixes the daytime-gap hole in Windows'
///                                     overnight branch, where !(end &lt; now) →
///                                     now &lt; start declared 12:00 "active" for a
///                                     20:00–06:00 window. Dat kon niet de bedoeling
///                                     zijn: het venster belooft juist dat er én///                                     gedeactiveerd wordt buiten 20:00–06:00.
/// </summary>
public static class DownloadSchedule
{
    /// <summary>How often a paused worker re-checks the clock. Windows: 1 minute.</summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether downloads may run at <paramref name="now"/>, given the schedule
    /// settings. Uses only the time-of-day part of the start and end values, exactly
    /// like Windows.
    /// </summary>
    /// <remarks>
    /// De nachtelijke tak is bewust géén letterlijke kopie van de Windows-branch:
    /// die laat met !(end &lt; now) → now &lt; start een gat open waar een
    /// 20:00–06:00-venster overdag gewoon doorloopt. Zie de klassedocumentatie.
    /// </remarks>
    public static bool IsDownloaderActiveTime(bool scheduleEnabled, TimeSpan start, TimeSpan end, TimeSpan now)
    {
        if (!scheduleEnabled)
        {
            return true;
        }
        if (start == end)
        {
            return true;
        }
        if (start < end)
        {
            return start <= now && now <= end;
        }
        // Overnight window, e.g. 22:00 → 06:00: active from start through midnight
        // up to and including end.
        return now >= start || now <= end;
    }

    /// <summary>
    /// Convenience overload that reads the settings out of a preference object and the
    /// clock, the way the Windows queue reads Settings.Default directly.
    /// </summary>
    public static bool IsDownloaderActiveTime(Services.UserPreferences prefs, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return IsDownloaderActiveTime(
            prefs.DownloaderSchedule,
            prefs.DownloaderStartTime.TimeOfDay,
            prefs.DownloaderEndTime.TimeOfDay,
            now.TimeOfDay);
    }

    /// <summary>
    /// Waits until the window opens (or the token fires). Polls once a minute like the
    /// Windows pause loop, but wakes sooner when the wait is shorter.
    /// </summary>
    public static async System.Threading.Tasks.Task WaitUntilActiveAsync(
        TimeSpan start, TimeSpan end, Func<DateTime> clock, System.Threading.CancellationToken ct)
    {
        while (!IsDownloaderActiveTime(true, start, end, clock().TimeOfDay))
        {
            System.Threading.Tasks.Task delay = System.Threading.Tasks.Task.Delay(RecheckInterval, ct);
            try
            {
                await delay.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
