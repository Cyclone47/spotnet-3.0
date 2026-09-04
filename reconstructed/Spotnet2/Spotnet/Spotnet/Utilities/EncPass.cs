using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Utilities;

internal class EncPass
{
	private static readonly Logger logger = LogManager.GetCurrentClassLogger();

	private readonly byte[] iv;

	private readonly byte[] key;

	public EncPass()
	{
		key = new byte[24]
		{
			1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
			11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
			21, 22, 23, 24
		};
		iv = new byte[8] { 65, 110, 68, 26, 69, 178, 200, 219 };
	}

	public string Decrypt(string inputInByts)
	{
		if (string.IsNullOrEmpty(inputInByts.Trim()))
		{
			return "";
		}
		checked
		{
			inputInByts = inputInByts.Substring(0, inputInByts.Length - 1);
			try
			{
				new UTF8Encoding();
				// TripleDES.Create() keeps CBC/PKCS7, so stored passwords still decrypt.
				using TripleDES tripleDESCryptoServiceProvider = TripleDES.Create();
				byte[] array = Convert.FromBase64String(inputInByts);
				ICryptoTransform transform = tripleDESCryptoServiceProvider.CreateDecryptor(key, iv);
				MemoryStream memoryStream = new MemoryStream();
				CryptoStream cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);
				cryptoStream.Write(array, 0, array.Length);
				cryptoStream.FlushFinalBlock();
				memoryStream.Position = 0L;
				byte[] array2 = new byte[(int)(memoryStream.Length - 1) + 1];
				memoryStream.Read(array2, 0, (int)memoryStream.Length);
				cryptoStream.Close();
				return new UTF8Encoding().GetString(array2);
			}
			catch (Exception ex)
			{
				logger.Exception(ex, showToClient: true);
				return null;
			}
		}
	}

	public string Encrypt(string plainText)
	{
		if (plainText == null)
		{
			return null;
		}
		byte[] bytes = new UTF8Encoding().GetBytes(plainText);
		using TripleDES tripleDes = TripleDES.Create();
		ICryptoTransform transform = tripleDes.CreateEncryptor(key, iv);
		MemoryStream memoryStream = new MemoryStream();
		CryptoStream cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);
		cryptoStream.Write(bytes, 0, bytes.Length);
		cryptoStream.FlushFinalBlock();
		memoryStream.Position = 0L;
		checked
		{
			byte[] array = new byte[(int)(memoryStream.Length - 1) + 1];
			memoryStream.Read(array, 0, (int)memoryStream.Length);
			cryptoStream.Close();
			return Convert.ToBase64String(array) + "=";
		}
	}
}
