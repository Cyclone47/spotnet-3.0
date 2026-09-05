using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spotnet.Mvvm;
using Spotnet.Mvvm.Threading;
using Spotnet.Extensions;
using Spotnet.Model;

namespace Spotnet.ViewModel;

public class SpamReportsViewModel : ViewModelBase
{
	private string _messageId;

	private int _lockIsLoading;

	private CancellationTokenSource _cancelSource;

	public ObservableCollection<SpamReportViewModel> Reports { get; }

	public string MessageId
	{
		get
		{
			return _messageId;
		}
		set
		{
			if (!(_messageId == value) && value != null)
			{
				_messageId = value;
				Reports.Clear();
			}
		}
	}

	public bool IsReportsLoading => _lockIsLoading == 1;

	public bool IsNoReports
	{
		get
		{
			if (!IsReportsLoading)
			{
				return !Reports.Any();
			}
			return false;
		}
	}

	public SpamReportsViewModel()
	{
		Reports = new ObservableCollection<SpamReportViewModel>();
	}

	public async void StartLoadSpamReports()
	{
		string messageId = MessageId;
		if (messageId.IsNullOrEmpty() || Interlocked.CompareExchange(ref _lockIsLoading, 1, 0) != 0)
		{
			return;
		}
		RaisePropertyChanged("IsReportsLoading");
		RaisePropertyChanged("IsNoReports");
		_cancelSource = new CancellationTokenSource();
		try
		{
			await Task.Run(delegate
			{
				LoadSpamReports(messageId);
			});
		}
		finally
		{
			Interlocked.Exchange(ref _lockIsLoading, 0);
			RaisePropertyChanged("IsReportsLoading");
			RaisePropertyChanged("IsNoReports");
		}
	}

	private void LoadSpamReports(string messageId)
	{
		foreach (SpamReport report in SpamReports.GetSpamReports(messageId, _cancelSource.Token))
		{
			if (_cancelSource.Token.IsCancellationRequested)
			{
				break;
			}
			if (report.BodyMessageId.IsNullOrWhiteSpace() || !Reports.All((SpamReportViewModel r) => r.BodyMessageId != report.BodyMessageId))
			{
				continue;
			}
			report.GetBody();
			if (_cancelSource.Token.IsCancellationRequested)
			{
				break;
			}
			DateTime date = report.Date;
			string bodyMessageId = report.BodyMessageId;
			string text = report.TextFormatedForOutput;
			string username = report.Username;
			if (!text.IsNullOrEmpty() && !username.IsNullOrEmpty())
			{
				DispatcherHelper.UIDispatcher.Invoke(delegate
				{
					Reports.Add(new SpamReportViewModel
					{
						Date = date,
						BodyMessageId = bodyMessageId,
						Text = text,
						Username = username
					});
				});
			}
		}
	}

	public void StopLoadSpamReports()
	{
		if (IsReportsLoading)
		{
			_cancelSource.Cancel();
		}
	}
}
