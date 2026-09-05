using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Xml.Serialization;
using Spotnet.Mvvm.Threading;
using NLog;
using Spotnet.Model;
using Spotnet.Properties;

namespace Spotnet.Helpers;

public static class PromotionHelper
{
	[XmlRoot("Tabs")]
	public class PromotionTabs
	{
		[XmlElement("Tab")]
		public PromotionTabInfo[] Tabs { get; set; }
	}

	[Serializable]
	public class PromotionTabInfo
	{
		[XmlElement("Title")]
		public string Title;

		[XmlElement("Url")]
		public string Url;

		[XmlElement("IsTabClosable")]
		public bool IsTabClosable;

		[XmlElement("IsActivatedOnOpen")]
		public bool IsActivatedOnOpen;
	}

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public static void OpenTabsAsync()
	{
		if (DateTime.Now.Date.Equals(Settings.Default.PromoLastDate.Date))
		{
			return;
		}
		Task.Run(delegate
		{
			string result = GetConfigAsync(AppHelper.GetServer(ServerType.Headers).Server).Result;
			if (result == null)
			{
				result = GetConfigAsync("default").Result;
				if (result == null)
				{
					return;
				}
			}
			PromotionTabs tabsList = ParseConfig(result);
			try
			{
				File.Delete(result);
			}
			catch
			{
			}
			DispatcherHelper.CheckBeginInvokeOnUI(delegate
			{
				try
				{
					Settings.Default.PromoLastDate = DateTime.Now;
					Settings.Default.Save();
					PromotionTabInfo[] tabs = tabsList.Tabs;
					for (int i = 0; i < tabs.Length; i++)
					{
						OpenTab(tabs[i]);
					}
					PromotionTabInfo promotionTabInfo = tabsList.Tabs.FirstOrDefault((PromotionTabInfo t) => t.IsActivatedOnOpen);
					if (promotionTabInfo != null)
					{
						TabItem promoTab = Sys.MainWindow.GetPromoTab(promotionTabInfo.Url);
						if (promoTab != null)
						{
							Sys.MainWindow.TabControl1.SelectedItem = promoTab;
						}
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			});
		});
	}

	private static void OpenTab(PromotionTabInfo tab)
	{
		Sys.MainWindow.OpenPromo(tab);
	}

	private static string GenerateTestConfig()
	{
		PromotionTabInfo promotionTabInfo = new PromotionTabInfo
		{
			Title = "Example Promotion",
			Url = "http://www.google.com/",
			IsActivatedOnOpen = false,
			IsTabClosable = true
		};
		PromotionTabInfo promotionTabInfo2 = new PromotionTabInfo
		{
			Title = "Example Promotion that cannot be closed",
			Url = "https://www.5eurousenet.com/en",
			IsActivatedOnOpen = false,
			IsTabClosable = false
		};
		PromotionTabs promotionTabs = new PromotionTabs();
		promotionTabs.Tabs = new PromotionTabInfo[2] { promotionTabInfo, promotionTabInfo2 };
		PromotionTabs o = promotionTabs;
		string tempFileName = AppHelper.GetTempFileName();
		XmlSerializer xmlSerializer = new XmlSerializer(typeof(PromotionTabs));
		using StreamWriter textWriter = new StreamWriter(tempFileName);
		xmlSerializer.Serialize(textWriter, o);
		return tempFileName;
	}

	private static PromotionTabs ParseConfig(string file)
	{
		XmlSerializer xmlSerializer = new XmlSerializer(typeof(PromotionTabs));
		using StreamReader textReader = new StreamReader(file);
		return (PromotionTabs)xmlSerializer.Deserialize(textReader);
	}

	private static async Task<string> GetConfigAsync(string providerDomainName)
	{
		return await Task.Run(delegate
		{
			string tempFileName = AppHelper.GetTempFileName();
			string text = Configuration.RemotePromoFolder + providerDomainName + ".xml";
			string content = "";
			return (!AppHelper.UpdateFileFromTheNet(AppHelper.AddHttp(text), tempFileName, ref content, showError: false)) ? null : tempFileName;
		});
	}
}
