using System;
using System.Configuration.Provider;
using System.Text.RegularExpressions;
using NLog;
using Spotnet.Extensions;
using Spotnet.Model;

namespace Spotnet.Helpers;

internal class SpotsUploader
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	public Tuple<long, string, string> FindSegment(string group, long offset, string filepart = null)
	{
		long num = offset;
		NNTP nNTP = new NNTP(AppHelper.HeaderPhuse);
		long first = 0L;
		long last = 0L;
		long count = 0L;
		if (!nNTP.SelectGroup(group, ref first, ref last, ref count, out var result, out var errorMsg))
		{
			if (errorMsg.Equals("Removed"))
			{
				return new Tuple<long, string, string>(num, null, null);
			}
			SystemStateChecker.AddProblem(SystemStateProblemEnum.NntpServerIsNotAvailable, errorMsg);
			throw new ProviderException(errorMsg);
		}
		last -= offset;
		string arg = "";
		while (last > first)
		{
			Log.Debug($"LastId: {last}. Date: {arg}");
			string[] array = nNTP.GetHeaders(group, last - 10000, last, null, out result, out errorMsg).Split('\n');
			if (array.Length == 3)
			{
				return new Tuple<long, string, string>(num, null, null);
			}
			for (int num2 = array.Length - 3; num2 > 0; num2--)
			{
				try
				{
					string[] array2 = array[num2].Trim().Split('\t');
					string text;
					string text2;
					string item;
					string text3;
					if (array2.Length >= 5)
					{
						text = array2[0];
						text2 = array2[1];
						arg = array2[3];
						item = array2[4];
						num++;
						text3 = ExtractFilenameFromHeader(text2);
						if (text3.IsNullOrWhiteSpace())
						{
							text3 = "filename not found: " + text2;
							Log.Debug("[" + text + "]: " + text3);
						}
						else if (filepart == null)
						{
							if (IsParFile(text3))
							{
								goto IL_0163;
							}
						}
						else if (text3.Contains(filepart))
						{
							goto IL_0163;
						}
					}
					goto end_IL_00c3;
					IL_0163:
					Log.Debug("[" + text + "]: " + text3);
					return new Tuple<long, string, string>(num, item, text2);
					end_IL_00c3:;
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			}
			last -= 10001;
		}
		return new Tuple<long, string, string>(num, null, null);
	}

	private bool IsParFile(string filename)
	{
		filename = filename.TrimEnd();
		bool num = filename.EndsWith(".par2");
		Regex regex = new Regex("(\\.vol(\\d+)\\+(\\d+)\\.par2$)", RegexOptions.IgnoreCase);
		if (num)
		{
			return !regex.IsMatch(filename);
		}
		return false;
	}

	private string ExtractFilenameFromHeader(string header)
	{
		Match match = Regex.Match(header, "\"(.*)\"", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			return match.Groups[1].ToString();
		}
		return null;
	}
}
