using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Spotnet.Tests;

[CollectionDefinition("Community pane", DisableParallelization = true)]
public sealed class CommunityPaneCollection
{
}

/// <summary>
/// The settings window builds each pane from a property setter that the binding engine
/// calls, and the binding engine swallows whatever that setter throws. A pane that fails
/// to construct therefore shows up as an empty panel with nothing in the log, which is
/// exactly how this one first shipped. These tests build the pane directly so the failure
/// is an error rather than a blank rectangle.
/// </summary>
[Collection("Community pane")]
public sealed class SettingsForCommunityTests : IDisposable
{
    private readonly string previousFolder = Spotnet.Helpers.AppHelper.SettingsFolder;
    private readonly string testFolder =
        Path.Combine(Path.GetTempPath(), "CommunityPaneTests-" + Guid.NewGuid().ToString("N"));

    public SettingsForCommunityTests()
    {
        Directory.CreateDirectory(testFolder);
        Spotnet.Helpers.AppHelper.SettingsFolder = testFolder;
        Spotnet.Community.CommunityConfig.Invalidate();
    }

    public void Dispose()
    {
        Spotnet.Helpers.AppHelper.SettingsFolder = previousFolder;
        Spotnet.Community.CommunityConfig.Invalidate();
        if (Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, true);
        }
    }

    /// <summary>Runs <paramref name="body"/> on an STA thread and rethrows anything it threw.</summary>
    private static void OnUiThread(Action body)
    {
        Exception error = null;
        Thread thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    [Fact]
    public void ThePaneBuildsWithoutThrowing()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            Assert.NotNull(pane.Content);
        });
    }

    [Fact]
    public void ThePaneLaysOutWithVisibleContent()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            pane.Measure(new Size(640, 2000));
            pane.Arrange(new Rect(pane.DesiredSize));
            pane.UpdateLayout();

            // An empty panel measures to nothing; a populated one does not.
            Assert.True(pane.DesiredSize.Height > 200,
                $"The pane laid out {pane.DesiredSize.Width}x{pane.DesiredSize.Height}, which means it rendered empty.");
        });
    }

    [Fact]
    public void ThePaneShowsTheConfiguredValues()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            TextBox spots = (TextBox)pane.FindName("SpotsGroupTextBox");
            TextBox whitelist = (TextBox)pane.FindName("WhitelistUrlTextBox");

            Assert.Equal("free.pt", spots.Text);
            Assert.Equal("http://spotcloud.spotnet.wf/spotnet/lists.new/whitelist.csv", whitelist.Text);

            // De indexer hoort niet meer op deze pagina thuis; die staat nu onder
            // Externe integraties. Zie SettingsForIntegrationsTests.
            Assert.Null(pane.FindName("NewznabKeyTextBox"));
        });
    }

    [Fact]
    public void ThePaneAcceptsItsOwnValuesBack()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            Assert.True(pane.VerifyFields());
        });
    }

    [Fact]
    public void ThePaneRefusesAnInvalidNewsgroup()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            ((TextBox)pane.FindName("SpotsGroupTextBox")).Text = "";

            Assert.False(pane.VerifyFields());
        });
    }

    [Fact]
    public void ThePaneShowsClassicListsControls()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            CheckBox classicCheck = (CheckBox)pane.FindName("UseClassicListsCheckBox");
            TextBox classicWhite = (TextBox)pane.FindName("ClassicWhitelistUrlTextBox");
            TextBox classicBlack = (TextBox)pane.FindName("ClassicBlacklistUrlTextBox");

            Assert.NotNull(classicCheck);
            Assert.NotNull(classicWhite);
            Assert.NotNull(classicBlack);

            Assert.False(classicCheck.IsChecked);
            Assert.Equal("https://spotlist.store/spotnet/whitelist.xml", classicWhite.Text);
            Assert.Equal("https://spotlist.store/spotnet/blacklist.xml", classicBlack.Text);
        });
    }

    [Fact]
    public void ThePaneValidatesClassicUrlsWhenChecked()
    {
        OnUiThread(() =>
        {
            Spotnet.Controls.SettingsForCommunity pane = new Spotnet.Controls.SettingsForCommunity();
            CheckBox classicCheck = (CheckBox)pane.FindName("UseClassicListsCheckBox");
            TextBox classicWhite = (TextBox)pane.FindName("ClassicWhitelistUrlTextBox");

            classicCheck.IsChecked = true;
            classicWhite.Text = "";

            Assert.False(pane.VerifyFields());

            classicWhite.Text = "ftp://invalid.example";
            Assert.False(pane.VerifyFields());

            classicWhite.Text = "https://spotlist.store/spotnet/whitelist.xml";
            Assert.True(pane.VerifyFields());
        });
    }
}

