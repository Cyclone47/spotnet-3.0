using System;
using System.Data.Common;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;
using Spotnet.Helpers;

namespace Spotnet.Model;

[Serializable]
public class Spot
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public long Article;

	public int Category;

	public long Filesize;

	public byte KeyID;

	public string MessageId;

	public string Modulus;

	public string Poster;

	public long Stamp;

	public byte SubCat;

	public string SubCats;

	public string Tag;

	public string Title;

	public int NumberOfSpamReports;

	public bool IsSpotnetDisposeReportFromAuthorOfSpot;

	public bool IsMarkedAsDisposeReport(out string messageIdToRemove)
	{
		messageIdToRemove = string.Empty;
		if (KeyID != 2 || Title.Length < 3)
		{
			return false;
		}
		string[] array = Title.Split();
		if (array.Length < 2)
		{
			return false;
		}
		if (!array[0].EqualsIgnoreCase("dispose"))
		{
			return false;
		}
		if (array[1].Length < 3)
		{
			return false;
		}
		messageIdToRemove = SpotHelper.MakeMsg(array[1], tag: false);
		return true;
	}

	internal bool GetSpotStampFromDb()
	{
		if (Article <= 0)
		{
			throw new Exception("Acticle is not set or wrong: " + Article);
		}
		using (ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true))
		{
			using ISqlDbTransaction transaction = sqlDb.BeginReadTransaction();
			DbCommand dbCommand = sqlDb.CreateCommand(transaction);
			dbCommand.CommandText = "SELECT date FROM spots WHERE rowid = " + Article;
			using DbDataReader dbDataReader = sqlDb.ExecuteReader(dbCommand);
			if (dbDataReader == null)
			{
				throw new Exception("Error during query executing.");
			}
			while (dbDataReader.Read())
			{
				Stamp = dbDataReader.GetInt32(0);
			}
		}
		return true;
	}
}
