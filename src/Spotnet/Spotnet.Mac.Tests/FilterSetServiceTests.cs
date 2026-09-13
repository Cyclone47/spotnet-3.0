using System;
using System.IO;
using System.Linq;
using Spotnet.Mac.Models;
using Spotnet.Mac.Services;
using Xunit;

namespace Spotnet.Mac.Tests;

/// <summary>
/// The filter sets live on disk in the Windows Filters.v2 layout, so a set can travel
/// between the two clients. These pin down the parts of that contract the Mac client
/// implements: the five bootstrapped sets, immutability with the fork into Aangepast,
/// the XML round-trip including the Image attribute, and the load-time query rewrites.
/// </summary>
public class FilterSetServiceTests : IDisposable
{
    private readonly TempAppPaths _paths = new();
    private readonly UserPreferencesService _prefs;
    private readonly FilterSetService _service;

    public FilterSetServiceTests()
    {
        _prefs = new UserPreferencesService(_paths);
        _service = new FilterSetService(_paths, _prefs);
        _service.InitializeDefaultSets();
    }

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void First_run_bootstraps_the_five_shipped_sets()
    {
        var names = _service.GetSetNames();

        foreach (string expected in FilterSetService.ImmutableSetNames.Append(FilterSetService.CustomSetName))
        {
            Assert.Contains(expected, names, StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(_paths.FiltersFolder, expected, "filters.xml")),
                $"{expected} should hold a filters.xml");
        }
    }

    [Fact]
    public void The_dutch_advanced_set_loads_the_same_145_nodes_windows_ships()
    {
        var tree = _service.LoadTree("Geavanceerd NL");

        Assert.Equal(145, CountNodes(tree));
        Assert.Contains(tree, n => n.Name == "Overzicht");
        Assert.Contains(tree, n => n.Name == "Beeld" && n.Children.Count > 0);
    }

    [Fact]
    public void Writing_an_immutable_set_is_refused()
    {
        var tree = _service.LoadTree("Geavanceerd NL");

        Assert.False(_service.SaveTree("Geavanceerd NL", tree));
    }

    [Fact]
    public void Mutating_while_an_immutable_set_is_active_forks_into_aangepast()
    {
        _prefs.Current.FilterSetName = "Geavanceerd NL";
        _prefs.Save(_prefs.Current);
        var tree = _service.LoadActiveTree();

        string target = _service.EnsureMutableSet(tree);

        Assert.Equal(FilterSetService.CustomSetName, target);
        Assert.Equal(FilterSetService.CustomSetName, _service.ActiveSet);
        // The fork carried the tree over.
        Assert.Equal(CountNodes(tree), CountNodes(_service.LoadActiveTree()));
    }

    [Fact]
    public void A_saved_tree_round_trips_including_the_windows_image_attribute()
    {
        var node = new FilterItem
        {
            Id = "Mijn filter",
            Kind = FilterKind.Custom,
            Name = "Mijn filter",
            Icon = "",
            ImageRef = "/Images/2/Vandaag.png",
            Query = "cat = 1 AND cats LIKE '%1a6%'"
        };
        node.Children.Add(new FilterItem
        {
            Id = "Mijn filter/Sub",
            Kind = FilterKind.Preset,
            Name = "Sub",
            Query = "cat = 1"
        });

        Assert.True(_service.SaveTree(FilterSetService.CustomSetName, new[] { node }));
        var loaded = _service.LoadTree(FilterSetService.CustomSetName);

        var roundTripped = Assert.Single(loaded);
        Assert.Equal("Mijn filter", roundTripped.Name);
        Assert.Equal("/Images/2/Vandaag.png", roundTripped.ImageRef);
        Assert.Equal("cat = 1 AND cats LIKE '%1a6%'", roundTripped.Query);
        var child = Assert.Single(roundTripped.Children);
        Assert.Equal("Sub", child.Name);
    }

    [Fact]
    public void Load_rewrites_the_legacy_query_spellings_windows_rewrites()
    {
        Assert.Equal("rowid > 100", FilterSetService.RewriteQuery("docid > 100"));
        Assert.Equal("cat = 6", FilterSetService.RewriteQuery("cat = 1 AND cats MATCH '1b4 OR 1d11'"));
        Assert.Equal("tag MATCH 'pzh'", FilterSetService.RewriteQuery("tag = 'pzh'"));
        Assert.Equal("sender MATCH 'x'", FilterSetService.RewriteQuery("sender = 'x'"));
        // A query that already scopes subcategories keeps its plain equality.
        Assert.Equal("subcat = 3 AND tag = 'pzh'", FilterSetService.RewriteQuery("subcat = 3 AND tag = 'pzh'"));
    }

    [Fact]
    public void SaveAs_refuses_shipped_names_and_RemoveSet_falls_back_to_dutch_advanced()
    {
        var tree = _service.LoadTree(FilterSetService.CustomSetName);

        var (ok, message) = _service.SaveAs("Geavanceerd NL", tree);
        Assert.False(ok);
        Assert.Equal("Cannot override default filters list", message);

        var (saved, _) = _service.SaveAs("Mijn Lijst", tree);
        Assert.True(saved);
        Assert.Equal("Mijn Lijst", _service.ActiveSet);

        Assert.True(_service.RemoveSet("Mijn Lijst"));
        Assert.Equal("Geavanceerd NL", _service.ActiveSet);
        Assert.False(_service.RemoveSet("Simple EN"));
    }

    [Fact]
    public void Expansion_state_round_trips()
    {
        _service.SaveExpanded(new[] { "Beeld", "Beeld/Genres" });

        var expanded = _service.LoadExpanded();

        Assert.Contains("Beeld", expanded);
        Assert.Contains("Beeld/Genres", expanded);
        Assert.DoesNotContain("Muziek", expanded);
    }

    private static int CountNodes(System.Collections.Generic.IEnumerable<FilterItem> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            count += 1 + CountNodes(node.Children);
        }
        return count;
    }
}
