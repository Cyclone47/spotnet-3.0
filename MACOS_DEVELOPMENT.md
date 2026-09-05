# Spotnet 3.0 macOS Client Development Guide

> **For AI Agents (Antigravity, Claude Code, Codex) & Developers on macOS**  
> This guide outlines how to pick up development of the macOS client on Apple Silicon (ARM64) and Intel (x86_64) / macOS Sonoma/Sequoia.

---

## 1. Getting Started on macOS

### 1.1. Clone and Checkout
Ensure you are on the `macos-client` branch:
```bash
git clone https://github.com/Cyclone47/spotnet-3.0.git -b macos-client
cd spotnet-3.0
```

### 1.2. Verify Tooling
Verify that .NET SDK (8.0 or 10.0) and Xcode CLI tools are installed:
```bash
dotnet --version
xcode-select -p
```
If Avalonia templates are needed:
```bash
dotnet new install Avalonia.Templates
```

### 1.3. Verify Core Libraries Build Cleanly on Mac
The platform-neutral core libraries can be built immediately on macOS:
```bash
dotnet build src/Spotnet/Spotnet.Enc/Spotnet.Enc.csproj
dotnet build src/Spotnet/Spotnet.Core/Spotnet.Core.csproj
```
Both projects target `net8.0` (AnyCPU) and have zero Windows/WPF dependencies.

---

## 2. Solution Architecture

```
src/Spotnet/
├── Spotnet.Core/                 <-- Platform-neutral core shared by Windows & macOS
│   ├── Abstractions/             <-- Core interfaces: IAppPaths, ISecretStore, IUiDispatcher, IUserSettings
│   ├── Model/                    <-- ServerInfo, NntpSettings, SpeedCalculator, Spot rows & counters
│   ├── Network/                  <-- Socks5Client (proxy, IPv4/IPv6, DNS)
│   ├── Phuse/                    <-- yEnc codecs (YEncCrc32, YEncDecoder, YEncEncoder) & NNTP transport
│   ├── Platform/                 <-- StandardAppPaths (macOS ~/Library/Application Support conventions)
│   └── Text/                     <-- StringExtension methods
│
├── Spotnet.Enc/                  <-- Platform-neutral spot header crypto and key verification
│
├── Spotnet/                      <-- Existing Windows WPF application (net8.0-windows)
│
└── Spotnet.Mac/ (or Avalonia)    <-- [TO BE CREATED ON MAC] The native macOS desktop client
```

---

## 3. macOS Implementation Directives

### 3.1. Project Structure
Create the Avalonia client under `src/Spotnet/Spotnet.Mac/`:
```bash
dotnet new avalonia.app -n Spotnet.Mac -o src/Spotnet/Spotnet.Mac
dotnet sln src/Spotnet/Spotnet.sln add src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj
dotnet add src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj reference src/Spotnet/Spotnet.Core/Spotnet.Core.csproj
dotnet add src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj reference src/Spotnet/Spotnet.Enc/Spotnet.Enc.csproj
```

### 3.2. Directory Paths on macOS
Use `Spotnet.Platform.StandardAppPaths` (or implement `IAppPaths`):
- **App Data & Database**: `~/Library/Application Support/Spotnet/`
- **Caches**: `~/Library/Caches/Spotnet/`
- **Logs**: `~/Library/Logs/Spotnet/`
- **Filters & Themes**: `~/Library/Application Support/Spotnet/Filters.v2/`
- **Downloads**: `~/Downloads`

### 3.3. SQLite & Database Portability (Apple Silicon ARM64)
- Replace Windows `System.Data.SQLite.Core` (which lacks `osx-arm64` interop) with `Microsoft.Data.Sqlite` in the Mac client.
- Include the SQLite bundle with FTS5:
  ```xml
  <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.8" />
  <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.8" />
  ```
- **Crucial**: The SQLite database schema, WAL journaling, and FTS5 indexes are 100% binary-compatible across Windows and macOS. An existing Windows Spotnet database file can be copied directly into `~/Library/Application Support/Spotnet/` and read.

