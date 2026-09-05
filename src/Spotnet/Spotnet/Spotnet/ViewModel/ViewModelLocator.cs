using System;

namespace Spotnet.ViewModel;

/// <summary>
/// The view models the XAML binds to, one instance each.
/// </summary>
/// <remarks>
/// This used MVVM Light's SimpleIoc behind CommonServiceLocator, but only ever to hold
/// five singletons with parameterless constructors - nothing was injected, resolved by
/// interface, or replaced in a test. Five lazy fields do the same thing without the two
/// packages, both of which were .NET Framework only.
/// </remarks>
public class ViewModelLocator
{
	private static readonly Lazy<VisibilityViewModel> VisibilityInstance =
		new Lazy<VisibilityViewModel>(() => new VisibilityViewModel());

	private static readonly Lazy<SpotsListViewModel> SpotsListInstance =
		new Lazy<SpotsListViewModel>(() => new SpotsListViewModel());

	private static readonly Lazy<StatusBarViewModel> StatusBarInstance =
		new Lazy<StatusBarViewModel>(() => new StatusBarViewModel());

	private static readonly Lazy<MainWindowViewModel> MainWindowInstance =
		new Lazy<MainWindowViewModel>(() => new MainWindowViewModel());

	private static readonly Lazy<SocksProxyTooltipViewModel> SocksProxyTooltipInstance =
		new Lazy<SocksProxyTooltipViewModel>(() => new SocksProxyTooltipViewModel());

	public VisibilityViewModel Visibility => VisibilityInstance.Value;

	public SpotsListViewModel SpotsList => SpotsListInstance.Value;

	public StatusBarViewModel StatusBar => StatusBarInstance.Value;

	public MainWindowViewModel MainWindow => MainWindowInstance.Value;

	public SocksProxyTooltipViewModel SocksProxyTooltip => SocksProxyTooltipInstance.Value;

	public static void Cleanup()
	{
	}
}
