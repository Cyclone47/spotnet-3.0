using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Spotnet.Mvvm;

/// <summary>
/// Change notification for view models.
/// </summary>
/// <remarks>
/// Replaces <c>GalaSoft.MvvmLight.ViewModelBase</c>. MVVM Light was unmaintained and
/// .NET Framework only, and it also supplied System.Windows.Interactivity, which does not
/// run on modern .NET at all. What this application took from it was small enough to keep
/// here: a base class raising PropertyChanged, a dispatcher helper, and a registry of
/// five view models. None of its messenger, command or design-time support was used.
///
/// The namespace mirrors the one it replaces, so the change at every call site is the
/// using directive and nothing else.
/// </remarks>
public class ViewModelBase : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler PropertyChanged;

	/// <summary>
	/// Raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/>.
	/// </summary>
	/// <remarks>
	/// Public rather than protected, because a few view models raise notifications for
	/// rows they own. Defaulted to the calling member so new code need not repeat the
	/// name, though every existing call passes it explicitly.
	/// </remarks>
	public virtual void RaisePropertyChanged([CallerMemberName] string propertyName = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
