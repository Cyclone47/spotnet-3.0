using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Model;

internal static class BlackAndWhite
{
	private struct UserModulusPair
	{
		internal string Modulus;

		internal string User;
	}

	private const string QuerySelectPart = "SELECT sender,s.modulus,COUNT(s.msgid) AS number_of_spots,SUM(sr.cnt) AS number_of_complains,SUM(sr.one) AS number_of_spots_complained FROM spots s LEFT JOIN (SELECT msgid,cnt,1 as one FROM spamgroup) sr ON (sr.msgid=s.msgid) ";

	private const string QueryGroupByModulusPart = "GROUP BY s.modulus ";

	private static readonly Logger Log;

	private static HashSet<string> _whiteList;

	private static HashSet<string> _blackList;

	private static HashSet<string> _spotWhiteList;

	private static HashSet<string> _spotBlackList;

	private static readonly HashSet<UserModulusPair> WhiteFakesList;

	private static readonly Timer UpdateListsFromTheNetTimer;

	private static readonly object LockWhitelist;

	private static readonly object LockBlacklist;

	private static readonly object LockSpotWhitelist;

	private static readonly object LockSpotBlacklist;

	public static event Action OnTrustedListUploaded;

	static BlackAndWhite()
	{
		Log = LogManager.GetCurrentClassLogger();
		WhiteFakesList = new HashSet<UserModulusPair>();
		LockWhitelist = new object();
		LockBlacklist = new object();
		LockSpotWhitelist = new object();
		LockSpotBlacklist = new object();
		int num = Convert.ToInt32(Settings.Default.ExternalListsUpdateInterval);
		if (num > 0)
		{
			UpdateListsFromTheNetTimer = new Timer(delegate
			{
				UpdateExternalListsAsync();
			}, null, TimeSpan.Zero, TimeSpan.FromMinutes(num));
		}
	}

	internal static void UpdateExternalListsAsync()
	{
		UpdateWhiteFromTheNetAsync();
		UpdateBlackFromTheNetAsync();
		UpdateSpotWhiteFromTheNetAsync();
		UpdateSpotBlackFromTheNetAsync();
	}

	internal static HashSet<string> BlackList()
	{
		lock (LockBlacklist)
		{
			if (_blackList != null)
			{
				return _blackList;
			}
			string file = System.IO.Path.Combine(AppHelper.SettingsFolder, "blacklist.xml");
			_blackList = new HashSet<string>();
			LoadToList(file, _blackList);
			LoadServerBlack();
			return _blackList;
		}
	}

