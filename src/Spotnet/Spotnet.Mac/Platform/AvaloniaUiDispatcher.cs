using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Spotnet.Platform;

namespace Spotnet.Mac.Platform;

/// <summary>
/// Avalonia-based UI dispatcher implementation for macOS.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }

    public void Invoke(Action action)
    {
        if (CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }

    public Task InvokeAsync(Action action)
    {
        if (CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (CheckAccess())
        {
            return Task.FromResult(func());
        }
        return Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }
}
