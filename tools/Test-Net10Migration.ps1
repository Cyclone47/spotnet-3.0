[CmdletBinding()]
param([string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $DotnetPath) { $DotnetPath = Join-Path $repo 'artifacts/net10-trial/sdk/dotnet.exe' }
if (-not (Test-Path -LiteralPath $DotnetPath)) { throw 'Pass -DotnetPath pointing to a .NET 10 SDK dotnet.exe.' }
$root = Join-Path $repo ('artifacts/net10-trial/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $root | Out-Null
function Invoke-Stage([string]$Name, [string[]]$Arguments) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $DotnetPath @Arguments *> (Join-Path $root "$Name.log")
    $code = $LASTEXITCODE
    $timer.Stop()
    Write-Host "$Name : exit $code, $($timer.Elapsed.TotalSeconds.ToString('F1')) seconds"
    if ($code -ne 0) { throw "Failed $Name; inspect $root/$Name.log" }
}
Push-Location $repo
try {
    Invoke-Stage 'tests' @('test', 'src/Spotnet/Spotnet.Tests/Spotnet.Tests.csproj', '-c', 'Release', '-p:SpotnetTrialFramework=net10.0-windows', '--logger', 'trx;LogFileName=net10.trx', '--results-directory', $root)
    Invoke-Stage 'publish' @('publish', 'src/Spotnet/Spotnet/Spotnet.csproj', '-c', 'Release', '-p:SpotnetTrialFramework=net10.0-windows', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', "$root/publish")
    Invoke-Stage 'probe-build' @('publish', 'tools/Net10Probe/Net10Probe.csproj', '-c', 'Release', '-p:SpotnetTrialFramework=net10.0-windows', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-o', "$root/probe")
    & "$root/probe/Net10Probe.exe" *> "$root/probe.log"
    $code = $LASTEXITCODE
    Get-Content "$root/probe.log"
    if ($code -ne 0) { throw "Migration probe failed; inspect $root/probe.log. This is not a release-ready build." }
    Write-Host "Migration checks passed. Outputs: $root"
} finally { Pop-Location }