### 3.4. Browser & Spot Rendering (WKWebView)
- Do not use WebView2 on macOS.
- Use Avalonia's native web view integration (`NativeWebView` / `WKWebView`):
  ```xml
  <PackageReference Include="Avalonia.Controls.WebView" Version="11.1.0" />
  ```
- Spotnet's existing spot detail templates (`whatsnew.html`, spot page HTML/CSS/JS) work directly inside `WKWebView`.

### 3.5. Credential Storage (Apple Keychain)
- Implement `ISecretStore` (`Spotnet.Platform.ISecretStore`) using macOS Keychain Services (`SecItemAdd`, `SecItemCopyMatching`, `SecItemDelete` from `Security.framework`).
- Provider passwords, API keys, and private spot signing identities must never be stored in plaintext configuration files.

### 3.6. Post-Download Processing (built in, no external tools)

`Spotnet.Mac/PostProcessing/` ports the Windows client's
`Spotnet.Downloader.PostProcessing` pipeline. `PostProcessCoordinator.RunAsync`
walks the same stages, in the same order, and reports each one into the Downloads
row (labels taken verbatim from `Spotnet.Properties.Words.nl.resx`):

| Stage | Label | What runs |
|---|---|---|
| `Verifying` | Verifiëren | `SplitFileJoiner` joins `name.ext.001/.002/…` sets |
| `Checking` | Controleren | `Par2Verifier` checks every slice's CRC32 then MD5 |
| `Repairing` | Repareren | `Par2Repairer` rebuilds damaged slices via Reed-Solomon |
| `Par2PieceDownloading` | Par2 downloaden | callback to fetch extra recovery blocks when short |
| `Unpacking` | Uitpakken | `ManagedArchiveExtractor` — rar, zip, 7z, tar |
| `Moving` | Verplaatsen | lifts the `__unpack` staging directory, drops the par2 files |
| `WrongPassword` | Wachtwoord? | encrypted archive, no usable password — the row waits |

**Nothing has to be installed.** The Windows client ships UnRAR.exe, 7za.exe and
phpar2.exe next to the binary and cannot work without them. Those cannot be
redistributed the same way on macOS, and making a user run `brew install` before
the app functions is not acceptable, so both jobs are part of the app:

* **Unpacking** is `SharpCompress` (MIT), a NuGet reference compiled into the
  build. It handles rar4/rar5 including multi-volume sets, zip, 7z and tar, with
  passwords, and travels with the bundle on both Apple Silicon and Intel.
* **par2 verification and repair** are implemented directly, in
  `Par2RecoverySet` (format), `Par2Verifier` (slice checking), `Galois16`
  (GF(2^16) arithmetic) and `Par2Repairer` (the Reed-Solomon solve). There is no
  managed par2 library on NuGet, so this is written against the Par2 v2 spec.

`PostProcessToolset` still looks for an `unrar` or `7zz` on the machine, but only
as a fallback for archives the built-in extractor cannot read. Its absence is
normal and is never reported to the user.

#### Safety properties worth preserving

* **Repair is gated on re-verification.** `Par2Repairer` re-checks the whole set
  after writing and returns `RepairDidNotVerify` rather than claiming success, so a
  mistake in the Reed-Solomon layer can never quietly produce corrupt files.
* **Only already-damaged slices are written.** A bad repair cannot destroy data
  that verified.
* **Archive entries cannot escape the download directory.** `../` paths are
  rejected — Usenet archives are untrusted input.
* **Password detection is proactive.** `ArchivePasswordProbe` reads RAR4/RAR5/ZIP
  headers before extraction, so an encrypted set is flagged without a doomed
  multi-minute unpack. Windows only ever reacts to UnRAR's exit code 11.

#### Tests

* `Par2Tests.cs` — field arithmetic, format parsing, verification and repair. The
  fixtures in `Par2Fixture.cs` generate real par2 files using a Reed-Solomon
  encoder written independently of the production code, so a passing repair means
  two separate implementations agree on the spec.
* `PostProcessPipelineTests.cs` — unpacking and the full coordinator against real
  zip archives and real par2 data.
