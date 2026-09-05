using System;
using Spotnet.Mvvm;
using NLog;

namespace Spotnet.ViewModel;

public class SpamReportViewModel : ViewModelBase
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private DateTime _date;

	private string _bodyMessageId;

	private string _username = "";

	private string _text = "";

	public DateTime Date
	{
		get
		{
			return _date;
		}
		set
		{
			_date = value;
			RaisePropertyChanged("Date");
		}
	}

	public string BodyMessageId
	{
		get
		{
			return _bodyMessageId;
		}
		set
		{
			_bodyMessageId = value;
			RaisePropertyChanged("BodyMessageId");
		}
	}

	public string Username
	{
		get
		{
			return _username;
		}
		set
		{
			_username = value;
			RaisePropertyChanged("Username");
		}
	}

	public string Text
	{
		get
		{
			return _text;
		}
		set
		{
			_text = value;
			RaisePropertyChanged("Text");
		}
	}
}
