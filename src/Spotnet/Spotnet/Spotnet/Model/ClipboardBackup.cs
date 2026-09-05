using System.Collections.Generic;
using System.Windows;
using Spotnet.Mvvm.Threading;

namespace Spotnet.Model;

public class ClipboardBackup
{
	private readonly Dictionary<string, object> _contents = new Dictionary<string, object>();

	public void Backup()
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			_contents.Clear();
			IDataObject dataObject = Clipboard.GetDataObject();
			string[] formats = dataObject.GetFormats();
			foreach (string text in formats)
			{
				_contents.Add(text, dataObject.GetData(text));
			}
		});
	}

	public void Restore()
	{
		DispatcherHelper.CheckBeginInvokeOnUI(delegate
		{
			DataObject dataObject = new DataObject();
			foreach (string key in _contents.Keys)
			{
				dataObject.SetData(key, _contents[key]);
			}
			Clipboard.SetDataObject(dataObject);
		});
	}
}
