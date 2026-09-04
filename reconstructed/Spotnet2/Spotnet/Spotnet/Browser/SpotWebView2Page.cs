using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using Microsoft.VisualBasic;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using NLog;
using Spotnet.Controls;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Properties;
using Spotnet.Utilities;
using Spotnet.ViewModel;

namespace Spotnet.Browser;

/// <summary>
/// A spot and its comments, rendered by Edge WebView2.
/// </summary>
/// <remarks>
/// The MSHTML page held live <c>HtmlElement</c> references, hung .NET handlers off them
/// and read the DOM whenever it liked. None of that exists here: the document is in
/// another process and every crossing is a message. <see cref="SpotPageBridge"/> is that
/// crossing, and it is deliberately arranged so the host never has to ask the page a
/// question and wait for the answer in the middle of doing something - a Send click
/// arrives with the nickname and body already attached, a quote click with the quoted
/// text and its author.
///
/// The document is written to a temporary file and opened over <c>file://</c> rather than
/// pushed in as a string. A string-loaded document has an opaque origin and could not
/// load the theme's own stylesheet, script and images, so every theme would have needed
/// rewriting; this way the themes are untouched.
/// </remarks>
internal class SpotWebView2Page : WebView2Page, ISpotPage
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	/// <summary>Files rendered for a tab, removed when that tab closes.</summary>
	private static readonly List<string> FilesToRemoveOnClose = new List<string>();

	/// <summary>
	/// Comments this user posted, kept so they reappear when the spot is opened again
	/// before the comment database has caught up with them.
	/// </summary>
	private static readonly Dictionary<string, List<Comment>> MessagesFromUserToShowOnNextTabOpen =
		new Dictionary<string, List<Comment>>();

	private readonly CancellationTokenSource _cancelGettingCommentsSource = new CancellationTokenSource();

	private readonly HashSet<long> _commentIdCache = new HashSet<long>();

	private readonly List<long> _fetchedCache = new List<long>();

	private readonly HashSet<string> _messagesFromUserAndAlreadyShown = new HashSet<string>();

	private readonly Dictionary<string, string> _uniqueCache = new Dictionary<string, string>();

	private readonly object _syncRoot = new object();

	private readonly System.Timers.Timer _updateCommentPreviewTimer;

	/// <summary>The document as first rendered, restored before a comment refresh.</summary>
	private string _commentProgressCache;

	private bool _commentsRefreshWasClickedAlready;

	private bool _documentCompletedFlag;

	private string _fileToRemoveOnClose;

	private string _htmlFile;

	private bool _isImageFullSized;

	private bool _isImageResizeable;

	private string _lastBody = "";

	private DateTime _lastTime;

	private bool _loadImageManually;

	private string _menuFrom;

	private string _menuModulus;

	private bool _showOnActivated;

	/// <summary>Set once the tab toolbar has been built, so teardown knows to undo it.</summary>
	private bool _toolbarInitialized;

	/// <summary>
	/// The comment box and nickname as the page last reported them.
	/// </summary>
	/// <remarks>
	/// Every message that could have changed them carries their current value, so posting
	/// a comment never has to read the DOM and wait.
	/// </remarks>
	private string _commentBody = "";

	private string _nickname = "";

	/// <summary>Text selected in the document, for the copy menu.</summary>
	private string _selectedText = "";

	private static SpotsListViewModel SpotsListVm =>
		((ViewModelLocator)System.Windows.Application.Current.Resources["Locator"]).SpotsList;

	public SpotEx SpotEx { get; }

	private bool IsClosing => CancellationTokenSource.IsCancellationRequested;

	internal SpotWebView2Page(string title, SpotEx spotEx)
	{
		SpotEx = spotEx;
		Title = title;
		PageDefaultType = PageTypeEnum.SpotLoaded;
		DocumentReadyEvent += OnDocumentReadyEvent;

		_updateCommentPreviewTimer = new System.Timers.Timer
		{
			AutoReset = false,
			Interval = 200.0
		};
		_updateCommentPreviewTimer.Elapsed += delegate
		{
			try
			{
				CreateJecSync(UpdatePreviewPanel);
			}
			catch (Exception ex)
			{
				Log.Exception(ex, showToClient: true);
			}
		};

		// The document has to exist before the browser starts, because the base class
		// navigates as soon as the control is loaded. Rendering it is string work over a
		// theme that is already cached, so it is done here rather than on a task whose
		// completion the navigation would then have to wait for.
		Uri = RenderDocument(spotEx);
	}

	/// <summary>Writes the parsed spot to a temporary file and returns its file:// URI.</summary>
	private Uri RenderDocument(SpotEx spotEx)
	{
		try
		{
			string html = SpotParser.ParseSpot(spotEx, Settings.Default.SpotFontSize);
			_htmlFile = AppHelper.GetTempFileName("htm");
			// No BOM: the themes declare their charset in a meta tag, and a BOM ahead of
			// the doctype pushes the document into quirks mode.
			File.WriteAllText(_htmlFile, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			FilesToRemoveOnClose.Add(_htmlFile);
			return new Uri(_htmlFile);
		}
		catch (Exception ex)
		{
			Log.Error("Failed to load page: " + ex.Message);
			return null;
		}
	}

	protected override async Task OnCoreWebView2ReadyAsync(CoreWebView2 core)
	{
		if (core == null)
		{
			return;
		}
		// The spot page has its own context menu, and the default one would offer
		// navigation actions that make no sense inside a tab.
		core.Settings.AreDefaultContextMenusEnabled = false;
		core.WebMessageReceived += OnWebMessageReceived;
		core.NavigationStarting += OnSpotNavigationStarting;
		await core.AddScriptToExecuteOnDocumentCreatedAsync(SpotPageBridge.Script);
	}

	/// <summary>
	/// Keeps the tab on its own document.
	/// </summary>
	/// <remarks>
	/// The bridge already intercepts the theme's <c>ubb:</c>-style links at the click, so
	/// nothing should reach here. This is the backstop for the paths it cannot see -
	/// script assigning <c>window.location</c>, a form post - which under MSHTML were
	/// cancelled navigations too.
	/// </remarks>
	private void OnSpotNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
	{
		if (IsClosing || e.Uri.IsNullOrEmpty())
		{
			return;
		}
		string target = e.Uri.Trim();
		// A fragment jump inside the theme's own iTunes panel is still this document.
		int fragment = target.IndexOf('#');
		string withoutFragment = fragment < 0 ? target : target.Substring(0, fragment);
		if (withoutFragment.EqualsIgnoreCase(Uri?.AbsoluteUri) || target.ToLower().Equals("about:blank"))
		{
			return;
		}
		e.Cancel = true;
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			HandleHostLink(target);
		});
	}

	// --- page messages -------------------------------------------------------

	private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
	{
		if (IsClosing)
		{
			return;
		}
		JObject message;
		try
		{
			message = JObject.Parse(e.TryGetWebMessageAsString());
		}
		catch (Exception)
		{
			// The document renders spot bodies and comments from Usenet; anything that is
			// not one of our own messages is simply not ours.
			Log.Debug("Ignoring an unreadable web message from the spot page.");
			return;
		}

		try
		{
			switch (Text(message, "type"))
			{
			case "nav":
				HandleHostLink(Text(message, "url"));
				break;
			case "click":
				HandleClick(message);
				break;
			case "input":
				_nickname = Text(message, "nickname");
				_commentBody = Text(message, "body");
				_updateCommentPreviewTimer.Start();
				break;
			case "contextmenu":
				HandleContextMenu(Text(message, "href"));
				break;
			case "select":
				_selectedText = Text(message, "text");
				if (!_selectedText.IsNullOrEmpty())
				{
					CreateCopySelectionMenu();
				}
				break;
			case "quote":
				InsertQuote(Text(message, "sender"), Text(message, "body"));
				break;
			case "reply":
				InsertReply(Text(message, "sender"));
				break;
			default:
				Log.Debug("Ignoring an unrecognized web message from the spot page.");
				break;
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	private static string Text(JObject message, string field)
	{
		return message[field]?.Value<string>() ?? "";
	}

	private void HandleClick(JObject message)
	{
		switch (Text(message, "id"))
		{
		case "AddComment":
			_nickname = Text(message, "nickname");
			_commentBody = Text(message, "body");
			AddComment();
			break;
		case "DownloadButton":
			DownloadSpot();
			break;
		case "SpotImage":
			if (_isImageResizeable)
			{
				ToggleImageSize();
			}
			break;
		case "ReportButton":
			if (!SpotEx.IsPreview)
			{
				Sys.MainWindow.AddComplainReportToTheSpot(SpotEx);
			}
			break;
		case "FavButton":
			if (!SpotEx.IsPreview)
			{
				ToggleFavourite();
			}
			break;
		case "ClosePreview":
			Settings.Default.CommentPreviewShow = false;
			Settings.Default.Save();
			UpdatePreviewPanel();
			break;
		case "CloseImdb":
			Settings.Default.SpotImdbShow = false;
			Settings.Default.Save();
			UpdateImdbPanel();
			break;
		case "CloseSmiles":
			Settings.Default.CommentSmilesShow = false;
			Settings.Default.Save();
			UpdateSmileysPanel();
			break;
		}
	}

	/// <summary>
	/// Runs one of the theme's pseudo-scheme links.
	/// </summary>
	/// <remarks>
	/// The link text comes from the document, so every branch validates its own payload
	/// before acting on it, exactly as the MSHTML page did.
	/// </remarks>
	private void HandleHostLink(string link)
	{
		if (link.IsNullOrWhiteSpace())
		{
			return;
		}
		try
		{
			string text = link.Trim();
			string lower = text.ToLower();
			if (lower.Equals("about:blank") || lower.StartsWith("res:"))
			{
				return;
			}
			if (lower.StartsWith("link:") && !lower.StartsWith("link:spotnet://"))
			{
				OpenWebLink(text.Substring("link:".Length));
			}
			else if (lower.StartsWith("http://") || lower.StartsWith("https://"))
			{
				// A plain web link in a spot body or a comment - IMDb, YouTube, an
				// uploader's site. The theme does not rewrite these into link:, so they
				// arrive here as themselves, either from the cancelled navigation or from
				// the bridge. Before this branch existed every one of them fell through
				// the chain and the click did nothing at all.
				OpenWebLink(text);
			}
			else if (lower.StartsWith("query:"))
			{
				string[] parts = text.Substring("query:".Length).Split('_');
				if (parts.Length > 1)
				{
					Sys.LeftPanel.SearchFilter(HttpUtility.UrlDecode(parts[1]), HttpUtility.UrlDecode(parts[0]));
				}
			}
			else if (lower.StartsWith("menu:"))
			{
				if (GetMenuSenderInfo(text.Substring("menu:".Length), out string senderName, out string modulus))
				{
					CreateMenu(senderName, modulus);
				}
			}
			else if (lower.StartsWith("spotnet:reload") && !SpotEx.IsPreview)
			{
				string error = "";
				if (!StartUpdateComments(SpotEx.MessageId, ref error))
				{
					Log.Debug("StartUpdateComments failed: " + error);
					AppHelper.Error(error);
				}
			}
			else if (lower.StartsWith("loadimg:"))
			{
				_loadImageManually = true;
				StartProcessImage();
			}
			else if (lower.StartsWith("smiley:"))
			{
				string smiley = text.Substring("smiley:".Length);
				if (Regex.IsMatch(smiley, "^[a-z]+$"))
				{
					ExecuteJavascript("window.spotnet.callSmiley(" + Quoted(smiley) + ");");
				}
			}
			else if (lower.StartsWith("ubb:"))
			{
				string tag = text.Substring("ubb:".Length);
				if (Regex.IsMatch(tag, "^[biulc]$"))
				{
					ExecuteJavascript("window.spotnet.applyUbb(" + Quoted(tag) + ", " + Quoted("spotnet://MSGID") + ");");
				}
			}
			else if (lower.StartsWith("show:"))
			{
				TogglePanel(text.Substring("show:".Length));
			}
			else if (lower.StartsWith("spotnet://") || lower.StartsWith("link:spotnet://"))
			{
				if (lower.StartsWith("link:"))
				{
					text = text.Substring("link:".Length);
				}
				Sys.MainWindow.ProcessSpotnetProtocol(text, saveParrentTab: true);
			}
			else if (lower.StartsWith("addtoblack:"))
			{
				if (GetMenuSenderInfo(text.Substring("addtoblack:".Length), out string senderName, out string modulus))
				{
					ReverseModulusBlackList(senderName, modulus);
				}
			}
			else if (lower.StartsWith("spamreports:"))
			{
				OpenSpamReportsPopup(text.Substring("spamreports:".Length));
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	/// <summary>
	/// Sends a web link where <see cref="Settings.ExternalBrowser"/> says it should go.
	/// </summary>
	/// <remarks>
	/// The link text comes out of a Usenet posting, so only http and https are ever
	/// launched. A javascript:, data: or file: href reaching
	/// <see cref="AppHelper.LaunchInExternalProgram"/> would be handed to the shell.
	/// </remarks>
	private static void OpenWebLink(string url)
	{
		if (!TryResolveWebLink(url, out string target))
		{
			Log.Debug("Not opening a link from a spot page that is not http or https: " + url);
			return;
		}
		if (Settings.Default.ExternalBrowser)
		{
			AppHelper.LaunchInExternalProgram(target);
		}
		else
		{
			Sys.MainWindow.OpenPage(PageTypeEnum.WebPage, target, saveParrentTab: true).Forget();
		}
	}

	/// <summary>
	/// Decides whether a link out of a spot page may leave the application, and what should
	/// be opened if so.
	/// </summary>
	/// <remarks>
	/// Kept separate from the opening itself so the rule can be tested. "undefined" is what
	/// the themes' own script produces for an anchor with no usable href, and it used to be
	/// checked only on the link: branch.
	/// </remarks>
	internal static bool TryResolveWebLink(string url, out string target)
	{
		target = null;
		if (url.IsNullOrWhiteSpace())
		{
			return false;
		}
		string candidate = url.Trim();
		if (candidate.EqualsIgnoreCase("undefined"))
		{
			return false;
		}
		if (!System.Uri.TryCreate(candidate, UriKind.Absolute, out System.Uri parsed)
			|| (!parsed.Scheme.EqualsIgnoreCase(System.Uri.UriSchemeHttp)
				&& !parsed.Scheme.EqualsIgnoreCase(System.Uri.UriSchemeHttps)))
		{
			return false;
		}
		target = candidate;
		return true;
	}

	/// <summary>
	/// A target="_blank" link on a spot page follows the same setting as any other web
	/// link, instead of always opening an internal tab the way the plain browser does.
	/// </summary>
	protected override void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
	{
		e.Handled = true;
		string url = e.Uri;
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			OpenWebLink(url);
		});
	}

	private void TogglePanel(string panel)
	{
		if (panel.Equals("p"))
		{
			Settings.Default.CommentPreviewShow = !Settings.Default.CommentPreviewShow;
			Settings.Default.Save();
			UpdatePreviewPanel();
		}
		else if (panel.Equals("c"))
		{
			Settings.Default.CommentSmilesShow = !Settings.Default.CommentSmilesShow;
			Settings.Default.Save();
			UpdateSmileysPanel();
		}
		else if (panel.Equals("i"))
		{
			Settings.Default.SpotImdbShow = !Settings.Default.SpotImdbShow;
			Settings.Default.Save();
			UpdateImdbPanel();
		}
	}

	/// <summary>
	/// Right-clicking an author opens their menu; anywhere else it closes the tab, which
	/// is how a spot tab has always been dismissed.
	/// </summary>
	private void HandleContextMenu(string href)
	{
		if (!href.IsNullOrEmpty()
			&& GetMenuSenderInfo(href.Substring("menu:".Length), out string senderName, out string modulus))
		{
			CreateMenu(senderName, modulus);
			return;
		}
		try
		{
			if (TabItem is CloseableTabItem closeable)
			{
				closeable.CloseMe();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	// --- document lifecycle --------------------------------------------------

	private async void OnDocumentReadyEvent(object sender, PageReadyEventArgs args)
	{
		if (args == null || args.ReadyState != PageReadyState.Loaded)
		{
			return;
		}
		lock (_syncRoot)
		{
			if (_documentCompletedFlag || IsClosing)
			{
				return;
			}
			_documentCompletedFlag = true;
		}
		try
		{
			// Read once, so a comment refresh can put the panel back the way the theme
			// shipped it. Under MSHTML this was a plain property read.
			_commentProgressCache = await GetHtmlAsync("CommentsProgress");

			AddSpotWarning();
			if (Settings.Default.ShowTabToolbar)
			{
				Toolbar.InitializeWithViewModel(SpotEx);
				ToolbarPositioningSetup();
				_toolbarInitialized = true;
			}
			UpdateFavButton();
			ExecuteJavascript("window.spotnet.setStyle('NzbInfoButton', 'visibility: hidden;');");
			UpdateImdbPanel();
			UpdateSmileysPanel();
			UpdatePreviewPanel();
			StartProcessImage();
			StartProcessComments();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	private void AddSpotWarning()
	{
		if (SpotEx.PosterIdent == PosterIdentType.Black)
		{
			Warning.Text = Words.SenderIsInBlackList;
			Warning.Visibility = Visibility.Visible;
		}
		else if (SpotEx.PosterIdent == PosterIdentType.SpotBlack)
		{
			Warning.Text = Words.SpotIsInBlackList;
			Warning.Visibility = Visibility.Visible;
		}
		else if (SpotEx.PosterIdent == PosterIdentType.Fake)
		{
			Warning.Text = Words.SenderIdIsFake;
			Warning.Visibility = Visibility.Visible;
		}
		if (Warning.Visibility == Visibility.Visible)
		{
			Warning.IsVisibleChanged += delegate
			{
				FocusDocument();
			};
		}
	}

	private void ToolbarPositioningSetup()
	{
		ToolbarPopup.Visibility = Visibility.Visible;
		Popup toolbarPopup = ToolbarPopup;
		toolbarPopup.CustomPopupPlacementCallback = (CustomPopupPlacementCallback)Delegate.Combine(
			toolbarPopup.CustomPopupPlacementCallback,
			(CustomPopupPlacementCallback)((Size popupSize, Size targetSize, Point offset) => new CustomPopupPlacement[1]
			{
				new CustomPopupPlacement
				{
					Point = new Point(targetSize.Width - popupSize.Width - 25.0, 10.0)
				}
			}));
		BrowserGotFocusEvent += RedrawPopup;
		Sys.MainWindow.LocationChanged += RedrawPopup;
		Sys.MainWindow.SizeChanged += RedrawPopup;
		Sys.MainWindow.Activated += MainWindowOnActivated;
		Sys.MainWindow.Deactivated += MainWindowOnDeactivated;
	}

	private void RedrawPopup()
	{
		double horizontalOffset = ToolbarPopup.HorizontalOffset;
		if (!ToolbarPopup.IsOpen && !SpotEx.IsPreview)
		{
			ToolbarPopup.IsOpen = true;
		}
		ToolbarPopup.HorizontalOffset = horizontalOffset + 1.0;
		ToolbarPopup.HorizontalOffset = horizontalOffset;
	}

	private void RedrawPopup(object sender, EventArgs eventArgs)
	{
		RedrawPopup();
	}

	private void MainWindowOnDeactivated(object sender, EventArgs eventArgs)
	{
		if (ToolbarPopup.IsOpen)
		{
			ToolbarPopup.IsOpen = false;
			_showOnActivated = true;
		}
	}

	private void MainWindowOnActivated(object sender, EventArgs eventArgs)
	{
		if (_showOnActivated)
		{
			ToolbarPopup.IsOpen = !SpotEx.IsPreview;
			_showOnActivated = false;
		}
	}

	// --- panels --------------------------------------------------------------

	private void UpdateFavButton()
	{
		SetClass("FavButton", Favorites.ContainsMessageId(SpotEx.MessageId) ? "favdel" : "favadd");
	}

	private void ToggleFavourite()
	{
		if (Favorites.ContainsMessageId(SpotEx.MessageId))
		{
			Favorites.Remove(SpotEx.MessageId);
			AppHelper.ShowPopupMessage(Words.FavoritesRemoved + "\r\n" + SpotEx.Title, inTheCenter: false, TimeSpan.FromSeconds(3.0));
		}
		else
		{
			Favorites.Add(SpotEx.MessageId);
			AppHelper.ShowPopupMessage(Words.FavoritesAdded + "\r\n" + SpotEx.Title, inTheCenter: false, TimeSpan.FromSeconds(3.0));
		}
		UpdateFavButton();
	}

	private void UpdatePreviewPanel()
	{
		if (IsClosing)
		{
			return;
		}
		if (!Settings.Default.CommentPreviewShow)
		{
			SetHtml("PreviewPanel",
				$"<a href='show:p' class='fill-div' style='padding:8px 8px 10px 8px;'>{Words.SpotThemePreviewShow}</a>");
			SetStyle("PreviewPanel", "padding:0 16px 0 0;");
			return;
		}
		Comment comment = new Comment
		{
			Created = DateAndTime.Now,
			From = _nickname,
			Body = _commentBody,
			MessageId = "preview@spot.com",
			User = new UserInfo()
		};
		if (AppHelper.GetAvatar() != null)
		{
			comment.User.Avatar = Settings.Default.Avatar;
		}
		comment.User.Signature = comment.MessageId;
		comment.User.Modulus = UserKeyHelper.GetModulus();
		comment.User.ValidSignature = true;
		string html = "<span class='Close' id='ClosePreview'>x</span>"
			+ GenerateCommentHtmlCode(comment, isVirtual: true, isPreview: true);
		SetHtml("PreviewPanel", html);
		SetStyle("PreviewPanel", "");
	}

	private void UpdateImdbPanel()
	{
		string category = AppHelper.HtmlEncode(AppHelper.CatDesc(SpotEx.Category, 0));
		if (SpotEx.Category == 1 && (SpotEx.SubCat == 12 || SpotEx.SubCat == 13))
		{
			category = AppHelper.HtmlEncode(AppHelper.CatDesc(SpotEx.Category, 5));
		}
		bool hasPanel = category.Equals(Categories.CatFilms)
			|| category.Equals(Categories.CatSeries)
			|| category.Equals(Categories.CatMusic);
		if (!hasPanel)
		{
			SetStyle("ImdbPanel", "display:none");
			SetStyle("ImdbPanel2", "display:none");
		}
		else if (Settings.Default.SpotImdbShow)
		{
			SetStyle("ImdbPanel", "display:true");
			SetStyle("ImdbPanel2", "display:none");
		}
		else
		{
			SetStyle("ImdbPanel", "display:none");
			SetStyle("ImdbPanel2", "display:true");
		}
	}

	private void UpdateSmileysPanel()
	{
		if (!Settings.Default.CommentSmilesShow)
		{
			SetHtml("SmileysPanel",
				"<a href='show:c' class='fill-div' style='padding:8px 8px 10px 8px;'>" + Words.SpotThemeShow + "</a>");
			SetStyle("SmileysPanel", "padding:0 16px 0 0;");
			return;
		}
		string html = "<span class='Close' id='CloseSmiles'>x</span>";
		int column = 0;
		foreach (string file in Directory.GetFiles(AppHelper.SmileysPath, "*.gif"))
		{
			string name = Path.GetFileNameWithoutExtension(file);
			html += "<a href='smiley:" + name + "'><img style='vertical-align:bottom;' title='" + name
				+ "' alt='" + name + "' src='file://" + file + "' border=0></a>&nbsp;&nbsp;";
			if (column++ == 14)
			{
				column = 0;
				html += "<br/>";
			}
		}
		SetHtml("SmileysPanel", html);
		SetStyle("SmileysPanel", "");
	}

	// --- image ---------------------------------------------------------------

	private void ToggleImageSize()
	{
		_isImageFullSized = !_isImageFullSized;
		ExecuteJavascript("window.spotnet.toggleImageSize(" + (_isImageFullSized ? "true" : "false") + ");");
	}

	private void StartProcessImage()
	{
		try
		{
			if (IsClosing)
			{
				return;
			}
			if (_loadImageManually)
			{
				SetOuterHtml("SpotImage", string.Format(
					"<img id='SpotImage' src=\"{0}/Images/loading.gif\" onfocus='this.blur()'>",
					"file://" + AppHelper.SettingsFolder));
			}
			if (!_loadImageManually && (!Settings.Default.LoadImageOnSpotTab || SpotEx.DoNotLoadImageAutomatically))
			{
				SetOuterHtml("SpotImage",
					"<div id='SpotImage' style='border: 1px solid black;padding: 10px;'"
					+ "onmouseover=\"this.style.background='#ffc'; this.style.cursor='pointer'\" "
					+ "onmouseout=\"this.style.background='transparent'; this.style.cursor='default'\">"
					+ "<i>" + Words.ImageLoadDisabledClickToLoad + "</i></div>");
			}
			else if (SpotEx.Image.IsNullOrEmpty() && SpotEx.ImageID.IsNullOrEmpty() && SpotEx.PreviewImage.IsNullOrEmpty())
			{
				SetOuterHtml("SpotImage", "<center><i>" + Words.ImageSourceNotSpecified + "</i></center>");
			}
			else if (!SpotEx.PreviewImage.IsNullOrEmpty())
			{
				ShowLocalImage(SpotEx.PreviewImage, removeOnClose: true);
			}
			else if (!SpotEx.Image.IsNullOrEmpty())
			{
				ShowLocalImage(SpotEx.Image, removeOnClose: false);
			}
			else
			{
				UpdateWithFullImageFromTheNet();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}

	private void ShowLocalImage(string file, bool removeOnClose)
	{
		SetAttribute("SpotImage", "SRC", "file://" + file.Replace("\\", "/"));
		Toolbar.SetImageAsync(System.Drawing.Image.FromFile(file));
		ExecuteJavascript("window.spotnet.prependStyle('SpotImage', 'cursor:pointer;');");
		_isImageResizeable = true;
		if (removeOnClose)
		{
			_fileToRemoveOnClose = file;
			FilesToRemoveOnClose.Add(file);
		}
	}

	private void UpdateWithFullImageFromTheNet()
	{
		string tmpFile = "";
		Task.Factory.StartNew(delegate
		{
			tmpFile = LoadAndSaveFullImage();
		}).ContinueWith(delegate(Task t)
		{
			if (IsClosing)
			{
				return;
			}
			bool shown = false;
			try
			{
				if (t.Exception != null)
				{
					Log.Exception(t.Exception, showToClient: true);
				}
				else if (!tmpFile.IsNullOrEmpty())
				{
					CreateJecSync(delegate
					{
						ShowLocalImage(tmpFile, removeOnClose: true);
					});
					shown = true;
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			finally
			{
				if (!IsClosing && !shown)
				{
					CreateJecSync(delegate
					{
						SetOuterHtml("SpotImage", "");
					});
				}
			}
		});
	}

	public string LoadAndSaveFullImage()
	{
		try
		{
			byte[] bytes = ImageHelper.LoadSpotFullImage(SpotEx);
			FileCacheManager.Save(SpotEx, bytes);
			if (bytes.IsNullOrEmpty())
			{
				return null;
			}
			return WriteBytesToTmpFile(bytes);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return null;
		}
	}

	private string WriteBytesToTmpFile(byte[] bytes)
	{
		string tempFileName = AppHelper.GetTempFileName();
		try
		{
			File.WriteAllBytes(tempFileName, bytes);
			return tempFileName;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return null;
		}
	}

	// --- comments ------------------------------------------------------------

	private void StartProcessComments()
	{
		if (IsClosing)
		{
			return;
		}
		Dispatcher.BeginInvoke((Action)delegate
		{
			string error = "";
			try
			{
				if (!Settings.Default.ShowComments || SpotEx.IsPreview)
				{
					CommentsDone("");
				}
				else if (!StartUpdateComments(SpotEx.MessageId, ref error))
				{
					CommentsDone(error);
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				CommentsDone("StartProcessComments: " + ex.Message);
			}
		}, DispatcherPriority.Background);
	}

	private bool StartUpdateComments(string messageId, ref string error)
	{
		try
		{
			if (IsClosing)
			{
				error = "Exiting";
				return false;
			}
			if (_commentProgressCache != null)
			{
				SetHtml("CommentsProgress", _commentProgressCache);
			}
			ProgressChanged(Words.CommentsLoading + "...", -1);
			Task.Factory.StartNew(delegate
			{
				UpdateComments(messageId);
			});
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	private void UpdateComments(string messageId)
	{
		try
		{
			if (IsClosing)
			{
				return;
			}
			Task task = null;
			if (!DbUpdater.IsDbUpdateInProgress)
			{
				task = Sys.MainWindow.ScheduleCommentsDbUpdate();
			}
			using (ISqlDb db = SqlDbFactory.CreateSqlDbComments(isReadOnly: true))
			{
				GetCommentsFromDb(db, messageId);
			}
			string error = ShowComments();
			if (task != null && !task.IsCompleted)
			{
				task.Wait(TimeSpan.FromSeconds(5.0));
				using (ISqlDb db = SqlDbFactory.CreateSqlDbComments(isReadOnly: true))
				{
					GetCommentsFromDb(db, messageId);
				}
				error = ShowComments();
			}
			CommentsDone(error);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			CommentsDone("UpdateComments: " + ex.Message);
		}
	}

	private void GetCommentsFromDb(ISqlDb db, string messageId)
	{
		string full = SpotHelper.MakeMsg(messageId, tag: false);
		string prefix = full.Substring(0, full.IndexOf("@", StringComparison.Ordinal));
		_fetchedCache.Clear();
		using (ISqlDbTransaction transaction = db.BeginReadTransaction())
		{
			DbCommand command = db.CreateCommand(transaction);
			command.CommandText = "SELECT rowid FROM comments WHERE spot MATCH '" + prefix.Replace("'", "") + "' ORDER BY rowid ASC";
			using DbDataReader reader = db.ExecuteReader(command);
			if (reader == null)
			{
				Log.Debug("Failed to access comments db");
				return;
			}
			while (reader.Read())
			{
				if (reader.IsDBNull(0))
				{
					continue;
				}
				long id = reader.GetInt64(0);
				if (id > 0 && !_fetchedCache.Contains(id))
				{
					_fetchedCache.Add(id);
				}
			}
		}
		Log.Debug("Comments for {0} loaded. Count: {1}", full, _fetchedCache.Count);
	}

	private string ShowComments()
	{
		try
		{
			if (IsClosing)
			{
				return "";
			}
			List<long> pending = _fetchedCache.Where((long id) => !_commentIdCache.Contains(id)).ToList();
			if (!pending.Any())
			{
				ShowCommentsFromUserPostedBefore();
				Thread.Sleep(500);
				return "";
			}
			Action<Comment> onNewComment = delegate(Comment c)
			{
				ShowNewComment(c, isVirtual: false);
			};
			Task task = Comments.StartLoadCommentsBody(AppHelper.HeaderPhuse, pending,
				Sys.MainWindow.CommentSettings(bIncludeLast: false), ProgressChanged, onNewComment,
				_cancelGettingCommentsSource.Token);
			if (task == null)
			{
				return "";
			}
			string result = "";
			task.ContinueWith(delegate(Task t)
			{
				try
				{
					ShowCommentsFromUserPostedBefore();
					if (t.IsFaulted)
					{
						result = t.Exception?.TheMostInnerException().Message ?? "Error on getting comments";
					}
				}
				catch (Exception ex)
				{
					result = ex.TheMostInnerException().Message;
				}
			}).Wait();
			return result;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return "ShowComments: " + ex.Message;
		}
	}

	private void ShowCommentsFromUserPostedBefore()
	{
		if (!MessagesFromUserToShowOnNextTabOpen.ContainsKey(SpotEx.MessageId))
		{
			return;
		}
		foreach (Comment comment in MessagesFromUserToShowOnNextTabOpen[SpotEx.MessageId])
		{
			ShowNewComment(comment, isVirtual: false);
			_messagesFromUserAndAlreadyShown.Add(SpotHelper.MakeMsg(comment.MessageId));
		}
	}

	private void ShowNewComment(Comment comment, bool isVirtual)
	{
		string html = GenerateCommentHtmlCode(comment, isVirtual, isPreview: false);
		if (html == null)
		{
			return;
		}
		ExecuteJavascript("window.spotnet.appendComment(" + Quoted(html) + ");");
		if (comment.Article == 0L)
		{
			return;
		}
		_commentIdCache.Add(comment.Article);
		if (!MessagesFromUserToShowOnNextTabOpen.ContainsKey(SpotEx.MessageId))
		{
			return;
		}
		foreach (Comment item in MessagesFromUserToShowOnNextTabOpen[SpotEx.MessageId])
		{
			if (item.MessageId == SpotHelper.MakeMsg(comment.MessageId, tag: false))
			{
				item.Article = comment.Article;
			}
		}
	}

	public string GenerateCommentHtmlCode(Comment comment, bool isVirtual, bool isPreview)
	{
		try
		{
			string commentClass = "comment";
			if (!isPreview)
			{
				if (IsClosing)
				{
					return null;
				}
				if (comment.MessageId.IsNullOrEmpty())
				{
					Log.Warn("Comment message ID is null or empty");
					return null;
				}
				if (comment.Article != 0L && _commentIdCache.Contains(comment.Article))
				{
					return null;
				}
				if (_messagesFromUserAndAlreadyShown.Contains(SpotHelper.MakeMsg(comment.MessageId)))
				{
					return null;
				}
				if (BlackAndWhite.BlackList().Contains(comment.User.Modulus))
				{
					if (comment.Article != 0L)
					{
						_commentIdCache.Add(comment.Article);
						Log.Debug("User is in black list: " + comment.User.Modulus + ". Skip the comment: " + comment.MessageId);
					}
					else
					{
						Log.Warn("Comment ID is zero: " + comment.User.Modulus);
					}
					return null;
				}
				comment.RemoveAvastMessageFromBody();
				comment.RemovePromoteSpotnetMessageFromBody();
				if (Settings.Default.HideCommentsWithLinks && SpotEx.User.Modulus != comment.User.Modulus
					&& comment.HasLinks() && !IsItMyComment(comment))
				{
					Log.Debug("Comment has links and ignored: " + comment.MessageId);
					return null;
				}
				if (!_uniqueCache.ContainsKey(SpotEx.Poster.ToUpper()))
				{
					_uniqueCache.Add(SpotEx.Poster.ToUpper(), SpotEx.User.Modulus);
				}
			}
			comment.From = AppHelper.StripNonAlphaNumericCharacters(comment.From);
			string tooltip;
			if (isVirtual)
			{
				tooltip = AppHelper.HtmlEncode(AppHelper.MakeUnique(UserKeyHelper.GetModulus()));
			}
			else if (comment.User.Modulus.IsNullOrEmpty() || !comment.User.ValidSignature)
			{
				tooltip = Words.Unknown;
				if (!comment.User.Organisation.IsNullOrEmpty())
				{
					tooltip = tooltip + "\r\n" + AppHelper.HtmlEncode(comment.User.Organisation);
				}
				if (comment.User.Trace.Length > 3)
				{
					tooltip = tooltip + "\r\n" + AppHelper.HtmlEncode(comment.User.Trace);
				}
			}
			else
			{
				if (!_uniqueCache.ContainsKey(comment.From.ToUpper()))
				{
					_uniqueCache.Add(comment.From.ToUpper(), comment.User.Modulus);
				}
				tooltip = AppHelper.HtmlEncode(AppHelper.MakeUnique(comment.User.Modulus));
				if (!comment.User.Organisation.IsNullOrEmpty())
				{
					tooltip = tooltip + "\r\n" + AppHelper.HtmlEncode(comment.User.Organisation);
				}
				if (IsItMyComment(comment) || BlackAndWhite.WhiteList().Contains(comment.User.Modulus))
				{
					commentClass = "trusted";
				}
				else if (comment.User.Modulus.EqualsIgnoreCase(SpotEx.User.Modulus))
				{
					commentClass = "author";
				}
				else
				{
					if (comment.User.Trace.Length > 3)
					{
						tooltip = tooltip + "\r\n" + AppHelper.HtmlEncode(comment.User.Trace);
					}
					if (!_uniqueCache[comment.From.ToUpper()].EqualsIgnoreCase(comment.User.Modulus))
					{
						comment.From = comment.From.Trim() + " (2)";
					}
				}
			}
			return "<SPAN style='visibility:visible'>"
				+ SpotParser.ParseComment(comment, commentClass, tooltip, isPreview) + "</SPAN>";
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return null;
		}
	}

	private bool IsItMyComment(Comment comment)
	{
		return comment.User.Modulus.Equals(UserKeyHelper.GetModulus());
	}

	private void CommentsDone(string error)
	{
		try
		{
			if (IsClosing)
			{
				return;
			}
			string reload = "<p><A onfocus='this.blur()' HREF='spotnet:reload'><IMG id='reload' onfocus='this.blur()' title='"
				+ Words.Refresh + "' style='border: 0px; cursor:pointer; width: 32px; height:32px;' SRC=\""
				+ SpotParser.LocalFilePrefix + AppHelper.SettingsFolder + "/Images/refresh1.png\"></A>";
			if (!error.IsNullOrEmpty())
			{
				SetHtml("CommentsProgress", "<center>" + AppHelper.HtmlEncode(error) + "<br></center>" + reload);
				return;
			}
			string html = "";
			if (_commentIdCache.Count == 0)
			{
				if (!Settings.Default.ShowComments && !_commentsRefreshWasClickedAlready)
				{
					html += "<center>" + Words.CommentsNotRetrieved + "<br></center>";
					_commentsRefreshWasClickedAlready = true;
				}
				else
				{
					html += "<center>" + Words.CommentsNotFound + "<br></center>";
				}
			}
			if (!Settings.Default.LoadComments)
			{
				if (!html.IsNullOrEmpty())
				{
					html += "<br>";
				}
				html += "<center><small>" + Words.CommentsUpdateDisabledWarning + "</small><br></center>";
			}
			else if (!SpotsListVm.IsSpotsDbUpToDate || !SpotsListVm.IsCommentsDbUpToDate)
			{
				if (!html.IsNullOrEmpty())
				{
					html += "<br>";
				}
				html += "<center><small>" + Words.CommentsDbIsNotUpToDateWarning + "</small><br></center>";
			}
			SetHtml("CommentsProgress", html + reload);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	private void ProgressChanged(string message, int value)
	{
		try
		{
			if (IsClosing)
			{
				return;
			}
			SetText("CommentsStatus", message);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	// --- posting a comment ---------------------------------------------------

	private void AddComment()
	{
		try
		{
			if (SpotEx.IsPreview)
			{
				return;
			}
			if (AppHelper.StripNonAlphaNumericCharacters(_commentBody).Trim().IsNullOrEmpty())
			{
				Interaction.MsgBox(Words.CannotPostEmptyMessage, MsgBoxStyle.Information, Words.Error);
				return;
			}
			if (AppHelper.StripNonAlphaNumericCharacters(_commentBody)
				.EqualsIgnoreCase(AppHelper.StripNonAlphaNumericCharacters(_lastBody)))
			{
				Interaction.MsgBox(Words.CannotPostMessageTwice, MsgBoxStyle.Information, Words.Error);
				return;
			}
			long secondsSinceLast = DateAndTime.DateDiff("s", _lastTime, DateAndTime.Now);
			if (secondsSinceLast < 10)
			{
				Interaction.MsgBox(string.Format(Words.NeedToWaitUntilNewMessage, 10 - secondsSinceLast),
					MsgBoxStyle.Information, Words.Error);
				return;
			}
			SetButtonEnabled("AddComment", enabled: false, title: "");
			Sys.MainWindow.DoWait(Words.Commenting);
			Dispatcher.BeginInvoke(new Action(DoComment), DispatcherPriority.Background);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	private void DoComment()
	{
		string error = "";
		try
		{
			string articleId = AppHelper.CreateMsgId(SpotEx.MessageId.Split('@')[0].Replace(".", "").Replace("<", ""));
			Settings.Default.Nickname = AppHelper.StripNonAlphaNumericCharacters(_nickname);
			Settings.Default.Save();
			if (Spots.CreateComment(AppHelper.UploadPhuse, _nickname, _commentBody, Settings.Default.ReplyGroup,
				SpotEx.MessageId, SpotEx.Title, AppHelper.GetAvatar(), UserKeyHelper.GetKey(), articleId, ref error))
			{
				_lastBody = _commentBody;
				_lastTime = DateAndTime.Now;
				_commentBody = "";
				SetValue("CommentBody", "");
				Comment comment = new Comment
				{
					Created = DateAndTime.Now,
					From = Settings.Default.Nickname,
					Body = _lastBody,
					MessageId = SpotHelper.MakeMsg(articleId, tag: false),
					User = new UserInfo()
				};
				if (AppHelper.GetAvatar() != null)
				{
					comment.User.Avatar = Settings.Default.Avatar;
				}
				comment.User.Signature = comment.MessageId;
				comment.User.Modulus = UserKeyHelper.GetModulus();
				comment.User.ValidSignature = true;
				ShowNewComment(comment, isVirtual: true);
				_messagesFromUserAndAlreadyShown.Add(SpotHelper.MakeMsg(articleId));
				if (!MessagesFromUserToShowOnNextTabOpen.ContainsKey(SpotEx.MessageId))
				{
					MessagesFromUserToShowOnNextTabOpen.Add(SpotEx.MessageId, new List<Comment>());
				}
				MessagesFromUserToShowOnNextTabOpen[SpotEx.MessageId].Add(comment);
				AppHelper.ShowPopupMessage(Words.CommentPosted);
			}
			else
			{
				Log.Error(error);
				AppHelper.Error(error);
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
		finally
		{
			Sys.MainWindow.EndWait();
			SetButtonEnabled("AddComment", enabled: true, title: Words.Send);
		}
	}

	private void DownloadSpot()
	{
		try
		{
			if (SpotEx.IsPreview)
			{
				return;
			}
			if (Settings.Default.DownloadAction <= 1)
			{
				Sys.MainWindow.TabControl1.SelectedIndex = 1;
			}
			SetButtonEnabled("DownloadButton", enabled: false, title: "");
			Task.Factory.StartNew(delegate
			{
				SpotHelper.DownloadNzbAndStartDownloadItem(SpotEx);
			}).ContinueWith(delegate
			{
				SetButtonEnabled("DownloadButton", enabled: true, title: Words.Download);
				Sys.MainWindow.EndWait();
			}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	// --- quoting -------------------------------------------------------------

	private void InsertQuote(string menuPayload, string body)
	{
		if (!GetMenuSenderInfo(menuPayload, out string senderName, out string _))
		{
			return;
		}
		ExecuteJavascript("window.spotnet.scrollToComment();");
		ExecuteJavascript("window.spotnet.insertIntoComment(" + Quoted(GenerateQuote(body, senderName)) + ");");
	}

	private void InsertReply(string menuPayload)
	{
		if (!GetMenuSenderInfo(menuPayload, out string senderName, out string _))
		{
			return;
		}
		ExecuteJavascript("window.spotnet.scrollToComment();");
		ExecuteJavascript("window.spotnet.insertIntoComment(" + Quoted("[b]" + senderName + "[/b]: ") + ");");
	}

	internal static string GenerateQuote(string text, string author)
	{
		text = Regex.Replace(text, "<(\\/)?(b|i|u)>", "[$1$2]", RegexOptions.IgnoreCase);
		text = text.ReplaceIgnoreCase("<br>", "\r\n");
		text = text.ReplaceIgnoreCase("&lt;", "<");
		text = text.ReplaceIgnoreCase("&gt;", ">");
		text = Regex.Replace(text, "<img [^>]*title=(\")?([^ \"]+)(\")?[^>]*>", "[img=$2]", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, "<a [^>]*href=\"link:([^ >]+)\"[^>]*>([^<>]*)</a>", "$2", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, "<span onmouseover[^ >']+'(.*)'[^>]*>[^<>]*</span>", "$1", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, "<blockquote><cite style=.display:[ ]+block;.>([a-zA-Z0-9]+) \\w+:</cite>[ \\r\\n]*",
			"[quote=\"$1\"]", RegexOptions.IgnoreCase);
		text = text.ReplaceIgnoreCase("</blockquote>", "[/quote]");
		return "[quote=\"" + author + "\"]" + text + "[/quote]\r\n";
	}

	// --- author menus --------------------------------------------------------

	internal static bool GetMenuSenderInfo(string href, out string senderName, out string modulus)
	{
		senderName = null;
		modulus = null;
		if (href.IsNullOrEmpty() || href.Contains("'"))
		{
			return false;
		}
		string[] parts = href.Split('_');
		if (parts.Length == 2)
		{
			modulus = parts[0];
			senderName = parts[1];
		}
		else if (parts.Length == 3)
		{
			modulus = parts[0];
			senderName = parts[2];
		}
		return !modulus.IsNullOrWhiteSpace();
	}

	private void CreateMenu(string from, string modulus)
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			SpotMenu = NewContextMenu();
			_menuFrom = from;
			_menuModulus = modulus;
			SpotMenu.Items.Add(MenuItemFor("search", Words.SearchByName, "search", enabled: true));
			SpotMenu.Items.Add(MenuItemFor("searchid", Words.SearchById, "search", enabled: true));
			if (modulus.EqualsIgnoreCase(UserKeyHelper.GetModulus()))
			{
				SpotMenu.Items.Add(new Separator());
				SpotMenu.Items.Add(MenuItemFor("ava", Words.ChangeAvatar, "settings", enabled: true));
			}
			else if (!from.IsNullOrEmpty())
			{
				SpotMenu.Items.Add(new Separator());
				SpotMenu.Items.Add(MenuItemFor("fav",
					BlackAndWhite.WhiteList().Contains(modulus) ? Words.WhiteListRemoveFrom : Words.WhiteListAddTo,
					"favorite",
					!modulus.IsNullOrEmpty() && !BlackAndWhite.BlackList().Contains(modulus)));
				SpotMenu.Items.Add(MenuItemFor("black",
					BlackAndWhite.BlackList().Contains(modulus) ? Words.BlackListRemoveFrom : Words.BlackListAddTo,
					"trash",
					!modulus.IsNullOrEmpty() && !BlackAndWhite.WhiteList().Contains(modulus)
						&& !modulus.EqualsIgnoreCase(UserKeyHelper.GetModulus())));
			}
			SpotMenu.IsOpen = true;
			SpotMenu.PreviewMouseUp += SpotMenu_PreviewMouseUp;
		});
	}

	private ContextMenu NewContextMenu()
	{
		return new ContextMenu
		{
			FontFamily = Sys.MainWindow.FontFamily,
			FontSize = (double)System.Windows.Application.Current.Resources["ContextMenuFontSize"],
			FontStyle = Sys.MainWindow.FontStyle,
			Resources = AppHelper.GetMenuResourceDictionary
		};
	}

	private static MenuItem MenuItemFor(string tag, string header, string icon, bool enabled)
	{
		MenuItem item = new MenuItem
		{
			Tag = tag,
			Header = header,
			Icon = AppHelper.GetIcon(icon),
			IsEnabled = enabled
		};
		if (item.Icon is UIElement element)
		{
			element.Opacity = enabled ? 1.0 : 0.5;
		}
		return item;
	}

	private void CreateCopySelectionMenu()
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			SpotMenu = NewContextMenu();
			System.Windows.Controls.Image icon;
			try
			{
				icon = new System.Windows.Controls.Image
				{
					Source = new BitmapImage(new Uri("pack://application:,,,/Spotnet;component/Resources/ImagesInternal/copy.png", UriKind.Absolute)),
					Width = 16.0,
					Height = 16.0
				};
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				icon = null;
			}
			SpotMenu.Items.Add(new MenuItem
			{
				Tag = "copy",
				IsEnabled = true,
				Header = Words.Copy,
				Icon = icon
			});
			SpotMenu.IsOpen = true;
			SpotMenu.PreviewMouseUp += SpotMenu_PreviewMouseUp;
		});
	}

	private void SpotMenu_PreviewMouseUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			if (!(e?.Source is MenuItem { Tag: not null } menuItem))
			{
				return;
			}
			string tag = menuItem.Tag.ToString().ToLower();
			if (tag.EqualsIgnoreCase("fav"))
			{
				ReverseModulusWhiteList(_menuFrom, _menuModulus);
			}
			else if (tag.EqualsIgnoreCase("black"))
			{
				ReverseModulusBlackList(_menuFrom, _menuModulus);
			}
			else if (tag.EqualsIgnoreCase("search"))
			{
				Sys.LeftPanel.SearchFilter("sender MATCH '" + _menuFrom.ToLower() + "'", _menuFrom);
			}
			else if (tag.EqualsIgnoreCase("searchid"))
			{
				Sys.LeftPanel.SearchFilter("modulus LIKE '" + _menuModulus + "'",
					_menuFrom + " (" + AppHelper.MakeUnique(_menuModulus) + ")");
			}
			else if (tag.EqualsIgnoreCase("ava"))
			{
				if (ImageHelper.ChangeAvatar(out string newAvatar) && !newAvatar.IsNullOrEmpty())
				{
					Settings.Default.Avatar = newAvatar;
					Settings.Default.Save();
					AppHelper.ShowPopupMessage(Words.AvatarChangedForFuturePosts);
					_updateCommentPreviewTimer.Start();
				}
			}
			else if (tag.EqualsIgnoreCase("copy"))
			{
				// The host already has the selection, so this copies it directly rather
				// than asking the document to run a clipboard command.
				CopySelectionToClipboard();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	private void CopySelectionToClipboard()
	{
		if (_selectedText.IsNullOrEmpty())
		{
			return;
		}
		try
		{
			Clipboard.SetText(_selectedText);
		}
		catch (Exception ex)
		{
			// The clipboard can be held by another process; losing a copy is not worth
			// an error dialog.
			Log.Debug("Could not copy the selection: " + ex.Message);
		}
		ExecuteJavascript("window.spotnet.clearSelection();");
	}

	// --- black and white lists -----------------------------------------------

	private void ReverseModulusBlackList(string username, string modulus)
	{
		if (modulus.IsNullOrEmpty() || BlackAndWhite.WhiteList().Contains(modulus))
		{
			return;
		}
		if (BlackAndWhite.BlackList().Contains(modulus))
		{
			BlackAndWhite.RemoveBlack(modulus);
			UpdateSpotPosterStatus(modulus);
			AppHelper.ShowPopupMessage(Words.BlackListYouWillReceiveFromSender, inTheCenter: false, TimeSpan.FromSeconds(3.0));
			UpdateCommentAuthor(modulus,
				modulus.EqualsIgnoreCase(SpotEx.User.Modulus) ? "author" : "comment",
				Words.BlackListAddTo, hideAvatarAndDesc: false, hideBlackLink: null);
		}
		else
		{
			BlackAndWhite.AddBlack(AppHelper.StripNonAlphaNumericCharacters(username), modulus);
			UpdateSpotPosterStatus(modulus);
			AppHelper.ShowPopupMessage(Words.BlackListYouWillNotReceiveFromSender, inTheCenter: false, TimeSpan.FromSeconds(3.0));
			UpdateCommentAuthor(modulus, "untrusted", Words.BlackListRemoveFrom,
				hideAvatarAndDesc: true, hideBlackLink: null);
		}
	}

	private void ReverseModulusWhiteList(string username, string modulus)
	{
		if (modulus.IsNullOrEmpty() || BlackAndWhite.BlackList().Contains(modulus))
		{
			return;
		}
		if (BlackAndWhite.WhiteList().Contains(modulus))
		{
			BlackAndWhite.RemoveWhite(modulus);
			UpdateSpotPosterStatus(modulus);
			UpdateCommentAuthor(modulus,
				modulus.EqualsIgnoreCase(SpotEx.User.Modulus) ? "author" : "comment",
				blackLinkText: null, hideAvatarAndDesc: null, hideBlackLink: false);
		}
		else
		{
			BlackAndWhite.AddWhite(AppHelper.StripNonAlphaNumericCharacters(username), modulus);
			UpdateSpotPosterStatus(modulus);
			UpdateCommentAuthor(modulus, "trusted",
				blackLinkText: null, hideAvatarAndDesc: null, hideBlackLink: true);
		}
	}

	private void UpdateCommentAuthor(string modulus, string className, string blackLinkText,
		bool? hideAvatarAndDesc, bool? hideBlackLink)
	{
		ExecuteJavascript(string.Format("window.spotnet.updateCommentAuthor({0}, {1}, {2}, {3}, {4});",
			Quoted(modulus), Quoted(className),
			blackLinkText == null ? "null" : Quoted(blackLinkText),
			Nullable(hideAvatarAndDesc), Nullable(hideBlackLink)));
	}

	private void UpdateSpotPosterStatus(string modulus)
	{
		if (!modulus.EqualsIgnoreCase(SpotEx.User.Modulus))
		{
			return;
		}
		SpotEx.PosterIdent = PosterIdentType.Unspecified;
		SetHtml("PosterIdentLinks",
			SpotParser.GeneratePosterLinksHtmlCode(SpotEx.Poster, SpotEx.User, SpotEx.PosterIdent));
		SetHtml("PosterIdentLabel", SpotParser.GeneratePosterIdentLabelHtmlCode(SpotEx.PosterIdent));
	}

	private void OpenSpamReportsPopup(string messageId)
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			SpotMenu = NewContextMenu();
			SpamReportsGrid grid = new SpamReportsGrid
			{
				MessageId = messageId
			};
			SpotMenu.Items.Add(grid);
			grid.Visibility = Visibility.Visible;
			SpotMenu.IsOpen = true;
		});
	}

	// --- document helpers ----------------------------------------------------

	/// <summary>Renders a value as a JavaScript string literal.</summary>
	/// <remarks>
	/// Everything crossing into the page goes through here. Most of it - comment bodies,
	/// spot descriptions - came off Usenet, so it is never pasted into a script by hand.
	/// </remarks>
	internal static string Quoted(string value)
	{
		return Newtonsoft.Json.JsonConvert.SerializeObject(value ?? "");
	}

	private static string Nullable(bool? value)
	{
		if (!value.HasValue)
		{
			return "null";
		}
		return value.Value ? "true" : "false";
	}

	private void SetHtml(string id, string html)
	{
		ExecuteJavascript("window.spotnet.setHtml(" + Quoted(id) + ", " + Quoted(html) + ");");
	}

	private void SetOuterHtml(string id, string html)
	{
		ExecuteJavascript("window.spotnet.setOuterHtml(" + Quoted(id) + ", " + Quoted(html) + ");");
	}

	private void SetText(string id, string text)
	{
		ExecuteJavascript("window.spotnet.setText(" + Quoted(id) + ", " + Quoted(text) + ");");
	}

	private void SetStyle(string id, string css)
	{
		ExecuteJavascript("window.spotnet.setStyle(" + Quoted(id) + ", " + Quoted(css) + ");");
	}

	private void SetAttribute(string id, string name, string value)
	{
		ExecuteJavascript("window.spotnet.setAttr(" + Quoted(id) + ", " + Quoted(name) + ", " + Quoted(value) + ");");
	}

	private void SetValue(string id, string value)
	{
		ExecuteJavascript("window.spotnet.setValue(" + Quoted(id) + ", " + Quoted(value) + ");");
	}

	private void SetClass(string id, string name)
	{
		ExecuteJavascript("window.spotnet.setClass(" + Quoted(id) + ", " + Quoted(name) + ");");
	}

	private void SetButtonEnabled(string id, bool enabled, string title)
	{
		ExecuteJavascript(string.Format("window.spotnet.setButtonEnabled({0}, {1}, {2});",
			Quoted(id), enabled ? "true" : "false", Quoted(title)));
	}

	/// <summary>Reads an element's inner HTML, unwrapping the JSON the engine returns.</summary>
	private async Task<string> GetHtmlAsync(string id)
	{
		string json = await ExecuteJavascriptWithResultAsync("window.spotnet.getHtml(" + Quoted(id) + ");");
		if (json.IsNullOrEmpty())
		{
			return null;
		}
		try
		{
			return Newtonsoft.Json.JsonConvert.DeserializeObject<string>(json);
		}
		catch (Exception ex)
		{
			Log.Debug("Could not read " + id + ": " + ex.Message);
			return null;
		}
	}

	// --- teardown ------------------------------------------------------------

	public override void Dispose()
	{
		try
		{
			_cancelGettingCommentsSource.Cancel();
			_updateCommentPreviewTimer.Stop();
			_updateCommentPreviewTimer.Dispose();
			if (_toolbarInitialized)
			{
				BrowserGotFocusEvent -= RedrawPopup;
				Sys.MainWindow.LocationChanged -= RedrawPopup;
				Sys.MainWindow.SizeChanged -= RedrawPopup;
				Sys.MainWindow.Activated -= MainWindowOnActivated;
				Sys.MainWindow.Deactivated -= MainWindowOnDeactivated;
				Toolbar.Dispose();
			}
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		// After the base class, so the browser has let go of the document file.
		base.Dispose();
		RemoveRenderedFiles();
	}

	private void RemoveRenderedFiles()
	{
		foreach (string file in new[] { _fileToRemoveOnClose, _htmlFile })
		{
			if (file.IsNullOrEmpty())
			{
				continue;
			}
			try
			{
				File.Delete(file);
				FilesToRemoveOnClose.Remove(file);
			}
			catch (Exception ex)
			{
				// A file the browser still holds is cleaned up with the temp directory.
				Log.Debug(ex.Message);
			}
		}
	}
}
