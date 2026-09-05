using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Spotnet.Helpers;

/// <summary>
/// Central ZIP boundary. Every extracted entry is resolved and checked before a file is
/// created, so archives cannot escape their destination with rooted or parent paths.
/// </summary>
internal static class SafeZip
{
	internal static void ExtractAll(string archivePath, string destinationDirectory, bool overwrite)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			ExtractEntry(entry, destinationDirectory, overwrite);
		}
	}

	internal static string ExtractEntry(ZipArchiveEntry entry, string destinationDirectory, bool overwrite)
	{
		if (entry == null)
		{
			throw new ArgumentNullException(nameof(entry));
		}

		string destinationPath = GetSafeDestinationPath(destinationDirectory, entry.FullName);
		if (string.IsNullOrEmpty(entry.Name))
		{
			Directory.CreateDirectory(destinationPath);
			return destinationPath;
		}

		string parent = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(parent))
		{
			Directory.CreateDirectory(parent);
		}
		entry.ExtractToFile(destinationPath, overwrite);
		return destinationPath;
	}

	internal static void Create(string archivePath, IEnumerable<Tuple<string, string>> files)
	{
		using FileStream stream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
		using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create);
		foreach (Tuple<string, string> file in files)
		{
			string entryName = file.Item2.Replace('\\', '/').TrimStart('/');
			if (entryName.Length == 0 || Path.IsPathRooted(entryName) || entryName.Contains("../"))
			{
				throw new InvalidDataException("Invalid ZIP entry name: " + file.Item2);
			}
			archive.CreateEntryFromFile(file.Item1, entryName, CompressionLevel.Optimal);
		}
	}

	private static string GetSafeDestinationPath(string destinationDirectory, string entryName)
	{
		if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
		{
			throw new InvalidDataException("Invalid ZIP entry path: " + entryName);
		}

		string root = Path.GetFullPath(destinationDirectory)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string destinationPath = Path.GetFullPath(Path.Combine(root, entryName));
		string rootPrefix = root + Path.DirectorySeparatorChar;
		if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("ZIP entry escapes its destination: " + entryName);
		}
		return destinationPath;
	}
}
