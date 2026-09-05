using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using Spotnet.Helpers;

namespace Spotnet.Downloader;

internal class NzbGetRarScanner
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private readonly string _path;

	private IEnumerable<string> RarFiles => System.IO.Directory.GetFiles(_path, "*.rar", SearchOption.AllDirectories).ToList();

	public NzbGetRarScanner(string path)
	{
		_path = path;
	}

	public Dictionary<string, long> ParseFiles()
	{
		Dictionary<string, long> fileSizes = new Dictionary<string, long>();
		foreach (string rarFile in RarFiles)
		{
			IEnumerable<string> rarListResponse = GetRarListResponse(rarFile);
			ParseFilesList(rarListResponse).ToList().ForEach(delegate(KeyValuePair<string, long> x)
			{
				fileSizes[x.Key] = x.Value;
			});
		}
		return fileSizes;
	}

	private IEnumerable<KeyValuePair<string, long>> ParseFilesList(IEnumerable<string> response)
	{
		bool dataStarted = false;
		foreach (string item in response)
		{
			if (item.StartsWith("-----"))
			{
				if (dataStarted)
				{
					break;
				}
				dataStarted = true;
			}
			else if (dataStarted)
			{
				string[] array = item.Split(new char[2] { ' ', '\t' }, 5, StringSplitOptions.RemoveEmptyEntries);
				if (array.Length >= 5 && long.TryParse(array[1], out var result))
				{
					yield return new KeyValuePair<string, long>(array[4], result);
				}
			}
		}
	}

	private IEnumerable<string> GetRarListResponse(string rarFilePath)
	{
		Process proc = ExecuteCmdProcess("\"" + ArchiveHelper.UnRarPath + "\" l \"" + rarFilePath + "\"", ".");
		while (!proc.StandardOutput.EndOfStream)
		{
			yield return proc.StandardOutput.ReadLine();
		}
	}

	private static Process ExecuteCmdProcess(string command, string workingDirectory)
	{
		Process process = new Process();
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = "/C \"" + command + "\"",
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			WorkingDirectory = workingDirectory
		};
		process.StartInfo = startInfo;
		process.Start();
		return process;
	}
}
