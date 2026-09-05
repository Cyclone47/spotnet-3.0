# Spotnet 3.0

*[Nederlandse versie](README.md)*

A modernized Windows Usenet client, built around the familiar Spotnet experience: browse
spots, search a local index, read and post comments, manage NZB downloads and preview
media — in one desktop application.

Spotnet is a *client*, not a Usenet service — you supply access to a news server.

| | |
| --- | --- |
| **Version** | 3.0.8.0 |
| **Platform** | Windows 10/11 x64 · C# / WPF · .NET 10 (shipped inside Setup) |
| **Tests** | 470 passing on the x64 Release host |
| **Based on** | Spotnet 2.0 (build 2.0.0.284), with Spotnet 1.8.1 as reference |

---

## Download

**[Download Spotnet 3.0.8.0 Setup](https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.8.0/Spotnet-3.0-x64-Setup.exe)**
· [Release notes and SHA-256](https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.8.0)

Setup handles both fresh installs and upgrades from a compatible Spotnet 2.x profile. It
closes Spotnet safely, copies your selected profile into a separate 3.0 data folder, and
updates your existing shortcuts. Old application files and the source profile are left
untouched; active download queues are not imported.

**.NET 10 ships inside Setup**, in Spotnet's own installation folder. No separate .NET
installation is needed and it does not appear as its own entry under *Installed apps*. If
Microsoft Edge WebView2 is missing, Setup fetches it from Microsoft.

> **The installer is unsigned.** Windows may show an unknown-publisher warning. No
> publisher certificate exists for this project yet.

Read the [installation and migration guide](docs/INSTALLER.md) before upgrading.

---

## What you can do with it

### Spots and downloads

The familiar WPF shell with spot browsing, categories, filters, favorites and comments;
local SQLite databases; the Phuse NNTP engine with the Spotnet metadata and signature
protocol; the integrated multi-connection downloader; and media preview with docking and
tab behavior.

Search runs on **SQLite FTS5**, with the index built once on first start. Spot pages and
comments render in **Edge WebView2** throughout.

Three styles are available — **Classic**, **Modern Light** and **Modern Dark** — chosen
during Setup or from *Edit ▸ Style*.

### Spotnet Remote — run Spotnet from your phone

Spotnet hosts its own web server, so you can drive the application from any phone, tablet
or computer on your network. The page is a PWA with a service worker, so it can be added
to a home screen and used like an app.

- **Pair with a QR code.** Scan the code on your screen; the device receives a pairing
  token, so you never type a password on your phone.
- **Password-only login** — since 3.0.8.0 there is no username — hashed with PBKDF2-SHA256,
  with protection against repeated failed attempts and a list of paired devices.
- **Full control:** search, categories and filters, viewing spots and posters, reading and
  posting comments, managing the download queue, adjusting the speed limit, and triggering
  a Usenet sync by hand.
- **Notifications** from the notification module show up on your phone as well.
- **Found automatically on your network** through a UDP broadcast, so there is no IP
  address to type in.
- **Keep the computer awake:** an optional setting that stops Windows from sleeping while
  Remote is running.

Remote listens on port **8770** by default, with network discovery on UDP port **8771**.
It is off by default and enabled under *Settings ▸ Remote*. Reaching Spotnet from outside
your home means setting up port forwarding yourself — set a strong password before you do.

### Android companion app

Alongside the web page there is an Android app (`nl.spotnet.companion`, Android 8.0 or
newer) that talks to the same Remote server:

- finds Spotnet on your network automatically and pairs through the QR code;
- native Android notifications when a rule matches or a download finishes;
- checks for new notifications in the background, even when the app is closed;
- pull-to-refresh to start a sync straight away;
- viewing and managing the download queue.

The APK (`SpotnetCompanion.apk`) is attached to
[release v3.0.7.0](https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.7.0); the
source lives in [`android/`](android/).

### Notifications and alerts

The notification module lets Spotnet watch for things you care about. It is managed from
the **bell in the title bar**, which shows the unread count and opens the notification
centre.

There are three kinds of rules:

- **Filter alert** — reports new spots matching one of your saved filters.
- **Keyword alert** — reports spots containing given keywords, optionally limited to a
  single category.
- **Download notification** — reports a finished download.

