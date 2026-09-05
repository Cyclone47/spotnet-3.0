using System.Text.RegularExpressions;
using System.IO;
using Spotnet.Extensions;

namespace Spotnet.Helpers;

public static class ArchiveHelper
{
	public static string UnRarPath => Path.Combine(AppHelper.AppPath(), "UnRAR.exe");

	public static string SevenZipPath => Path.Combine(AppHelper.AppPath(), "7za.exe");

	public static bool IsRarFile(string path)
	{
		path = path.Trim();
		if (path.IsNullOrEmpty())
		{
			return false;
		}
		return new Regex("\\.rar$|\\.[rz]\\d\\d$|\\.\\d+$", RegexOptions.IgnoreCase).IsMatch(path);
	}
}
