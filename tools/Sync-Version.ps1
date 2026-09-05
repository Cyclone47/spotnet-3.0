<#
.SYNOPSIS
    Checks - or sets - the version number across every place it is written by hand.

.DESCRIPTION
    AssemblyInfo.cs holds the one version number a human edits. The installer, the About
    dialog and the release notes page all read it back at build or run time, so they never
    need touching. What cannot be derived is prose and published data: the release notes,
    the READMEs and the update feed.

    Run without arguments to verify those agree with AssemblyInfo.cs. Run with -Set to bump
    the version and rewrite the mechanical parts, then write the prose the summary lists.

    See docs/VERSIONING.md for the full picture.

.PARAMETER Set
    The new version to write, as four components (for example 3.0.9.0).

.EXAMPLE
    pwsh ./tools/Sync-Version.ps1

.EXAMPLE
    pwsh ./tools/Sync-Version.ps1 -Set 3.0.9.0
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Set
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$assemblyInfo = Join-Path $repo 'src/Spotnet/Spotnet/Properties/AssemblyInfo.cs'
$readmeNl = Join-Path $repo 'README.md'
$readmeEn = Join-Path $repo 'README_EN.md'
$feed = Join-Path $repo 'updates/latest.json'
$notesFiles = @(
    (Join-Path $repo 'src/Spotnet/Spotnet/Resources/ReleaseNotes/whatsnew.html'),
    (Join-Path $repo 'src/Spotnet/Spotnet/Resources/ReleaseNotes/whatsnew.nl.html')
)
$resx = Join-Path $repo 'src/Spotnet/Spotnet/Spotnet.Properties.Resources.resx'

function Get-AssemblyVersion {
    $content = Get-Content -LiteralPath $assemblyInfo -Raw
    $match = [regex]::Match($content, 'AssemblyVersion\("(?<v>\d+\.\d+\.\d+\.\d+)"\)')
    if (-not $match.Success) { throw "No AssemblyVersion found in $assemblyInfo." }
    return $match.Groups['v'].Value
}

if ($Set) {
    $old = Get-AssemblyVersion
    if ($old -eq $Set) {
        Write-Host "AssemblyInfo.cs already reads $Set." -ForegroundColor Yellow
    }
    else {
        $content = Get-Content -LiteralPath $assemblyInfo -Raw
        $content = $content -replace ('AssemblyVersion\("' + [regex]::Escape($old) + '"\)'), ('AssemblyVersion("' + $Set + '")')
        $content = $content -replace ('AssemblyFileVersion\("' + [regex]::Escape($old) + '"\)'), ('AssemblyFileVersion("' + $Set + '")')
        Set-Content -LiteralPath $assemblyInfo -Value $content -Encoding UTF8 -NoNewline
        Write-Host "AssemblyInfo.cs: $old -> $Set" -ForegroundColor Green
    }

    # The READMEs carry the version in a table row and in two release links. Those are
    # mechanical; the surrounding text is not, and is left alone.
    foreach ($readme in @($readmeNl, $readmeEn)) {
        $content = Get-Content -LiteralPath $readme -Raw
        $before = $content
        $content = $content -replace '(\| \*\*Versie\*\* \| )\d+\.\d+\.\d+\.\d+( \|)', ('${1}' + $Set + '${2}')
        $content = $content -replace '(\| \*\*Version\*\* \| )\d+\.\d+\.\d+\.\d+( \|)', ('${1}' + $Set + '${2}')
        $content = $content -replace 'Spotnet \d+\.\d+\.\d+\.\d+ Setup', ('Spotnet ' + $Set + ' Setup')
        $content = $content -replace 'releases/download/v\d+\.\d+\.\d+\.\d+/Spotnet-3\.0-x64-Setup\.exe', ('releases/download/v' + $Set + '/Spotnet-3.0-x64-Setup.exe')
        $content = $content -replace '(releases/tag/v)\d+\.\d+\.\d+\.\d+(\)| )', ('${1}' + $Set + '${2}')
        if ($content -ne $before) {
            Set-Content -LiteralPath $readme -Value $content -Encoding UTF8 -NoNewline
            Write-Host ("{0}: version row and download links updated." -f (Split-Path -Leaf $readme)) -ForegroundColor Green
        }
    }

    Write-Host ''
    Write-Host 'Still to write by hand:' -ForegroundColor Cyan
    Write-Host ("  1. docs/releases/v{0}.md - English and Dutch changelog." -f $Set)
    Write-Host '  2. Resources/ReleaseNotes/whatsnew.html and whatsnew.nl.html - a new'
    Write-Host '     section on top, and replace the previous gh-tag badge with its date.'
    Write-Host '  3. The same two documents inside Spotnet.Properties.Resources.resx'
    Write-Host '     (whatsnew and whatsnew_nl) - that copy is what actually ships.'
    Write-Host '  4. After publishing the GitHub release: updates/latest.json.'
    Write-Host ''
    Write-Host 'Then run this script without -Set, and the test suite, to confirm.'
    return
}

