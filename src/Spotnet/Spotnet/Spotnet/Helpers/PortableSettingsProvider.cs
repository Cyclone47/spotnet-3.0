using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Xml;
using NLog;
using System.IO;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

internal class PortableSettingsProvider : SettingsProvider
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private XmlDocument _settingsXml;

	private XmlDocument SettingsXml
	{
		get
		{
			if (_settingsXml == null)
			{
				_settingsXml = new XmlDocument();
				_settingsXml.XmlResolver = null;
				try
				{
					_settingsXml.Load(Path.Combine(GetAppSettingsPath(), GetAppSettingsFilename()));
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
					_settingsXml.AppendChild(_settingsXml.CreateXmlDeclaration("1.0", "utf-8", string.Empty));
					_settingsXml.AppendChild(_settingsXml.CreateNode(XmlNodeType.Element, "Settings", ""));
				}
			}
			return _settingsXml;
		}
	}

	public override string ApplicationName
	{
		get
		{
			return "Spotnet";
		}
		set
		{
		}
	}

	public PortableSettingsProvider()
	{
		_settingsXml = null;
	}

	private string GetValue(SettingsProperty setting)
	{
		try
		{
			return SettingsXml.SelectSingleNode("Settings/" + setting.Name).InnerText;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return (setting.DefaultValue == null) ? "" : setting.DefaultValue.ToStringSafely();
		}
	}

	private void SetValue(SettingsPropertyValue propVal)
	{
		XmlElement xmlElement;
		try
		{
			xmlElement = (XmlElement)SettingsXml.SelectSingleNode("Settings/" + propVal.Name);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			xmlElement = null;
		}
		if (xmlElement != null)
		{
			xmlElement.InnerText = ((propVal.SerializedValue == null) ? string.Empty : propVal.SerializedValue.ToStringSafely());
			return;
		}
		XmlElement xmlElement2 = SettingsXml.CreateElement(propVal.Name);
		xmlElement2.InnerText = propVal.SerializedValue.ToStringSafely();
		SettingsXml.SelectSingleNode("Settings").AppendChild(xmlElement2);
	}

	public virtual string GetAppSettingsFilename()
	{
		return "settings.xml";
	}

	public virtual string GetAppSettingsPath()
	{
		return AppHelper.SettingsFolder;
	}

	public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection props)
	{
		SettingsPropertyValueCollection settingsPropertyValueCollection = new SettingsPropertyValueCollection();
		foreach (SettingsProperty prop in props)
		{
			settingsPropertyValueCollection.Add(new SettingsPropertyValue(prop)
			{
				IsDirty = false,
				SerializedValue = GetValue(prop)
			});
		}
		return settingsPropertyValueCollection;
	}

	public override void Initialize(string name, NameValueCollection col)
	{
		base.Initialize(ApplicationName, col);
	}

	public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection propvals)
	{
		foreach (SettingsPropertyValue propval in propvals)
		{
			SetValue(propval);
		}
		try
		{
			SettingsXml.Save(Path.Combine(GetAppSettingsPath(), GetAppSettingsFilename()));
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
		}
	}
}
