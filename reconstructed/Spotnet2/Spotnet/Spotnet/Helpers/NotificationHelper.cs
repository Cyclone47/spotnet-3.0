using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using NLog;
using Spotnet.Extensions;
using Spotnet.Mvvm.Threading;
using Spotnet.Properties;

namespace Spotnet.Helpers;

/// <summary>
/// Windows desktop notifications for the few things worth interrupting the user about.
/// </summary>
/// <remarks>
/// Built on the shell's own notification icon rather than a toast library. Windows 10 and
/// 11 render a balloon from a notification icon as a real toast and file it in the Action
/// Center, so this gets the native presentation without a package that would need an
/// AppUserModelID and a registered Start menu shortcut to work at all - and Spotnet is also
/// installed portable, where no such shortcut exists.
///
/// The main window already owns a notification icon for minimise-to-tray, and
/// <see cref="AttachTrayIcon"/> hands it over so there is never a second entry in the tray.
/// Without one attached - before the window exists - an icon is created and owned here.
///
/// Either way the icon is only made visible around a notification and hidden again
/// afterwards, on a timer. The previous code raised the balloon and restored the icon's
/// visibility in the same breath; a hidden icon takes its balloon with it, which is why
/// those notifications so often never appeared. Whatever the icon's visibility was before
/// is restored, so a window sitting minimised in the tray keeps its icon.
///
/// Everything here is best-effort. A notification that cannot be shown is logged and
/// dropped; it never affects the download or the repair that triggered it.
/// </remarks>
public static class NotificationHelper
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly object SyncRoot = new object();

	/// <summary>How long the icon stays alive after a balloon is raised.</summary>
	private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(12.0);

	/// <summary>
	/// How long the shell gets to register a newly added icon before it is asked for a
	/// balloon on it.
	/// </summary>
	private static readonly TimeSpan IconSettleDelay = TimeSpan.FromMilliseconds(750.0);

	/// <summary>
	/// The shell truncates beyond roughly this, and a spot title is often far longer.
	/// </summary>
	private const int MaxBodyLength = 200;

	private static NotifyIcon _icon;

	/// <summary>True when <see cref="_icon"/> belongs to the main window, not to us.</summary>
	private static bool _iconIsBorrowed;

	/// <summary>The borrowed icon's visibility before a notification took it over.</summary>
	private static bool _visibilityBeforeNotification;

	private static System.Windows.Threading.DispatcherTimer _hideTimer;

	private static bool _shutdown;

	/// <summary>Tells whether the user wants notifications at all.</summary>
	private static bool Enabled
	{
		get
		{
			try
			{
				return Settings.Default.ShowDesktopNotifications;
			}
			catch (Exception)
			{
				// Settings can be unreadable during a profile migration; silence is the
				// safe answer.
				return false;
			}
		}
	}

	/// <summary>
	/// Hands over the main window's notification icon, so notifications use it instead of
	/// adding a second icon to the tray.
	/// </summary>
	public static void AttachTrayIcon(NotifyIcon icon)
	{
		lock (SyncRoot)
		{
			if (icon == null || _shutdown)
			{
				return;
			}
			// Anything created here before the window came up is no longer needed.
			if (_icon != null && !_iconIsBorrowed)
			{
				try
				{
					_icon.Visible = false;
					_icon.Dispose();
				}
				catch (Exception ex)
				{
					Log.Debug("Failed to drop the temporary notification icon: " + ex.Message);
				}
			}
			_icon = icon;
			_iconIsBorrowed = true;
		}
	}

	/// <summary>
	/// Raises a notification on the user's explicit request, from the settings button.
	/// </summary>
	/// <remarks>
	/// Deliberately ignores <see cref="Settings.ShowDesktopNotifications"/>. The point of the
	/// button is to separate "Windows is not delivering it" from "a setting is off", and a
	/// test that silently did nothing because of a setting would answer neither. The log line
	/// it leaves behind says which side of that line a failure falls on.
	/// </remarks>
	public static void ShowTest()
	{
		Log.Info("Test notification requested from the settings window.");
		string text = Shorten(Words.NotificationTestBody);
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			ShowOnUiThread(Words.NotificationTestTitle, text);
		});
	}

	/// <summary>A download finished and, where applicable, unpacked.</summary>
	public static void NotifyDownloadFinished(string spotTitle)
	{
		Show(Words.NotificationDownloadFinished, spotTitle);
	}

	/// <summary>A database rebuild or repair finished.</summary>
	public static void NotifyDatabaseRecovered(string detail)
	{
		Show(Words.NotificationDatabaseReady, detail);
	}

	/// <summary>
	/// Raises one notification. Safe to call from any thread; the work is marshalled onto
	/// the UI thread, which is the one with a message loop for the shell to talk to.
	/// </summary>
	public static void Show(string title, string body)
	{
		if (title.IsNullOrWhiteSpace())
		{
			Log.Info("Desktop notification skipped: it has no title.");
			return;
		}
		if (_shutdown)
		{
			Log.Info("Desktop notification skipped: Spotnet is closing.");
			return;
		}
		if (!Enabled)
		{
			Log.Info("Desktop notification suppressed by the ShowDesktopNotifications setting: " + title);
			return;
		}
		string text = Shorten(body);
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			ShowOnUiThread(title, text);
		});
	}

	/// <summary>Drops the notification icon. Called once, as the application closes.</summary>
	public static void Shutdown()
	{
		lock (SyncRoot)
		{
			_shutdown = true;
			_hideTimer?.Stop();
			_hideTimer = null;
			if (_icon != null && !_iconIsBorrowed)
			{
				try
				{
					_icon.Visible = false;
					_icon.Dispose();
				}
				catch (Exception ex)
				{
					Log.Debug("Failed to remove the notification icon: " + ex.Message);
				}
			}
			_icon = null;
			_iconIsBorrowed = false;
		}
	}

	private static void ShowOnUiThread(string title, string body)
	{
		try
		{
			lock (SyncRoot)
			{
				if (_shutdown)
				{
					return;
				}
				NotifyIcon icon = _icon ?? (_icon = CreateIcon());
				if (icon == null)
				{
					return;
				}
				// Only remember the visibility of the first notification in a burst; a
				// later one would otherwise record the icon we made visible ourselves.
				if (_hideTimer == null || !_hideTimer.IsEnabled)
				{
					_visibilityBeforeNotification = icon.Visible;
				}
				icon.BalloonTipTitle = title;
				// An empty body gives no balloon at all, so the title stands in for it.
				icon.BalloonTipText = body.IsNullOrWhiteSpace() ? title : body;
				icon.BalloonTipIcon = ToolTipIcon.Info;
				bool wasAlreadyInTheTray = icon.Visible;
				icon.Visible = true;
				RestartHideTimer();
				if (wasAlreadyInTheTray)
				{
					RaiseBalloon(icon, title);
				}
				else
				{
					// Making the icon visible sends the shell a NIM_ADD, and a balloon asked
					// for in the same breath arrives before the shell has finished adding the
					// icon it belongs to - which it then drops on the floor. Minimise to tray
					// is off by default, so the icon is hidden almost every time and almost
					// every notification was being lost this way. Give the shell a turn of the
					// message pump first.
					StartBalloonAfterTheIconSettles(title);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("Failed to show a desktop notification: " + ex.Message);
		}
	}

	/// <summary>Raises the balloon once the shell has had time to add the icon.</summary>
	private static void StartBalloonAfterTheIconSettles(string title)
	{
		System.Windows.Threading.DispatcherTimer settle = new System.Windows.Threading.DispatcherTimer
		{
			Interval = IconSettleDelay
		};
		settle.Tick += delegate
		{
			settle.Stop();
			lock (SyncRoot)
			{
				if (!_shutdown && _icon != null)
				{
					RaiseBalloon(_icon, title);
					// The icon has to outlive the balloon, not the request that asked for it.
					RestartHideTimer();
				}
			}
		};
		settle.Start();
	}

	private static void RaiseBalloon(NotifyIcon icon, string title)
	{
		try
		{
			icon.ShowBalloonTip((int)VisibleFor.TotalMilliseconds);
			Log.Info("Desktop notification raised: " + title);
		}
		catch (Exception ex)
		{
			Log.Warn("The shell refused a desktop notification: " + ex.Message);
		}
	}

	/// <remarks>
	/// Restarted rather than stacked, so a burst of finished downloads keeps the icon alive
	/// until the last of them has had its time on screen.
	/// </remarks>
	private static void RestartHideTimer()
	{
		if (_hideTimer == null)
		{
			_hideTimer = new System.Windows.Threading.DispatcherTimer { Interval = VisibleFor };
			_hideTimer.Tick += delegate
			{
				lock (SyncRoot)
				{
					_hideTimer?.Stop();
					if (_icon != null)
					{
						_icon.Visible = _visibilityBeforeNotification;
					}
				}
			};
		}
		_hideTimer.Stop();
		_hideTimer.Start();
	}

	private static NotifyIcon CreateIcon()
	{
		Icon shellIcon = LoadApplicationIcon();
		if (shellIcon == null)
		{
			Log.Debug("No icon available, so no desktop notifications.");
			return null;
		}
		NotifyIcon created = new NotifyIcon
		{
			Icon = shellIcon,
			Text = "Spotnet",
			Visible = false
		};
		// Clicking the notification brings Spotnet forward, which is the only thing the
		// user could want from it.
		created.BalloonTipClicked += delegate
		{
			ActivateMainWindow();
		};
		return created;
	}

	/// <remarks>
	/// The .ico is compiled in as a WPF resource and is also the executable's own icon, so
	/// either route produces the same image. The pack URI is tried first because it works
	/// under a single-file host, where the assembly has no file path to extract from.
	/// </remarks>
	private static Icon LoadApplicationIcon()
	{
		try
		{
			System.Windows.Resources.StreamResourceInfo resource = System.Windows.Application.GetResourceStream(
				new Uri("pack://application:,,,/Resources/ImagesInternal/spotnet.ico", UriKind.Absolute));
			if (resource?.Stream != null)
			{
				using (resource.Stream)
				{
					return new Icon(resource.Stream);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Debug("Could not read the packed application icon: " + ex.Message);
		}
		try
		{
			string path = Assembly.GetEntryAssembly()?.Location;
			if (!path.IsNullOrEmpty())
			{
				return Icon.ExtractAssociatedIcon(path);
			}
		}
		catch (Exception ex)
		{
			Log.Debug("Could not read the executable's icon: " + ex.Message);
		}
		return SystemIcons.Application;
	}

	private static void ActivateMainWindow()
	{
		try
		{
			Window main = System.Windows.Application.Current?.MainWindow;
			if (main == null)
			{
				return;
			}
			if (main.WindowState == WindowState.Minimized)
			{
				main.WindowState = WindowState.Normal;
			}
			main.Activate();
		}
		catch (Exception ex)
		{
			Log.Debug("Failed to activate the main window from a notification: " + ex.Message);
		}
	}

	/// <summary>Cuts an over-long body at a word boundary rather than mid-word.</summary>
	internal static string Shorten(string text)
	{
		if (text == null)
		{
			return "";
		}
		string trimmed = text.Trim();
		if (trimmed.Length <= MaxBodyLength)
		{
			return trimmed;
		}
		string cut = trimmed.Substring(0, MaxBodyLength);
		int lastSpace = cut.LastIndexOf(' ');
		if (lastSpace > MaxBodyLength / 2)
		{
			cut = cut.Substring(0, lastSpace);
		}
		return cut.TrimEnd() + "...";
	}
}
