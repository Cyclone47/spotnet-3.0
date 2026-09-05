using System;

namespace Spotnet.Platform;

/// <summary>
/// Cross-platform directory path provider for Spotnet profiles, caches, databases, and downloads.
/// </summary>
public interface IAppPaths
{
	/// <summary>Directory holding the spots database, servers.xml, and configuration files.</summary>
	string DataFolder { get; }

	/// <summary>Directory holding temporary cached images, previews, or downloaded segments.</summary>
	string CacheFolder { get; }

	/// <summary>Directory holding log files.</summary>
	string LogsFolder { get; }

	/// <summary>Directory holding custom filters and theme definitions.</summary>
	string FiltersFolder { get; }

	/// <summary>Default destination directory for completed downloads and NZB files.</summary>
	string DownloadsFolder { get; }

	/// <summary>Directory for transient scratch files.</summary>
	string TempFolder { get; }

	/// <summary>Computes the path to the SQLite database file for a given NNTP server.</summary>
	string GetDatabasePath(string serverAddress);

	/// <summary>Generates a temporary file path inside the Spotnet temp directory.</summary>
	string GetTempFileName(string ext = null, string filename = null);

	/// <summary>Ensures that all standard Spotnet folders exist on disk.</summary>
	void EnsureDirectoriesExist();
}
