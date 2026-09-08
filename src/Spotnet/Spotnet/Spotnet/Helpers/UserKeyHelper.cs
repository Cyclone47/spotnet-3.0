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

		RSACryptoServiceProvider storeKey = null;
		try
		{
			storeKey = GetStoreKey();
		}
		catch (Exception ex)
		{
			Log.Warn("GetStoreKey failed: {0}", ex.Message);
		}

		RSACryptoServiceProvider dbKey = null;
		try
		{
			dbKey = GetDbKey();
		}
		catch (Exception ex)
		{
			Log.Warn("GetDbKey failed: {0}", ex.Message);
		}

		RSACryptoServiceProvider resolvedKey = null;

		if (dbKey != null)
		{
			resolvedKey = dbKey;
			try
			{
				if (storeKey == null || !resolvedKey.ToXmlString(includePrivateParameters: false).Equals(storeKey.ToXmlString(includePrivateParameters: false)))
				{
					SetStoreKey(resolvedKey);
				}
			}
			catch (Exception ex)
			{
				Log.Debug(ex.Message);
			}
		}
		else if (storeKey != null)
		{
			resolvedKey = storeKey;
			try
			{
				SetDbKey(resolvedKey);
			}
			catch (Exception ex)
			{
				Log.Debug(ex.Message);
			}
		}
		else
		{
			Log.Info("Generating a new Spotnet user key...");
			try
			{
				resolvedKey = CreateFreshKey();
				try { SetDbKey(resolvedKey); } catch (Exception ex) { Log.Debug(ex.Message); }
				try { SetStoreKey(resolvedKey); } catch (Exception ex) { Log.Debug(ex.Message); }
			}
			catch (Exception ex)
			{
				Log.Warn("Failed to persist fresh key; using ephemeral key: {0}", ex.Message);
				resolvedKey = new RSACryptoServiceProvider(384);
			}
		}

		_rsaKeyProvider = resolvedKey;
		return _rsaKeyProvider;
	}

	private static RSACryptoServiceProvider CreateFreshKey()
	{
		try
		{
			return new RSACryptoServiceProvider(384, new CspParameters
			{
				KeyContainerName = "Spotnet User Key",
				Flags = (CspProviderFlags.UseArchivableKey | CspProviderFlags.NoPrompt)
			});
		}
		catch (Exception)
		{
			TryDeleteContainer("Spotnet User Key");
			return new RSACryptoServiceProvider(384, new CspParameters
			{
				KeyContainerName = "Spotnet User Key",
				Flags = (CspProviderFlags.UseArchivableKey | CspProviderFlags.NoPrompt)
			});
		}
	}

	private static void TryDeleteContainer(string containerName)
	{
		try
		{
			CspParameters parameters = new CspParameters
			{
				KeyContainerName = containerName,
				Flags = CspProviderFlags.UseExistingKey
			};
			using RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(parameters);
			rsa.PersistKeyInCsp = false;
			rsa.Clear();
			Log.Info("Removed existing key container via CSP: {0}", containerName);
			return;
		}
		catch
		{
		}

		try
		{
			string rsaFolder = System.IO.Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				@"Microsoft\Crypto\RSA");
			if (System.IO.Directory.Exists(rsaFolder))
			{
				foreach (string sidDir in System.IO.Directory.GetDirectories(rsaFolder))
				{
					foreach (string file in System.IO.Directory.GetFiles(sidDir))
					{
						try
						{
							byte[] bytes = System.IO.File.ReadAllBytes(file);
							string text = System.Text.Encoding.ASCII.GetString(bytes);
							if (text.Contains(containerName))
							{
								System.IO.File.Delete(file);
								Log.Info("Deleted corrupted RSA key container file: {0}", file);
							}
						}
						catch
						{
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Debug("TryDeleteContainer folder search exception: {0}", ex.Message);
		}
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
				Log.Debug("Failed to open Spotnet key container '{0}': {1}", "Spotnet User Key" + text, ex.Message);
				if (num == 1 && text == "")
				{
					// If the main user key container is corrupt (e.g. NTE_BAD_KEY_STATE 0x8009000B),
					// clean it up and retry once before falling back.
					TryDeleteContainer("Spotnet User Key");
					try
					{
						return new RSACryptoServiceProvider(384, new CspParameters
						{
							KeyContainerName = "Spotnet User Key",
							Flags = (CspProviderFlags.UseArchivableKey | CspProviderFlags.NoPrompt)
						});
					}
					catch
					{
					}
				}

				if (num > 10)
				{
					break;
				}
				text = ((!(text == "")) ? (" New " + ++num) : " New");
				continue;
			}
		}
		return null;
	}

	private static void SetStoreKey(RSACryptoServiceProvider key)
	{
		if (key == null)
		{
			return;
		}

		try
		{
			new RSACryptoServiceProvider(384, new CspParameters
			{
				KeyContainerName = "Spotnet User Key",
				Flags = CspProviderFlags.NoPrompt
			}).ImportCspBlob(key.ExportCspBlob(includePrivateParameters: true));
		}
		catch (Exception)
		{
			TryDeleteContainer("Spotnet User Key");
			new RSACryptoServiceProvider(384, new CspParameters
			{
				KeyContainerName = "Spotnet User Key",
				Flags = CspProviderFlags.NoPrompt
			}).ImportCspBlob(key.ExportCspBlob(includePrivateParameters: true));
		}
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
		if (key == null)
		{
			return;
		}

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

		try
		{
			string text = StringCipher.Decrypt(encrypted);
			if (text.IsNullOrEmpty() || text.Length < 2)
			{
				return null;
			}
			RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider(384);
			rSACryptoServiceProvider.ImportCspBlob(Convert.FromBase64String(text.Substring(1)));
			return rSACryptoServiceProvider;
		}
		catch (Exception ex)
		{
			Log.Warn("Failed to decrypt stored user key: {0}", ex.Message);
			return null;
		}
	}

	private static void ClearKey(RSACryptoServiceProvider key)
	{
		if (key == null)
		{
			return;
		}
		try
		{
			key.PersistKeyInCsp = false;
			key.Clear();
		}
		catch
		{
		}
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
