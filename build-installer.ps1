[CmdletBinding()]
param(
    [string]$CompilerPath,
    [switch]$BootstrapCompiler,
    [switch]$SkipBuild,

    # Authenticode signing, opt-in. Give a certificate thumbprint from the current user's
    # store, or a complete command for anything else (HSM, cloud signing service). No
    # password ever passes through this script: a PFX belongs in the certificate store
    # first, and its thumbprint is what comes here.
    [string]$SignThumbprint,
    [string]$SignCommand,
    [string]$SignTimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts\installer'
$toolRoot = Join-Path $repoRoot 'artifacts\installer-tools'
New-Item -ItemType Directory -Force -Path $artifactRoot, $toolRoot | Out-Null

# Reads a PE file's target architecture: 'amd64', 'anycpu' for a managed assembly that
# reports I386 but carries a CLI header, 'x86' for a real 32-bit binary, or the raw
# machine value for anything else.
function Get-BinaryArchitecture([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Missing payload binary: $Path" }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $peOffset = [BitConverter]::ToInt32($bytes, 60)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    if ($machine -eq 0x8664) { return 'amd64' }
    if ($machine -ne 0x14C) { return ('0x{0:X4}' -f $machine) }
    # Data directory 14 is the CLR header. PE32+ puts the directories 112 bytes into the
    # optional header, PE32 96 bytes in.
    $optional = $peOffset + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    $directories = $optional + $(if ($magic -eq 0x20B) { 112 } else { 96 })
    $cliRva = [BitConverter]::ToInt32($bytes, $directories + (14 * 8))
    if ($cliRva -ne 0) { return 'anycpu' }
    return 'x86'
}

function Resolve-SignTool {
    if ($SignToolPath) {
        if (-not (Test-Path -LiteralPath $SignToolPath)) { throw "signtool.exe not found at $SignToolPath." }
        return $SignToolPath
    }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $found = Get-ChildItem -LiteralPath $kits -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like '*\x64' } |
        Sort-Object -Property FullName -Descending |
        Select-Object -First 1
    if (-not $found) { throw 'signtool.exe was not found. Install the Windows SDK signing tools or pass -SignToolPath.' }
    return $found.FullName
}

# The command Inno Setup runs per file, with $f standing in for the file name. The same
# template drives the payload signing below, so both use exactly one code path.
function Get-SignTemplate {
    if ($SignThumbprint -and $SignCommand) {
        throw 'Give either -SignThumbprint or -SignCommand, not both.'
    }
    if ($SignCommand) {
        if ($SignCommand -notmatch '\$f') { throw '-SignCommand must contain $f, which is replaced by the file to sign.' }
        return $SignCommand
    }
    if (-not $SignThumbprint) { return $null }
    if ($SignThumbprint -notmatch '^[0-9A-Fa-f]{40}$') { throw '-SignThumbprint must be a 40-character SHA1 certificate thumbprint.' }
    $tool = Resolve-SignTool
    # RFC 3161 timestamping, so signatures outlive the certificate.
    return ('"{0}" sign /fd sha256 /td sha256 /tr "{1}" /sha1 {2} $f' -f $tool, $SignTimestampUrl, $SignThumbprint)
}

function Invoke-SignFile([string]$Template, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Cannot sign a file that does not exist: $Path" }
    $command = $Template.Replace('$f', '"' + (Resolve-Path -LiteralPath $Path).Path + '"')
    & cmd.exe /c $command
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $Path (exit $LASTEXITCODE)." }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -eq 'NotSigned') { throw "Signing reported success but $Path carries no signature." }
}

function Get-SignedDownload([string]$Uri, [string]$Path, [string]$Publisher) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "Downloading $(Split-Path $Path -Leaf)..."
        Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $Publisher) {
        throw "Publisher signature validation failed for $Path. File will not be executed or packaged."
    }
}

