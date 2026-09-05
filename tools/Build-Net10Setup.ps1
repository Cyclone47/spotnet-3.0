[CmdletBinding()]
param([string]$DotnetPath, [string]$CompilerPath, [switch]$Release)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $DotnetPath) {
    $DotnetPath = Join-Path $repo 'artifacts/net10-trial/sdk/dotnet.exe'
    if (-not (Test-Path -LiteralPath $DotnetPath)) { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
if (-not $CompilerPath) { $CompilerPath = Join-Path $repo 'artifacts/installer-tools/InnoSetup7/ISCC.exe' }
foreach ($tool in @($DotnetPath, $CompilerPath)) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Missing build tool: $tool" }
}
$output = Join-Path $repo 'artifacts/installer'
$work = Join-Path $output ('net10-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$preview = Join-Path $work 'previews'
$suffix = if ($Release) { '' } else { '_net10' }
$setupName = "Spotnet-3.0-x64-Setup$suffix.exe"
New-Item -ItemType Directory -Force -Path $payload, $preview | Out-Null
function Invoke-Build([string]$Name, [string[]]$Arguments) {
    & $DotnetPath @Arguments *> (Join-Path $work "$Name.log")
    if ($LASTEXITCODE -ne 0) { throw "Failed $Name; see $work/$Name.log" }
    Write-Host "Completed $Name"
}
Push-Location $repo
try {
    Invoke-Build 'tests' @('test', 'src/Spotnet/Spotnet.Tests/Spotnet.Tests.csproj', '-c', 'Release', '-p:SpotnetTrialFramework=net10.0-windows', '--logger', 'trx;LogFileName=net10.trx', '--results-directory', $work)
    Invoke-Build 'publish' @('publish', 'src/Spotnet/Spotnet/Spotnet.csproj', '-c', 'Release', '-p:SpotnetTrialFramework=net10.0-windows', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', $payload)
    # Only version-controlled defaults may enter the installer, never local profile data.
    foreach ($relative in @('Data', 'Resources/ReleaseNotes')) {
        $target = [IO.Path]::GetFullPath((Join-Path $payload $relative))
        $allowed = [IO.Path]::GetFullPath($payload) + '\'
        if (-not $target.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path' }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        $assets = @(git ls-files -- "src/Spotnet/Spotnet/$relative")
        if ($LASTEXITCODE -ne 0 -or $assets.Count -eq 0) { throw "Missing tracked assets: $relative" }
        foreach ($asset in $assets) {
            $dest = Join-Path $payload $asset.Substring('src/Spotnet/Spotnet/'.Length)
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
            Copy-Item -LiteralPath (Join-Path $repo $asset) -Destination $dest
        }
    }
    $config = Get-Content (Join-Path $payload 'Spotnet.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App', 'Microsoft.AspNetCore.App')) {
        if (-not ($config.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $framework -and $_.version -like '10.*' })) { throw "Missing bundled .NET 10 framework: $framework" }
    }
    foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'Microsoft.AspNetCore.dll', 'Spotnet.exe', 'SQLite.Interop.dll', 'nl/Spotnet.resources.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) { throw "Missing payload: $required" }
    }
    Invoke-Build 'probe' @('publish', 'tools/Net10Probe/Net10Probe.csproj', '-c', 'Release', '-p:SpotnetTrialFramework=net10.0-windows', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', "$work/probe")
    & "$work/probe/Net10Probe.exe" *> "$work/probe-results.log"
    if ($LASTEXITCODE -ne 0) { throw "Published dependency probe failed: $work/probe-results.log" }
    Invoke-Build 'helper' @('build', 'tools/Spotnet.SetupHelper/Spotnet.SetupHelper.csproj', '-c', 'Release')
    Invoke-Build 'preview' @('build', 'tools/Spotnet.ThemePreview/Spotnet.ThemePreview.csproj', '-c', 'Release')
    & $DotnetPath './tools/Spotnet.ThemePreview/bin/Release/net10.0-windows/Spotnet.ThemePreview.dll' --output $preview --icons (Join-Path $repo 'src/Spotnet/Spotnet/Data/Filters.v2/Images')
    if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed' }
    $webview = Join-Path $repo 'artifacts/installer-tools/MicrosoftEdgeWebview2Setup.exe'
    $signature = Get-AuthenticodeSignature -LiteralPath $webview
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Invalid WebView2 bootstrapper signature' }
    $arguments = @('/Q', '/DSelfContained', "/DSetupSuffix=$suffix", "/DPayloadDir=$payload", "/DHelperDir=$repo/tools/Spotnet.SetupHelper/bin/Release/net472", "/DWebViewBootstrapper=$webview", "/DOutputDir=$output", "/DPreviewDir=$preview", 'installer/Spotnet3.iss')
    & $CompilerPath @arguments *> (Join-Path $work 'compiler.log')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $work/compiler.log" }
    $setup = Join-Path $output $setupName
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    "$hash  $setupName" | Set-Content -LiteralPath "$setup.sha256" -Encoding ASCII
    Write-Host "Built: $setup"
    Write-Host "Build files: $work"
    Write-Host 'Self-contained .NET 10 build. No release manifest was created.'
} finally { Pop-Location }
