using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spotnet.Tests;

public sealed class InstallerLocalizationTests
{
    private static string InstallerScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "installer", "Spotnet3.iss");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("Cannot find installer/Spotnet3.iss from the test output.");
    }

    [Fact]
    public void EveryEnglishCustomMessageHasADutchTranslationAndEveryCodeKeyExists()
    {
        string source = InstallerScript();
        string section = Regex.Match(source, @"(?ms)^\[CustomMessages\]\s*(.*?)^\[").Groups[1].Value;
        var messages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["english"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ["dutch"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
        foreach (Match match in Regex.Matches(section, @"(?m)^(english|dutch)\.([A-Za-z0-9]+)=(.+)$"))
        {
            Assert.False(string.IsNullOrWhiteSpace(match.Groups[3].Value));
            Assert.True(messages[match.Groups[1].Value].Add(match.Groups[2].Value), "Duplicate translation key: " + match.Value);
        }
        Assert.True(messages["english"].Count >= 60, "Too few localized installer strings.");
        Assert.Equal(messages["english"].OrderBy(x => x), messages["dutch"].OrderBy(x => x));
        foreach (Match reference in Regex.Matches(source, @"CM\('([A-Za-z0-9]+)'\)"))
            Assert.Contains(reference.Groups[1].Value, messages["english"]);
        Assert.Contains("Description: \"{cm:LaunchSpotnet}\"", source);
        for (int index = 1; index <= 5; index++) Assert.Contains("CM('Welcome" + index + "')", source);
    }

    [Fact]
    public void LengthyPreparationUsesAVisibleProgressPageAndAlwaysHidesIt()
    {
        string source = InstallerScript();
        Assert.Contains("CreateOutputProgressPage(CM('ProgressTitle'), CM('ProgressDescription'))", source);
        Assert.Contains("ProgressPage.Show", source);
        Assert.Contains("ProgressPage.SetText(CM('StatusProfile'), CM('ProgressDetail'))", source);
        // The page is hidden and the marquee stopped on every exit path, successful or not.
        Assert.Matches(@"(?s)try.*finally\s+if ShowProgress then begin\s+SetBusy\(False\);\s+ProgressPage\.Hide;", source);
    }

    [Fact]
    public void PrerequisiteStepsShowMovementTheyCannotMeasure()
    {
        string source = InstallerScript();
        // Microsoft's installers report nothing back, so a step bar would stand still for
        // minutes and read as a hang. The bar marquees and the unpack step says so.
        Assert.Contains("ProgressPage.ProgressBar.Style := npbstMarquee", source);
        Assert.Contains("CM('StatusDotNetPrepare')", source);
        // An attended run shows Microsoft's own progress window; an unattended one stays silent.
        Assert.Contains("'/install /passive /norestart'", source);
        Assert.Contains("'/install /quiet /norestart'", source);
    }

    [Fact]
    public void SetupRefusesAProfileCopyTheDriveCannotHold()
    {
        string source = InstallerScript();
        Assert.Contains("measure --profile", source);
        Assert.Contains("CM('SpaceShort')", source);
        // Measured on the Ready page, and refused there rather than halfway through the copy.
        Assert.Matches(@"(?s)CurPageID = wpReady.*SpaceMeasured and not SpaceFits.*Result := False", source);
    }

    [Fact]
    public void ShortcutTasksAreOfferedAndReachTheHelper()
    {
        string source = InstallerScript();
        foreach (string task in new[] { "programsicon", "desktopicon" })
        {
            Assert.Contains("Name: \"" + task + "\"", source);
            Assert.Contains("WizardIsTaskSelected('" + task + "')", source);
        }
        Assert.Contains("' --create '", source);
        Assert.Contains("ShortcutParameters + ShortcutCreation", source);
    }

    [Fact]
    public void ClassicChoicesReplaceTechnicalPagesAndAreSkippedWithoutClassic()
    {
        string source = InstallerScript();
        Assert.DoesNotContain("CreateInputFilePage", source);
        Assert.DoesNotContain("CreateInputDirPage", source);
        Assert.Contains("(PageID = MigrationPage.ID) and (ExistingProfile or not ClassicAvailable)", source);
        foreach (string choice in new[] { "MigrateReplace", "MigrateAlongside", "CleanAlongside", "CleanReplace" })
            Assert.Contains("MigrationPage.Add(CM('" + choice + "'))", source);
        Assert.Contains("' --classic-mode ' + ClassicShortcutMode", source);
        Assert.Matches(@"(?s)if CurStep = ssPostInstall.*if not ShortcutFailure then begin.*'complete-move --profile '", source);
        Assert.Contains("CM('MoveConfirmation')", source);
    }

    [Fact]
    public void UninstallRetainsTheProfileUnlessRemovalIsExplicitlySelected()
    {
        string source = InstallerScript();
        Assert.Contains("RemoveDataCheck.Checked := False", source);
        Assert.Contains("{param:REMOVEPERSONALDATA|0}", source);
        Assert.Contains("(CurUninstallStep = usPostUninstall) and RemovePersonalData", source);
        Assert.Contains("DelTree(ProfileRoot, True, True, True)", source);
        Assert.Contains("Shortcut backups live inside the profile, so removal must be the final step", source);
    }
}
