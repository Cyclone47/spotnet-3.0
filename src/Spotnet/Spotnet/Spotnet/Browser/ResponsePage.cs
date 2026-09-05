using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using NLog;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Browser;

internal class ResponsePage : WebView2Page
{
	private const string PageTitleOfResponseSite = "Feedback";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	/// <summary>
	/// The single message the feedback page is allowed to send the host.
	/// </summary>
	/// <remarks>
	/// WebView2 exposes a host function without a COM-visible object through a script shim
	/// that posts a message, handled here. Because the page is remote, the handler treats anything arriving on that
	/// channel as untrusted input: it accepts exactly this one literal and ignores
	/// everything else, so the page cannot reach any other host behaviour.
	/// </remarks>
	private const string UploadLogsMessage = "UploadLogs";

	/// <summary>
	/// Recreates the <c>app.UploadLogs()</c> entry point the feedback page calls.
	/// </summary>
	private const string HostBridgeScript =
		"window.app = window.app || {};" +
		"window.app.UploadLogs = function () {" +
		"  if (window.chrome && window.chrome.webview) {" +
		"    window.chrome.webview.postMessage('" + UploadLogsMessage + "');" +
		"  }" +
		"};";

	private bool _uploadDisabled;

	private readonly object _lockRoot = new object();

	public ResponsePage()
	{
		base.Uri = new Uri(GetResponseSiteUrl(), UriKind.RelativeOrAbsolute);
		Title = PageTitleOfResponseSite;
		base.PageDefaultType = PageTypeEnum.ResponseSite;
	}

	protected override async Task OnCoreWebView2ReadyAsync(CoreWebView2 core)
	{
		if (core == null)
		{
			return;
		}
		core.WebMessageReceived += OnWebMessageReceived;
		// Injected before the first document so app.UploadLogs exists by the time the
		// page's own scripts run. This replaces the old bind-on-DocumentReady approach,
		// which could race the page and needed its own "already bound" guard.
		await core.AddScriptToExecuteOnDocumentCreatedAsync(HostBridgeScript);
	}

	private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
	{
		string message;
		try
		{
			message = e.TryGetWebMessageAsString();
		}
		catch (ArgumentException)
		{
			// Not a string message; nothing this page sends looks like that.
			return;
		}

		if (!UploadLogsMessage.Equals(message, StringComparison.Ordinal))
		{
			Log.Debug("Ignoring unrecognized web message from the feedback page.");
			return;
		}

		lock (_lockRoot)
		{
			if (_uploadDisabled)
			{
				return;
			}
			_uploadDisabled = true;
		}

		bool uploadStarted = false;
		try
		{
			ChangeStateOfResponseSubmitButton(enabled: false);
			uploadStarted = ZipAndUploadLogs();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
		finally
		{
			if (!uploadStarted)
			{
				ChangeStateOfResponseSubmitButton(enabled: true);
				lock (_lockRoot)
				{
					// Nothing was sent, so let the user try again.
					_uploadDisabled = false;
				}
			}
		}
	}

	public static string GetResponseSiteUrl()
	{
		string providerDomainHtmlSave = AppHelper.GetProviderDomainHtmlSave();
		string text = ((providerDomainHtmlSave == null) ? "" : ("&provider=" + providerDomainHtmlSave));
		return string.Format("{0}/?id={1}&appversion={2}&lang={3}{4}", Configuration.ResponseSiteUrl, UserKeyHelper.GetModulusUriCompatable(), AppHelper.AppVersion, (UserLanguageHelper.Language == "en") ? "en" : "nl", text);
	}

	private bool ZipAndUploadLogs()
	{
		string text = LogHelper.ZipLogFiles();
		if (text.IsNullOrEmpty())
		{
			Log.Debug("No log files to attach.");
			return false;
		}
		return AppHelper.UploadFile(text, Configuration.ResponseSiteUploadLogsUrl, delegate(object s, UploadFileCompletedEventArgs e)
		{
			if (e.Error != null)
			{
				Log.Exception(e.Error);
			}
			ChangeStateOfResponseSubmitButton(enabled: true);
		});
	}

	private void ChangeStateOfResponseSubmitButton(bool enabled)
	{
		string script = string.Format("document.getElementById('form-submit-btn').disabled = {0};", enabled ? "false" : "true");
		ExecuteJavascript(script);
	}
}
