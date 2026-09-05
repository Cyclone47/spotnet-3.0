using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Threading;
using Spotnet.Mvvm.Threading;
using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spotnet.Controls;
using Spotnet.Downloader.ViewModel;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;
using Spotnet.Phuse.NNTP.Net;
using Spotnet.Properties;
using Spotnet.Views;

namespace Spotnet.Downloader;

public class NzbGetDownloader : IDownloader, INotifyPropertyChanged, IDisposable
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static bool _isWarningAboutDbUpdateClosedByUser;

	private bool _isUpdateItemsOrderDisabledForMassUpdate;

	private DownloaderItems _items;

	private Visibility _spotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility = Visibility.Collapsed;

	private DownloaderTotals _totalItems;

	private int _lockShutdownDialogShown;

	private readonly object _lockRpcRequest = new object();

	private bool _isDownloaderNotAvailable;

	private System.Timers.Timer _updateTimer;

	private readonly object _lockUpdateItems = new object();

	private bool _throwExceptionForUpdateOneTimeFlag;

	private readonly object _lockStartStop = new object();

	public DownloaderItems Items => LazyInitializer.EnsureInitialized(ref _items, () => new DownloaderItems());

	public DownloaderTotals TotalItems => LazyInitializer.EnsureInitialized(ref _totalItems, delegate
	{
		DownloaderTotals downloaderTotals = new DownloaderTotals(Items);
		downloaderTotals.ProgressChanged += TotalsOnProgressChanged;
		return downloaderTotals;
	});

	public bool IsDownloaderNotAvailable
	{
		get
		{
			return _isDownloaderNotAvailable;
		}
		private set
		{
			if (this.OnDownloaderLoadedFirstTime != null && _isDownloaderNotAvailable != value)
			{
				_isDownloaderNotAvailable = value;
				OnPropertyChanged("IsDownloaderNotAvailable");
			}
		}
	}

	private string RpcUrl => string.Format("http://{2}:{3}/{0}:{1}/jsonrpc", DownloaderProps.ControlUsername, DownloaderProps.ControlPassword, DownloaderProps.ControlIp, DownloaderProps.ControlPort);

	private bool IsDownloadsTabActive
	{
		get
		{
			if (!Sys.IsShutdownRequested)
			{
				return DispatcherHelper.UIDispatcher.Invoke(() => Sys.MainWindow != null && Sys.MainWindow.IsDownloadsTabSelectedAndVisible, DispatcherPriority.Input);
			}
			return false;
		}
	}

	public Visibility SpotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility
	{
		get
		{
			return _spotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility;
		}
		set
		{
			if (_spotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility != value)
			{
				_spotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility = value;
				OnPropertyChanged("SpotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility");
			}
		}
	}

	public bool IsStarted { get; private set; }

	public event Action OnDownloaderLoadedFirstTime;

	public event Action ItemsOrderChanged;

	public event Action<object, DownloadStatus> DownloaderStatusChanged;

	public event Action<int> DownloadsProgressChanged;

	public event PropertyChangedEventHandler PropertyChanged;

	public NzbGetDownloader()
	{
		DbUpdater.OnDbUpdateStart += DbUpdaterOnDbUpdateStart;
		DbUpdater.OnDbUpdateEnd += DbUpdaterOnDbUpdateEnd;
	}

	private void DbUpdaterOnDbUpdateEnd()
	{
		if (SpotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility == Visibility.Collapsed)
		{
			_isWarningAboutDbUpdateClosedByUser = true;
		}
		else
		{
			SpotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility = Visibility.Collapsed;
		}
	}

	private void DbUpdaterOnDbUpdateStart()
	{
		if (!_isWarningAboutDbUpdateClosedByUser)
		{
			SpotsDbIsNotUpToDateSoSpeedCanBeLowWarningVisibility = Visibility.Visible;
		}
	}

	private void OnIsHistoryChanged(DownloaderItemViewModel sender, bool isHistory)
	{
		if (!isHistory || sender.RawStatus == DownloadStatus.Deleted)
		{
			return;
		}
		DispatcherHelper.CheckBeginInvokeOnUI(async delegate
		{
			try
			{
				// Sys.MainWindow, not Application.Current.MainWindow. Nothing ever assigns the
				// latter - Spotnet drives its own startup and shows a splash first - so it was
				// null here, the cast quietly produced null, and the call threw a
				// NullReferenceException that this catch swallowed. Every finished download has
				// been reported into that catch block rather than to the user.
				MainWindow main = Sys.MainWindow;
				if (main == null)
				{
					Log.Warn("No main window, so the finished download cannot be reported: " + sender.Titel);
				}
				else
				{
					main.DisplayTooltip(sender.Titel + " " + Words.isComplete, sender.RawStatus == DownloadStatus.Success);
				}
				await ProcessShutdownPcAfterDownloads();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		});
	}

	private async Task ProcessShutdownPcAfterDownloads()
	{
		if (!Sys.ShutdownPCAfterDownloads || IsAnyActiveDownloads() || Interlocked.CompareExchange(ref _lockShutdownDialogShown, 1, 0) != 0)
		{
			return;
		}
		try
		{
			ShutdownComputerDialog shutdownComputerDialog = new ShutdownComputerDialog(Words.ShutdownDialogComputerToBeShutdownDownloadsCompleted);
			shutdownComputerDialog.Owner = Sys.MainWindow;
			shutdownComputerDialog.Show();
			shutdownComputerDialog.Activate();
			shutdownComputerDialog.Topmost = true;
			shutdownComputerDialog.Topmost = false;
			shutdownComputerDialog.Focus();
			ManualResetEventSlim waitForWindowToClose = new ManualResetEventSlim();
			shutdownComputerDialog.Closed += delegate
			{
				waitForWindowToClose.Set();
			};
			await Task.Run(delegate
			{
				waitForWindowToClose.Wait();
			});
		}
		finally
		{
			Interlocked.Exchange(ref _lockShutdownDialogShown, 0);
		}
	}

	private void TotalsOnProgressChanged(double p)
	{
		this.DownloadsProgressChanged?.Invoke((int)p);
	}

	public bool IsDownloadInQueueAlready(string messageId, out DownloaderItemViewModel item)
	{
		return Items.IsDownloadQueuedAlready(messageId, out item);
	}

	public bool IsAnyActiveDownloads()
	{
		return Items.Any((DownloaderItemViewModel i) => i.IsDownloading || i.IsQueued);
	}

	public void RestartProcessAsync()
	{
		ShutdownProcessAsync().ContinueWith(delegate(Task<bool> t)
		{
			if (t.Result)
			{
				StartProcessAsync();
			}
			else
			{
				AppHelper.Error("Failed to restart downloader, check logs for details");
				this.DownloaderStatusChanged?.Invoke(this, DownloadStatus.Failure);
			}
		});
	}

	public bool AddToDownloadQueue(string pathToNzb, DownloaderItemViewModel item)
	{
		if (!item.MessageId.IsNullOrEmpty() && IsDownloadInQueueAlready(item.MessageId, out var item2))
		{
			item2.Blink();
			Log.Debug("The spot in the download queue already: " + item.MessageId);
			return false;
		}
		if (!System.IO.File.Exists(pathToNzb))
		{
			Log.Error("Nzb file not found: " + pathToNzb);
			return false;
		}
		item.RawStatus = DownloadStatus.NzbDownloading;
		Task.Run(delegate
		{
			item.PathToNzb = pathToNzb;
			try
			{
				StopUpdateTimer();
				if (AddDownloadToNzbGet(item, out var _))
				{
					if (!item.MessageId.IsNullOrEmpty())
					{
						Items.SetNewMsgIdCatRelation(item.ID, item.MessageId, item.Category);
					}
					item.RawStatus = DownloadStatus.Queued;
					item.IsHistoryChanged += OnIsHistoryChanged;
				}
			}
			finally
			{
				InitializeUpdateTimer();
			}
		});
		return true;
	}

	private byte[] FixXmlEncodingInfo(byte[] xmlContentBytes)
	{
		int count = ((xmlContentBytes.Length > 200) ? 200 : xmlContentBytes.Length);
		if (AppHelper.AnsiEnc().GetString(xmlContentBytes, 0, count).Trim().ToUpper()
			.Contains("<?XML "))
		{
			return xmlContentBytes;
		}
		return AppHelper.AnsiEnc().GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>").Concat(xmlContentBytes).ToArray();
	}

	private bool AddDownloadToNzbGet(DownloaderItemViewModel item, out string error)
	{
		error = "";
		byte[] xmlContentBytes = System.IO.File.ReadAllBytes(item.PathToNzb);
		string text = Convert.ToBase64String(FixXmlEncodingInfo(xmlContentBytes));
		string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(item.PathToNzb);
		if (item.PathToNzb.EndsWith(".nzb"))
		{
			fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(item.PathToNzb);
		}
		JObject jObject = JsonRpcRequest("append", default(TimeSpan), fileNameWithoutExtension, text, "", 0, false, false, "", 0, "all");
		if (jObject == null)
		{
			error = "Empty response from NzbGet on append. Please check NzbGet logs to know the reason.";
			return false;
		}
		string text2 = jObject.ToResultString();
		if (text2 != null)
		{
			int num = Convert.ToInt32(text2);
			if (num > 0)
			{
				int iD = item.ID;
				item.ID = num;
				Sys.Downloader.Items.RefreshId(iD);
				return true;
			}
		}
		error = jObject.ToErrorString();
		if (error.IsNullOrEmpty())
		{
			if (!System.IO.Directory.Exists(Settings.Default.DownloadFolder))
			{
				error = "Download folder doesn't exist: " + Settings.Default.DownloadFolder + ". Please create one or change DownloadFolder setting.";
			}
			else
			{
				error = "Empty response from NzbGet on append. Please check NzbGet logs: {0} to know the reason.";
			}
		}
		return false;
	}

	public DownloaderItemViewModel AddFakeItemBeforeNzbDownloaded(string sTitle, string messageId, int category)
	{
		return new NzbGetDownloaderItemViewModel(-1, sTitle, DownloadStatus.NzbDownloading, 0, 0.0, 0, -1, null, null, "", messageId, category, null, 0L, 0L);
	}

	public JObject JsonRpcRequest(string method, TimeSpan timeout = default(TimeSpan), params object[] parameters)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(RpcUrl);
		httpWebRequest.ContentType = "application/json-rpc";
		httpWebRequest.Method = "POST";
		httpWebRequest.Timeout = ((timeout == default(TimeSpan)) ? 5000 : ((int)timeout.TotalMilliseconds));
		JObject jObject = new JObject
		{
			{ "jsonrpc", "1.0" },
			{ "id", "1" },
			{ "method", method }
		};
		if (parameters != null && parameters.Length != 0)
		{
			JArray jArray = new JArray();
			foreach (object content in parameters)
			{
				jArray.Add(content);
			}
			jObject.Add(new JProperty("params", jArray));
		}
		string s = JsonConvert.SerializeObject(jObject);
		byte[] bytes = AppHelper.AnsiEnc().GetBytes(s);
		httpWebRequest.ContentLength = bytes.Length;
		lock (_lockRpcRequest)
		{
			try
			{
				using Stream stream = httpWebRequest.GetRequestStream();
				stream.Write(bytes, 0, bytes.Length);
			}
			catch (WebException ex)
			{
				Log.Warn(ex.Message);
				IsDownloaderNotAvailable = true;
				return null;
			}
			try
			{
				using WebResponse webResponse = httpWebRequest.GetResponse();
				using Stream stream2 = webResponse.GetResponseStream();
				using StreamReader streamReader = new StreamReader(stream2);
				JObject result = JsonConvert.DeserializeObject<JObject>(streamReader.ReadToEnd());
				IsDownloaderNotAvailable = false;
				return result;
			}
			catch (WebException ex2)
			{
				Log.Warn("NzbGet: " + ex2.Message);
				if (ex2.Response == null)
				{
					Log.Error("No response to " + method + " from NzbGet");
					IsDownloaderNotAvailable = true;
					return null;
				}
				using Stream stream3 = ex2.Response.GetResponseStream();
				using StreamReader streamReader2 = new StreamReader(stream3);
				JObject result2 = JsonConvert.DeserializeObject<JObject>(streamReader2.ReadToEnd());
				IsDownloaderNotAvailable = false;
				return result2;
			}
			catch (Exception ex3)
			{
				Log.Exception(ex3);
				return null;
			}
		}
	}

	public bool IsDownloaderResponding(TimeSpan timeout = default(TimeSpan))
	{
		try
		{
			return !JsonRpcRequest("version", timeout).ToResultString().IsNullOrEmpty();
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	public bool WaitForResponding(TimeSpan timeout = default(TimeSpan))
	{
		DateTime now = DateTime.Now;
		if (timeout == default(TimeSpan))
		{
			timeout = TimeSpan.FromSeconds(10.0);
		}
		int num = 1000;
		while (!IsDownloaderResponding(TimeSpan.FromMilliseconds(num)))
		{
			Thread.Sleep(200);
			num += 300;
			if (DateTime.Now - now > timeout)
			{
				return false;
			}
		}
		return true;
	}

	public void InitializeUpdateTimer()
	{
		if (_updateTimer == null)
		{
			_updateTimer = new System.Timers.Timer
			{
				AutoReset = false,
				Interval = 10.0
			};
			_updateTimer.Elapsed += UpdateTimerElapsed;
			_updateTimer.Start();
		}
	}

	private void StopUpdateTimer()
	{
		if (_updateTimer != null)
		{
			lock (_lockUpdateItems)
			{
				_updateTimer.Elapsed -= UpdateTimerElapsed;
				_updateTimer.Stop();
				_updateTimer.Dispose();
				_updateTimer = null;
			}
		}
	}

	public void UpdateItems()
	{
		NzbGetGroups nzbGetGroups;
		try
		{
			nzbGetGroups = new NzbGetGroups();
		}
		catch (ExternalException ex)
		{
			Log.Debug(ex.Message);
			return;
		}
		Items.Update(nzbGetGroups.Groups.Concat(nzbGetGroups.HistoryGroups).ToList());
		TotalItems.UpdateTotal(nzbGetGroups.OnGlobalPause);
	}

	public void QuickTimerRefresh()
	{
		if (_updateTimer != null)
		{
			_updateTimer.Interval = 10.0;
		}
	}

	private void UpdateTimerElapsed(object sender, ElapsedEventArgs e)
	{
		if (!Monitor.TryEnter(_lockUpdateItems) || _updateTimer == null)
		{
			return;
		}
		try
		{
			SetUpdateTimerInterval(IsDownloadsTabActive ? 1 : 10);
			UpdateItems();
		}
		catch (Exception ex)
		{
			if (!Sys.IsShutdownRequested)
			{
				Log.Exception(ex, !_throwExceptionForUpdateOneTimeFlag);
				if (!_throwExceptionForUpdateOneTimeFlag)
				{
					_throwExceptionForUpdateOneTimeFlag = true;
				}
			}
		}
		finally
		{
			if (_updateTimer != null)
			{
				_updateTimer.Start();
			}
			Monitor.Exit(_lockUpdateItems);
		}
	}

	private void SetUpdateTimerInterval(int iFactor)
	{
		if (_updateTimer != null)
		{
			_updateTimer.Interval = checked(Settings.Default.NzbGetRefresh * iFactor);
		}
	}

	public Task<bool> StartProcessAsync()
	{
		return Task.Run(delegate
		{
			lock (_lockStartStop)
			{
				if (IsStarted)
				{
					QuickTimerRefresh();
					return true;
				}
				if (Settings.Default.DownloadAction > 1)
				{
					return true;
				}
				try
				{
					this.DownloaderStatusChanged?.Invoke(this, DownloadStatus.Starting);
					this.OnDownloaderLoadedFirstTime?.Invoke();
					if (!WaitForResponding())
					{
						Log.Error("Timeout on waiting for NzbGet to respond.");
						IsDownloaderNotAvailable = true;
						SystemStateChecker.AddProblem(SystemStateProblemEnum.NzbGet, "Timeout on waiting for NzbGet to respond.");
						return false;
					}
					InitializeUpdateTimer();
					SystemStateChecker.RemoveProblem(SystemStateProblemEnum.NzbGet);
					IsStarted = true;
				}
				finally
				{
					this.DownloaderStatusChanged?.Invoke(this, DownloadStatus.Queued);
				}
			}
			return true;
		});
	}

	public Task<bool> ShutdownProcessAsync()
	{
		return Task.Run(delegate
		{
			StopUpdateTimer();
			IsStarted = false;
			return true;
		});
	}

	public bool CanMoveDown(IEnumerable<DownloaderItemViewModel> items)
	{
		DownloaderItemViewModel[] source = (items as DownloaderItemViewModel[]) ?? items.ToArray();
		if (source.Any((DownloaderItemViewModel i) => i.IsHistory))
		{
			return false;
		}
		DownloaderItemViewModel downloaderItemViewModel = NextPriorityItem(source.OrderByDescending((DownloaderItemViewModel i) => i.Index).FirstOrDefault());
		if (downloaderItemViewModel != null)
		{
			return !downloaderItemViewModel.IsHistory;
		}
		return false;
	}

	public bool CanMoveUp(IEnumerable<DownloaderItemViewModel> items)
	{
		DownloaderItemViewModel[] source = (items as DownloaderItemViewModel[]) ?? items.ToArray();
		if (source.Any((DownloaderItemViewModel i) => i.IsHistory))
		{
			return false;
		}
		DownloaderItemViewModel downloaderItemViewModel = PrevPriorityItem(source.OrderBy((DownloaderItemViewModel i) => i.Index).FirstOrDefault());
		if (downloaderItemViewModel != null)
		{
			return !downloaderItemViewModel.IsHistory;
		}
		return false;
	}

	public void SetPlayInactiveToAllItems()
	{
		foreach (DownloaderItemViewModel value in Items.ItemsDict.Values)
		{
			value.IsPlayActive = false;
		}
	}

	public void MoveUp(IEnumerable<DownloaderItemViewModel> items)
	{
		DownloaderItemViewModel[] array = (items as DownloaderItemViewModel[]) ?? items.ToArray();
		List<DownloaderItemViewModel> list = array.OrderBy((DownloaderItemViewModel i) => i.Index).ToList();
		if (CanMoveUp(array))
		{
			Task.Run(delegate
			{
				Move(list, -1);
				QuickTimerRefresh();
			});
		}
	}

	public void MoveDown(IEnumerable<DownloaderItemViewModel> items)
	{
		DownloaderItemViewModel[] array = (items as DownloaderItemViewModel[]) ?? items.ToArray();
		List<DownloaderItemViewModel> list = array.OrderByDescending((DownloaderItemViewModel i) => i.Index).ToList();
		if (CanMoveDown(array))
		{
			Task.Run(delegate
			{
				Move(list, 1);
				QuickTimerRefresh();
			});
		}
	}

	private bool Move(List<DownloaderItemViewModel> items, int offset)
	{
		if (!EditQueue("GroupMoveOffset", offset, "", out var error, items.ToArray()))
		{
			error = "Failed to move. " + error;
			Log.Debug(error);
			return false;
		}
		return true;
	}

	private bool EditQueue(string command, int offset, string text, out string error, params DownloaderItemViewModel[] items)
	{
		error = "";
		if (items == null || !items.Any())
		{
			return true;
		}
		List<int> list = items.Select((DownloaderItemViewModel item) => item.ID).ToList();
		JObject jObject = JsonRpcRequest("editqueue", default(TimeSpan), command, offset, text, list);
		string text2 = jObject.ToResultString();
		if (text2 != null && Convert.ToBoolean(text2))
		{
			return true;
		}
		if (jObject != null)
		{
			error = jObject.ToErrorString();
		}
		return false;
	}

	public void MoveTop(DownloaderItemViewModel item)
	{
		if (Items.Count <= 1 || item.IsHistory)
		{
			return;
		}
		Task.Run(delegate
		{
			if (!EditQueue("GroupMoveTop", 0, "", out var error, item))
			{
				error = $"Failed to move to top: {item.Titel}. {error}";
				Log.Warn(error);
			}
		});
	}

	public void UpdateItemsOrder()
	{
		if (!_isUpdateItemsOrderDisabledForMassUpdate)
		{
			this.ItemsOrderChanged?.Invoke();
		}
	}

	public void RemoveItemsAsync(IEnumerable<DownloaderItemViewModel> items)
	{
		Task.Run(delegate
		{
			List<DownloaderItemViewModel> arr = items.ToList();
			try
			{
				Items.RemoveItems(arr, delegate
				{
					if (!RemoveItemsFromNzbGet(arr, out var error))
					{
						string text = "Failed to remove download. Error: " + error;
						AppHelper.Error(text);
						Log.Error(text);
						return false;
					}
					return true;
				});
			}
			finally
			{
				QuickTimerRefresh();
			}
		});
	}

	private bool RemoveItemsFromNzbGet(List<DownloaderItemViewModel> items, out string error)
	{
		error = "";
		List<int> list;
		lock (_lockUpdateItems)
		{
			list = (from i in items
				where !i.IsHistory && !i.IsPostProcess
				select i.ID).ToList();
		}
		if (list.Any())
		{
			JObject obj = JsonRpcRequest("editqueue", default(TimeSpan), "GroupFinalDelete", 0, "", list);
			string text = obj.ToResultString();
			if (text == null || !Convert.ToBoolean(text))
			{
				error = obj.ToErrorString();
			}
		}
		lock (_lockUpdateItems)
		{
			list = (from i in items
				where i.IsPostProcess
				select i.ID).ToList();
		}
		if (list.Any())
		{
			JObject obj2 = JsonRpcRequest("editqueue", default(TimeSpan), "PostDelete", 0, "", list);
			string text2 = obj2.ToResultString();
			if (text2 == null || !Convert.ToBoolean(text2))
			{
				error = obj2.ToErrorString();
			}
		}
		lock (_lockUpdateItems)
		{
			list = (from i in items
				where i.IsHistory
				select i.ID).ToList();
		}
		if (list.Any())
		{
			JObject jObject = JsonRpcRequest("editqueue", default(TimeSpan), "HistoryFinalDelete", 0, "", list);
			string text3 = jObject.ToResultString();
			if (text3 != null && Convert.ToBoolean(text3))
			{
				return true;
			}
			if (jObject != null)
			{
				error = jObject.ToErrorString();
			}
			return false;
		}
		return true;
	}

	public void PauseItemsAsync(IEnumerable<DownloaderItemViewModel> items)
	{
		try
		{
			_isUpdateItemsOrderDisabledForMassUpdate = true;
			DownloaderItemViewModel[] arr = items.ToArray();
			StopUpdateTimer();
			DownloaderItemViewModel[] array = arr;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].DownloadPause();
			}
			Task.Run(delegate
			{
				if (!EditQueue("GroupPause", 0, "", out var error, arr))
				{
					Log.Debug("Failed to pause. " + error);
				}
			}).ContinueWith(delegate
			{
				InitializeUpdateTimer();
			});
		}
		finally
		{
			_isUpdateItemsOrderDisabledForMassUpdate = false;
		}
	}

	public void ResumeItemsAsync(IEnumerable<DownloaderItemViewModel> items)
	{
		try
		{
			_isUpdateItemsOrderDisabledForMassUpdate = true;
			DownloaderItemViewModel[] arr = items.ToArray();
			StopUpdateTimer();
			DownloaderItemViewModel[] array = arr;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].DownloadResume();
			}
			Task.Run(delegate
			{
				if (!EditQueue("GroupResume", 0, "", out var error, arr))
				{
					Log.Debug("Failed to resume. " + error);
				}
			}).ContinueWith(delegate
			{
				InitializeUpdateTimer();
			});
		}
		finally
		{
			_isUpdateItemsOrderDisabledForMassUpdate = false;
		}
	}

	public int GetNewHistoryIndex(int oldIndex)
	{
		if (oldIndex > 20000)
		{
			return oldIndex;
		}
		int result = 50000;
		try
		{
			result = Items.Where((DownloaderItemViewModel i) => i.IsHistory && i.Index != oldIndex).Min((DownloaderItemViewModel i) => i.Index) - 1;
		}
		catch (InvalidOperationException)
		{
		}
		return result;
	}

	public bool UpdateDownloadSpeedLimit(int kbps)
	{
		VirtualNNTP.SetDownloadSpeedLimit(kbps);
		if (kbps < 0)
		{
			kbps = 0;
		}
		JObject jObject = JsonRpcRequest("rate", default(TimeSpan), kbps);
		string text = jObject.ToResultString();
		if (text == null || !Convert.ToBoolean(text))
		{
			if (jObject != null)
			{
				Log.Error(jObject.ToErrorString());
			}
			return false;
		}
		return true;
	}

	public int GetPriority(DownloaderItemViewModel item)
	{
		if (item.IsHistory || item.IsTotals)
		{
			return 0;
		}
		return (from i in Items
			where !i.IsHistory && !i.IsTotals
			orderby i.Index
			select i).ToList().IndexOf(item) + 1;
	}

	private DownloaderItemViewModel NextPriorityItem(DownloaderItemViewModel item)
	{
		return Items.OrderBy((DownloaderItemViewModel i) => i.Index).FirstOrDefault((DownloaderItemViewModel i) => i.Index > item.Index);
	}

	private DownloaderItemViewModel PrevPriorityItem(DownloaderItemViewModel item)
	{
		return Items.OrderBy((DownloaderItemViewModel i) => i.Index).LastOrDefault((DownloaderItemViewModel i) => i.Index < item.Index);
	}

	private void OnPropertyChanged(string propertyName)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public void Dispose()
	{
		ShutdownProcessAsync().Wait();
		DbUpdater.OnDbUpdateStart -= DbUpdaterOnDbUpdateStart;
		DbUpdater.OnDbUpdateEnd -= DbUpdaterOnDbUpdateEnd;
		_totalItems.ProgressChanged -= TotalsOnProgressChanged;
	}
}
