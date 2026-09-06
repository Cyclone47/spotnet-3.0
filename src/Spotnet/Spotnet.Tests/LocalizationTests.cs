using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using Spotnet.Helpers;
using Spotnet.Localization;
using Xunit;

namespace Spotnet.Tests;

/// <summary>
/// Guards the two-language setup: that Dutch and English stay in step, and that the
/// markup keeps reaching the resources through a binding rather than through x:Static.
/// </summary>
/// <remarks>
/// The x:Static form is resolved once while a window is built, which is what used to make
/// a language change need a restart. A single reintroduced x:Static is invisible until
/// somebody switches language and finds one stubborn label, so it is checked here.
/// </remarks>
[Collection("UserLanguage")]
public sealed class LocalizationTests
{
    private static DirectoryInfo ProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Spotnet", "Spotnet");
            if (Directory.Exists(candidate)) return new DirectoryInfo(candidate);
        }
        throw new DirectoryNotFoundException("Cannot find src/Spotnet/Spotnet from the test output.");
    }

    private static Dictionary<string, string> Resx(string fileName)
    {
        string path = Path.Combine(ProjectDirectory().FullName, fileName);
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
    }

    private static IEnumerable<string> MarkupFiles()
    {
        return Directory.EnumerateFiles(ProjectDirectory().FullName, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("Spotnet.Properties.Words.resx", "Spotnet.Properties.Words.nl.resx")]
    [InlineData("Spotnet.Properties.Categories.resx", "Spotnet.Properties.Categories.nl.resx")]
    public void EnglishAndDutchDefineTheSameKeysAndNoneIsEmpty(string english, string dutch)
    {
        var en = Resx(english);
        var nl = Resx(dutch);

        Assert.Equal(en.Keys.OrderBy(k => k, StringComparer.Ordinal), nl.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(en, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), "Empty English value: " + entry.Key));
        Assert.All(nl, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), "Empty Dutch value: " + entry.Key));
    }

    [Fact]
    public void AFormatPlaceholderInOneLanguageExistsInTheOtherToo()
    {
        var en = Resx("Spotnet.Properties.Words.resx");
        var nl = Resx("Spotnet.Properties.Words.nl.resx");
        var placeholder = new Regex(@"\{(\d+)[^}]*\}");

        foreach (var entry in en)
        {
            var english = placeholder.Matches(entry.Value).Select(m => m.Groups[1].Value).ToHashSet();
            var dutch = placeholder.Matches(nl[entry.Key]).Select(m => m.Groups[1].Value).ToHashSet();
            Assert.True(english.SetEquals(dutch),
                $"{entry.Key} formats {english.Count} argument(s) in English and {dutch.Count} in Dutch.");
        }
    }

    /// <summary>
    /// Words.cs and Categories.cs are checked in rather than generated at build time, so
    /// a key added to the resx alone compiles fine and then fails only where code reads it.
    /// </summary>
    [Theory]
    [InlineData("Spotnet.Properties.Words.resx", "Words.cs")]
    [InlineData("Spotnet.Properties.Categories.resx", "Categories.cs")]
    public void TheCheckedInResourceClassExposesExactlyTheKeysTheResxDefines(string resx, string designer)
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory().FullName, "Spotnet", "Properties", designer));
        var exposed = Regex.Matches(source, @"public static string (\w+) =>").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(exposed.Count, exposed.Distinct().Count());
        Assert.Equal(Resx(resx).Keys.OrderBy(k => k, StringComparer.Ordinal),
                     exposed.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void NoMarkupResolvesAResourceThroughXStatic()
    {
        var offenders = MarkupFiles()
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"x:Static\s+\w+:(Words|Categories)\."))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files resolve a resource once at load time; use {loc:Loc Key} instead: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryKeyTheMarkupAsksForExists()
    {
        var words = Resx("Spotnet.Properties.Words.resx");
        var categories = Resx("Spotnet.Properties.Categories.resx");
        var reference = new Regex(@"\{loc:(Loc|Cat)\s+([A-Za-z0-9_]+)");
        var missing = new List<string>();
        int found = 0;

        foreach (string file in MarkupFiles())
        {
            string markup = File.ReadAllText(file);
            if (reference.IsMatch(markup))
            {
                Assert.Contains("xmlns:loc=\"clr-namespace:Spotnet.Localization\"", markup);
            }

            foreach (Match match in reference.Matches(markup))
            {
                found++;
                var set = match.Groups[1].Value == "Loc" ? words : categories;
                if (!set.ContainsKey(match.Groups[2].Value))
                {
                    missing.Add(Path.GetFileName(file) + ": " + match.Groups[2].Value);
                }
            }
        }

        Assert.True(found > 200, "Expected the markup to bind hundreds of strings, found " + found);
        Assert.Empty(missing);
    }

    [Fact]
    public void TheBindingSourceFollowsTheCulture()
    {
        CultureInfo original = Spotnet.Properties.Words.Culture;
        try
        {
            Spotnet.Properties.Words.Culture = CultureInfo.GetCultureInfo("nl");
            Assert.Equal("Opslaan", TranslationSource.Words["Save"]);

            Spotnet.Properties.Words.Culture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("Save", TranslationSource.Words["Save"]);
        }
        finally
        {
            Spotnet.Properties.Words.Culture = original;
        }
    }

    /// <summary>
    /// The end-to-end proof: real markup, parsed by WPF, repainting on a culture change
    /// with no window rebuilt. Runs on its own STA thread and never constructs an
    /// Application - a process may only have one, and MenuThemeTests owns it.
    /// </summary>
    [Fact]
    public void ParsedMarkupFollowsALanguageSwitchWithoutBeingRebuilt()
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            CultureInfo original = Spotnet.Properties.Words.Culture;
            try
            {
                const string markup =
                    "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:loc=\"clr-namespace:Spotnet.Localization;assembly=Spotnet\" Text=\"{loc:Loc Save}\" />";

                Spotnet.Properties.Words.Culture = CultureInfo.GetCultureInfo("nl");
                var block = (TextBlock)XamlReader.Parse(markup);
                Assert.Equal("Opslaan", block.Text);

                Spotnet.Properties.Words.Culture = CultureInfo.GetCultureInfo("en");
                Assert.Equal("Opslaan", block.Text); // Nothing has told it to look again yet.

                TranslationSource.Words.Invalidate();
                Assert.Equal("Save", block.Text);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Spotnet.Properties.Words.Culture = original;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The markup test did not finish.");
        if (error != null) throw error;
    }

    [Fact]
    public void AnUnknownKeyShowsItselfInsteadOfThrowing()
    {
        Assert.Equal("NoSuchKeyExists", TranslationSource.Words["NoSuchKeyExists"]);
        Assert.Equal(string.Empty, TranslationSource.Words[null]);
    }

    [Fact]
    public void InvalidatingAnnouncesThatEveryKeyChanged()
    {
        var raised = new List<string>();
        TranslationSource.Words.PropertyChanged += Record;
        try
        {
            TranslationSource.Words.Invalidate();
        }
        finally
        {
            TranslationSource.Words.PropertyChanged -= Record;
        }

        // "Item[]" is what WPF listens for to re-read every indexer binding.
        Assert.Equal(new[] { "Item[]" }, raised);

        void Record(object sender, System.ComponentModel.PropertyChangedEventArgs e) => raised.Add(e.PropertyName);
    }

    [Theory]
    [InlineData("Nieuw", "New")]
    [InlineData("Overzicht", "Overview")]
    [InlineData("Films", "Movies")]
    [InlineData("Series", "Series")]
    [InlineData("Boeken", "Books")]
    [InlineData("Muziek", "Music")]
    [InlineData("Spellen", "Games")]
    [InlineData("Applicaties", "Applications")]
    [InlineData("Erotiek", "Erotica")]
    [InlineData("Laatste 24 uur", "Last 24 hours")]
    [InlineData("Beeld", "Movies")]
    [InlineData("Beeld - Genres", "Movies - Genres")]
    [InlineData("Beeld - TV Series", "Movies - TV Series")]
    [InlineData("Muziek - Genres", "Music - Genres")]
    [InlineData("Spellen - Console", "Games - Console")]
    [InlineData("Spellen - Mobile", "Games - Mobile")]
    [InlineData("Applicaties - Mobile", "Applications - Mobile")]
    [InlineData("Favorieten", "Favorites")]
    [InlineData("Actie", "Action")]
    [InlineData("Komedie", "Comedy")]
    [InlineData("Documentaire", "Documentary")]
    [InlineData("Oorlog", "War")]
    public void FilterTranslationHelperTranslatesDefaultFiltersToEnglish(string dutch, string expectedEnglish)
    {
        var original = UserLanguageHelper.Culture;
        try
        {
            UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("en");
            Assert.Equal(expectedEnglish, FilterTranslationHelper.GetTranslatedName(dutch));
        }
        finally
        {
            UserLanguageHelper.Culture = original;
        }
    }

    [Theory]
    [InlineData("New", "Nieuw")]
    [InlineData("Overview", "Overzicht")]
    [InlineData("Movies", "Films")]
    [InlineData("Books", "Boeken")]
    [InlineData("Music", "Muziek")]
    [InlineData("Games", "Spellen")]
    [InlineData("Applications", "Applicaties")]
    [InlineData("Favorites", "Favorieten")]
    [InlineData("Action", "Actie")]
    public void FilterTranslationHelperTranslatesEnglishFiltersBackToDutch(string english, string expectedDutch)
    {
        var original = UserLanguageHelper.Culture;
        try
        {
            UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("nl");
            Assert.Equal(expectedDutch, FilterTranslationHelper.GetTranslatedName(english));
        }
        finally
        {
            UserLanguageHelper.Culture = original;
        }
    }

    [Fact]
    public void FilterTranslationHelperPreservesWhitespaceAndNewCountInFilterViewModel()
    {
        var original = UserLanguageHelper.Culture;
        try
        {
            UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("en");

            // Leading whitespace preserved
            Assert.Equal(" New", FilterTranslationHelper.GetTranslatedName(" Nieuw"));
            Assert.Equal("  Overview", FilterTranslationHelper.GetTranslatedName("  Overzicht"));

            // FilterViewModel DisplayText integration
            var vm = new Spotnet.ViewModel.FilterViewModel(" Overzicht", "cat!=9");
            Assert.Equal(" Overview", vm.DisplayText);

            vm.NewCount = 2;
            Assert.Equal(" Overview (2)", vm.DisplayText);

            var vmBooks = new Spotnet.ViewModel.FilterViewModel("Boeken", "cat=5");
            vmBooks.NewCount = 1;
            Assert.Equal("Books (1)", vmBooks.DisplayText);
        }
        finally
        {
            UserLanguageHelper.Culture = original;
        }
    }

    [Fact]
    public void FilterTranslationHelperLeavesCustomNamesUnchanged()
    {
        var original = UserLanguageHelper.Culture;
        try
        {
            UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("en");
            Assert.Equal("Mijn Aangepaste Filter", FilterTranslationHelper.GetTranslatedName("Mijn Aangepaste Filter"));
        }
        finally
        {
            UserLanguageHelper.Culture = original;
        }
    }

    [Fact]
    public void FilterTranslationHelperTranslatesFilterSetNames()
    {
        var original = UserLanguageHelper.Culture;
        try
        {
            UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("en");
            Assert.Equal("Custom", FilterTranslationHelper.GetTranslatedFilterSetName("Aangepast"));

            UserLanguageHelper.Culture = CultureInfo.CreateSpecificCulture("nl");
            Assert.Equal("Aangepast", FilterTranslationHelper.GetTranslatedFilterSetName("Custom"));
            Assert.Equal("Aangepast", FilterTranslationHelper.GetTranslatedFilterSetName("Aangepast"));
        }
        finally
        {
            UserLanguageHelper.Culture = original;
        }
    }
}
