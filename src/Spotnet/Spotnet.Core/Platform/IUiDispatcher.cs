using System;
using System.Threading.Tasks;

namespace Spotnet.Platform;

/// <summary>
/// Cross-platform UI dispatcher contract abstracting WPF Dispatcher on Windows
/// and Avalonia Dispatcher on macOS.
/// </summary>
public interface IUiDispatcher
{
	bool CheckAccess();
	void Invoke(Action action);
	Task InvokeAsync(Action action);
	Task<T> InvokeAsync<T>(Func<T> func);
}
