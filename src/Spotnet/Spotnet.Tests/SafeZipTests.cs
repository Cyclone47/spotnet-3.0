using System;
using System.IO;
using System.IO.Compression;
using Spotnet.Helpers;
using Xunit;

namespace Spotnet.Tests;

public sealed class SafeZipTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "SpotnetSafeZipTests", Guid.NewGuid().ToString("N"));

	[Fact]
	public void ExtractAllExtractsFilesInsideDestination()
	{
		string archivePath = CreateArchive("folder/file.txt", "safe");
		string destination = Path.Combine(_root, "output");

		SafeZip.ExtractAll(archivePath, destination, overwrite: true);

		Assert.Equal("safe", File.ReadAllText(Path.Combine(destination, "folder", "file.txt")));
	}

	[Fact]
	public void ExtractAllRejectsParentTraversal()
	{
		string archivePath = CreateArchive("../escaped.txt", "unsafe");
		string destination = Path.Combine(_root, "output");

		Assert.Throws<InvalidDataException>(() => SafeZip.ExtractAll(archivePath, destination, overwrite: true));
		Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
	}

	private string CreateArchive(string entryName, string contents)
	{
		Directory.CreateDirectory(_root);
		string archivePath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
		using FileStream stream = File.Create(archivePath);
		using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create);
		using StreamWriter writer = new StreamWriter(archive.CreateEntry(entryName).Open());
		writer.Write(contents);
		return archivePath;
	}

	public void Dispose()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
		GC.SuppressFinalize(this);
	}
}