	internal static HashSet<string> WhiteList()
	{
		if (_whiteList != null)
		{
			return _whiteList;
		}
		lock (LockWhitelist)
		{
			string text = System.IO.Path.Combine(AppHelper.SettingsFolder, "whitelist.xml");
			if (!System.IO.File.Exists(text))
			{
				CreateList(text, new List<ListItem>
				{
					new ListItem("1wt6jlePL/IADm4wL8lMqHaGVznPTiUvcovAtj3eCgvt3wTyM9Fd8ptx8+xzmAHL", "Albertina"),
					new ListItem("ynakBYOJnwLBuXQZvglD1N/uZ0mZqYad9dKX9KxyOe2mPoEZIE8Y/x93U8VL4tnv", "Bacoben1"),
					new ListItem("6ibY+eDYDwXOjV992fdCqhE0V0B2rRwqvxmoodPlpgjSshPCUgVjTHqpoC1AzbqR", "Boaz"),
					new ListItem("vaaHp9taPnRVbYZaa5etSK6y4Caft5aOrnzqfjPljgD2UE/89TBz6JbA/NeJpK+p", "BOB1961"),
					new ListItem("s7xw10e0wq6dZgrkD59T9F/lj0zSaht0Zv0gYVvS2gR7I4VPjo/TrqxhwSP3by//", "Biky"),
					new ListItem("zNOkGYubV87uJaL1KIqqHHs+nKWNwhD0yNEu0Mz4TKBVkDkxdTB8RvcAa79tMyaL", "Blowan"),
					new ListItem("0pGKk73HQkkj1waqHSjuMtpqAuAhItXNYXOQXHQL+rqORxzqMMoQeg523iJKUbvf", "Bradje"),
					new ListItem("snmypn4sZq+N4tn+UT6IFPn9Ii67iteD/T/weYVVQbQWvui4M1SSUxaqvIFQtQ8l", "CaptainSalvo"),
					new ListItem("58UoKbJ7JgNbRFJJqpdwO3MYKexHlkkUt6KfZvP7lykUNHRm/sZssM4o2jUm6TUh", "CaptainSalvo"),
					new ListItem("rCpFxtuo9ijYWTg4WpDnQQO2dVQGSlhGamuUmWCrpilfEbWKNLap+EFnNEqCHdbF", "Dick42"),
					new ListItem("z+U5teGdCtU0MVePPPZu1APEJfpSAPNh/RR1EyBXRD1G8d73M+qJZJqfJUL9smUF", "Falang01"),
					new ListItem("ru3rhWGBsx4dCglEwjE3bL9nVfH2gJVS0kb0OrXQTceeMXLDVb4rsuA+ty85M3If", "Hagenees1978"),
					new ListItem("xC2V+4i7J07fm6+ND+Mr5hvD359l2R/bkeOt2cGUpeFznxhItdMEVJKDNthKFNIb", "Hagenees1978"),
					new ListItem("ySv0wJaY8WQPb1KUkJeOVr4dGqR2UoxaOxsnqYmcgkbiPhigkb235eVvoIj4AVM7", "Hannes3"),
					new ListItem("z/4mkqzLE27ur8iNOTerBbFK37//itkNa5APDIRLTQ3gBJZORgOcqT+51lw2qnQx", "HendrikjeStoffel"),
					new ListItem("twJLKIJYDQvTGhk3hnLSWdgE9oXkH/RypTAI7Bo2rBHkH5FfL/FOJEvOp/MVRWFP", "Inge2222"),
					new ListItem("reZxfDPBE/Bxqa63PW4LFiDTh6xl7w1Sh3eoYmUbYbiI8AbmtWNmWAWjC6mHef+b", "Kaj7"),
					new ListItem("szsAIT5lVEzonnwg81DoU/44KTXkdIYrAdAFpoB/99Fw0VC6QVad7PRKgDPFeDW5", "kww"),
					new ListItem("qIxm7gFn8z6eIheHbstSa0vEhciwEMzNMjYlvBXJEBmivtcfrTXXz57VMfIDtKZB", "kww"),
					new ListItem("4ci3BuoC+JHlHVTxYacoEmk7rXGnrRlmgp1zuNO/wrtX0M0ixhK1MUlMMZIaVJ39", "Oldtimer"),
					new ListItem("rla55FY/Gm1DgPFwo4+HgMq8bElbjW9W8dIBUFun3ujfujp89p07LAQkS32FWQNb", "Ricardoo"),
					new ListItem("qp1ja8wjPDlh7aEssytHTflMCeKLF1TDoZlA41Qp9rkifx+qz9oY21FqZxgOiQgN", "Ricardoo"),
					new ListItem("+QIm6ZjUIY8Jgn0venbvGoik2hZyPZpNlXJrGlCbQgRndiN4apVb9awMsp2YGY5j", "SubmarinesSpot"),
					new ListItem("p2T1o0E6djKXSBqv8sRPLVsKxZnZOzuQgzY25QBdF5l5+5El81ziGD+5RBuUXSkT", "SubmarinesSpot"),
					new ListItem("0mqEBWp/z9l8W15lwntuXNxpcY04o96/MxGe4OCg6dFCjzQ6g8kSej/QoL9tkhB/", "Sophia1949"),
					new ListItem("9lfEiCusAUMTMqCOi6sc6P2IYoDslFbGEIYZ16ku6Nqrclc5oyLE7wz2fUI0RJvx", "Trein1600"),
					new ListItem("yf0oZC/mJLo0iHunzKn1YyvPCyI6r/ACTNAG3K53BzF4efYWe37EC9P4nRmHEDPJ", "xxxwebwatchers"),
					new ListItem("zxCiZ9F9yZ7DdEPj+1Ta/nQl679amRgc+BcmFuRpWvt9VjnHzY7dUTMPUavB8jUN", "Y0os"),
					new ListItem("u9bdM+NQl4OPvhi4GHiRvyvDRuTVBemAeAh70lpIWGRqiv03hDvI7W53FuQk3rDX", "Zoutoplossing"),
					new ListItem("uH19iDBeTjye6rhOi4uLR+T59MThUBQNL0ZgQhsX6BQqQxZNYflwccud9ZN64Rb5", "Zoutoplossing")
				});
			}
			_whiteList = new HashSet<string>();
			LoadToList(text, _whiteList);
			LoadServerWhite();
			return _whiteList;
		}
	}

