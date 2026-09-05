using System;
using System.Data.Common;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using NLog;

namespace Spotnet.DAL;

/// <summary>
/// Registers SQLite's FTS5 module on a freshly opened connection.
/// </summary>
/// <remarks>
/// The System.Data.SQLite.Core binaries are not built with SQLITE_ENABLE_FTS5:
/// `sqlite_compileoption_used('ENABLE_FTS5')` returns 0 and `USING fts5` fails with
/// "no such module: fts5" on a bare connection. FTS5 is present all the same —
/// SQLite.Interop.dll carries it as a loadable extension and exports
/// `sqlite3_fts5_init`. Loading that entry point registers the module, and `bm25()`
/// with it, on that one connection, so every connection reaching `search` or
/// `comments` has to do it. Loading twice on the same handle is a no-op.
///
/// Extension loading is switched back off immediately afterwards. Filters arrive as
/// user-supplied SQL, and leaving it on would hand them `load_extension()`; once off,
/// SQLite answers that with "not authorized" while the module stays registered.
/// </remarks>
internal static class Fts5Module
{
	private static readonly Logger Log = LogManager.GetCurrentClassLogger();

	private const string InteropFileName = "SQLite.Interop.dll";

	private const string EntryPoint = "sqlite3_fts5_init";

	private static readonly Lazy<string> InteropPath = new Lazy<string>(ResolveInteropPath);

	/// <summary>
	/// Makes `fts5` available on <paramref name="connection"/>, which must be open.
	/// </summary>
	internal static void Register(DbConnection connection)
	{
		if (!(connection is SQLiteConnection sqliteConnection))
		{
			return;
		}
		string interop = InteropPath.Value;
		if (interop == null)
		{
			return;
		}
		try
		{
			sqliteConnection.EnableExtensions(enable: true);
			sqliteConnection.LoadExtension(interop, EntryPoint);
		}
		catch (Exception ex)
		{
			// Not fatal here: the queries that need FTS5 will fail with a clear
			// "no such module" of their own, and everything else keeps working.
			Log.Warn("Could not register the FTS5 module from {0}: {1}", interop, ex.Message);
		}
		finally
		{
			try
			{
				sqliteConnection.EnableExtensions(enable: false);
			}
			catch (Exception ex)
			{
				Log.Warn("Could not disable extension loading: {0}", ex.Message);
			}
		}
	}

	/// <summary>
	/// Locates the interop DLL that this process actually loaded, falling back to the
	/// per-architecture layout the NuGet package lays down next to the executable.
	/// </summary>
	private static string ResolveInteropPath()
	{
		try
		{
			foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
			{
				string fileName;
				try
				{
					fileName = module.FileName;
				}
				catch (Exception)
				{
					// A module can refuse its path; it is not the one we are after.
					continue;
				}
				if (InteropFileName.Equals(Path.GetFileName(fileName), StringComparison.OrdinalIgnoreCase))
				{
					return fileName;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn("Could not enumerate loaded modules: {0}", ex.Message);
		}

		// Two layouts: .NET Framework staged the interop in x86/ and x64/ beside the
		// executable, .NET puts native assets under runtimes/<rid>/native.
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		string architecture = Environment.Is64BitProcess ? "x64" : "x86";
		string[] candidates =
		{
			Path.Combine(baseDirectory, "runtimes", "win-" + architecture, "native", InteropFileName),
			Path.Combine(baseDirectory, architecture, InteropFileName),
			Path.Combine(baseDirectory, InteropFileName)
		};
		foreach (string candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		Log.Warn("{0} was not found; full-text search will be unavailable", InteropFileName);
		return null;
	}
}
