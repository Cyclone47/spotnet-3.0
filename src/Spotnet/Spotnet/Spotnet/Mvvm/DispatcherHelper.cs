using System;
using System.Windows;
using System.Windows.Threading;

namespace Spotnet.Mvvm.Threading;

/// <summary>
/// Marshals work onto the UI thread.
/// </summary>
/// <remarks>
/// Replaces <c>GalaSoft.MvvmLight.Threading.DispatcherHelper</c>, keeping the same four
/// members so the ninety-odd call sites did not have to change. See
/// <see cref="Spotnet.Mvvm.ViewModelBase"/> for why MVVM Light was dropped.
///
/// One deliberate difference: MVVM Light threw if <see cref="Initialize"/> had not run.
/// This falls back to the application dispatcher instead. Background work reaches these
/// methods during startup and shutdown, when initialization has either not happened yet
/// or no longer matters, and an exception there is worse than simply finding the right
/// thread.
/// </remarks>
public static class DispatcherHelper
{
	private static Dispatcher _uiDispatcher;

	/// <summary>The UI thread's dispatcher.</summary>
	public static Dispatcher UIDispatcher
	{
		get
		{
			if (_uiDispatcher != null)
			{
				return _uiDispatcher;
			}
			return Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
		}
		private set
		{
			_uiDispatcher = value;
		}
	}

	/// <summary>Records the calling thread as the UI thread. Called once, from startup.</summary>
	public static void Initialize()
	{
		if (_uiDispatcher == null || !_uiDispatcher.Thread.IsAlive)
		{
			UIDispatcher = Dispatcher.CurrentDispatcher;
		}
	}

	/// <summary>Forgets the recorded dispatcher.</summary>
	public static void Reset()
	{
		UIDispatcher = null;
	}

	/// <summary>
	/// Runs <paramref name="action"/> on the UI thread, immediately when already there.
	/// </summary>
	public static void CheckBeginInvokeOnUI(Action action)
	{
		if (action == null)
		{
			return;
		}
		Dispatcher dispatcher = UIDispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			dispatcher.BeginInvoke(action);
		}
	}

	/// <summary>Queues <paramref name="action"/> on the UI thread without waiting.</summary>
	public static DispatcherOperation RunAsync(Action action)
	{
		return UIDispatcher?.BeginInvoke(action);
	}
}
