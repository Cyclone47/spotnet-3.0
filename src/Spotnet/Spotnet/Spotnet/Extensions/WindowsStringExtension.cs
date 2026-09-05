using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Spotnet.Helpers;

namespace Spotnet.Extensions;

// The members of the old StringExtension that cannot leave Windows: DPAPI
// (ProtectedData is Windows-only), SecureString marshalling via BSTR, and the ANSI
// codepage lookup behind AppHelper. Same namespace as the portable half, so extension
// method resolution at the call sites is unaffected.
//
// EncryptString, DecryptString and DecodeFromUtf8 currently have no callers. They are
// kept rather than deleted here because this is a port-preparation change, not a
// cleanup; when the macOS Keychain-backed ISecretStore lands, the DPAPI pair should be
// deleted rather than ported.
public static class WindowsStringExtension
{
	private static readonly byte[] Entropy = Encoding.Unicode.GetBytes("just a salt, it is not a password");

	public static string DecodeFromUtf8(this string utf8String)
	{
		if (utf8String.IsNullOrWhiteSpace())
		{
			return utf8String;
		}
		byte[] array = new byte[utf8String.Length];
		for (int i = 0; i < utf8String.Length; i++)
		{
			if ('\0' <= utf8String[i] && utf8String[i] <= 'ÿ')
			{
				array[i] = (byte)utf8String[i];
				continue;
			}
			throw new Exception("The char must be in byte's range");
		}
		return AppHelper.AnsiEnc().GetString(array, 0, array.Length);
	}

	public static string EncryptString(SecureString input)
	{
		return Convert.ToBase64String(ProtectedData.Protect(Encoding.Unicode.GetBytes(input.ToInsecureString()), Entropy, DataProtectionScope.CurrentUser));
	}

	public static SecureString DecryptString(string encryptedData)
	{
		try
		{
			byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(encryptedData), Entropy, DataProtectionScope.CurrentUser);
			return Encoding.Unicode.GetString(bytes).ToSecureString();
		}
		catch
		{
			return new SecureString();
		}
	}

	public static SecureString ToSecureString(this string input)
	{
		SecureString secureString = new SecureString();
		foreach (char c in input)
		{
			secureString.AppendChar(c);
		}
		secureString.MakeReadOnly();
		return secureString;
	}

	public static string ToInsecureString(this SecureString input)
	{
		IntPtr intPtr = Marshal.SecureStringToBSTR(input);
		try
		{
			return Marshal.PtrToStringBSTR(intPtr);
		}
		finally
		{
			Marshal.ZeroFreeBSTR(intPtr);
		}
	}
}