$version = Get-AssemblyVersion
Write-Host "AssemblyInfo.cs: $version" -ForegroundColor Cyan
Write-Host ''

$problems = New-Object System.Collections.Generic.List[string]

function Test-NewestSection {
    param([string] $Label, [string] $Text)
    $match = [regex]::Match($Text, '<h3>Spotnet (?<v>\d+(\.\d+)+)')
    if (-not $match.Success) {
        $problems.Add("$Label - no '<h3>Spotnet <version>' heading found.")
        return
    }
    $found = $match.Groups['v'].Value
    if ($found -ne $script:version) {
        $problems.Add("$Label - leads with $found, expected $script:version.")
    }
    else {
        Write-Host "  ok  $Label leads with $found"
    }
    $badges = ([regex]::Matches($Text, 'gh-tag')).Count
    if ($badges -ne 1) {
        $problems.Add("$Label - $badges 'New' badges; only the newest release may carry one.")
    }
}

Write-Host 'Release notes' -ForegroundColor Cyan
foreach ($file in $notesFiles) {
    Test-NewestSection -Label (Split-Path -Leaf $file) -Text (Get-Content -LiteralPath $file -Raw)
}

# The resx copies are what the build embeds; the .html files are only the editable source.
$resxXml = [xml](Get-Content -LiteralPath $resx -Raw)
foreach ($name in @('whatsnew', 'whatsnew_nl')) {
    $node = $resxXml.root.data | Where-Object { $_.name -eq $name }
    if (-not $node) {
        $problems.Add("Resources.resx - no '$name' entry.")
        continue
    }
    Test-NewestSection -Label "Resources.resx ($name)" -Text $node.value
}

Write-Host ''
Write-Host 'Documentation' -ForegroundColor Cyan
$releaseNotes = Join-Path $repo ("docs/releases/v{0}.md" -f $version)
if (Test-Path -LiteralPath $releaseNotes) {
    Write-Host ("  ok  docs/releases/v{0}.md exists" -f $version)
}
else {
    $problems.Add(("docs/releases/v{0}.md is missing." -f $version))
}

foreach ($readme in @($readmeNl, $readmeEn)) {
    $name = Split-Path -Leaf $readme
    $content = Get-Content -LiteralPath $readme -Raw
    if ($content -notmatch ('\| \*\*(Versie|Version)\*\* \| ' + [regex]::Escape($version) + ' \|')) {
        $problems.Add("$name - the version row does not read $version.")
    }
    elseif ($content -notmatch ('releases/download/v' + [regex]::Escape($version) + '/')) {
        $problems.Add("$name - the download link does not point at v$version.")
    }
    else {
        Write-Host "  ok  $name advertises $version"
    }
}

Write-Host ''
Write-Host 'Update feed' -ForegroundColor Cyan
$manifest = Get-Content -LiteralPath $feed -Raw | ConvertFrom-Json
# The feed names the published release, which legitimately lags the version being built.
# It is reported, never failed on: opening the gate is a deliberate, separate step.
if ($manifest.version -eq $version) {
    Write-Host ("  ok  updates/latest.json publishes {0} (clientUpdate {1})" -f $manifest.version, $manifest.clientUpdate)
}
else {
    Write-Host ("  --  updates/latest.json still publishes {0}; {1} is not released yet." -f $manifest.version, $version) -ForegroundColor Yellow
    Write-Host '      That is expected until the GitHub release exists. See docs/UPDATES.md.'
}

Write-Host ''
if ($problems.Count -eq 0) {
    Write-Host "Everything agrees with $version." -ForegroundColor Green
    exit 0
}

Write-Host ("{0} problem(s):" -f $problems.Count) -ForegroundColor Red
foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Red }
exit 1
