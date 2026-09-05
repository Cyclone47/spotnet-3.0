using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Phuse.NNTP.Net;

public class NNTPInputPriorityComparer : IComparer<NNTPInput>
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public int Compare(NNTPInput f1, NNTPInput f2)
	{
		try
		{
			if (f1 == null)
			{
				return 1;
			}
			if (f2 == null)
			{
				return -1;
			}
			if (f1.IsParOrParPiece && f2.IsParOrParPiece)
			{
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			if (f1.IsParOrParPiece && !f2.IsParOrParPiece)
			{
				return 1;
			}
			if (!f1.IsParOrParPiece && f2.IsParOrParPiece)
			{
				return -1;
			}
			if (!ArchiveHelper.IsRarFile(f1.Filename) && !ArchiveHelper.IsRarFile(f2.Filename))
			{
				return f1.Index.CompareTo(f2.Index);
			}
			if (ArchiveHelper.IsRarFile(f1.Filename) && !ArchiveHelper.IsRarFile(f2.Filename))
			{
				return 1;
			}
			if (!ArchiveHelper.IsRarFile(f1.Filename) && ArchiveHelper.IsRarFile(f2.Filename))
			{
				return -1;
			}
			Regex regex = new Regex("(.+)\\.part(\\d+)\\.rar$", RegexOptions.IgnoreCase);
			Regex regex2 = new Regex("(.+)\\.rar$", RegexOptions.IgnoreCase);
			Regex regex3 = new Regex("(.+)\\.[rz](\\d+)$", RegexOptions.IgnoreCase);
			Regex regex4 = new Regex("(.+)\\.(\\d+)$");
			if (regex.IsMatch(f1.Filename) && !regex.IsMatch(f2.Filename))
			{
				return 1;
			}
			if (!regex.IsMatch(f1.Filename) && regex.IsMatch(f2.Filename))
			{
				return -1;
			}
			if (regex.IsMatch(f1.Filename) && regex.IsMatch(f2.Filename))
			{
				string value = regex.Match(f1.Filename).Groups[1].Value;
				string value2 = regex.Match(f2.Filename).Groups[1].Value;
				if (value.Equals(value2) && int.TryParse(regex.Match(f1.Filename).Groups[2].Value, out var result) && int.TryParse(regex.Match(f2.Filename).Groups[2].Value, out var result2))
				{
					return result.CompareTo(result2);
				}
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			if (regex4.IsMatch(f1.Filename) && !regex4.IsMatch(f2.Filename))
			{
				return 1;
			}
			if (!regex4.IsMatch(f1.Filename) && regex4.IsMatch(f2.Filename))
			{
				return -1;
			}
			if (regex4.IsMatch(f1.Filename) && regex4.IsMatch(f2.Filename))
			{
				string value3 = regex4.Match(f1.Filename).Groups[1].Value;
				string value4 = regex4.Match(f2.Filename).Groups[1].Value;
				if (value3.Equals(value4) && int.TryParse(regex4.Match(f1.Filename).Groups[2].Value, out var result3) && int.TryParse(regex4.Match(f2.Filename).Groups[2].Value, out var result4))
				{
					return result3.CompareTo(result4);
				}
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			if (regex2.IsMatch(f1.Filename) && regex2.IsMatch(f2.Filename))
			{
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			if (regex3.IsMatch(f1.Filename) && regex3.IsMatch(f2.Filename))
			{
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			if (regex2.IsMatch(f1.Filename) && regex3.IsMatch(f2.Filename))
			{
				string value5 = regex2.Match(f1.Filename).Groups[1].Value;
				string value6 = regex3.Match(f2.Filename).Groups[1].Value;
				if (value5.Equals(value6))
				{
					return -1;
				}
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			if (regex3.IsMatch(f1.Filename) && regex2.IsMatch(f2.Filename))
			{
				string value7 = regex2.Match(f2.Filename).Groups[1].Value;
				string value8 = regex3.Match(f1.Filename).Groups[1].Value;
				if (value7.Equals(value8))
				{
					return 1;
				}
				return string.Compare(f1.Filename, f2.Filename, StringComparison.Ordinal);
			}
			Log.Warn("Should never be happen. Strange files comparison: \"" + f1.Filename + "\" and \"" + f2.Filename + "\".");
			return f1.Index.CompareTo(f2.Index);
		}
		catch (Exception ex)
		{
			Log.Debug("Failed to sort: " + ex.Message);
			return 0;
		}
	}
}