Each rule sets its own check frequency: immediately on every sync, every 15 or 30 minutes,
hourly, every 8 or 24 hours, or a custom interval of at least 5 minutes. Rules can be
switched on and off individually, and tested on the spot to see what they would produce.

Notifications collect in the notification centre with their matching spots, and optionally
appear as Windows notifications in the system tray. Marking as read, deleting individual
items and clearing everything all happen in the same window. The same notifications appear
in Spotnet Remote and in the Android app.

### Automatic updates

An installed Spotnet checks for a newer version itself, offers it together with the
release notes, and lets Setup replace it. The check runs on the splash screen, before the
databases are opened; if the update server is unreachable, Spotnet waits at most three
seconds. Downloads are verified on size and SHA-256 before anything is executed, and an
interrupted download is resumed.

*Help ▸ Check for updates* still works on demand, and `AutoUpdateEnabled` in your profile
turns the periodic check off. See [docs/UPDATES.md](docs/UPDATES.md) for how a release is
published.

---

## Where this project comes from

The goal is keeping Spotnet usable and maintainable: the application source was recovered,
obsolete components replaced and reliability improved — without discarding the existing
workflow or breaking compatibility with the Spotnet network. This is an incremental
modernization, not a from-scratch rewrite.

**Spotnet 3.0** is the name of the modernized application in this repository, not a claim
of an official upstream release. References to 1.8.1 or 2.0 describe the original
versions this was recovered from; the original release package sits in
[`reference/`](reference/). The working notes under `docs/internal/` still call the source
tree `reconstructed/Spotnet2/` — that is the former name of `src/Spotnet/`.

Background: [source provenance](docs/reference/SOURCE_PROVENANCE.md) ·
[original binary inventory](docs/reference/INVENTORY.md).

### The main replacements

| Area | Original | Now |
| --- | --- | --- |
| Platform | x86, constrained by native components | x64 (`Prefer32Bit=false`), .NET 10 |
| Embedded web view | Awesomium / old Chromium integration | **Microsoft Edge WebView2 1.0.3351.48** |
| Media preview | `Meta.Vlc` | **LibVLCSharp.WPF 3.10.1** with **VideoLAN.LibVLC.Windows 3.0.23.1** (x64) |
| SQLite | Loose legacy provider and interop DLLs | **System.Data.SQLite.Core 1.0.119** via NuGet |
| yEnc decoder | Mixed-mode x86 `Spotnet.Enc.dll` | Managed C# `Spotnet.Enc` (x64) |
| ZIP archives | Ionic.Zip / DotNetZip | `System.IO.Compression` behind the path-validated `SafeZip` |
| Other libraries | Loose legacy DLLs | SharpZipLib 1.4.2 · Newtonsoft.Json 13.0.3 · NLog 5.5.1 · HtmlAgilityPack 1.12.4 |

`phpar2.exe`, `UnRAR.exe` and `7za.exe` are still 32-bit helper executables. They run as
separate child processes and do not force Spotnet itself to run 32-bit. This is a Windows
x64 build, not a native ARM64 or cross-platform port — WPF ties it to Windows.

### Reliability and security

The writable database uses **write-ahead logging (WAL)** with `synchronous=NORMAL` instead
of the old `synchronous=OFF`, with a busy timeout and respect for read-only intent.
**Rebuild Database** copies readable records into a fresh database and keeps the original
as a backup — a recovery attempt, not a guarantee. WAL is no substitute for your own
backups.

Beyond that: NNTP requires encryption on TLS connections and validates server
certificates; SQL values and identifiers are parameterized and checked; ZIP extraction
rejects paths escaping the target folder; external XML resolution is disabled. These are
targeted improvements, not proof of a passed security audit.

Details: [database and recovery](docs/DATABASE.md) ·
[NNTP, spot XML and signatures](docs/PROTOCOL.md).

---

## Build from source

You need Windows x64, the **.NET 10 SDK** with the Windows desktop workload, the
**Microsoft Edge WebView2 Evergreen Runtime**, and NuGet access for package restore.

```powershell
dotnet build src/Spotnet/Spotnet.sln -c Release
dotnet test src/Spotnet/Spotnet.Tests/Spotnet.Tests.csproj -c Release --no-build
& "./src/Spotnet/Spotnet/bin/Release/net10.0-windows/Spotnet.exe"
```

