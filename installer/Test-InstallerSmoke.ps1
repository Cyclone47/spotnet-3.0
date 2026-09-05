[CmdletBinding()]
param(
    [string]$TestRoot,
    [string]$InstallerPath,
    [ValidateSet('english', 'dutch')]
    [string]$Language = 'english',
    [ValidateRange(-1, 3)]
    [int]$ClassicMode = -1
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $TestRoot) { $TestRoot = Join-Path $repoRoot 'artifacts\installer-smoke' }
$testRoot = [IO.Path]::GetFullPath($TestRoot)
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + '\'
if (-not $testRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke-test root must be under repository artifacts.' }
$testInstaller = if ($InstallerPath) { [IO.Path]::GetFullPath($InstallerPath) } else { Join-Path $repoRoot 'artifacts\installer\Spotnet-3.0-x64-Setup-smoke.exe' }
if (Get-Process Spotnet -ErrorAction SilentlyContinue) { throw 'Close Spotnet before the smoke test. This test never closes the real application.' }
if (-not (Test-Path -LiteralPath $testInstaller)) { throw 'Compile Spotnet3.iss with /DSmokeTestRoot matching -TestRoot first.' }
if (Test-Path -LiteralPath $testRoot) { throw 'Smoke-test directory already exists. Retain or relocate the previous test artifacts before a fresh run.' }
New-Item -ItemType Directory -Path $testRoot | Out-Null
$appRoot = Join-Path $testRoot 'App'
$profileRoot = Join-Path $testRoot 'Profile\Data'
$desktopRoot = Join-Path $testRoot 'Desktop'
$programsRoot = Join-Path $testRoot 'Programs'
function Write-TestLink([string]$Path, [string]$Target, [string]$Arguments = '') {
    New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $Target
    $link.Arguments = $Arguments
    $link.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
function Assert-TestLink([string]$Path, [string]$Target) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Missing shortcut $Path" }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $actual = $link.TargetPath
    $arguments = $link.Arguments
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    if ($actual -ne $Target -or $arguments -ne '') { throw "Incorrect shortcut target/arguments: $Path" }
}
function Invoke-SmokeSetup([string]$LogName) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/FRESH=1', ('/LANG=' + $Language), ('/DIR="' + $appRoot + '"'), ('/LOG="' + (Join-Path $testRoot $LogName) + '"'))
    if ($ClassicMode -ge 0) { $arguments += '/SMOKECLASSICMODE=' + $ClassicMode }
    $process = Start-Process -FilePath $testInstaller -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Smoke setup failed ($($process.ExitCode)); inspect $LogName." }
}
if ($ClassicMode -ge 0) {
    # A real version resource exercises discovery; the historical executable is never run.
    $classicExe = Join-Path $testRoot 'Local\Spotnet\Spotnet.exe'
    $classicData = Join-Path $testRoot 'Local\Spotnet\Data'
    New-Item -ItemType Directory -Force -Path $classicData | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'spotnet-2.0.0.284-binary\Spotnet.exe') -Destination $classicExe
    $sourceDb = Join-Path $classicData 'spots.dbs'
    'synthetic-profile-for-copy-verification' | Set-Content -LiteralPath $sourceDb
    '<Servers><Server Type="Download" Server="test.invalid" /><Server Type="Header" /><Server Type="Upload" /></Servers>' | Set-Content -LiteralPath (Join-Path $classicData 'servers.xml')
    New-Item -ItemType Directory -Path (Join-Path $classicData 'Cache') | Out-Null
    'excluded-cache' | Set-Content -LiteralPath (Join-Path $classicData 'Cache\keep.txt')
    $sourceHash = (Get-FileHash -LiteralPath $sourceDb).Hash
    $classicHashes = @{}
    foreach ($shellRoot in @($desktopRoot, $programsRoot)) {
        $classicLink = Join-Path $shellRoot 'Spotnet.lnk'
        Write-TestLink $classicLink $classicExe
        $classicHashes[$classicLink] = (Get-FileHash -LiteralPath $classicLink).Hash
    }
    Invoke-SmokeSetup 'classic-install.log'
    $copiedDb = Join-Path $profileRoot 'spots.dbs'
    if ($ClassicMode -le 1) {
        if ((Get-FileHash -LiteralPath $copiedDb).Hash -ne $sourceHash) { throw 'Migration copy differs from its source.' }
    } elseif (Test-Path -LiteralPath $copiedDb) { throw 'Clean mode imported Classic data.' }
    if ($ClassicMode -eq 0) {
        if (Test-Path -LiteralPath $sourceDb) { throw 'Move mode retained the migrated source file.' }
        if (Test-Path -LiteralPath (Join-Path $testRoot 'Profile\classic-move.xml')) { throw 'Move was not finalized.' }
    } elseif ((Get-FileHash -LiteralPath $sourceDb).Hash -ne $sourceHash) { throw 'Non-move mode changed the Classic profile.' }
    if (-not (Test-Path -LiteralPath $classicExe) -or -not (Test-Path -LiteralPath (Join-Path $classicData 'Cache\keep.txt'))) { throw 'Excluded Classic files were removed.' }
    $alongside = $ClassicMode -in @(1, 2)
    foreach ($shellRoot in @($desktopRoot, $programsRoot)) {
        $classicLink = Join-Path $shellRoot 'Spotnet.lnk'
        if ($alongside) {
            if ((Get-FileHash -LiteralPath $classicLink).Hash -ne $classicHashes[$classicLink]) { throw 'Alongside changed a Classic shortcut.' }
            Assert-TestLink (Join-Path $shellRoot 'Spotnet 3.0.lnk') (Join-Path $appRoot 'Spotnet.exe')
        } else {
            Assert-TestLink $classicLink (Join-Path $appRoot 'Spotnet.exe')
            if (Test-Path -LiteralPath (Join-Path $shellRoot 'Spotnet 3.0.lnk')) { throw 'Replace created a versioned shortcut.' }
        }
    }
    Invoke-SmokeSetup 'classic-upgrade.log'
    foreach ($shellRoot in @($desktopRoot, $programsRoot)) {
        if ($alongside) {
            $classicLink = Join-Path $shellRoot 'Spotnet.lnk'
            if ((Get-FileHash -LiteralPath $classicLink).Hash -ne $classicHashes[$classicLink]) { throw 'Upgrade forgot alongside mode.' }
            Assert-TestLink (Join-Path $shellRoot 'Spotnet 3.0.lnk') (Join-Path $appRoot 'Spotnet.exe')
        } else { Assert-TestLink (Join-Path $shellRoot 'Spotnet.lnk') (Join-Path $appRoot 'Spotnet.exe') }
    }
    $uninstaller = Join-Path $appRoot 'unins000.exe'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/REMOVEPERSONALDATA=1') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)" }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'Profile')) { throw 'Explicit removal retained the synthetic profile.' }
    foreach ($classicLink in $classicHashes.Keys) {
        if ((Get-FileHash -LiteralPath $classicLink).Hash -ne $classicHashes[$classicLink]) { throw 'Classic shortcut was not preserved/restored.' }
    }
    Write-Host "PASS ($Language, Classic mode $ClassicMode): detection, copy/move/clean, shortcut names, upgrade mode retention, opt-in profile deletion. Synthetic profile deleted; real data untouched."
    return
}
Invoke-SmokeSetup 'fresh.log'
# nl\Spotnet.resources.dll is listed because its absence is silent: the app falls back to the
# neutral English table and simply runs in the wrong language, which is how it shipped unnoticed.
$runtimeConfig = Get-Content (Join-Path $appRoot 'Spotnet.runtimeconfig.json') -Raw | ConvertFrom-Json
$nativePayload = @('runtimes\win-x64\native\WebView2Loader.dll', 'runtimes\win-x64\native\SQLite.Interop.dll')
if ($runtimeConfig.runtimeOptions.PSObject.Properties['includedFrameworks']) {
    $nativePayload = @('WebView2Loader.dll', 'SQLite.Interop.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'Microsoft.AspNetCore.dll')
}
foreach ($required in @('Spotnet.exe', 'Spotnet.dll', 'Spotnet.runtimeconfig.json', 'Spotnet.install', 'NLog.config', 'libvlc\win-x64\libvlc.dll', 'Data\TabThemes', 'nl\Spotnet.resources.dll') + $nativePayload) {
    if (-not (Test-Path -LiteralPath (Join-Path $appRoot $required))) { throw "Missing payload: $required" }
}
if (-not (Test-Path -LiteralPath (Join-Path $profileRoot 'profile.ready'))) { throw 'Fresh profile was not initialized.' }
# Setup seeds the language it ran in, so a Dutch install starts Spotnet in Dutch.
$expectedLanguage = if ($Language -eq 'dutch') { 'nl' } else { 'en' }
$seeded = Select-Xml -Path (Join-Path $profileRoot 'user.config') -XPath "/configuration/userSettings/Spotnet.Properties.Settings/setting[@name='UserLanguage']/value"
if (-not $seeded -or $seeded.Node.InnerText -ne $expectedLanguage) { throw "Setup did not seed UserLanguage=$expectedLanguage into the new profile." }
$freshLinks = @((Join-Path $desktopRoot 'Spotnet.lnk'), (Join-Path $programsRoot 'Spotnet.lnk'))
foreach ($link in $freshLinks) { Assert-TestLink $link (Join-Path $appRoot 'Spotnet.exe') }
# Seed legacy and earlier 3.0 links only inside the synthetic shell folders.
$oldLink = Join-Path $desktopRoot 'My old Spotnet.lnk'
$currentLink = Join-Path $desktopRoot 'Spotnet 3.0.lnk'
$squirrelLink = Join-Path $programsRoot 'Spotnet\Spotnet 2.0.lnk'
$unrelatedLink = Join-Path $desktopRoot 'Unrelated.lnk'
Write-TestLink $oldLink (Join-Path $testRoot 'Legacy\Spotnet.exe')
Write-TestLink $currentLink (Join-Path $testRoot 'Previous3\Spotnet.exe')
Write-TestLink $squirrelLink (Join-Path $testRoot 'Spotnet\Update.exe') '--processStart Spotnet.exe'
Write-TestLink $unrelatedLink (Join-Path $testRoot 'Other.exe')
$originalHashes = @{}
foreach ($link in @($oldLink, $currentLink, $squirrelLink, $unrelatedLink)) { $originalHashes[$link] = (Get-FileHash -LiteralPath $link).Hash }
$fixture = Join-Path $profileRoot 'smoke-personal-data.txt'
'preserve-this-test-fixture' | Set-Content -LiteralPath $fixture
$fixtureHash = (Get-FileHash -LiteralPath $fixture).Hash
# Existing profiles are preserved in place; upgrades do not duplicate them.
# Seed an older backup to verify that upgrade/uninstall preserve it as well.
$backupFixture = Join-Path $testRoot 'Profile\Backups\previous-backup'
New-Item -ItemType Directory -Force -Path $backupFixture | Out-Null
Copy-Item -LiteralPath $fixture -Destination $backupFixture
# Stand in for a binary an earlier layout left behind. The one that mattered was
# x64\SQLite.Interop.dll: beside the copy under runtimes it loaded a second SQLite into
# the process, and the first query corrupted the heap. Setup has to clear these.
$staleDirectory = Join-Path $appRoot 'x64'
New-Item -ItemType Directory -Force -Path $staleDirectory | Out-Null
'not a real library' | Set-Content -LiteralPath (Join-Path $staleDirectory 'SQLite.Interop.dll')
$staleFile = Join-Path $appRoot 'GalaSoft.MvvmLight.dll'
'retired dependency' | Set-Content -LiteralPath $staleFile
Invoke-SmokeSetup 'upgrade.log'
if (Test-Path -LiteralPath $staleDirectory) { throw 'Upgrade kept a stale directory from an earlier layout.' }
if (Test-Path -LiteralPath $staleFile) { throw 'Upgrade kept a retired dependency.' }
foreach ($link in @($oldLink, $squirrelLink) + $freshLinks) { Assert-TestLink $link (Join-Path $appRoot 'Spotnet.exe') }
if (Test-Path -LiteralPath $currentLink) { throw 'Replace retained the obsolete Spotnet 3.0 shortcut name.' }
if ((Get-ChildItem -LiteralPath $desktopRoot -Filter '*.lnk').Count -ne 3) { throw 'Duplicate desktop launcher created.' }
if ((Get-FileHash -LiteralPath $unrelatedLink).Hash -ne $originalHashes[$unrelatedLink]) { throw 'Unrelated shortcut changed.' }
if ((Get-FileHash -LiteralPath $fixture).Hash -ne $fixtureHash) { throw 'Upgrade changed existing profile data.' }
$backups = @(Get-ChildItem (Join-Path $testRoot 'Profile\Backups') -Directory)
if ($backups.Count -ne 1 -or $backups[0].Name -ne 'previous-backup') { throw 'Upgrade must preserve existing backups without duplicating the profile.' }
if ((Get-FileHash -LiteralPath (Join-Path $backups[0].FullName 'smoke-personal-data.txt')).Hash -ne $fixtureHash) { throw 'Backup does not match.' }
$uninstaller = Join-Path $appRoot 'unins000.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Uninstall failed: $($process.ExitCode)" }
if (Test-Path -LiteralPath (Join-Path $appRoot 'Spotnet.exe')) { throw 'Application was not uninstalled.' }
if ((Get-FileHash -LiteralPath $fixture).Hash -ne $fixtureHash) { throw 'Uninstall altered profile data.' }
if (-not (Test-Path -LiteralPath $backups[0].FullName)) { throw 'Uninstall removed the backup.' }
foreach ($link in $freshLinks) { if (Test-Path -LiteralPath $link) { throw 'Installer-created shortcut survived uninstall.' } }
foreach ($link in $originalHashes.Keys) { if ((Get-FileHash -LiteralPath $link).Hash -ne $originalHashes[$link]) { throw "Original shortcut was not restored: $link" } }

# Reinstall over the retained profile, then exercise the explicit permanent-removal option.
Invoke-SmokeSetup 'reinstall-for-profile-removal.log'
if (-not (Test-Path -LiteralPath $fixture)) { throw 'Reinstall did not retain the existing profile.' }
$uninstaller = Join-Path $appRoot 'unins000.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/REMOVEPERSONALDATA=1', ('/LOG="' + (Join-Path $testRoot 'uninstall-remove-profile.log') + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Profile-removing uninstall failed: $($process.ExitCode)" }
if (Test-Path -LiteralPath (Join-Path $appRoot 'Spotnet.exe')) { throw 'Application survived profile-removing uninstall.' }
if (Test-Path -LiteralPath (Join-Path $testRoot 'Profile')) { throw 'Explicit profile-removing uninstall retained personal data.' }
foreach ($link in $originalHashes.Keys) { if ((Get-FileHash -LiteralPath $link).Hash -ne $originalHashes[$link]) { throw "Original shortcut was not restored before profile removal: $link" } }

Write-Host "PASS ($Language): fresh install, payloads, old/current/Squirrel shortcut replacement, no duplicates, unrelated links preserved, upgrade profile/backup retention, default uninstall retention, and opt-in permanent profile removal."
Write-Host "Logs retained in $testRoot. The synthetic profile was permanently deleted; real installations and profiles were untouched."
