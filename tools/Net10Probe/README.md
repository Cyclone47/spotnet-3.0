# .NET 10 migration trial

Run from the repository root:

```powershell
./tools/Test-Net10Migration.ps1 -DotnetPath ./artifacts/net10-trial/sdk/dotnet.exe
```

Requires a .NET 10 SDK. The script runs the complete suite, publishes Spotnet
with the Windows x64 runtime included, and publishes/runs a separate dependency
probe. It writes timestamped logs and TRX results under `artifacts/net10-trial`.
The normal application target is .NET 10; `SpotnetTrialFramework` can override
the target for compatibility testing. Use `Build-Net10Setup.ps1 -Release` for the release filename.

## Findings, 2026-09-05

- SDK 10.0.400 / bundled runtime 10.0.11: all 444 tests passed, including
  nine JSON-cache regression cases added after the initial 435-test trial.
- Test execution was about 3 seconds; repeat full test commands including
  restore/build checks took 6.9–7.3 seconds. Cold compilation is additional.
- Self-contained Spotnet publish succeeded. Core, Desktop and ASP.NET runtimes
  are included. The diagnostic executable verified that its core runtime was
  actually loaded from its own published folder.
- Published native SQLite and WPF dark-theme loading passed.
- The initial FileCache.Signed 2.2.0 probe failed on writing with PlatformNotSupportedException:
  BinaryFormatter has been removed. The same disk-cache round trip passed on
  .NET 8.0.30. This dependency and ObjectBinder have now been removed.
- JsonSpotCache uses a versioned JSON format containing only the public data
  fields of SpotEx and nested models, with no polymorphic type metadata or UI
  property evaluation. Hashed filenames, atomic replacement, a 50 MiB budget,
  an 8 MiB entry limit and cache-miss behavior on corrupt/inaccessible files
  protect normal retrieval. Partial updates retain cached body/image content.
- Legacy binary caches are ignored and left untouched. New entries go under
  `Cache/Json-v1`; the application refetches old details/images as needed.
- The published probe now passes bundled runtime, SQLite, WPF theme and JSON
  cache round-trip checks. Failures still stop the migration script.

Build a test installer with `./tools/Build-Net10Setup.ps1`. It produces
`artifacts/installer/Spotnet-3.0-x64-Setup_net10.exe`, runs the full suite and
published probe, and does not write a release/update manifest. The installer
uses the usual Spotnet 3.0 application/profile identity (it is a test update,
not a separate side-by-side product). Upgrades preserve existing databases in
place: no automatic full-profile backup is created, to avoid exhausting disk
space on large databases. The SelfContained compiler flag omits
the separate .NET Desktop Runtime bootstrapper and prerequisite step.

The probe does not start Spotnet.App or touch a real user profile. It creates
only a plain WPF Application to register pack resources and isolated diagnostic
cache directories. It does not verify the full application startup, WebView2,
VLC playback, real NNTP downloads or tray behavior.
The Dutch installer smoke test passed fresh installation, bundled payload
checks, upgrade profile/previous-backup retention, shortcut replacement,
uninstall retention and explicit synthetic-profile removal. This used an
isolated test profile, not the real user profile.
Full application checks remain release acceptance checks, ideally on a clean Windows VM without
installed .NET runtimes. WebView2 remains a separate prerequisite. The app's
uncompressed self-contained trial directory is about 506 MiB (including native
media libraries); this is not the compressed installer size.

## Test cadence

Keep the full suite as the default after a coherent code change and before
merging/releasing: seven seconds is too little to justify a second maintained
light suite. For a tight edit/debug loop, xUnit class filters are sufficient:

```powershell
dotnet test src/Spotnet/Spotnet.Tests/Spotnet.Tests.csproj -c Release --filter 'FullyQualifiedName~SpotnetRemoteTests'
```

Use the full suite after changes to shared code, dependencies or framework
targets. Run self-contained publishing and installation/update smoke tests
for packaging changes and releases. Do not rerun the whole migration script
after every small UI or text edit. `--no-build` is only safe when the matching
configuration/framework has already been rebuilt after the latest change.

Microsoft references:
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/10
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/9.0/binaryformatter-removal