Keep the **entire output directory** together. `Spotnet.exe` alone is not a working
distribution: native runtimes, managed dependencies, configuration and resources all
belong with it.

Build the installer with:

```powershell
./build-installer.ps1 -BootstrapCompiler
```

Output: `artifacts/installer/Spotnet-3.0-x64-Setup.exe`. Signing is supported through
`-SignThumbprint <cert>` for a certificate in your own store, or `-SignCommand` for an HSM
or cloud service; the build refuses to package if anything ends up unsigned. Close Spotnet
and back up your configuration and databases before testing a new build against an
existing installation.

More detail: [build and setup guide](docs/BUILDING.md).

### Releasing a new version

The version number lives in one place — `AssemblyInfo.cs` — and everything the user sees
has to follow it. Which places those are, and the order to update them in, is documented
in [docs/VERSIONING.md](docs/VERSIONING.md). To verify:

```powershell
pwsh ./tools/Sync-Version.ps1
```

The regression suite enforces the same thing: bumping the version without carrying the
release notes, README or update feed along makes `VersionConsistencyTests` fail.

---

## Repository layout

```text
build-installer.ps1           Builds the x64 Setup
providers.json                Usenet provider list, fetched by clients on launch
updates/latest.json           Update feed for installed clients

src/Spotnet/
    Spotnet.sln               Main solution
    Spotnet/                  WPF application, XAML, resources and data
    Spotnet.Enc/              Managed yEnc decoder
    Spotnet.Tests/            xUnit regression tests

android/                      Android companion app (Kotlin)
installer/                    Inno Setup script and smoke test
reference/                    The original Spotnet 2.0.0.284 release package
tools/                        Setup helper, theme preview, database tools, build scripts
docs/                         Documentation, release notes and reference material
```

For development, edit `src/Spotnet/` and launch its build output.

---

## Documentation

- [Build and setup](docs/BUILDING.md)
- [Installer, migration and rollback](docs/INSTALLER.md)
- [Updating version numbers](docs/VERSIONING.md)
- [Publishing automatic updates](docs/UPDATES.md)
- [Updating the provider list](docs/PROVIDERS.md)
- [Database schema and recovery](docs/DATABASE.md)
- [NNTP, spot XML and signatures](docs/PROTOCOL.md)
- [Release notes per version](docs/releases/)

Reference material on the versions this was recovered from lives in
[`docs/reference/`](docs/reference/), and chronological working notes in
[`docs/internal/`](docs/internal/). Those notes contain intermediate states that are
sometimes superseded — this README is the current overview.

---

## Validation and remaining work

The Release build passes **470 automated tests** on the x64 host, with zero build errors.
That is a local checkpoint, not a CI badge, and says nothing about production readiness.

Still outstanding:

- Live news-server TLS connections and a full header import.
- Desktop checks for WebView2 navigation, downloads and media playback.
- Recovery testing with genuinely corrupt databases, beyond the automated fixtures.
- Replacing the 32-bit child-process utilities (`phpar2.exe`, `UnRAR.exe`, `7za.exe`).
- Broader acceptance testing of the x64 installer and the migration flow.
- Profiling a real import before attempting parallel verification or SIMD decoding.

---

## Contributing and attribution

Useful contributions include reproducible bug reports, provider and runtime compatibility
testing, regression tests, and focused modernization changes. Include the build or commit,
your Windows version, reproduction steps and redacted logs. Do not publish credentials,
tokens or personal database content.

Preserve Spotnet protocol compatibility and add tests for behavior changes. Database
changes should weigh migration and recovery; native dependency changes should be checked
in the real x64 output, on a desktop.

Credit belongs to the original Spotnet and Phuse authors, to the authors of the bundled
libraries and tools, and to everyone contributing to this reconstruction. See the
[provenance documentation](docs/reference/SOURCE_PROVENANCE.md).

There is no repository-level `LICENSE` file yet; this README assigns no blanket license to
the recovered application or its third-party components.

---

## macOS client (alpha)

A macOS variant is being worked on in the
**[`macos-client`](https://github.com/Cyclone47/spotnet-3.0/tree/macos-client)** branch.

That branch is **alpha**: meant for trying out and contributing to, not for daily use.
Expect missing features, rough edges and changes without warning. The Windows build on
`main` remains the version to install if you just want to use Spotnet.
