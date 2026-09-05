using System;
using System.IO;

namespace Spotnet.Deployment;

/// <summary>The installer uses a stable, per-user profile, isolated from legacy Spotnet.</summary>
internal static class InstalledProfile
{
    internal const string MarkerName = "Spotnet.install";
    internal static bool Enabled => File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MarkerName));
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotnet3");
    internal static string DataDirectory => Path.Combine(Root, "Data");
    internal static string SettingsPath => Path.Combine(DataDirectory, "user.config");
}
