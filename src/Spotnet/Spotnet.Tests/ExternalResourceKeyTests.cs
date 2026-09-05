using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Checks that every resource key the application expects from MahApps still exists in
/// the MahApps it builds against.
/// </summary>
/// <remarks>
/// This is the failure neither the compiler nor a smoke test catches. A missing style key
/// is not a build error and not an exception either - WPF resolves it to nothing and the
/// control simply renders unstyled. MahApps 2 renamed its entire resource vocabulary, so
/// the upgrade silently blanked everything still asking by a 1.x name.
///
/// The keys are looked for as strings in the assembly rather than resolved through WPF.
/// Resolving needs a <see cref="System.Windows.Application"/>, and a process may only ever
/// have one - MenuThemeTests owns it, and already proves the dictionaries themselves load.
/// What is left to prove here is that the names exist, and a compiled resource dictionary
/// carries its keys as literal strings.
///
/// The application defines its own palette in classiclight.xaml and moderndark.xaml, so
/// the brushes are its own. These four are what its XAML takes from the toolkit; the list
/// comes from subtracting every x:Key the application declares from every
/// StaticResource/DynamicResource it references.
/// </remarks>
public sealed class ExternalResourceKeyTests
{
    private static readonly string[] KeysTakenFromMahApps =
    {
        "MahApps.Styles.Button.Square.Accent",
        "MahApps.Styles.ComboBox",
        "MahApps.Styles.Thumb.ColumnHeaderGripper",
        "MahApps.Brushes.Control.Disabled"
    };

    /// <summary>Names MahApps 2 retired, kept here so a revival is noticed.</summary>
    private static readonly string[] KeysRetiredInMahApps2 =
    {
        "AccentedSquareButtonStyle",
        "MetroComboBox",
        "MetroColumnHeaderGripperStyle"
    };

    private static byte[] MahAppsAssembly()
    {
        string directory = Path.GetDirectoryName(new Uri(typeof(ExternalResourceKeyTests).Assembly.CodeBase).LocalPath);
        string path = Path.Combine(directory, "MahApps.Metro.dll");
        Assert.True(File.Exists(path), "MahApps.Metro.dll is not next to the test assembly: " + path);
        return File.ReadAllBytes(path);
    }

    /// <summary>True when the assembly carries <paramref name="key"/> as a literal.</summary>
    /// <remarks>
    /// Compiled resource dictionaries store keys in either encoding depending on the
    /// record, so both are searched.
    /// </remarks>
    private static bool Contains(byte[] assembly, string key)
    {
        return Encoding.ASCII.GetString(assembly).IndexOf(key, StringComparison.Ordinal) >= 0
            || Encoding.Unicode.GetString(assembly).IndexOf(key, StringComparison.Ordinal) >= 0;
    }

    [Fact]
    public void EveryKeyTheApplicationTakesFromMahAppsExists()
    {
        byte[] assembly = MahAppsAssembly();

        List<string> missing = KeysTakenFromMahApps.Where(key => !Contains(assembly, key)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void TheMahApps1NamesAreReallyGone()
    {
        // The other direction. If these ever resolve again the mapping above was
        // unnecessary, and if they do not, any XAML reintroducing one styles nothing
        // and reports nothing.
        byte[] assembly = MahAppsAssembly();

        List<string> resurrected = KeysRetiredInMahApps2.Where(key => Contains(assembly, key)).ToList();

        Assert.Empty(resurrected);
    }
}
