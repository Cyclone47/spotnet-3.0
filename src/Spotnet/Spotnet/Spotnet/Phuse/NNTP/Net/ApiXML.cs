using System;
using System.Globalization;
using System.Xml;

namespace Spotnet.Phuse.NNTP.Net;

internal static class ApiXML
{
	internal static bool Slot(XmlWriter xR, VirtualSlot vSlot)
	{
		xR.WriteStartElement("slot");
		xR.WriteElementString("nzo_id", Convert.ToString(vSlot.ID));
		xR.WriteElementString("name", Module.CleanString(vSlot.Name));
		xR.WriteElementString("filename", Module.CleanString(vSlot.Name));
		xR.WriteElementString("status", Module.TranslateStatus((int)vSlot.Status));
		if (vSlot.Status == SlotStatus.Failed)
		{
			xR.WriteElementString("fail_message", Module.CleanString(vSlot.StatusLine));
		}
		if (!vSlot.History)
		{
			NNTPInfo info = vSlot.Info;
			int num = info.SecondsLeft(vSlot.SpeedAverage, vSlot.TotalTime);
			string value = "00:00:00";
			string value2 = Module.FormatDate(DateTime.UtcNow);
			if (num > 0)
			{
				value = Module.FormatElapsed(new TimeSpan(0, 0, num));
				value2 = Module.FormatDate(DateTime.UtcNow.AddSeconds(num));
			}
			xR.WriteElementString("index", Convert.ToString(vSlot.Index));
			xR.WriteElementString("percentage", Convert.ToString(Math.Round(info.Percentage, 0)));
			xR.WriteElementString("bytes", Convert.ToString(info.Expected));
			xR.WriteElementString("kbpersec", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", new object[1] { (decimal)vSlot.Speed / 1000m }));
			xR.WriteElementString("mb", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", new object[1] { Module.BytesToMegabytes(info.Expected) }));
			xR.WriteElementString("mbleft", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", new object[1] { Module.BytesToMegabytes(info.BytesLeft) }));
			xR.WriteElementString("size", string.Format(CultureInfo.InvariantCulture, "{0:0.0}", new object[1] { Module.BytesToMegabytes(info.Expected) }) + " MB");
			xR.WriteElementString("eta", value2);
			xR.WriteElementString("timeleft", value);
			xR.WriteElementString("priority", "Normal");
		}
		else
		{
			_ = vSlot.Status;
		}
		xR.WriteEndElement();
		xR.Flush();
		return true;
	}

	internal static bool Slots(XmlWriter xR, Slots vSlots)
	{
		xR.WriteStartElement("queue");
		NNTPInfo info = vSlots.Info;
		string value = "00:00:00";
		string value2 = Module.FormatDate(DateTime.UtcNow);
		int num = info.SecondsLeft(vSlots.SpeedAverage, vSlots.TotalTime);
		if (num > 0)
		{
			value = new TimeSpan(0, 0, 0, num, 0).ToString("c");
			value2 = Module.FormatDate(DateTime.UtcNow.AddSeconds(num));
		}
		xR.WriteElementString("status", vSlots.Status);
		xR.WriteElementString("paused", Module.BoolToString(vSlots.Paused));
		xR.WriteElementString("mb", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", new object[1] { Module.BytesToMegabytes(Math.Abs(info.Expected)) }));
		xR.WriteElementString("mbleft", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", new object[1] { Module.BytesToMegabytes(Math.Abs(info.BytesLeft)) }));
		xR.WriteElementString("kbpersec", string.Format(CultureInfo.InvariantCulture, "{0:0.00}", new object[1] { (decimal)vSlots.Speed / 1000m }));
		xR.WriteElementString("eta", value2);
		xR.WriteElementString("timeleft", value);
		xR.WriteElementString("uptime", Module.FormatElapsed(vSlots.Uptime));
		xR.WriteElementString("start", "0");
		xR.WriteElementString("limit", "0");
		xR.WriteElementString("speedlimit", "0");
		xR.WriteElementString("noofslots", Convert.ToString(vSlots.Count));
		xR.WriteElementString("have_warnings", Convert.ToString(vSlots.Log.Count));
		if (vSlots.Log.Count > 0)
		{
			string sIn = Module.ReadLog(vSlots.Log, 1).Replace(Environment.NewLine, "");
			xR.WriteElementString("last_warning", Module.CleanString(sIn));
		}
		foreach (VirtualSlot item in vSlots.List())
		{
			if (!Slot(xR, item))
			{
				return false;
			}
		}
		xR.WriteEndElement();
		xR.Flush();
		return true;
	}

	internal static bool Server(XmlWriter xR, VirtualServer vServer)
	{
		xR.WriteStartElement("server");
		xR.WriteElementString("nzo_id", Convert.ToString(vServer.ID));
		xR.WriteElementString("host", vServer.Host);
		xR.WriteElementString("port", Convert.ToString(vServer.Port));
		xR.WriteElementString("ssl", Module.BoolToString(vServer.SSL));
		xR.WriteElementString("username", Module.CleanString(vServer.Username));
		xR.WriteElementString("password", Module.Repeat("*", vServer.Password.Length));
		xR.WriteElementString("priority", TranslatePriority(vServer.Priority));
		xR.WriteElementString("connections", Convert.ToString(vServer.Connections.Count(vServer.ID)));
		xR.WriteEndElement();
		xR.Flush();
		return true;
	}

	private static string TranslatePriority(ServerPriority sP)
	{
		return sP switch
		{
			ServerPriority.High => "high", 
			ServerPriority.Low => "low", 
			_ => "default", 
		};
	}
}
