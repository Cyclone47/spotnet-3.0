using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace Spotnet.Helpers;

internal static class NzrDecoder
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private static readonly string[] PassPhrase = new string[2] { "ie81uiodi3dil!@#$)_~!+DWCKQEC>w/,c.e;ric03", "e4r&2yq8%GLcZg3v?v+G5JVqVvbk?eZ5n*Z2*ehDdU" };

	/// <summary>The initialization vector, cut to the AES block size.</summary>
	/// <remarks>
	/// The literal is eighteen bytes and AES uses sixteen. .NET Framework's
	/// RijndaelManaged took the first block's worth and ignored the rest without saying
	/// anything; .NET checks the length and throws. Cutting it here reproduces exactly
	/// what the old code did, which is what keeps everything encrypted by earlier
	/// versions readable - including the user's signing key, which is read at startup.
	/// </remarks>
	private static readonly byte[] InitVectorBytes =
		Encoding.ASCII.GetBytes("tu8922jkde2j0t89u2").Take(16).ToArray();

	internal static string Decode(string cipherText, int key)
	{
		if (key > PassPhrase.Length - 1)
		{
			Log.Debug("Key is out of range: " + key);
			return null;
		}
		try
		{
			byte[] array = Convert.FromBase64String(cipherText);
			string @string;
			using (Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(PassPhrase[key], new byte[8] { 5, 31, 2, 123, 82, 23, 1, 100 }))
			{
				byte[] bytes = rfc2898DeriveBytes.GetBytes(32);
				using Aes aes = Aes.Create();
				using ICryptoTransform transform = aes.CreateDecryptor(bytes, InitVectorBytes);
				using MemoryStream stream = new MemoryStream(array);
				using CryptoStream cryptoStream = new CryptoStream(stream, transform, CryptoStreamMode.Read);
				// One Read returns a single pass worth; see StringCipher.Decrypt.
				using MemoryStream plainText = new MemoryStream();
				cryptoStream.CopyTo(plainText);
				@string = Encoding.UTF8.GetString(plainText.ToArray());
			}
			return @string;
		}
		catch (Exception ex)
		{
			Log.Error("Decoding nzr: " + ex.Message);
			return null;
		}
	}

	internal static string Encode(string plainText, int key)
	{
		if (key > PassPhrase.Length - 1)
		{
			Log.Debug("Key is out of range: " + key);
			return null;
		}
		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(plainText);
			string result;
			using (Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes(PassPhrase[key], new byte[8] { 5, 31, 2, 123, 82, 23, 1, 100 }))
			{
				byte[] bytes2 = rfc2898DeriveBytes.GetBytes(32);
				using Aes aes = Aes.Create();
				aes.Mode = CipherMode.CBC;
				using ICryptoTransform transform = aes.CreateEncryptor(bytes2, InitVectorBytes);
				using MemoryStream memoryStream = new MemoryStream();
				using CryptoStream cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);
				cryptoStream.Write(bytes, 0, bytes.Length);
				cryptoStream.FlushFinalBlock();
				result = Convert.ToBase64String(memoryStream.ToArray());
			}
			return result;
		}
		catch (Exception ex)
		{
			Log.Error("Encoding: " + ex.Message);
			return null;
		}
	}
}
