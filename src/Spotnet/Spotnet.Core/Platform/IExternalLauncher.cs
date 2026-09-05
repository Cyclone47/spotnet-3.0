namespace Spotnet.Platform;

/// <summary>
/// Cross-platform launcher for web links, files, and folders.
/// </summary>
public interface IExternalLauncher
{
	bool OpenUrl(string url);
	bool OpenFolder(string folderPath);
	bool OpenFile(string filePath);
}
