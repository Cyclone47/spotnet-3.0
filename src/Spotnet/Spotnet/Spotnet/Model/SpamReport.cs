using System;
using System.Data.Common;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;
using Spotnet.Properties;

namespace Spotnet.Model;

public class SpamReport
{
	public DateTime Date;

	public string MessageId;

	public string Modulus;

	public long ReportId;

	public string BodyMessageId;

	public string Username;

	public string Text;

	public string TextFormatedForOutput
	{
		get
		{
			if (Text.IsNullOrWhiteSpace())
			{
				return "";
			}
			string text = Text.Trim();
			text = text.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
			if (text.Length > 100)
			{
				return text.Substring(0, 100);
			}
			return text;
		}
	}

	public void GetBody()
	{
		if (!BodyMessageId.IsNullOrWhiteSpace() && new NNTP(AppHelper.HeaderPhuse).GetBody(Settings.Default.ReportGroup, BodyMessageId, out string resp, out int _, out string _) && resp.EndsWith("\r\n.\r\n"))
		{
			resp = resp.Substring(resp.IndexOf("\r\n", StringComparison.Ordinal) + 2);
			if (resp.Length > 5)
			{
				Text = resp.Substring(0, resp.Length - 5).Trim();
			}
		}
	}

	internal void GetReportDateFromDb(ISqlDb db)
	{
		if (ReportId <= 0)
		{
			throw new Exception("Acticle is not set or wrong: " + ReportId);
		}
		using ISqlDbTransaction transaction = db.BeginReadTransaction();
		DbCommand dbCommand = db.CreateCommand(transaction);
		dbCommand.CommandText = "SELECT date FROM spamreports WHERE rowid = " + ReportId;
		using DbDataReader dbDataReader = db.ExecuteReader(dbCommand);
		if (dbDataReader == null)
		{
			throw new Exception("Error during query executing.");
		}
		while (dbDataReader.Read())
		{
			Date = dbDataReader.GetInt32(0).FromUnixTime();
		}
	}
}
