using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualBasic;

namespace Spotnet.Extensions;

// The platform-neutral half of the old StringExtension. The namespace is deliberately
// unchanged from when this lived in Spotnet.dll, so every existing `using
// Spotnet.Extensions;` call site resolves exactly as before. The DPAPI and ANSI-codepage
// members stayed behind in WindowsStringExtension.
public static class StringExtension
{
	public static bool IsNullOrEmpty(this string str)
	{
		return string.IsNullOrEmpty(str);
	}

	public static bool IsNullOrWhiteSpace(this string str)
	{
		return string.IsNullOrWhiteSpace(str);
	}

	public static bool IsNullOrEmpty(this Array array)
	{
		if (array != null)
		{
			return array.Length == 0;
		}
		return true;
	}

	public static bool EqualsIgnoreCase(this string str1, string str2)
	{
		if (str1 == null || str2 == null)
		{
			return false;
		}
		return str1.ToUpperInvariant().Equals(str2.ToUpperInvariant());
	}

	public static bool EqualsIgnoreCase(this string str1, object obj)
	{
		if (str1 == null || obj == null)
		{
			return false;
		}
		return str1.ToUpperInvariant().Equals(obj.ToString().ToUpperInvariant());
	}

	public static string Format(this string str, params object[] parameters)
	{
		return string.Format(str, parameters);
	}

	public static string Format(this string str, object param1)
	{
		return string.Format(str, param1);
	}

	public static string Format(this string str, object param1, object param2)
	{
		return string.Format(str, param1, param2);
	}

	public static string Format(this string str, object param1, object param2, object param3)
	{
		return string.Format(str, param1, param2, param3);
	}

	public static string FormatFromDictionary(this string formatString, Dictionary<string, string> valueDict)
	{
		int num = 0;
		StringBuilder stringBuilder = new StringBuilder(formatString);
		Dictionary<string, int> keyToInt = new Dictionary<string, int>();
		foreach (KeyValuePair<string, string> item in valueDict)
		{
			stringBuilder = stringBuilder.Replace("{" + item.Key + "}", "{" + num + "}");
			keyToInt.Add(item.Key, num);
			num++;
		}
		return string.Format(stringBuilder.ToString(), ((IEnumerable<object>)(from x in valueDict
			orderby keyToInt[x.Key]
			select x.Value)).ToArray());
	}

	public static byte[] ToByteArray(this string str)
	{
		byte[] array = new byte[str.Length * 2];
		Buffer.BlockCopy(str.ToCharArray(), 0, array, 0, array.Length);
		return array;
	}

	public static string ToString(this byte[] bytes)
	{
		char[] array = new char[bytes.Length / 2];
		Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
		return new string(array);
	}

	public static string Repeat(this string s, int n)
	{
		return new string(Enumerable.Range(0, n).SelectMany((int x) => s).ToArray());
	}

	public static string Repeat(this char c, int n)
	{
		return new string(c, n);
	}

	public static string ReplaceIgnoreCase(this string s, string oldValue, string newValue)
	{
		return Strings.Replace(s, oldValue, newValue, 1, -1, CompareMethod.Text);
	}

	public static string ReadLine(this string text, int lineNumber)
	{
		StringReader stringReader = new StringReader(text);
		int num = 0;
		string text2;
		do
		{
			num++;
			text2 = stringReader.ReadLine();
		}
		while (text2 != null && num < lineNumber);
		if (num != lineNumber)
		{
			return string.Empty;
		}
		return text2;
	}

	public static int CountLines(this string str)
	{
		if (str == null)
		{
			throw new ArgumentNullException("str");
		}
		if (str == string.Empty)
		{
			return 0;
		}
		int num = -1;
		int num2 = 0;
		while (-1 != (num = str.IndexOf(Environment.NewLine, num + 1, StringComparison.Ordinal)))
		{
			num2++;
		}
		return num2 + 1;
	}
}