	internal static HashSet<string> SpotBlackList()
	{
		if (_spotBlackList == null)
		{
			_spotBlackList = new HashSet<string>();
			LoadServerSpotBlack();
		}
		return _spotBlackList;
	}

	internal static HashSet<string> SpotWhiteList()
	{
		if (_spotWhiteList == null)
		{
			_spotWhiteList = new HashSet<string>();
			LoadServerSpotWhite();
		}
		return _spotWhiteList;
	}

	internal static bool RemoveBlack(string sKey)
	{
		if (_blackList.Remove(sKey))
		{
			AddToList("", sKey, "blacklist.srv.removed.xml");
		}
		return RemoveFromList(sKey, "blacklist.xml");
	}

	internal static bool RemoveWhite(string sKey)
	{
		_whiteList.Remove(sKey);
		return RemoveFromList(sKey, "whitelist.xml");
	}

	internal static void LoadServerBlack()
	{
		string file = System.IO.Path.Combine(AppHelper.SettingsFolder, "blacklist.srv.csv");
		string fileToExclude = System.IO.Path.Combine(AppHelper.SettingsFolder, "blacklist.srv.removed.xml");
		lock (LockBlacklist)
		{
			if (_blackList == null)
			{
				_blackList = new HashSet<string>();
			}
			LoadToList(file, _blackList, fileToExclude);
		}
	}

	internal static void LoadServerSpotBlack()
	{
		string file = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_blacklist.srv.csv");
		string fileToExclude = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_blacklist.srv.removed.xml");
		lock (LockSpotBlacklist)
		{
			if (_spotBlackList == null)
			{
				_spotBlackList = new HashSet<string>();
			}
			LoadToList(file, _spotBlackList, fileToExclude);
		}
	}

	internal static void LoadServerWhite()
	{
		string file = System.IO.Path.Combine(AppHelper.SettingsFolder, "whitelist.srv.csv");
		lock (LockWhitelist)
		{
			LoadToFakeUsernamesList(file, WhiteFakesList);
		}
	}

	internal static void LoadServerSpotWhite()
	{
		string file = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_whitelist.srv.csv");
		string fileToExclude = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_whitelist.srv.removed.xml");
		lock (LockSpotWhitelist)
		{
			if (_spotWhiteList == null)
			{
				_spotWhiteList = new HashSet<string>();
			}
			LoadToList(file, _spotWhiteList, fileToExclude);
		}
	}