* `PostProcessingTests.cs` — naming rules, split joining, archive header probes.

The parser and verifier were additionally validated against real Usenet downloads:
slice geometry, Main-packet file ordering and IFSC counts all matched, and files
that were intact verified clean against genuine par2 hashes.

---

### 3.6. macOS User Experience & HIG
- **Menu Bar**: Native menu items (App Menu, File, Edit, View, Help).
- **Keyboard Shortcuts**:
  - `Cmd+,` -> Preferences / Provider Settings
  - `Cmd+F` -> Search spots
  - `Cmd+W` -> Close tab / spot detail
  - `Cmd+Q` -> Quit
- **Appearance**: Support macOS system Light and Dark themes dynamically.
- **Dock**: Optional unread count badge or notification on sync completion.

---

## 4. Immediate Milestone: Phase 0 Spike (Go / No-Go Test)

The first task to execute on macOS is a self-contained spike verifying four critical capabilities on Apple Silicon:

1. **Window**: Launch a clean Avalonia window on macOS.
2. **Network/TLS**: Connect to a real Usenet provider over TLS (port 563) via `Spotnet.Core.Model.ServerInfo` / `Phuse` and authenticate.
3. **Database & FTS5**: Open a Spotnet SQLite database on macOS, execute an FTS5 search query, and return results.
4. **Spot Details**: Display a spot page inside `WKWebView`.

---

## 5. Build & Packaging on macOS

### Standalone Self-Contained App:
For Intel (x86_64):
```bash
dotnet publish src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj \
  -c Release \
  -r osx-x64 \
  --self-contained true \
  -p:PublishTrimmed=false
```

For Apple Silicon (ARM64):
```bash
dotnet publish src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishTrimmed=false
```

### Full .app Bundle Creation:
```bash
./tools/make_app_bundle.sh         # Auto-detects Intel or ARM64
./tools/make_app_bundle.sh x64     # Explicit Intel
./tools/make_app_bundle.sh arm64   # Explicit Apple Silicon
```

### macOS Installer Creation (DMG & PKG):
Generates compressed `.dmg` disk image and native `.pkg` installer package:
```bash
./tools/make_installer.sh          # Builds both DMG and PKG (auto architecture)
./tools/make_installer.sh dmg      # Builds only DMG
./tools/make_installer.sh pkg      # Builds only PKG
./tools/make_installer.sh all arm64 # Explicit Apple Silicon
./tools/make_installer.sh all x64   # Explicit Intel
```
Outputs in `artifacts/`:
- `Spotnet-3.0.0-Alpha.dmg` (with `/Applications` symlink, Retina background)
- `Spotnet-3.0.0-Alpha.pkg` (native macOS installer package)
- `SHA256SUMS.txt` (SHA256 hashes)

### Apple Developer ID Signing & Notarization:
```bash
# Code signing
codesign --deep --force --options runtime --sign "Developer ID Application: YourName (TeamID)" "Spotnet.app"

# Notarization
xcrun notarytool submit "Spotnet.dmg" --keychain-profile "AC_PASSWORD" --wait
xcrun stapler staple "Spotnet.dmg"
```

---

## 6. Key Source References in Windows Repo

| Feature | Windows Location | Mac Action |
|---|---|---|
| **Spot XML & Decryption** | `Spotnet/Helpers/SpotHelper.cs` | Use `Spotnet.Enc.SpotnetDecoder` + `Spotnet.Core` |
| **Socks5 Proxy** | `Spotnet.Core/Network/Socks5Client.cs` | Already portable, ready to use |
| **yEnc Decoder** | `Spotnet.Core/Phuse/YEncDecoder.cs` | Already portable, ready to use |
| **SQLite DAL** | `Spotnet/DAL/SQliteDb.cs`, `Fts5Module.cs` | Adapt to `Microsoft.Data.Sqlite` |
| **Provider Catalogue** | `Spotnet/Model/ProviderCatalogue.cs` | Portable Usenet provider presets |
| **SABnzbd / NZBGet** | `Spotnet/Downloader/NzbGetDownloader.cs` | Call external REST APIs over HTTP |
