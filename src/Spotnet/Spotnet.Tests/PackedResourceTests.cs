using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using Xunit;

namespace Spotnet.Tests
{
    /// <summary>
    /// Checks that the resources the application loads by path are really in the assembly.
    /// </summary>
    /// <remarks>
    /// A missing packed resource says nothing. Application.GetResourceStream returns null,
    /// the caller assigns null to an Image.Source, and the control renders as an empty
    /// outline - which is exactly how the three view-mode buttons disappeared: their paths
    /// used backslashes, .NET Framework quietly rewrote those to forward slashes, and
    /// modern .NET does not.
    ///
    /// The list is written down rather than discovered, because these paths are built at
    /// run time from string literals. It fails when a resource is renamed, moved, or
    /// dropped from the project.
    /// </remarks>
    public class PackedResourceTests
    {
        /// <summary>Resource paths the application asks for by name, as pack URIs do.</summary>
        private static readonly string[] LoadedByPath =
        {
            // The copy entry in the spot page's selection menu.
            "resources/imagesinternal/copy.png",
            // Tray icon and the spots-list backdrop.
            "resources/imagesinternal/smallspotnet.ico",
            "resources/imagesinternal/spotsbg.png",
            // Dictionaries merged or swapped at run time.
            "style/mainmenustyle.baml",
            "style/progressringstyle.baml",
            "style/classiclight.baml",
            "style/moderndark.baml"
        };

        private static HashSet<string> ResourceNames()
        {
            string directory = Path.GetDirectoryName(new Uri(typeof(PackedResourceTests).Assembly.CodeBase).LocalPath);
            string path = Path.Combine(directory, "Spotnet.dll");
            Assert.True(File.Exists(path), "Spotnet.dll is not next to the test assembly: " + path);

            Assembly spotnet = Assembly.LoadFrom(path);
            using Stream stream = spotnet.GetManifestResourceStream("Spotnet.g.resources");
            Assert.NotNull(stream);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = new ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                names.Add((string)entry.Key);
            }
            return names;
        }

        [Fact]
        public void EveryResourceTheApplicationLoadsByPathIsPresent()
        {
            HashSet<string> present = ResourceNames();

            List<string> missing = LoadedByPath.Where(name => !present.Contains(name)).ToList();

            Assert.Empty(missing);
        }
    }
}
