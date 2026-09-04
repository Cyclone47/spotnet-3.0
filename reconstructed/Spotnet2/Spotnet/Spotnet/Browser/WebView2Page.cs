using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NLog;
using System.IO;
using Spotnet.Controls;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Browser;

/// <summary>
/// An <see cref="IPage"/> backed by Edge WebView2.
/// </summary>
/// <remarks>
/// WebView2 requires the Evergreen Runtime on the machine - present by default on
/// Windows 11 and current Windows 10. <see cref="IsRuntimeAvailable"/> reports whether it
/// is, and the control renders an explanatory message rather than an empty tab if not.
/// </remarks>
public partial class WebView2Page : System.Windows.Controls.UserControl, IPage, ICloseableView, IDisposable, INotifyPropertyChanged
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static string _userDataFolder;

	private readonly object _syncRoot = new object();

	private bool _isDisposed;

	private bool _isNavigating;

	/// <summary>Set once the Ready event that attaches this page to its tab has fired.</summary>
	private bool _attachRaised;

	/// <summary>Set once browser initialization has been kicked off, so Loaded is idempotent.</summary>
	private bool _initStarted;

	private PageTypeEnum _oldType;

	private string _title;

	protected CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

	public WebView2 Browser { get; private set; }

	public bool IsDomReady { get; private set; }

	public Uri Uri { get; protected set; }

	public virtual string Title
	{
		get
		{
			return _title;
		}
		protected set
		{
			if (_title != value)
			{
				_title = value;
				this.TitleChangedEvent?.Invoke(this);
			}
		}
	}

	public PageTypeEnum PageDefaultType { get; protected set; }

	public PageTypeEnum PageType => _isNavigating ? PageTypeEnum.Loading : PageDefaultType;

	public TabItem TabItem { get; set; }

	public event PropertyChangedEventHandler PropertyChanged;

	public event Action<object> TitleChangedEvent;

	public event Action<object> TypeChangedEvent;

	public event Action<object> AddressChangedEvent;

	public event Action<object, PageReadyEventArgs> DocumentReadyEvent;

	public event Action DocumentUnloadedEvent;

	public event Action BrowserGotFocusEvent;

	protected WebView2Page()
	{
		Browser = new WebView2();
		InitializeComponent();
		WebGrid.Children.Add(Browser);
		// WebView2 is an HwndHost: it cannot create its browser process until the control
		// is in a visual tree with a window handle. Initialization therefore waits for
		// Loaded rather than running in the constructor.
		Browser.Loaded += OnBrowserLoaded;
	}

	public WebView2Page(string url)
		: this()
	{
		if (url.IsNullOrWhiteSpace())
		{
			throw new ArgumentException("URL should be specified", nameof(url));
		}
		PageDefaultType = PageTypeEnum.WebPage;
		Uri = new Uri(url.Trim(), UriKind.RelativeOrAbsolute);
		Title = Uri.IsAbsoluteUri ? Uri.Host : url;
	}

	/// <summary>
	/// Attaches the page to its tab, then lets initialization proceed.
	/// </summary>
	/// <remarks>
	/// MainWindow assigns <c>newTab.Content = page</c> only from the DocumentReady
	/// handler, and nowhere else. WebView2 needs the visual tree before it can navigate;
	/// raising Ready here avoids a cycle that would otherwise leave the tab blank.
	///
	/// Raising Ready here breaks the cycle: the tab takes the control, the control gets a
	/// window handle, Loaded fires, and only then does the browser start. Loaded is still
	/// raised at the real end of navigation, so consumers that wait for a finished
	/// document are unaffected.
	/// </remarks>
	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		if (_attachRaised || CancellationTokenSource.IsCancellationRequested)
		{
			return;
		}
		_attachRaised = true;
		this.DocumentReadyEvent?.Invoke(this, new PageReadyEventArgs(PageReadyState.Ready));
	}

	private async void OnBrowserLoaded(object sender, RoutedEventArgs e)
	{
		if (_initStarted || CancellationTokenSource.IsCancellationRequested)
		{
			return;
		}
		_initStarted = true;
		await InitializeBrowserAsync();
	}

	/// <summary>
	/// True when the WebView2 Evergreen Runtime is installed on this machine.
	/// </summary>
	/// <remarks>Used by diagnostics and startup checks.</remarks>
	public static bool IsRuntimeAvailable()
	{
		try
		{
			return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
		}
		catch (WebView2RuntimeNotFoundException)
		{
			return false;
		}
		catch (Exception ex)
		{
			Log.Warn("Could not determine WebView2 runtime availability: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// Creates the browser environment and navigates to <see cref="Uri"/>.
	/// </summary>
	private async Task InitializeBrowserAsync()
	{
		try
		{
			if (_userDataFolder == null)
			{
				// Keep browser scratch data in Spotnet's normal temporary directory.
				_userDataFolder = Path.Combine(AppHelper.GetTempPath(), "WebView2Cache");
				Directory.CreateDirectory(_userDataFolder);
			}

			CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
			await Browser.EnsureCoreWebView2Async(environment);

			if (CancellationTokenSource.IsCancellationRequested)
			{
				return;
			}

			CoreWebView2 core = Browser.CoreWebView2;
			core.Settings.AreDefaultContextMenusEnabled = true;
			core.Settings.AreDevToolsEnabled = false;
			core.Settings.IsStatusBarEnabled = false;
			// Spot pages are rendered from local content; no need for the host to offer
			// password saving or autofill inside them.
			core.Settings.IsPasswordAutosaveEnabled = false;
			core.Settings.IsGeneralAutofillEnabled = false;

			core.DocumentTitleChanged += OnDocumentTitleChanged;
			core.SourceChanged += OnSourceChanged;
			core.NewWindowRequested += OnNewWindowRequested;
			core.DownloadStarting += OnDownloadStarting;
			Browser.NavigationStarting += OnNavigationStarting;
			Browser.NavigationCompleted += OnNavigationCompleted;
			Browser.PreviewKeyDown += OnBrowserPreviewKeyDown;

			// Subclasses get their chance to register script bridges before navigation,
			// so anything injected with AddScriptToExecuteOnDocumentCreatedAsync is in
			// place for the first document.
			await OnCoreWebView2ReadyAsync(core);

			WebGrid.Visibility = Visibility.Visible;
			if (Uri != null)
			{
				Browser.Source = Uri;
			}
		}
		catch (WebView2RuntimeNotFoundException)
		{
			ShowRuntimeMissingMessage();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			ShowUnavailableMessage("This page could not be opened: " + ex.Message);
		}
	}

	/// <summary>
	/// Called once the browser is initialized and before the first navigation, so a
	/// derived page can register script bridges or message handlers.
	/// </summary>
	/// <remarks>
	/// Host functions are exposed without a COM-visible object by injecting a small
	/// shim with <c>AddScriptToExecuteOnDocumentCreatedAsync</c> that forwards calls
	/// through <c>window.chrome.webview.postMessage</c>, then handle them in
	/// <c>WebMessageReceived</c>. <see cref="ResponsePage"/> does exactly that.
	/// </remarks>
	protected virtual Task OnCoreWebView2ReadyAsync(CoreWebView2 core)
	{
		return Task.FromResult(0);
	}

	private void ShowRuntimeMissingMessage()
	{
		ShowUnavailableMessage(
			"The Microsoft Edge WebView2 Runtime is not installed on this machine, so this " +
			"page cannot be displayed.\n\nInstall it from Microsoft to use this tab.");
	}

	private void ShowUnavailableMessage(string message)
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			UnavailableMessage.Text = message;
			UnavailableMessage.Visibility = Visibility.Visible;
			WebGrid.Visibility = Visibility.Collapsed;
		});
	}

	// --- engine events ------------------------------------------------------

	private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
	{
		_isNavigating = true;
		IsDomReady = false;
		OnTypeProbablyChanged();
	}

	private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		_isNavigating = false;
		OnTypeProbablyChanged();

		if (CancellationTokenSource.IsCancellationRequested)
		{
			return;
		}

		// Ready was already raised from OnApplyTemplate to attach this page to its tab,
		// so only the completion state is raised here.
		IsDomReady = true;
		this.DocumentReadyEvent?.Invoke(this, new PageReadyEventArgs(PageReadyState.Loaded));

		if (!e.IsSuccess)
		{
			// Keep the tab visible so the engine can show its own error page.
			Log.Warn("Navigation failed for {0}: {1}", Uri, e.WebErrorStatus);
		}
	}

	/// <summary>
	/// Adopts the document title, but only for pages that browse the web.
	/// </summary>
	/// <remarks>
	/// Spots, the release notes and the downloads page all carry a title the app itself
	/// decided on. They are rendered from a generated local file with no &lt;title&gt;
	/// element, so the engine falls back to reporting the file name - a GUID .htm - and
	/// that overwrote the spot title in the tab header and the window caption a moment
	/// after the spot opened.
	/// </remarks>
	private void OnDocumentTitleChanged(object sender, object e)
	{
		if (CancellationTokenSource.IsCancellationRequested || PageDefaultType != PageTypeEnum.WebPage)
		{
			return;
		}

		string documentTitle = Browser.CoreWebView2?.DocumentTitle;
		if (!documentTitle.IsNullOrWhiteSpace())
		{
			Title = documentTitle;
		}
	}

	private void OnSourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
	{
		if (CancellationTokenSource.IsCancellationRequested)
		{
			return;
		}
		string source = Browser.CoreWebView2?.Source;
		if (!source.IsNullOrEmpty() && System.Uri.TryCreate(source, UriKind.Absolute, out Uri parsed))
		{
			Uri = parsed;
		}
		this.AddressChangedEvent?.Invoke(this);
	}

	/// <summary>
	/// Opens target="_blank" links as Spotnet tabs instead of detached popup windows.
	/// </summary>
	/// <remarks>
	/// Virtual because a spot page routes web links by the ExternalBrowser setting rather
	/// than always into a tab; see <see cref="SpotWebView2Page"/>.
	/// </remarks>
	protected virtual void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
	{
		e.Handled = true;
		string url = e.Uri;
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			if (!url.IsNullOrEmpty() && url.StartsWith(ResponsePage.GetResponseSiteUrl(), StringComparison.OrdinalIgnoreCase))
			{
				Sys.MainWindow.OpenPage(PageTypeEnum.ResponseSite);
			}
			else
			{
				Sys.MainWindow.OpenPage(PageTypeEnum.WebPage, url);
			}
		});
	}

	/// <summary>
	/// Routes an NZB download into the Spotnet queue rather than letting the browser save
	/// it to disk.
	/// </summary>
	private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
	{
		try
		{
			string mimeType = e.DownloadOperation?.MimeType ?? "";
			string url = e.DownloadOperation?.Uri ?? "";
			if (!mimeType.IndexOf("nzb", StringComparison.OrdinalIgnoreCase).Equals(-1)
				|| url.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
			{
				e.Cancel = true;
				string nzbUrl = url.Replace("nzbindex.nl/release", "nzbindex.nl/download");
				Task.Run(delegate
				{
					ProcessNzb(nzbUrl);
				});
				CloseThisTabAsync();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	private static bool ProcessNzb(string url)
	{
		DownloaderItemViewModel downloaderItemViewModel = null;
		try
		{
			string path = AppHelper.GenerateNzbFilePath(Path.GetFileName(url));
			if (Settings.Default.DownloadAction <= 1)
			{
				downloaderItemViewModel = Sys.Downloader.AddFakeItemBeforeNzbDownloaded(Path.GetFileNameWithoutExtension(path), null, 0);
			}
			if (!DownloadFile(url, path) || new FileInfo(path).Length == 0L)
			{
				return false;
			}
			if (!path.IsNullOrEmpty())
			{
				if (downloaderItemViewModel == null)
				{
					downloaderItemViewModel = DownloaderItemFactory.New(Path.GetFileNameWithoutExtension(path));
				}
				SpotHelper.DoDownload(path, downloaderItemViewModel);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
		return true;
	}

	private static bool DownloadFile(string url, string fileName)
	{
		using WebClient webClient = new WebClient();
		try
		{
			webClient.DownloadFile(url, fileName);
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	private void CloseThisTabAsync()
	{
		DispatcherHelper.RunAsync(delegate
		{
			if (TabItem is CloseableTabItem closeable)
			{
				if (Settings.Default.DownloadAction <= 1)
				{
					Sys.MainWindow.TabControl1.SelectedIndex = 1;
					closeable.SetParentTab(null);
				}
				closeable.CloseMe();
			}
		});
	}

	private void OnBrowserPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (!CancellationTokenSource.IsCancellationRequested
			&& Keyboard.Modifiers == ModifierKeys.Control
			&& Sys.MainWindow.OnKeyDown(new PreviewKeyDownEventArgs((Keys)(0x20000 | KeyInterop.VirtualKeyFromKey(e.Key)))))
		{
			e.Handled = true;
		}
	}

	private void OnTypeProbablyChanged()
	{
		if (_oldType != PageType)
		{
			_oldType = PageType;
			this.TypeChangedEvent?.Invoke(this);
		}
	}

	// --- IPage ---------------------------------------------------------------

	public void FocusDocument()
	{
		if (!CancellationTokenSource.IsCancellationRequested)
		{
			Browser.Focus();
			this.BrowserGotFocusEvent?.Invoke();
		}
	}

	/// <summary>
	/// Marshals JavaScript-context callbacks onto the UI thread.
	/// </summary>
	public DispatcherOperation CreateJecAsync(Action action)
	{
		return DispatcherHelper.UIDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)delegate
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				action?.Invoke();
			}
		});
	}

	public void CreateJecSync(Action action)
	{
		DispatcherHelper.UIDispatcher.Invoke(delegate
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				action?.Invoke();
			}
		});
	}

	public void ExecuteJavascript(string script)
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			if (!CancellationTokenSource.IsCancellationRequested)
			{
				_ = Browser.CoreWebView2?.ExecuteScriptAsync(script);
			}
		});
	}

	/// <summary>Runs a script and returns its result as a JSON string.</summary>
	public async Task<string> ExecuteJavascriptWithResultAsync(string script)
	{
		if (CancellationTokenSource.IsCancellationRequested || Browser.CoreWebView2 == null)
		{
			return null;
		}
		return await Browser.ExecuteScriptAsync(script);
	}

	public bool IsDocumentReady()
	{
		return IsDomReady;
	}

	public void Unload()
	{
		lock (_syncRoot)
		{
			if (CancellationTokenSource.IsCancellationRequested)
			{
				return;
			}
			CancellationTokenSource.Cancel();
		}

		try
		{
			if (Browser != null)
			{
				Browser.Loaded -= OnBrowserLoaded;
				Browser.NavigationStarting -= OnNavigationStarting;
				Browser.NavigationCompleted -= OnNavigationCompleted;
				Browser.PreviewKeyDown -= OnBrowserPreviewKeyDown;
				if (Browser.CoreWebView2 != null)
				{
					Browser.CoreWebView2.DocumentTitleChanged -= OnDocumentTitleChanged;
					Browser.CoreWebView2.SourceChanged -= OnSourceChanged;
					Browser.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
					Browser.CoreWebView2.DownloadStarting -= OnDownloadStarting;
				}
				Browser.Dispose();
			}
			this.DocumentUnloadedEvent?.Invoke();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	public virtual void Dispose()
	{
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}
			_isDisposed = true;
		}
		if (!CancellationTokenSource.IsCancellationRequested)
		{
			Unload();
			GC.SuppressFinalize(this);
		}
	}

	protected virtual void OnPropertyChanged(string propertyName = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
