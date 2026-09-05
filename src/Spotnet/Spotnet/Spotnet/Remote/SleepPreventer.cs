using System;
using System.Runtime.InteropServices;
using NLog;

namespace Spotnet.Remote;

/// <summary>
/// Controls Windows power management to prevent the computer from entering sleep/hibernation
/// while Spotnet Remote is active and the "Keep Awake" setting is enabled.
/// </summary>
public static class SleepPreventer
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static bool _isPreventingSleep;
    private static readonly object Lock = new object();

    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    public static bool IsPreventingSleep
    {
        get
        {
            lock (Lock)
            {
                return _isPreventingSleep;
            }
        }
    }

    /// <summary>
    /// Updates sleep prevention state. When enabled, prevents Windows from entering sleep mode
    /// while still allowing the monitor/display to turn off for energy saving.
    /// </summary>
    public static void UpdateState(bool shouldKeepAwake)
    {
        lock (Lock)
        {
            try
            {
                if (shouldKeepAwake)
                {
                    if (!_isPreventingSleep)
                    {
                        // Away mode allows the display to sleep while keeping CPU/network/Spotnet alive
                        EXECUTION_STATE result = SetThreadExecutionState(
                            EXECUTION_STATE.ES_CONTINUOUS |
                            EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                            EXECUTION_STATE.ES_AWAYMODE_REQUIRED);

                        if (result == 0)
                        {
                            // Away mode might not be supported on some machines; fallback to system required
                            SetThreadExecutionState(
                                EXECUTION_STATE.ES_CONTINUOUS |
                                EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                        }

                        _isPreventingSleep = true;
                        Log.Info("Sleep prevention enabled: PC will stay awake while Spotnet Remote is active.");
                    }
                }
                else
                {
                    if (_isPreventingSleep)
                    {
                        // Restore standard Windows power management
                        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                        _isPreventingSleep = false;
                        Log.Info("Sleep prevention disabled: normal Windows power management restored.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to update execution state for sleep prevention: {0}", ex.Message);
            }
        }
    }
}