if (-not $CompilerPath) {
    $candidates = @(
        (Join-Path $toolRoot 'InnoSetup7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { $CompilerPath = $candidate; break }
    }
}
if (-not $CompilerPath -and $BootstrapCompiler) {
    $download = Join-Path $toolRoot 'innosetup-7.1.0-x64.exe'
    Get-SignedDownload 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' $download 'CN=Pyrsys B\.V\.'
    $compilerDirectory = Join-Path $toolRoot 'InnoSetup7'
    $process = Start-Process -FilePath $download -ArgumentList @('/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $compilerDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Inno Setup bootstrap failed: $($process.ExitCode)" }
    $CompilerPath = Join-Path $compilerDirectory 'ISCC.exe'
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 7.1+, supply -CompilerPath, or use -BootstrapCompiler for a verified portable compiler under artifacts/.'
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        & dotnet build src/Spotnet/Spotnet.sln -c Release -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
        & dotnet test src/Spotnet/Spotnet.Tests/Spotnet.Tests.csproj -c Release --no-build -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed; refusing to package.' }
    }
    & dotnet build tools/Spotnet.SetupHelper/Spotnet.SetupHelper.csproj -c Release -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Migration helper build failed.' }
    $appOutput = Join-Path $artifactRoot ('publish-' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish src/Spotnet/Spotnet/Spotnet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $appOutput
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained application publish failed.' }
    $helperOutput = Join-Path $repoRoot 'tools\Spotnet.SetupHelper\bin\Release\net472'
    # Nothing 32-bit or ARM may reach an x64 package. Native payloads have to be AMD64
    # outright; a managed assembly may also be AnyCPU, which is what the platform-neutral
    # libraries are built as so the future macOS client can share them. AnyCPU and a
    # 32-bit native DLL both report I386, so the CLI header is what tells them apart.
    foreach ($binary in @('Spotnet.exe', 'WebView2Loader.dll', 'SQLite.Interop.dll', 'coreclr.dll', 'libvlc\win-x64\libvlc.dll')) {
        if ((Get-BinaryArchitecture (Join-Path $appOutput $binary)) -ne 'amd64') { throw "Not an AMD64 binary: $binary" }
    }
    foreach ($binary in @('Spotnet.dll', 'Spotnet.Enc.dll')) {
        $architecture = Get-BinaryArchitecture (Join-Path $appOutput $binary)
        if ($architecture -notin @('amd64', 'anycpu')) { throw "Not an AMD64 or AnyCPU assembly: $binary ($architecture)" }
    }
    # A fresh staging directory prevents stale DLLs or personal runtime data entering the package.
    $payload = Join-Path $artifactRoot ('payload-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $payload | Out-Null
    $trackedAssets = @(git ls-files -- src/Spotnet/Spotnet/Data src/Spotnet/Spotnet/Resources/ReleaseNotes)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked application assets.' }
    foreach ($file in Get-ChildItem -LiteralPath $appOutput -File) {
        if ($file.Extension -in @('.dll', '.exe', '.json') -or $file.Name -in @('Spotnet.dll.config', 'NLog.config')) {
            if ($file.Name -match '^(Awesomium|awesomium|Meta\.Vlc|Ionic\.Zip|Pri\.LongPath)' -or $file.Name -eq 'Squirrel.exe') { continue }
            Copy-Item -LiteralPath $file.FullName -Destination $payload
        }
    }
    # Windows assets under runtimes/. Both the native ones (runtimes/win-x64/native) and
    # the managed ones a package ships per RID (runtimes/win/lib/...), which the host
    # resolves through deps.json and will not fall back to the root copy for.
    $runtimeDirectories = @(Get-ChildItem -LiteralPath (Join-Path $appOutput 'runtimes') -Directory |
        Where-Object { $_.Name -like 'win*' } | ForEach-Object { 'runtimes\' + $_.Name })
    foreach ($directory in $runtimeDirectories + @('libvlc\win-x64')) {
        $target = Join-Path $payload (Split-Path $directory -Parent)
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item -LiteralPath (Join-Path $appOutput $directory) -Destination $target -Recurse
    }
    foreach ($asset in $trackedAssets) {
        $relative = $asset.Substring('src/Spotnet/Spotnet/'.Length)
        $destination = Join-Path $payload $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $asset) -Destination $destination
    }
    # Everything the host will look for has to be in the payload. The copy rules above are
    # hand-written, and they once missed the three assemblies a package ships per RID: the
    # host resolves those by their deps.json path and never falls back to the copy in the
    # root, so opening a spot died on a System.Runtime.Caching it could see but not use.
    $deps = Get-Content -LiteralPath (Join-Path $payload 'Spotnet.deps.json') -Raw | ConvertFrom-Json
    $missingAssets = @()
    foreach ($target in $deps.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            # lib/ assets are flattened into the root, so only the file name has to match.
            foreach ($section in @('runtime', 'native')) {
                # Strict mode makes a missing property an error, so ask for it by name.
                $property = $library.Value.PSObject.Properties[$section]
                if (-not $property) { continue }
                $assets = $property.Value
                if (-not $assets) { continue }
                foreach ($asset in $assets.PSObject.Properties) {
                    $name = Split-Path ($asset.Name -replace '/', '\\') -Leaf
                    if ($name -and -not (Test-Path -LiteralPath (Join-Path $payload $name))) { $missingAssets += $asset.Name }
                }
            }
            # RID-specific assets keep their runtimes/... path and are looked up there.
            $runtimeTargets = $library.Value.PSObject.Properties['runtimeTargets']
            if (-not $runtimeTargets -or -not $runtimeTargets.Value) { continue }
            foreach ($asset in $runtimeTargets.Value.PSObject.Properties) {
                # Only the architectures this build ships. x86 and arm64 assets are
                # listed by the packages but deliberately left out of an x64 release.
                if ($asset.Value.rid -notin @('win', 'win-x64')) { continue }
                $relative = $asset.Name -replace '/', '\\'
                if (-not (Test-Path -LiteralPath (Join-Path $payload $relative))) { $missingAssets += $asset.Name }
            }
        }
    }
    $missingAssets = $missingAssets | Sort-Object -Unique
    if ($missingAssets) { throw "The payload is missing assets the runtime resolves through deps.json:`n  " + ($missingAssets -join "`n  ") }

    # Preserve satellite resource assemblies, if generated by the application build.
    foreach ($culture in @('nl', 'en', 'en-US')) {
        $cultureDir = Join-Path $appOutput $culture
        if (Test-Path -LiteralPath $cultureDir) { Copy-Item -LiteralPath $cultureDir -Destination $payload -Recurse }
    }
    # Preserve Spotnet Remote web assets
    $webDir = Join-Path $appOutput 'Web'
    if (Test-Path -LiteralPath $webDir) {
        Copy-Item -LiteralPath $webDir -Destination (Join-Path $payload 'Web') -Recurse
    }
    $webview = Join-Path $toolRoot 'MicrosoftEdgeWebview2Setup.exe'
    Get-SignedDownload 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' $webview 'O=Microsoft Corporation'
    # .NET 10 is included in the published payload, not installed as a shared runtime.

    # Render the wizard's style previews from the application's own theme dictionaries,
    # so a palette change shows up in Setup instead of leaving a stale picture behind.
    $previewProject = Join-Path $repoRoot 'tools\Spotnet.ThemePreview\Spotnet.ThemePreview.csproj'
    $previewDir = Join-Path $artifactRoot 'previews'
    New-Item -ItemType Directory -Force -Path $previewDir | Out-Null
    & dotnet build $previewProject -c Release -v quiet --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Building the style preview renderer failed.' }
    $previewExe = Join-Path $repoRoot 'tools\Spotnet.ThemePreview\bin\Release\net10.0-windows\Spotnet.ThemePreview.exe'
    $filterIcons = Join-Path $repoRoot 'src\Spotnet\Spotnet\Data\Filters.v2\Images'
    & $previewExe --output $previewDir --icons $filterIcons
    if ($LASTEXITCODE -ne 0) { throw 'Rendering the style previews failed.' }
    foreach ($tile in @('style-modern-light.bmp', 'style-modern-dark.bmp', 'style-classic.bmp')) {
        if (-not (Test-Path -LiteralPath (Join-Path $previewDir $tile))) { throw "The style preview $tile was not produced." }
    }

    # Sign what this repository produces. The third-party assemblies arrive signed by
    # their own publishers and are left alone.
    $signTemplate = Get-SignTemplate
    $compilerArguments = @('/Q', '/DSelfContained', ('/DPayloadDir=' + $payload), ('/DHelperDir=' + $helperOutput), ('/DWebViewBootstrapper=' + $webview), ('/DOutputDir=' + $artifactRoot), ('/DPreviewDir=' + $previewDir))
    if ($signTemplate) {
        $ourBinaries = @('Spotnet.exe', 'Spotnet.Enc.dll') |
            ForEach-Object { Join-Path $payload $_ }
        $ourBinaries += Get-ChildItem -LiteralPath $payload -Filter 'Spotnet.resources.dll' -Recurse |
            Select-Object -ExpandProperty FullName
        $ourBinaries += (Join-Path $helperOutput 'Spotnet.SetupHelper.exe')
        foreach ($binary in $ourBinaries) {
            Write-Host "Signing $(Split-Path $binary -Leaf)..."
            Invoke-SignFile $signTemplate $binary
        }
        # Inno signs the installer and the uninstaller with the same named tool.
        $compilerArguments += '/DSignSetup'
        $compilerArguments += ('/Sspotnet=' + $signTemplate)
    }

    & $CompilerPath @compilerArguments installer/Spotnet3.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setupFile = Join-Path $artifactRoot 'Spotnet-3.0-x64-Setup.exe'
    $hash = (Get-FileHash -LiteralPath $setupFile -Algorithm SHA256).Hash
    "$hash  Spotnet-3.0-x64-Setup.exe" | Set-Content -LiteralPath ($setupFile + '.sha256') -Encoding ASCII
    Write-Host "Built: $setupFile"
    Write-Host "SHA256: $hash"

    # The update manifest for this build, ready to be copied over updates/latest.json.
    # clientUpdate stays 0: releasing to clients is a separate, deliberate edit, so a build
    # can be uploaded and tried out before anyone is offered it. See docs/updates.md.
    $version = (Get-Item -LiteralPath (Join-Path $payload 'Spotnet.exe')).VersionInfo.FileVersion
    $manifest = [ordered]@{
        schema          = 1
        clientUpdate    = 0
        version         = $version
        minimumVersion  = '3.0.0.0'
        forced          = 0
        url             = "https://github.com/Cyclone47/spotnet-3.0/releases/download/v$version/Spotnet-3.0-x64-Setup.exe"
        size            = (Get-Item -LiteralPath $setupFile).Length
        sha256          = $hash.ToLowerInvariant()
        releaseNotesUrl = "https://github.com/Cyclone47/spotnet-3.0/releases/tag/v$version"
    }
    $manifestFile = Join-Path $artifactRoot 'latest.json'
    # No byte-order mark: this file is copied into the repository as-is and read back by a
    # JSON parser, and Set-Content -Encoding utf8 writes a BOM on Windows PowerShell.
    [IO.File]::WriteAllText($manifestFile, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    Write-Host "Update manifest: $manifestFile (clientUpdate 0; set it to 1 to release)"
    if ($signTemplate) {
        $signature = Get-AuthenticodeSignature -LiteralPath $setupFile
        if ($signature.Status -eq 'NotSigned') { throw 'The installer was built but carries no signature.' }
        Write-Host ("Signed by: " + $signature.SignerCertificate.Subject)
        Write-Host ("Signature status: " + $signature.Status)
        if ($signature.Status -ne 'Valid') {
            Write-Warning 'The signature is present but does not chain to a trusted root on this machine. That is expected for a test certificate; a real publisher certificate will validate.'
        }
    }
    else {
        Write-Warning 'The Spotnet installer is unsigned. Pass -SignThumbprint or -SignCommand to sign it. No installer was run against your profile.'
    }
}
finally { Pop-Location }
