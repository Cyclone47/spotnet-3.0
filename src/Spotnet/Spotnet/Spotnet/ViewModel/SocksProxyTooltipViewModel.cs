using System.Windows.Media;
using Spotnet.Mvvm;
using NLog;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.ViewModel;

public class SocksProxyTooltipViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public string SettingsIconToolTip => "Settings";

	public string HelpIconToolTip => "Help";

	public string ToggleSwitchIcon
	{
		get
		{
			if (!Settings.Default.UseSocksProxy)
			{
				return "\uf204";
			}
			return "\uf205";
		}
	}

	public Brush ToggleSwitchIconColor
	{
		get
		{
			if (!Settings.Default.UseSocksProxy)
			{
				return new SolidColorBrush(Colors.Gray);
			}
			return new BrushConverter().ConvertFromString("#00A600") as SolidColorBrush;
		}
	}

	public string ConnectionStatus
	{
		get
		{
			if (!Settings.Default.UseSocksProxy)
			{
				return "DISCONNECTED";
			}
			return "CONNECTED";
		}
	}

	public SocksProxyTooltipViewModel()
	{
		SocksProxy.StateChanged += delegate
		{
			RaisePropertyChanged("ToggleSwitchIcon");
			RaisePropertyChanged("ToggleSwitchIconColor");
			RaisePropertyChanged("ConnectionStatus");
		};
	}
}
