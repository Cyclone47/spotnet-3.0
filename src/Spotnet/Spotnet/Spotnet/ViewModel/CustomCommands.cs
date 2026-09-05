using System.Windows.Input;

namespace Spotnet.ViewModel;

public static class CustomCommands
{
	public static RoutedCommand CustomCommand = new RoutedCommand();

	public static RoutedCommand CopySpotToWhiteCommand = new RoutedCommand();

	public static RoutedCommand CopySpotToBlackCommand = new RoutedCommand();

	public static RoutedCommand CopyPosterAndModulusToWhiteCommand = new RoutedCommand();

	public static RoutedCommand CopyPosterAndModulusToBlackCommand = new RoutedCommand();

	public static RoutedCommand UpdateDbCommand = new RoutedCommand();

	public static RoutedCommand AddNewSpotCommand = new RoutedCommand();

	public static RoutedCommand OpenNzbCommand = new RoutedCommand();

	public static RoutedCommand OpenSpotlinkCommand = new RoutedCommand();

	public static RoutedCommand SelectProviderCommand = new RoutedCommand();

	public static RoutedCommand DownloadFolderChangeCommand = new RoutedCommand();

	public static RoutedCommand CloseTabCommand = new RoutedCommand();

	public static RoutedCommand TestCommand = new RoutedCommand();

	public static RoutedCommand GotoTab1 = new RoutedCommand();

	public static RoutedCommand GotoTab2 = new RoutedCommand();

	public static RoutedCommand GotoTab3 = new RoutedCommand();

	public static RoutedCommand GotoTab4 = new RoutedCommand();

	public static RoutedCommand GotoTab5 = new RoutedCommand();

	public static RoutedCommand GotoTab6 = new RoutedCommand();

	public static RoutedCommand GotoTab7 = new RoutedCommand();

	public static RoutedCommand GotoTab8 = new RoutedCommand();

	public static RoutedCommand GotoTab9 = new RoutedCommand();
}
