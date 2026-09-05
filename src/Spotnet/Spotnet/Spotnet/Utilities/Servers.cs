using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Microsoft.VisualBasic;
using NLog;
using Spotnet.Downloader;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Model;

namespace Spotnet.Utilities;

public class Servers
{
	internal const string ServersFilename = "servers.xml";

	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	internal ServerInfo ODown;

	internal ServerInfo OHeader;

	internal ServerInfo OUp;

	internal ServerInfo OMasterCache;

	internal ServerInfo OSlaveCache;

	public bool LoadServers()
	{
		OUp = null;
		ODown = null;
		OHeader = null;
		OMasterCache = null;
		OSlaveCache = null;
		if (!System.IO.File.Exists(System.IO.Path.Combine(AppHelper.SettingsFolder, "servers.xml")))
		{
			OUp = new ServerInfo();
			ODown = new ServerInfo();
			OHeader = new ServerInfo();
			OMasterCache = new ServerInfo();
			OSlaveCache = new ServerInfo();
			return SaveServers();
		}
		EncPass encPass = new EncPass();
		XmlDocument xmlDocument = new XmlDocument();
		try
		{
			xmlDocument.XmlResolver = null;
			xmlDocument.Load(System.IO.Path.Combine(AppHelper.SettingsFolder, "servers.xml"));
			XmlElement documentElement = xmlDocument.DocumentElement;
			if (documentElement == null)
			{
				return false;
			}
			foreach (XmlElement item in documentElement)
			{
				ServerInfo serverInfo = checked(new ServerInfo
				{
					SSL = item.GetAttribute("SSL").Trim().Equals("1"),
					Port = (int)Math.Round(Conversion.Val(item.GetAttribute("Port"))),
					Server = item.GetAttribute("Server"),
					Password = (Strings.Right(item.GetAttribute("Password"), 1).Equals("=") ? encPass.Decrypt(item.GetAttribute("Password")) : item.GetAttribute("Password")),
					Username = item.GetAttribute("Username"),
					Connections = (int)Math.Round(Conversion.Val(item.GetAttribute("Connections")))
				});
				string text = item.GetAttribute("Type").Trim().ToUpper();
				if (text.Equals("UPLOAD") || text.Equals("UPLOADS"))
				{
					OUp = serverInfo;
				}
				else if (text.Equals("DOWNLOAD") || text.Equals("DOWNLOADS"))
				{
					ODown = serverInfo;
					InitCacheServers();
				}
				else if (text.Equals("HEADER") || text.Equals("HEADERS"))
				{
					OHeader = serverInfo;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			return false;
		}
	}

	public void InitCacheServers()
	{
		OMasterCache = ODown.Clone() as ServerInfo;
		OSlaveCache = ODown.Clone() as ServerInfo;
		if (OMasterCache != null)
		{
			OMasterCache.Connections = 2;
			OMasterCache.Server = (AppHelper.IsSnelNlProvider ? CachingSystem.MasterHostnameSnelNl : (AppHelper.Is5EuroProvider ? CachingSystem.MasterHostname5Euro : ""));
			OMasterCache.SSL = !string.IsNullOrWhiteSpace(OMasterCache.Server) && OMasterCache.DoesProviderUseSsl();
		}
	}

	public bool SaveServers()
	{
		string sError = "";
		return SaveServers(ref sError);
	}

	public bool SaveServers(ref string sError)
	{
		XmlDocument xmlDocument = new XmlDocument();
		EncPass encPass = new EncPass();
		xmlDocument.XmlResolver = null;
		try
		{
			XmlElement xmlElement = xmlDocument.CreateElement("Spotnet");
			List<ServerInfo> obj = new List<ServerInfo> { OHeader, ODown, OUp };
			int num = 0;
			foreach (ServerInfo item in obj)
			{
				XmlElement xmlElement2 = xmlDocument.CreateElement("Server");
				num++;
				switch (num)
				{
				case 1:
					xmlElement2.SetAttribute("Type", "Headers");
					break;
				case 2:
					xmlElement2.SetAttribute("Type", "Downloads");
					break;
				case 3:
					xmlElement2.SetAttribute("Type", "Uploads");
					break;
				}
				xmlElement2.SetAttribute("Server", item.Server);
				xmlElement2.SetAttribute("Username", item.Username);
				xmlElement2.SetAttribute("Password", (!item.Password.IsNullOrEmpty()) ? encPass.Encrypt(item.Password) : "");
				xmlElement2.SetAttribute("Port", item.Port.ToStringSafely());
				xmlElement2.SetAttribute("SSL", item.SSL ? "1" : "0");
				xmlElement2.SetAttribute("Connections", item.Connections.ToStringSafely());
				xmlElement.AppendChild(xmlElement2);
			}
			xmlDocument.AppendChild(xmlElement);
			if (System.IO.File.Exists(System.IO.Path.Combine(AppHelper.SettingsFolder, "servers.xml")))
			{
				System.IO.File.SetAttributes(System.IO.Path.Combine(AppHelper.SettingsFolder, "servers.xml"), FileAttributes.Normal);
			}
			xmlDocument.Save(System.IO.Path.Combine(AppHelper.SettingsFolder, "servers.xml"));
			return true;
		}
		catch (Exception ex)
		{
			Log.Exception(ex, showToClient: true);
			sError = "SaveServers: " + ex.Message;
			return false;
		}
	}
}
