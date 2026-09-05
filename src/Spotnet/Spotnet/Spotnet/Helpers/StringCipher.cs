using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Spotnet.Helpers;

internal static class StringCipher
{
	private const int Keysize = 256;

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

	private const string PassPhrase = "ie81uiodi3dil!@#$)_~!+DWCKQEC>w/,c.e;ric03";

	public static string Encrypt(string plainText)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(plainText);
		using Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes("ie81uiodi3dil!@#$)_~!+DWCKQEC>w/,c.e;ric03", new byte[8] { 5, 31, 2, 123, 82, 23, 1, 100 });
		byte[] bytes2 = rfc2898DeriveBytes.GetBytes(32);
		using Aes aes = Aes.Create();
		aes.Mode = CipherMode.CBC;
		using ICryptoTransform transform = aes.CreateEncryptor(bytes2, InitVectorBytes);
		using MemoryStream memoryStream = new MemoryStream();
		using CryptoStream cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);
		cryptoStream.Write(bytes, 0, bytes.Length);
		cryptoStream.FlushFinalBlock();
		return Convert.ToBase64String(memoryStream.ToArray());
	}

	public static string Decrypt(string cipherText)
	{
		byte[] array = Convert.FromBase64String(cipherText);
		using Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes("ie81uiodi3dil!@#$)_~!+DWCKQEC>w/,c.e;ric03", new byte[8] { 5, 31, 2, 123, 82, 23, 1, 100 });
		byte[] bytes = rfc2898DeriveBytes.GetBytes(32);
		using Aes aes = Aes.Create();
		aes.Mode = CipherMode.CBC;
		using ICryptoTransform transform = aes.CreateDecryptor(bytes, InitVectorBytes);
		using MemoryStream stream = new MemoryStream(array);
		using CryptoStream cryptoStream = new CryptoStream(stream, transform, CryptoStreamMode.Read);
		// Read to the end rather than trusting one call. A single Read on a CryptoStream
		// returns what one pass produced, which on .NET Framework happened to be
		// everything and here is one block - so anything longer than sixteen bytes came
		// back truncated, the signing key included.
		using MemoryStream plainText = new MemoryStream();
		cryptoStream.CopyTo(plainText);
		return Encoding.UTF8.GetString(plainText.ToArray());
	}
}