	private static void UpdateWhiteFromTheNetAsync()
	{
		if (!Settings.Default.DownloadExternalLists)
		{
			return;
		}
		string sFile = System.IO.Path.Combine(AppHelper.SettingsFolder, "whitelist.srv.csv");
		string url = Settings.Default.WhitelistURL;
		if (url.IsNullOrWhiteSpace())
		{
			url = Configuration.RemoteWhitelistUrl;
		}
		Task.Run(delegate
		{
			if (AppHelper.UpdateKeysFileFromTheNet(AppHelper.AddHttp(url), sFile + ".new"))
			{
				lock (LockWhitelist)
				{
					MoveFileWithOverride(sFile + ".new", sFile);
					LoadToFakeUsernamesList(sFile, WhiteFakesList);
				}
				BlackAndWhite.OnTrustedListUploaded?.Invoke();
			}
		});
	}

	private static void MoveFileWithOverride(string file, string fileNew)
	{
		try
		{
			if (System.IO.File.Exists(fileNew))
			{
				System.IO.File.Delete(fileNew);
			}
			System.IO.File.Move(file, fileNew);
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
	}

	private static void UpdateBlackFromTheNetAsync()
	{
		if (!Settings.Default.DownloadExternalLists)
		{
			return;
		}
		string listFullPath = System.IO.Path.Combine(AppHelper.SettingsFolder, "blacklist.srv.csv");
		string listForRemovedFullPath = System.IO.Path.Combine(AppHelper.SettingsFolder, "blacklist.srv.removed.xml");
		string url = Settings.Default.BlacklistURL;
		if (url.IsNullOrWhiteSpace())
		{
			url = Configuration.RemoteBlacklistUrl;
		}
		Task.Run(delegate
		{
			BlackList();
			if (AppHelper.UpdateKeysFileFromTheNet(AppHelper.AddHttp(url), listFullPath + ".new"))
			{
				lock (LockBlacklist)
				{
					MoveFileWithOverride(listFullPath + ".new", listFullPath);
					LoadToList(listFullPath, _blackList, listForRemovedFullPath);
				}
			}
		});
	}

	private static void UpdateSpotWhiteFromTheNetAsync()
	{
		if (!Settings.Default.DownloadExternalLists)
		{
			return;
		}
		string listFullPath = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_whitelist.srv.csv");
		string listForRemovedFullPath = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_whitelist.srv.removed.xml");
		string url = Settings.Default.SpotWhitelistURL;
		if (url.IsNullOrWhiteSpace())
		{
			url = Configuration.RemoteSpotWhitelistUrl;
		}
		Task.Run(delegate
		{
			LoadServerSpotWhite();
			if (AppHelper.UpdateKeysFileFromTheNet(AppHelper.AddHttp(url), listFullPath + ".new"))
			{
				lock (LockSpotWhitelist)
				{
					MoveFileWithOverride(listFullPath + ".new", listFullPath);
					LoadToList(listFullPath, _spotWhiteList, listForRemovedFullPath);
				}
			}
		});
	}

	private static void UpdateSpotBlackFromTheNetAsync()
	{
		if (!Settings.Default.DownloadExternalLists)
		{
			return;
		}
		string listFullPath = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_blacklist.srv.csv");
		string listForRemovedFullPath = System.IO.Path.Combine(AppHelper.SettingsFolder, "spot_blacklist.srv.removed.xml");
		string url = Settings.Default.SpotBlacklistURL;
		if (url.IsNullOrWhiteSpace())
		{
			url = Configuration.RemoteSpotBlacklistUrl;
		}
		Task.Run(delegate
		{
			LoadServerSpotBlack();
			if (AppHelper.UpdateKeysFileFromTheNet(AppHelper.AddHttp(url), listFullPath + ".new"))
			{
				lock (LockSpotBlacklist)
				{
					MoveFileWithOverride(listFullPath + ".new", listFullPath);
					LoadToList(listFullPath, _spotBlackList, listForRemovedFullPath);
				}
			}
		});
	}

	private static bool LoadToList(string file, HashSet<string> list, string fileToExclude = null)
	{
		try
		{
			if (!System.IO.File.Exists(file))
			{
				CreateList(file, new List<ListItem>());
				return true;
			}
			HashSet<string> hashSet = new HashSet<string>();
			if (fileToExclude != null)
			{
				LoadToList(fileToExclude, hashSet);
			}
			if (System.IO.Path.GetExtension(file).EqualsIgnoreCase(".xml"))
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.XmlResolver = null;
				xmlDocument.Load(file);
				XmlElement documentElement = xmlDocument.DocumentElement;
				if (!documentElement.Name.EqualsIgnoreCase("keys"))
				{
					throw new Exception("XML Error");
				}
				foreach (XmlElement item3 in documentElement)
				{
					if (fileToExclude == null || !hashSet.Contains(item3.InnerText))
					{
						list.Add(item3.InnerText);
					}
				}
			}
			else
			{
				string[] array = System.IO.File.ReadAllLines(file);
				for (int i = 0; i < array.Length; i++)
				{
					string[] array2 = array[i].Split(',');
					if (array2.Length == 2)
					{
						string item = array2[1].Trim();
						if (fileToExclude == null || !hashSet.Contains(item))
						{
							list.Add(item);
						}
					}
					else if (array2.Length == 1)
					{
						string item2 = array2[0].Trim();
						if (fileToExclude == null || !hashSet.Contains(item2))
						{
							list.Add(item2);
						}
					}
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			CreateList(file, new List<ListItem>());
			return true;
		}
	}

	private static bool LoadToFakeUsernamesList(string file, ICollection<UserModulusPair> set)
	{
		try
		{
			if (!System.IO.File.Exists(file))
			{
				CreateList(file, new List<ListItem>());
				return true;
			}
			if (System.IO.Path.GetExtension(file).EqualsIgnoreCase(".xml"))
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.XmlResolver = null;
				xmlDocument.Load(file);
				XmlElement documentElement = xmlDocument.DocumentElement;
				if (!documentElement.Name.EqualsIgnoreCase("keys"))
				{
					throw new Exception("XML Error");
				}
				UserModulusPair item = default(UserModulusPair);
				foreach (XmlElement item3 in documentElement)
				{
					item.User = item3.GetAttribute("Name");
					item.Modulus = item3.InnerText;
					set.Add(item);
				}
			}
			else
			{
				string[] array = System.IO.File.ReadAllLines(file);
				UserModulusPair item2 = default(UserModulusPair);
				for (int i = 0; i < array.Length; i++)
				{
					string[] array2 = array[i].Split(',');
					if (array2.Length == 2)
					{
						item2.User = array2[0].Trim();
						item2.Modulus = array2[1].Trim();
						set.Add(item2);
					}
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			CreateList(file, new List<ListItem>());
			return true;
		}
	}

	private static bool CreateList(string file, List<ListItem> list)
	{
		try
		{
			StreamWriter streamWriter = new StreamWriter(file, append: false, Encoding.UTF8);
			bool flag = System.IO.Path.GetExtension(file).EqualsIgnoreCase(".xml");
			if (flag)
			{
				streamWriter.WriteLine("<Keys>");
			}
			foreach (ListItem item in list)
			{
				if (!item.Key.IsNullOrWhiteSpace())
				{
					string text = item.Key.Replace("<", "").Trim();
					string text2 = item.Name.Replace("\"", "").Trim();
					string value = ((!flag) ? (text + "," + text2) : (text2.IsNullOrWhiteSpace() ? ("\t<Key>" + text + "</Key>") : ("\t<Key Name=\"" + text2 + "\">" + text + "</Key>")));
					streamWriter.WriteLine(value);
				}
			}
			if (flag)
			{
				streamWriter.WriteLine("</Keys>");
			}
			streamWriter.Close();
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
		return false;
	}

	private static bool RemoveFromList(string key, string file)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument();
			bool flag = false;
			string text = System.IO.Path.Combine(AppHelper.SettingsFolder, file);
			if (!System.IO.File.Exists(text))
			{
				return false;
			}
			xmlDocument.XmlResolver = null;
			xmlDocument.Load(text);
			XmlElement documentElement = xmlDocument.DocumentElement;
			if (!documentElement.Name.EqualsIgnoreCase("keys"))
			{
				throw new Exception("XML Error");
			}
			foreach (XmlElement item in documentElement)
			{
				if (item.InnerText.Trim().EqualsIgnoreCase(key.Trim()))
				{
					flag = true;
					documentElement.RemoveChild(item);
				}
			}
			if (!flag)
			{
				return false;
			}
			if (System.IO.File.Exists(text))
			{
				System.IO.File.SetAttributes(text, FileAttributes.Normal);
			}
			xmlDocument.Save(text);
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
		return false;
	}

	internal static bool AddBlack(string name, string key)
	{
		_blackList.Add(key);
		bool num = AddToList(name, key, "blacklist.xml");
		if (num)
		{
			RemoveFromList(key, "blacklist.srv.removed.xml");
		}
		return num;
	}

	internal static bool AddWhite(string name, string key)
	{
		_whiteList.Add(key);
		return AddToList(name, key, "whitelist.xml");
	}

	internal static void GenerateAutoBlacklist()
	{
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		string sQuery = "SELECT sender,s.modulus,COUNT(s.msgid) AS number_of_spots,SUM(sr.cnt) AS number_of_complains,SUM(sr.one) AS number_of_spots_complained FROM spots s LEFT JOIN (SELECT msgid,cnt,1 as one FROM spamgroup) sr ON (sr.msgid=s.msgid) GROUP BY s.modulus  HAVING (number_of_spots > 5   AND number_of_spots <= 20  AND number_of_spots_complained*1.0 / number_of_spots > 0.8) OR (number_of_spots > 20  AND number_of_spots <= 50  AND number_of_spots_complained*1.0 / number_of_spots > 0.6) OR (number_of_spots > 50  AND number_of_spots <= 100 AND number_of_spots_complained*1.0 / number_of_spots > 0.5) OR (number_of_spots > 100 AND number_of_spots <= 200 AND number_of_spots_complained*1.0 / number_of_spots > 0.3) OR (number_of_spots > 200 AND number_of_spots_complained*1.0 / number_of_spots > 0.2)";
		string file = System.IO.Path.Combine(AppHelper.SettingsFolder, "blacklist.auto.xml");
		CreateList(file, new List<ListItem>());
		using (ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true))
		{
			using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
			using DbDataReader dbDataReader = sqlDb.ExecuteReader(sQuery, transaction);
			if (dbDataReader == null)
			{
				throw new Exception("Error during query executing.");
			}
			while (dbDataReader.Read())
			{
				string name = RuntimeHelpers.GetObjectValue(dbDataReader[0]) as string;
				string key = RuntimeHelpers.GetObjectValue(dbDataReader[1]) as string;
				AddToList(name, key, file);
			}
		}
		Log.Debug("auto blacklist generated in ms: " + stopwatch.ElapsedMilliseconds);
	}

	internal static void GenerateAutoWhitelist()
	{
		Stopwatch stopwatch = new Stopwatch();
		stopwatch.Start();
		string text = string.Format("{0} WHERE s.date < (datetime('now', '-30 days')) AND s.modulus IS NOT NULL AND s.modulus!='none' {1}, sender HAVING (number_of_spots > 100 AND number_of_spots_complained*1.0 / number_of_spots < 0.05) OR (number_of_spots > 50 AND number_of_spots <= 100 AND number_of_spots_complained*1.0 / number_of_spots < 0.10) OR (number_of_spots > 10 AND number_of_spots <= 50  AND number_of_spots_complained*1.0 / number_of_spots < 0.15) OR (number_of_spots > 5 AND number_of_spots <= 10 AND number_of_spots_complained*1.0 / number_of_spots < 0.30) OR (number_of_spots <= 5 AND number_of_spots_complained*1.0 / number_of_spots < 0.50)", "SELECT sender,s.modulus,COUNT(s.msgid) AS number_of_spots,SUM(sr.cnt) AS number_of_complains,SUM(sr.one) AS number_of_spots_complained FROM spots s LEFT JOIN (SELECT msgid,cnt,1 as one FROM spamgroup) sr ON (sr.msgid=s.msgid) ", "GROUP BY s.modulus ");
		Log.Debug("Whitelist query: " + text);
		using (ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true))
		{
			using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
			using DbDataReader dbDataReader = sqlDb.ExecuteReader(text, transaction);
			if (dbDataReader == null)
			{
				throw new Exception("Error during query executing.");
			}
			List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
			int num = 1;
			while (dbDataReader.Read())
			{
				string key = RuntimeHelpers.GetObjectValue(dbDataReader[0]) as string;
				string value = RuntimeHelpers.GetObjectValue(dbDataReader[1]) as string;
				list.Add(new KeyValuePair<string, string>(key, value));
				if (list.Count >= 100)
				{
					System.IO.File.WriteAllLines(System.IO.Path.Combine(AppHelper.SettingsFolder, "whitelist." + num++ + ".csv"), list.Select((KeyValuePair<string, string> p) => p.Key + ";" + p.Value));
					list = new List<KeyValuePair<string, string>>();
				}
			}
			if (list.Count > 0)
			{
				System.IO.File.WriteAllLines(System.IO.Path.Combine(AppHelper.SettingsFolder, "whitelist." + num + ".csv"), list.Select((KeyValuePair<string, string> p) => p.Key + ";" + p.Value));
			}
		}
		Log.Debug("auto whitelist generated in ms: " + stopwatch.ElapsedMilliseconds);
	}

	private static bool AddToList(string name, string key, string file, bool allowMultipleModuluses = false)
	{
		try
		{
			XmlDocument xmlDocument = new XmlDocument
			{
				XmlResolver = null
			};
			string text = System.IO.Path.Combine(AppHelper.SettingsFolder, file);
			if (!System.IO.File.Exists(text))
			{
				CreateList(text, new List<ListItem>());
			}
			xmlDocument.Load(text);
			XmlElement documentElement = xmlDocument.DocumentElement;
			if (!documentElement.Name.EqualsIgnoreCase("keys"))
			{
				throw new Exception("XML Error");
			}
			if (!allowMultipleModuluses)
			{
				foreach (XmlElement item in documentElement)
				{
					if (item.InnerText.Trim().EqualsIgnoreCase(key.Trim()))
					{
						return true;
					}
				}
			}
			XmlNode xmlNode = xmlDocument.CreateElement("Key");
			XmlAttribute xmlAttribute = xmlDocument.CreateAttribute("Name");
			xmlAttribute.Value = name;
			xmlNode.Attributes.Append(xmlAttribute);
			xmlNode.InnerText = key;
			documentElement.AppendChild(xmlNode);
			System.IO.File.SetAttributes(text, FileAttributes.Normal);
			xmlDocument.Save(text);
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
		}
		return false;
	}

	public static bool IsModulusInServerWhitelist(string modulus)
	{
		try
		{
			return WhiteFakesList.Any((UserModulusPair pair) => pair.Modulus.Equals(modulus));
		}
		catch
		{
			return false;
		}
	}

	public static bool IsUsernameInServerWhitelist(string poster)
	{
		try
		{
			return WhiteFakesList.Any((UserModulusPair pair) => pair.User.EqualsIgnoreCase(poster));
		}
		catch
		{
			return false;
		}
	}

	public static int NumberOfUsersTrusted()
	{
		try
		{
			return WhiteFakesList.Count;
		}
		catch
		{
			return 0;
		}
	}
}
