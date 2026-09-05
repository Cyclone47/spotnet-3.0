using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Xml;
using NLog;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

internal static class SpotnetUpdateVerifier
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	internal static bool VerifyFileSign(string xmlFileName)
	{
		XmlDocument xmlDocument = new XmlDocument();
		xmlDocument.PreserveWhitespace = true;
		// This parses a downloaded update manifest before its signature has been checked,
		// so external entities must not be resolved.
		xmlDocument.XmlResolver = null;
		xmlDocument.Load(xmlFileName);
		return VerifySign(xmlDocument);
	}

	internal static bool VerifySign(XmlDocument signedXml)
	{
		try
		{
			RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider();
			rSACryptoServiceProvider.FromXmlString("<RSAKeyValue><Modulus>xJ8rOq1i0xsDWuHgRDbCngSyrYGBsamWnKzlFxHQXyPrNo9UjpFU4hONPTnzo5JJlX7SVnbVvY9k64xe3KbTQmXRnU+0GZQ0ikz0XjJgfHTpI+4MmSILx12ZMbN50rDDWHa6Mda/6O/xwV2Tcpi+dFxL63UoGnIW+13pEHg/Dfc=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>");
			bool num = VerifyXml(signedXml, rSACryptoServiceProvider);
			if (!num)
			{
				Log.Debug("Update.xml signature verification failed");
			}
			return num;
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	internal static bool VerifyFile(string file, string sumExpected)
	{
		try
		{
			if (!System.IO.File.Exists(file))
			{
				return false;
			}
			using MD5 mD = MD5.Create();
			using FileStream inputStream = System.IO.File.OpenRead(file);
			return BitConverter.ToString(mD.ComputeHash(inputStream)).Replace("-", "").EqualsIgnoreCase(sumExpected);
		}
		catch (Exception ex)
		{
			Log.Exception(ex);
			return false;
		}
	}

	private static bool VerifyXml(XmlDocument doc, RSA key)
	{
		if (doc == null)
		{
			throw new ArgumentException("Doc");
		}
		if (key == null)
		{
			throw new ArgumentException("Key");
		}
		SignedXml signedXml = new SignedXml(doc);
		XmlNodeList elementsByTagName = doc.GetElementsByTagName("Signature");
		if (elementsByTagName.Count <= 0)
		{
			throw new CryptographicException("Verification failed: No Signature was found in the document.");
		}
		if (elementsByTagName.Count >= 2)
		{
			throw new CryptographicException("Verification failed: More that one signature was found for the document.");
		}
		signedXml.LoadXml((XmlElement)elementsByTagName[0]);
		return signedXml.CheckSignature(key);
	}
}
