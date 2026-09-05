using System;
using System.Data.Common;
using System.Security.Cryptography;
using NLog;
using Spotnet.DAL;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

internal static class UserKeyHelper
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static RSACryptoServiceProvider _rsaKeyProvider;

	private static string _modulus;

	internal static RSACryptoServiceProvider GetKey()
	{
		if (_rsaKeyProvider != null)
		{
			return _rsaKeyProvider;
		}
		RSACryptoServiceProvider storeKey = GetStoreKey();
		RSACryptoServiceProvider rSACryptoServiceProvider = GetDbKey();
		if (rSACryptoServiceProvider == null)
		{
			try
			{
				SetDbKey(storeKey);
			}
			catch (Exception ex)
			{
				Log.Debug(ex.Message);
			}
			rSACryptoServiceProvider = storeKey;
		}
		else if (storeKey == null || !rSACryptoServiceProvider.ToXmlString(includePrivateParameters: false).Equals(storeKey.ToXmlString(includePrivateParameters: false)))
		{
			try
			{
				SetStoreKey(rSACryptoServiceProvider);
			}
			catch (Exception ex)
			{
				Log.Debug(ex.Message);
			}
		}
		_rsaKeyProvider = rSACryptoServiceProvider;
		return _rsaKeyProvider;
	}

	private static RSACryptoServiceProvider GetStoreKey()
	{
		string text = "";
		int num = 1;
		while (true)
		{
			try
			{
				return new RSACryptoServiceProvider(384, new CspParameters
				{
					KeyContainerName = "Spotnet User Key" + text,
					Flags = (CspProviderFlags.UseArchivableKey | CspProviderFlags.NoPrompt)
				});
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
				if (num > 10)
				{
					break;
				}
				Log.Error("Failed to get default Spotnet key. So try to create and use another one...");
				text = ((!(text == "")) ? (" New " + ++num) : " New");
				continue;
			}
		}
		return null;
	}

	private static void SetStoreKey(RSACryptoServiceProvider key)
	{
		new RSACryptoServiceProvider(384, new CspParameters
		{
			KeyContainerName = "Spotnet User Key",
			Flags = CspProviderFlags.NoPrompt
		}).ImportCspBlob(key.ExportCspBlob(includePrivateParameters: true));
	}

	private static RSACryptoServiceProvider GetDbKey()
	{
		string @string;
		try
		{
			using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots(isReadOnly: true);
			DbCommand dbCommand = sqlDb.CreateCommand();
			dbCommand.CommandText = "SELECT key FROM userkey LIMIT 1";
			using DbDataReader dbDataReader = dbCommand.ExecuteReader();
			if (!dbDataReader.Read())
			{
				return null;
			}
			@string = dbDataReader.GetString(0);
		}
		catch (Exception)
		{
			return null;
		}
		return DecryptKey(@string);
	}

	private static void SetDbKey(RSACryptoServiceProvider key)
	{
		using ISqlDb sqlDb = SqlDbFactory.CreateSqlDbSpots();
		using ISqlDbTransaction sqlDbTransaction = sqlDb.BeginWriteTransaction();
		if (sqlDb.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS userkey(key TEXT)", sqlDbTransaction) != 0)
		{
			throw new Exception("CREATE TABLE userkey");
		}
		sqlDb.ExecuteNonQuery("DELETE FROM userkey", sqlDbTransaction);
		DbCommand dbCommand = sqlDb.CreateCommand(sqlDbTransaction);
		dbCommand.CommandText = "INSERT INTO userkey(key) VALUES(@key)";
		DbParameter dbParameter = dbCommand.CreateParameter();
		dbParameter.ParameterName = "key";
		dbParameter.Value = EncryptKey(key);
		dbCommand.Parameters.Add(dbParameter);
		if (dbCommand.ExecuteNonQuery() != 1)
		{
			throw new Exception("INSERT INTO userkey");
		}
		sqlDbTransaction.Commit();
	}

	private static string EncryptKey(RSACryptoServiceProvider key)
	{
		byte[] inArray = key.ExportCspBlob(includePrivateParameters: true);
		int num = new Random().Next(0, 26);
		return StringCipher.Encrypt((char)(97 + num) + Convert.ToBase64String(inArray));
	}

	private static RSACryptoServiceProvider DecryptKey(string encrypted)
	{
		if (encrypted.IsNullOrEmpty())
		{
			return null;
		}
		string text = StringCipher.Decrypt(encrypted);
		if (text.IsNullOrEmpty())
		{
			return null;
		}
		RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider(384);
		rSACryptoServiceProvider.ImportCspBlob(Convert.FromBase64String(text.Substring(1)));
		return rSACryptoServiceProvider;
	}

	private static void ClearKey(RSACryptoServiceProvider key)
	{
		key.PersistKeyInCsp = false;
		key.Clear();
	}

	internal static string GetModulus()
	{
		try
		{
			RSACryptoServiceProvider key = GetKey();
			if (key == null)
			{
				return string.Empty;
			}
			return _modulus ?? (_modulus = Convert.ToBase64String(key.ExportParameters(includePrivateParameters: false).Modulus));
		}
		catch (Exception ex)
		{
			Log.Debug(ex.Message);
			return string.Empty;
		}
	}

	internal static string GetModulusUriCompatable()
	{
		return (GetModulus() ?? string.Empty).Replace("+", "-").Replace("/", ".");
	}
}
