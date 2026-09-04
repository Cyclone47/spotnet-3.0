# Spotnet 3.0 â€” Handoff

Single self-contained document: the prompt to start a new AI session, the current state,
the sequenced plan for what remains, and the full record of what changed and why.

**Build:** 0 errors. **Tests:** 395/395 passing under the x64 test host.
Target: `net8.0-windows`, `x64` only.

## Usability and hardening pass: unreleased

Six changes on top of 3.0.6.6. Build is 0 errors; the suite is 395/395, with three new test
files. No cryptographic hash, signature scheme or wire format was touched.

### 1. The archive password is taken from the download itself

`Helpers/UnpackPasswordDetector.cs` reads the password an NZB declares in its head section -
both `<meta type="password">secret</meta>` and the `value="secret"` attribute form several
posting tools emit - and, failing that, the conventional labels in a spot title or body
(`[b]Wachtwoord:[/b] x`, `Password: x`, `pwd = x`, quoted values, label on its own line).

- `SpotHelper.DownloadNzbAndStartDownloadItem` applies it from the spot body at queue time,
  which is the only moment the body exists.
- `Unpack.Run` re-reads the NZB kept in the queue directory, so a download resumed in a later
  session still finds it.
- Neither ever overwrites a non-empty `UnpackPassword`, so a password the user typed into
  `ChangeUnpackPasswordWindow` wins. When nothing is found, or the found value turns out to be
  wrong, unrar reports the password problem and the manual dialog opens exactly as before.
- The detector refuses to guess: `Wachtwoord: geen`, `Password: none`, `n/a` and bare
  punctuation return nothing, because inventing a password breaks an unpack that would
  otherwise have worked. The password is never written to the log.

### 2. A first synchronisation no longer starts fifteen years back

New setting `InitialFetchDays` (default 90; 30 / 90 / 365 / everything, in Settings ->
Database). It applies only while the spots database is still empty - `Position.First == -1`.

`Model/ArticleWatermark.cs` binary-searches the group for the first article at or after the
cutoff, using XOVER probes of 200 articles and a budget of 32 of them. Article numbers rise
with posting time, so a few round trips replace hours of header downloading.

`DbUpdater.RetentionStartDate` is the floor even when the user asks for everything: headers
older than it were already discarded on save in `Headers.OnWorkDone`, so fetching them was
pure waiting. Anything undeterminable - a provider that will not answer a probe, dates that
will not parse - falls back to the full range, which is the previous behaviour.

### 3. Web links in a spot description work again

`SpotWebView2Page.HandleHostLink` handled only the theme's `link:` pseudo-scheme. A plain
`http://` or `https://` anchor was cancelled by `OnSpotNavigationStarting` and then fell
through every branch, so the click did nothing; a `target="_blank"` one went to the base
class and always opened an internal tab regardless of the setting.

- An `http`/`https` branch was added, and `OnNewWindowRequested` is now `protected virtual`
  and overridden on the spot page.
- Both route through one opener that honours `Settings.Default.ExternalBrowser`:
  `AppHelper.LaunchInExternalProgram` when on, an internal WebView2 tab when off.
- `ExternalBrowser` now **defaults to True**. It was False; an IMDb or YouTube link out of a
  spot belongs in the user's own browser, with their sessions and extensions. Existing
  profiles keep their saved value - only fresh ones see the new default.
- `TryResolveWebLink` is the scheme filter and is separately tested: only absolute http and
  https ever leave the application. `javascript:`, `data:`, `file:` and `ftp:` hrefs out of a
  Usenet posting are dropped rather than handed to the shell. `spotnet://`, `query:`, `menu:`
  and the rest stay strictly internal.

### 4. Dialogs follow the theme

`Views/DbRecoveryWindow.xaml` hardcoded a complete light palette (`#333333`, `#666666`,
`#F2F2F2`, tinted button faces) and rendered dark-grey-on-dark under Modern Dark. Every colour
is now a `DynamicResource` into the active palette. The palette vocabulary inverts between the
light and dark dictionaries - `BlackBrush` is the primary text colour and `GrayBrush9`/`10` the
quietest surface in both - so one set of keys serves all three themes. The colour coding
survives: the recommended action uses the notice palette, the destructive one the error colour.

Four other genuine contrast failures were fixed the same way: the system-status bubble (near
black text on a hardcoded white gradient), the spam-report author colour, the encrypted-NZB
hint on the post dialog, and the speed-limit arrow on the downloads status bar.

Deliberately left alone: the splash window, the SOCKS proxy tooltip, the video player overlay
and the filter panel. Those commit to a single fixed-contrast look by design rather than
following the theme, and the remaining `#00FFFFFF` values are transparency, not colour.

### 5. FIPS-safe crypto construction

`new MD5CryptoServiceProvider()` in `Helpers/Par2File.cs:92` threw `InvalidOperationException`
on a Windows 11 machine with the FIPS policy enabled, failing every par2 validation. It is now
`MD5.Create()`. The same migration was applied to the other FIPS-fragile constructions in the
solution:

| Was | Now | Where |
| --- | --- | --- |
| `new MD5CryptoServiceProvider()` | `MD5.Create()` | `Par2File` |
| `new SHA1CryptoServiceProvider()` | `SHA1.Create()` | `AppHelper`, `SpotHelper` |
| `new SHA1Managed().ComputeHash(x)` | `SHA1.HashData(x)` | `AppHelper`, `SpotHelper` |
| `new SHA1Managed()` | `SHA1.Create()` | `Worker` |
| `new TripleDESCryptoServiceProvider()` | `TripleDES.Create()` | `EncPass` |

Every one produces an identical digest or ciphertext, so par2 validation, spot hashes and
stored passwords are unaffected. `RSACryptoServiceProvider` was **left alone on purpose**: it
is the FIPS-approved CAPI provider, the spot signature path depends on its
`SignHash`/`VerifyHash(hash, null, sig)` shape, and `UserKeyHelper` stores keys as CSP blobs in
a named key container. Migrating it would change both the signature API and the on-disk key
format - exactly the wire-protocol change this work is not allowed to make.

### 6. Desktop notifications

`Helpers/NotificationHelper.cs` is the single implementation. It borrows the main window's
existing tray `NotifyIcon` through `AttachTrayIcon`, so there is never a second entry in the
tray, and falls back to one of its own if called before the window exists.

Four separate defects stood between a finished download and a notification. All three were
found by instrumenting the path and reading the installed profile's log
(`%LOCALAPPDATA%\Spotnet3\Data\Logs\spotnet.log`), not by reasoning about it - the first two
guesses were wrong, and the log is what settled it.

1. **The finished-download report threw a NullReferenceException on every download, and its
   own catch block swallowed it.** Both downloaders called
   `((MainWindow)Application.Current.MainWindow).DisplayTooltip(...)`. Nothing in Spotnet ever
   assigns `Application.Current.MainWindow` - it drives its own startup and shows a splash
   first - so that property was null, the cast quietly produced null, and the call threw. The
   surrounding `catch` logged it at Error and moved on, so the only trace was one
   `MoveNext [System.NullReferenceException]` line in the log with no context. `Sys.MainWindow`
   is the reference the rest of the codebase uses, and is what both call sites use now, with a
   null guard that says so in the log. This predates all of this work: the download-complete
   notification has never functioned.
2. **The subscription only covered half the items.** `IsHistoryChanged` was wired in
   `SpotnetDownloader.AddToDownloadQueue` alone, so it reached items queued during the current
   run and nothing else. A download restored from the queue file after a restart - the normal
   case for anything still in progress when Spotnet last closed - reached history with no
   handler attached and finished in total silence. This was the actual reason nothing appeared
   on screen: the notification code was never called at all. `Items` is an
   `ObservableCollection`, so the downloader now follows it from its constructor and subscribes
   every item, by whatever route it arrives, idempotently.
3. **The balloon was raised before the shell had the icon.** `Visible = true` sends NIM_ADD;
   asking for a balloon in the same statement gets it dropped, because the icon it belongs to
   is not registered yet. Minimise-to-tray defaults to off, so the icon was hidden - and this
   fired - essentially every time. The helper now shows the icon, gives the shell 750 ms of
   message pump, then raises the balloon.
4. **The icon was hidden again in the same breath.** The original `DisplayTooltip` restored
   the icon's visibility immediately after `ShowBalloonTip`, and a hidden notification icon
   takes its balloon with it. The helper keeps it alive on a 12-second timer measured from the
   balloon, then restores whatever visibility it had, so a window sitting minimised in the tray
   keeps its icon.

Every gate now logs at Info - reached history, raised, suppressed by which setting, or in the
foreground - so the log answers "how far did it get?" without another round of guessing. There
is also a **Test notification** button next to the setting in Settings -> Common, which raises
one immediately and deliberately ignores the master switch, so the delivery path can be checked
without waiting for a download and without a setting hiding the answer.

None of this is covered by the test suite: a toast needs a real shell and a message pump. The
log and that button are the verification mechanism instead.

- Fires when a download reaches history, carrying the spot title, and now says which of
  "finished" or "finished with problems" happened - it previously announced failures as
  complete. Success is `RawStatus == DownloadStatus.Success`, which
  `SpotnetDownloaderItemViewModel` sets from `PostProcessCoordinator.Run()`, so it genuinely
  means downloaded, repaired, unpacked and moved.
- Fires when a Quick Repair or a Rebuild finishes in `DbRecoveryWindow`.
- New master switch `ShowDesktopNotifications` (default True, Settings -> General). The
  existing per-event `NotifyAboutDownloadComplete` still applies on top of it.

### Notes for the next session

- New user strings were added to `Spotnet.Properties.Words.resx` and `.nl.resx` with matching
  Dutch text.
- Warning count moved 1467 -> 1485. All of it is `CA1416` platform noise on the Windows-only
  `NotifyIcon` API; 27 files including `MainWindow.cs` already carry that category. No new
  warning category was introduced.
- Nothing was pushed. Verify with `dotnet build reconstructed/Spotnet2/Spotnet.sln -c Release`
  and `dotnet test reconstructed/Spotnet2/Spotnet.Tests/Spotnet.Tests.csproj -c Release`.

## Provider-dialog hotfix: 3.0.4

- Application version is `3.0.4.0`; the current release and README target `v3.0.4`.
- The dialog caps its initial and maximum size to the Windows working area and can shrink to
  480 x 360 DIPs. Its central content scrolls while the header and Connect/Cancel footer stay
  visible.
- Provider filtering is deferred until the editable ComboBox has completed the current
  keystroke. It clears stale selection state before refreshing, then restores the exact text
  and caret. This prevents changing provider names and the `ArgumentOutOfRangeException`
  reported for `news`.
- Two regression tests pin the small-screen layout and safe deferred filtering.

## Connect dialog, provider list and Dutch: 3.0.3

- Superseded by the 3.0.4 hotfix above; this section records the 3.0.3 work.
- The provider list was re-verified against the live servers by reading each NNTP greeting.
  KPN v1/v2 answer "500 ... gestopt met Usenet-toegang" and are removed; 5 Euro Usenet and
  SnelNL moved off port 80, which accepts a connection and never answers; 15 were added.
- Dutch worked for the first time. 2.0 shipped `nl\Spotnet.resources.dll` but the
  reconstruction recovered only the text dumps, so every Dutch install fell back to English.
  The culture must appear in the satellite's logical resource name or it resolves nothing.
- The list is published as `providers.json` and fetched on launch. `ProviderCatalogue`
  validates it before any of it is used and the built-in list stays authoritative until a
  fetched copy passes in full. See [PROVIDERS.md](../PROVIDERS.md).

## Installer UI fix: 3.0.2

- Superseded by 3.0.3 above; this section records the 3.0.2 work.
- All custom Setup wizard copy has matching English and Dutch messages. Choosing Dutch now
  localizes welcome, profile/source/settings pages, Ready summary, progress, failures,
  completion, shortcuts and uninstall promptsâ€”not only Inno's standard controls.
- `PrepareToInstall` shows a dedicated progress page with explicit shutdown, WebView2 and
  profile copy/verification stages. It is hidden in a `finally` block on all exit paths.
- Two regression tests enforce English/Dutch key parity, referenced-key existence, translated
  welcome/run strings, and visible progress-page construction/cleanup.
- 111 tests pass and the actual installer lifecycle passed with `/LANG=dutch` in an isolated
  workspace root. The real installation, shell shortcuts and profile were untouched.

## Menu contrast fix: 3.0.1

- Application version is `3.0.1.0`; the README download targets GitHub release `v3.0.1`.
- Menus share application-wide styles and dedicated light/dark palette keys. All legacy
  context-menu Aero dictionary injection was removed.
- The toolbar's `ToolBar.MenuStyleKey` and menu item style selection are explicitly covered;
  the default library templates otherwise override an ordinary implicit MenuItem style.
- Text, keyboard shortcuts, checkmarks, arrows, hover/open/focus and disabled states use
  matched foreground/background colors. Disabled rows are not faded into unreadability.
- A real WPF template regression verifies contrast of at least 4.5:1, three submenu levels,
  on-demand context menus, separators, and repeated light/dark switching. Rendered previews
  were visually checked. No real Spotnet profile is opened by the test.

## Installer checkpoint

`build-installer.ps1` builds an Inno Setup 7 x64 package at
`artifacts/installer/Spotnet-3.0-x64-Setup.exe`. See [INSTALLER.md](../INSTALLER.md)
for the full migration, backup, prerequisite, and recovery behavior.
Initial package scope is recorded in [releases/v3.0.0.md](../releases/v3.0.0.md);
menu fixes are described in [releases/v3.0.1.md](../releases/v3.0.1.md), and the current
installer UI patch is described in [releases/v3.0.2.md](../releases/v3.0.2.md).

- Per-user application install with an isolated `%LOCALAPPDATA%\Spotnet3\Data` profile.
- Detects legacy 2.x data/settings, requests a graceful Spotnet exit, and copies selected
  profile data with held read locks and SHA-256 verification. Originals remain unchanged.
- Existing 3.0 profiles get a verified pre-upgrade backup. Uninstall retains personal data.
- `Spotnet.install` activates the stable settings provider and bypasses legacy Squirrel updates.
- Active legacy download queues are intentionally not imported. Older VB/1.x formats are
  not promised to be compatible. The package is unsigned.
- Updates current-user Desktop/Start Menu `.lnk` launchers for old/current/Squirrel Spotnet
  in place; creates both on a fresh install. Uninstall restores originals unless user-edited.
  Shared shortcuts, pins, and ClickOnce `.appref-ms` launchers are outside this scope.
- 111 tests pass, including installer/profile/shutdown/localization tests, two fresh-database tests,
  and 18 shortcut matching/replacement/recovery cases.
  Isolated actual Setup tests passed fresh installation, repeat upgrade/backup and uninstall.
- First-run testing caught an additional SQLite PRAGMA return-value bug in fresh database
  creation; the path now verifies values and refuses to initialize a database with user tables.

The older chronological notes below predate this installer checkpoint (including their
69-test counts and statements about installer work remaining).

---

## 1. Prompt for a new session

Copy everything inside the fence into a fresh Codex (or other assistant) session.

```
You are continuing work on Spotnet 3.0, a reconstructed and modernized C# / WPF Usenet client at
D:\sourcecode.

Read docs/HANDOFF.md first - it is the single source of truth for this project: current
state, what is done, what is open and in what order, and the conventions to work by.
Supporting detail lives in README.md, docs/DATABASE.md (schema and FTS) and
docs/PROTOCOL.md (NNTP topology, spot XML, RSA signature verification).

Solution: reconstructed/Spotnet2/Spotnet.sln

  dotnet build reconstructed/Spotnet2/Spotnet.sln -c Release
  dotnet test reconstructed/Spotnet2/Spotnet.Tests/Spotnet.Tests.csproj -c Release

Build has 0 errors and 191/191 tests pass. Warnings are an intentional analyzer backlog,
not new defects.

Work through the open items in the order given in section 4 of HANDOFF.md. The x64 and
FTS5 tracks are complete; performance work awaits measurement.

Follow the conventions in section 5. The two that matter most:

  - The characterization tests (WorkerCharacterizationTests, QueryBuilderTests) are CHANGE
    DETECTORS, not specifications. If one fails, behaviour moved. Decide deliberately
    whether that is correct, then update the assertion. Never just make it pass.
  - Verify claims against the code before acting on them, and measure before optimizing.
    Several plausible assumptions about this codebase turned out to be false; section 7
    lists them.

Do not change the wire protocol. SHA-1 signing is weak but it is what Spotnet uses;
changing it forks this client off the network.

Keep docs/HANDOFF.md updated as you go - it is how the next session picks this up cold.
```

---

## 2. Orientation

```
D:\sourcecode
â”œâ”€â”€ reconstructed/Spotnet2/          the live solution
â”‚   â”œâ”€â”€ Spotnet/                     WPF app (net472, x64)
â”‚   â”œâ”€â”€ Spotnet.Enc/                 yEnc decoder
â”‚   â”œâ”€â”€ Spotnet.Tests/               xUnit, 69 tests
â”‚   â”œâ”€â”€ lib/                         third-party DLLs (being migrated to PackageReference)
â”‚   â”œâ”€â”€ Directory.Build.props        analyzers on, NuGetAudit on
â”‚   â””â”€â”€ .editorconfig                analyzer severity triage
â”œâ”€â”€ tools/
â”‚   â”œâ”€â”€ DbDiagnostic/                `inspect` and `bench` subcommands
â”‚   â”œâ”€â”€ DbRepair/                    standalone repair utility
â”‚   â””â”€â”€ BamlExtractor/, WpfCleaner/  reconstruction tooling
â”œâ”€â”€ docs/                            this file plus the archaeology docs
â””â”€â”€ decompiled_200/                  raw decompiler output, reference only - do not edit
```

Build and test:

```
dotnet build reconstructed/Spotnet2/Spotnet.sln -c Release
dotnet test reconstructed/Spotnet2/Spotnet.Tests/Spotnet.Tests.csproj -c Release
```

Measure and inspect:

```
dotnet run --project tools/DbDiagnostic -c Release -- bench 50000
dotnet run --project tools/DbDiagnostic -c Release -- inspect
```

Web content is rendered exclusively by Edge WebView2; the legacy engine and its command-line
switches have been removed.

---

## 3. Where things stand

Spotnet 3.0 is the maintained continuation of a client originally built on a 2014-era stack. Work so far has been
concentrated on four things: the cause of database corruption, TLS that was about to stop
working, closing SQL injections, and completing the 64-bit migration.

**Verified against a real system:** WAL is active on both live databases (1.0 GB spots /
2.29M rows, 1.34 GB comments) and both pass `quick_check`. SQLite 1.0.119 reads them
correctly.

**Not verified by anyone yet:** the TLS handshake against a real news server, a full header
import end to end, the WebView2 pages on a real desktop, and the Rebuild path against a
genuinely corrupt database. Ask the owner to run these rather than assuming they work.

**One deliberate behaviour change:** TLS certificates are now validated. A provider using a
self-signed or expired certificate will fail to connect until the user enables
*Allow invalid server certificate*. The log names the exact `SslPolicyErrors` and points at
the setting. To revert, set the `DefaultSettingValue` on `Settings.AllowInvalidServerCertificate`
to `True` and the matching entry in `app.config`.

---

## 4. What is left, sequenced

The dependency, filter-hardening, browser replacement, media replacement, x64 and FTS5
tracks are complete. Performance work remains blocked on measurement.

### Desktop gate â€” exercise WebView2 and LibVLCSharp on a real machine

Open a link inside a spot, Help â†’ Release Notes, the feedback page, and Advanced Downloads.
Also play, pause, seek, mute, switch playlist entries, and enter/leave full screen on a local
video. Automated tests confirm the AMD64 build and non-UI logic, but not GPU/video rendering.

### Track 1 â€” Dependency sweep (completed for the 3.0 checkpoint)

Nothing depends on these and they depend on nothing. One package per commit so a
regression is bisectable; the SQLite upgrade proved the pattern.

Completed security work:

- **SharpZipLib 0.86 â†’ 1.4.2.** It is now used only for NNTP zlib responses.
- **Ionic.Zip removed.** ZIP creation/extraction uses `System.IO.Compression` through a
  traversal-safe boundary covered by regression tests.
- **Newtonsoft.Json 7.0.1 â†’ 13.0.3.**

Completed maintenance work:

- **NLog 3.2 â†’ 5.5.1** with asynchronous targets and the NLog 5 API changes applied.
- **HtmlAgilityPack 1.4.6 â†’ 1.12.4.**
- **Pri.LongPath removed.** Framework long-path switches and the Windows `longPathAware`
  manifest flag preserve long-path behavior.

Defer, as its own decision: **MahApps.Metro 1.0 â†’ 2.4**, **AvalonDock 3.5 â†’ Dirkster 4.7x**,
**MvvmLight â†’ CommunityToolkit.Mvvm**. Each is breaking across many files; MahApps touches
all 65 XAML files, and there is recent theme work sitting on top of MahApps 1.0 that would
need redoing. Weigh it against how much more theming is planned.

### Track 2 â€” Finish the data layer (unblocked; the tests for it now exist)

- **Filter-expression builders completed.** `FilterExpressionCompiler` validates the
  user-authored mini-language against identifier/operator allowlists, rejects comments and
  statement separators, and binds every user literal. Parameter objects now flow through
  row queries and delayed count queries. All bundled advanced filters are regression-tested.
- **FTS4 â†’ FTS5 (completed).** `search` and `comments` are FTS5, addressed by `rowid`,
  behind a `user_version` 2 â†’ 3 bump that rebuilds the index from `spots` in one exclusive
  transaction. Filters saved with FTS4's `docid` are rewritten as `filters.xml` is read,
  and the bundled catalogue was updated in place.
  **Correct the assumption this backlog carried:** the shipped `System.Data.SQLite.Core`
  binaries are *not* built with `SQLITE_ENABLE_FTS5` â€” `USING fts5` on a bare connection
  fails with "no such module: fts5". The interop carries FTS5 as a loadable extension and
  exports `sqlite3_fts5_init`, so `Fts5Module` registers it per connection and switches
  extension loading straight back off, because filters reach SQLite as user-supplied SQL.
  No provider swap and no .NET migration were needed.
  Ranking still uses neither `matchinfo()` nor `bm25()`; ordering is by `rowid` or by a
  `spots` column, so there was nothing to port.

### Track 3 â€” x64 platform move (completed)

- All three solution projects target AMD64 with `Prefer32Bit=false`.
- Edge WebView2 renders every page. The Awesomium source, managed references and native
  assets were removed here; the spot page followed later (see Track 5), and the MSHTML
  control it used is still present as its fallback.
- Video preview now uses LibVLCSharp 3.10.1 and VideoLAN.LibVLC.Windows 3.0.23.1.
- System.Data.SQLite.Core selects its x64 interop library at runtime.
- `Spotnet.exe` and `Spotnet.Tests.dll` both report `ProcessorArchitecture=Amd64`.
- The post-processing executables remain child processes and do not constrain host architecture.

### Track 5 â€” Spot page on WebView2 (done in code; needs the desktop gate)

- `SpotWebView2Page` replaces `SpotNativePage` for `PageTypeEnum.SpotLoaded`, and
  `PagesFactory` picks it unless `UseNativeBrowser` is set or the Evergreen Runtime is
  missing. That setting now selects the engine and nothing else; it used to also switch
  `SpotParser.LocalFilePrefix` to an `asset://` scheme for a virtual host mapping that was
  never wired up.
- The document is written to a temporary file and opened over `file://`. A string-loaded
  document has an opaque origin and could not load the theme's own script, stylesheet and
  images, so this leaves all seven themes untouched.
- `SpotPageBridge` is the whole host/document boundary. Page to host is one
  `postMessage` channel; host to page is the `window.spotnet` functions. Messages that the
  host would otherwise have to read the DOM for carry their data with them - a Send click
  brings the nickname and comment body, a quote click brings the quoted text and its
  author - which is what removes the synchronous DOM reads MSHTML allowed and WebView2
  does not.
- Listeners are delegated from `document`, so the panels the host rewrites wholesale (the
  comment preview, the smiley strip) keep working with no rebinding. This was verified by
  running the bridge against a fixture built from the real `comment.htm` markup.
- `SpotPageBridgeTests` pins the two sides against each other: a host call to a bridge
  function that does not exist is otherwise completely silent.
- **The MSHTML stack is removed.** `SpotNativePage`, `IEWebBrowser`, `WebNativePage`,
  `ReleaseNotesNativePage`, `iewebbrowser.xaml` and the `WindowsFormsIntegration`
  reference are gone, and with them the last `using mshtml` and the last
  `WindowsFormsHost` in the application. `UseNativeBrowser` went too: it selected the
  engine and there is now only one.
- **Still open:** the page has not been exercised against a live spot with comments, an
  image and a working news server. The fallback for that is now the previous commit
  rather than a runtime switch.

### Track 6 â€” Dependencies, signing, and what a .NET move actually costs

- **33 vendored DLLs removed from `lib/`.** MahApps.Metro, MVVM Light (with
  CommonServiceLocator and System.Windows.Interactivity), the Xceed toolkit,
  Starksoft.Aspen and FileCache now arrive as packages at the same major versions, so this
  is a sourcing change and not a library upgrade. The rest were already superseded by
  existing PackageReferences and had simply been left behind. The build output is
  composed identically before and after, file for file.
- **What stays vendored:** Squirrel 0.9.2 and the assemblies it loads. No NuGet release
  matches that version, and the updater is 1096 lines woven through startup, profile
  creation and restart. Replacing it is a feature decision, not dependency cleanup.
- **`lib/Spotnet.exe` no longer reaches the output.** The copy rule globbed `lib/*.exe`,
  which included the decompiled 2.0 reference binary. The compiler's own output won, so
  nothing shipped wrong, but the hazard is now excluded explicitly.
- **Authenticode signing is wired end to end**, opt-in via `-SignThumbprint` (a
  certificate already in the user's store) or `-SignCommand` (HSM, cloud signing). No
  password passes through the script. It signs what this repository produces - Spotnet.exe,
  Spotnet.Enc.dll, the satellite resources and the setup helper - then hands the same
  command to Inno Setup for the installer and, via `SignedUninstaller=yes`, the
  uninstaller. Verified with a stub signer: Inno accepted the directives and called the
  tool for `uninst.e64.tmp`, and both Inno and this script refuse to continue when the
  tool reports success without producing a signature. Only a certificate is missing.
- **WCF and ClickOnce are gone.** `System.ServiceModel` was referenced solely for
  `SynchronizedCollection<T>`; there are no services. It is replaced by `SynchronizedList<T>`,
  which additionally enumerates a snapshot, so the download queue can no longer throw
  "collection was modified" while workers add and remove. `System.Deployment` was
  referenced for `ApplicationDeployment.IsNetworkDeployed`, which is false in every install
  path this application has. `System.Management` now comes from its package.

#### The .NET migration (done)

The application targets `net8.0-windows`. The blockers this backlog listed - `user.config`,
the Microsoft.VisualBasic code, System.Drawing for avatars, the WCF references, the old WPF
libraries - were either already resolved or never stopped the compiler.

Two things did, and neither was on the list:

- **Code pages.** `Microsoft.VisualBasic.Strings.Chr` resolves anything above 127 through
  the system ANSI code page, and .NET no longer carries those. The header parser calls it
  for every spot; the call threw, the per-line handler swallowed it, and an import yielded
  nothing at all - no error, no log line. `EncodingSetup` registers the provider from a
  module initializer so no entry path can miss it. Seven characterization tests caught this.
- **`Encoding.Default` changed meaning**, from the system ANSI code page to UTF-8. Every
  place that relied on the old meaning now names `AppHelper.AnsiEnc()` instead, including
  the two that read files written by earlier versions.

Also moved: NLog's configuration, which it reads from app.config only on .NET Framework,
now lives in NLog.config where both find it. app.config keeps the settings defaults and
nothing else - the startup, runtime and system.web elements were Framework directives.

Squirrel was the expected casualty and is not one. Every entry point into it returns early
for an Inno-installed profile, and its assembly, types and `UpdateManager` constructor were
all verified to load on .NET 8.

Setup installs the .NET 8 Desktop Runtime the same way it installs WebView2, which is why
the installer grew from 43 MB to 98 MB. Both the English and Dutch installer smoke tests
pass end to end.

Compiling is not running - but the packages that would have been loaded in compatibility
mode are all gone now, and a net8.0-windows build reports no errors and no NU1701 at all:

| Package | Why it matters |
|---|---|
| ~~MahApps.Metro 1.6.5~~ | Done: 2.4.11, which targets .NET 5+ |
| ~~ControlzEx 3.0.2.4~~ | Done: 4.4.0, came with MahApps 2 |
| ~~MvvmLightLibs 5.4.1.1~~ | Done: removed, its three used pieces now live in the project |
| ~~Extended.Wpf.Toolkit 3.5.0~~ | Removed. Upgrading was not open to us: from 4.0 the licence permits distribution to fewer than ten end users. Its one control became a TextBox |
| ~~starksoft.aspen 1.1.8~~ | Removed. There is no modern-.NET build and never will be, so the SOCKS5 handshake it provided is now `Socks5Client`, with the tests that package never had |

`System.Windows.Interactivity` was the hard one - it does not work on modern .NET at all -
and it is gone with MahApps 1.x and MVVM Light. Microsoft.Xaml.Behaviors.Wpf replaces it.

So the sequencing in this backlog was right, but for a narrower reason than assumed. The
UI-library track is not a prerequisite because the code will not compile otherwise - it
compiles today. It is a prerequisite because those five packages have to be proven at
runtime first. That is a much smaller, better-defined job than "a separate large project".

### Track 4 â€” Performance, only where it is earned (blocked on measurement)

Two claims in the original plan proved wrong once measured. Do not start these until a real
import has been profiled.

- **Profile a real import first.** The benchmark harness measures components in isolation.
  What is missing is the breakdown of a real sync: how much is network, how much SQLite,
  how much parse and verify.
- **Then, and only then: parallelize verification** â€” `Model/Worker.cs` `DoWork`.
  `VerifyHash` is 78% of verification cost and only comes down with threads. But `DoWork`
  is a decompiled goto-lattice with four pieces of shared mutable state, and its output
  order is load-bearing (pinned by `ReturnsSpotsInAscendingArticleOrder`). The prize is
  ~24 seconds per million spots on a path that is probably network-bound. If the profile
  says verification is not a visible share, **close this item unfixed**.
- **Throttle the per-batch settings write.** `Settings.Default.Save()` rewrites the whole
  `user.config` after every save batch. Worth throttling like the row counts were, but it
  carries the import watermark â€” losing it on a crash costs re-downloading.
- **Triage the blocking waits.** 33 sync-over-async calls and 47 `Thread.Sleep` calls.
  Convert the ones reachable from a UI event handler first. Replace polling sleeps around
  the download queue with `SemaphoreSlim` waits. Leave deliberate backpressure alone, such
  as the 50 ms pause in the retention delete loop.
- **yEnc vectorization â€” expect to close this unfixed.** The decoder is a scalar loop
  despite the README claiming SIMD. On a saturated connection you are disk- and
  network-bound long before you are yEnc-bound. Listed so the decision is explicit.

### Track 5 â€” .NET 8/10 retarget (last, blocked on Track 3)

Largest single change and most likely to surface unrelated breakage, so it should land
against a codebase that is otherwise settled. WPF runs on modern .NET and the projects are
already SDK-style, so it is mostly a `TargetFramework` edit plus fixing what falls out.
Expect breakage in four known places: `System.Configuration` settings (the whole `Settings`
class and `app.config` userSettings), `Microsoft.VisualBasic` (`SpotParser`, `Worker`),
`System.Drawing` (avatars), and the WCF references.

Payoff: much better GC, modern JIT, `Span<T>` and `ArrayPool` throughout, and
`System.Text.Json` replacing Newtonsoft. `dotnet upgrade-assistant` produces a starting
diff, but expect to drive it manually. Keep it a separate commit from the x64 flip.

### Cross-cutting â€” the analyzer backlog

Not a checklist item, but the standing list of real defects the tooling already found.
Deduplicated counts from the current build:

| Rule | Count | What it means here | Priority |
| :--- | ---: | :--- | :--- |
| CA2211 | 30 | Mutable static fields â€” the shared-state class of bug. Found the static `_rsaParameters` race. | High |
| CA1001 | 19 | Types owning `IDisposable` fields without being disposable. Real leaks. | High |
| CA3075 | 19 | XML external entity handling. Three genuine gaps fixed; the rest read as mitigated but need confirming. | Medium |
| CA2100 | 13 | SQL built from strings. Shrinks as Track 2 lands. | Tracked |
| CS4014 | 10 | Un-awaited async calls â€” silently swallowed exceptions. | Medium |
| CA1816 | 8 | `Dispose` not calling `SuppressFinalize`. | Low |
| CA2016 | 6 | `CancellationToken` not forwarded â€” cancellation that silently does not. | Medium |

As each category reaches zero, promote it in `.editorconfig` from `suggestion` to `warning`
to `error`, so the fix cannot regress. That turns a backlog into a ratchet.

---

## 5. Conventions for working on this codebase

- **It is decompiled source.** Much of it is machine-generated shapes: goto lattices, names
  like `text4` / `num2` / `flag`. `Model/Worker.cs` `DoWork` is the worst example. Do not
  "tidy" these opportunistically â€” change them only with a specific reason and with the
  characterization tests as the gate.
- **The characterization tests are change detectors, not specifications.**
  `WorkerCharacterizationTests` and `QueryBuilderTests` record what the code does *today*.
  A failure means behaviour moved; decide deliberately whether that is correct, then update
  the assertion. Never just make it pass.
- **Verify before acting.** Several plausible-sounding assumptions about this project turned
  out to be false â€” see section 7.
- **Measure before optimizing.** `tools/DbDiagnostic` has `bench` and `inspect`.
- **Do not change the wire protocol.** SHA-1 signing is weak but it is what Spotnet uses;
  changing it forks this client off the network. Migrate implementations
  (`RSACryptoServiceProvider` â†’ `RSA.Create()`), never algorithms.
- **One package per commit** when migrating `lib/` to PackageReference.
- **Keep this file updated.** It is how the next session picks the project up cold.

---

## 6. Detailed record of what changed

Every completed item, with the files it touched and the reasoning. Phase numbering
comes from the original plan and is kept so older notes still line up.

### Phase 0 â€” Test net

- [x] **Analyzer baseline** â€” `Directory.Build.props` + `.editorconfig`
      `EnableNETAnalyzers` with `latest-recommended`, plus `NuGetAudit` for when packages
      land. Raw output was ~7000 findings, which buries the useful ones, so `.editorconfig`
      demotes the high-volume stylistic categories to suggestions and keeps the ones worth
      acting on as warnings (CA1001 disposables, CA2211 mutable statics, CA3075 XXE,
      CA2100 SQL, CA2022 short reads). SHA-1/MD5 rules are set to `none` with a note: they
      are the Spotnet wire protocol, not a defect.
      **Promote categories back to `warning`, then `error`, as they get cleaned up.**
- [x] **Durability tests** â€” `Spotnet.Tests/DbDurabilityTests.cs` (3)
      WAL actually active on a real on-disk database opened with the DAL's connection
      string; `synchronous` never OFF; the page_size-before-WAL ordering constraint.
- [x] **RSA cache tests** â€” `Spotnet.Tests/RsaVerifierCacheTests.cs` (4)
      Same modulus returns the same instance; distinct moduli don't collide; malformed
      moduli return null; a cached verifier accepts a valid signature and rejects a
      tampered hash.
- [x] **Rebuild tests** â€” `Spotnet.Tests/SpotsDbRebuilderTests.cs` (6)
      500 spots survive a rebuild; signing key and spam counts survive; a wiped FTS index
      comes back fully searchable; the original is kept as .bak with no stray working file;
      a non-database file and a missing file both fail cleanly without consuming the input.
- [x] **SQL parameterization tests** â€” `Spotnet.Tests/SqlParameterizationTests.cs` (5)
      Favourite add/remove/contains still behave identically, and a message id containing
      `"` `'` and `OR 1=1 --` is treated as data rather than altering the statement.
- [x] **Characterization tests on `Worker`** â€” `Spotnet.Tests/WorkerCharacterizationTests.cs` (15)
      Pins the header parser: field layout, ascending output order (it walks the block
      backwards then reverses), timestamp floor and future-clamping, malformed lines
      skipped without failing the batch, the 94165742 filesize sentinel, negative sizes
      normalized to zero, and signature-checking on/off behaviour.
      **These are change detectors, not a spec** â€” a failure after a refactor means
      behaviour moved; decide if that is correct, then update the assertion.
- [x] **Golden-output tests on the query builders** â€” `Spotnet.Tests/QueryBuilderTests.cs` (18)
      All four builders across the sort-column / sort-direction / erotica-toggle /
      minRowId / search matrix, plus `[SN:DATE]` and `[SN:NEW]` substitution. The four
      methods were made `internal` to allow this. The FTS5 query shape (`rowid`, not
      FTS4's `docid`) is pinned here.
- [x] **Benchmark tool** â€” `tools/DbDiagnostic` rewritten
      `DbDiagnostic inspect [path]` reports journal mode, page size, synchronous, schema,
      row counts and `quick_check` on a real database (auto-discovers ProgramData).
      `DbDiagnostic bench [rows]` measures the journalling and RSA changes.
      **See "Measured results" below â€” one of them corrected a claim in the plan.**

### Phase 1 â€” Data integrity

- [x] **WAL journalling on the import path** â€” `DAL/SpotSaver.cs`
      `journal_mode=WAL`, `synchronous=NORMAL`, `wal_autocheckpoint=4000`,
      `cache_size=-65536`, `temp_store=MEMORY`. Removed the `synchronous` parameter and its
      `synchronous: false` call site â€” that was the corruption switch.
- [x] **New databases created in WAL at 8192-byte pages** â€” `DAL/SpotProvider.cs`
      `page_size` must precede WAL and can't change afterwards, hence the ordering.
      Dropped the no-op `page_size=16384` from the import path.
- [x] **Connection string carries `BusyTimeout` and WAL** â€” `DAL/SQliteDb.cs`
      Writable connections get `Journal Mode=WAL`; read-only ones deliberately don't
      (they can't create the `-wal` file).
      *Skipped `Pooling=True` on purpose* â€” `SQliteDb` pins each connection to the managed
      thread that opened it (`CheckThread`), and ADO pooling would hand physical connections
      across threads for no measurable gain.
- [x] **Read-only connections actually read-only** â€” `DAL/SqlDbFactory.cs`
      Removed `isReadOnly = false;`, which discarded the flag at all 15 call sites (checked:
      all 15 are genuinely SELECT-only). Falls back to a writable connection if the
      read-only open fails, since a read-only connection can't replay a `-wal` needing recovery.
- [x] **Corruption detected by result code, not message text** â€” `DAL/SQliteDb.cs`
      New `IsCorruptionError` switches on `SQLiteException.ResultCode` (`Corrupt`, `NotADb`,
      `IoErr`, masking extended codes) and walks inner exceptions, with the old message
      match kept as a fallback for our own wrapper exceptions.
      `ISqlDb.ProcessMalformedDbState` now takes an `Exception` rather than a string.
- [x] **Schema extracted to one place** â€” `DAL/SpotsSchema.cs`
      Tables, indexes, FTS triggers and column lists, so a rebuilt database cannot drift
      from a created one. `SpotProvider` now builds from it.
- [x] **Third recovery tier** â€” `DAL/SpotsDbRebuilder.cs`, `Views/DbRecoveryWindow.{cs,xaml}`
      "Rebuild Database" sits between Quick Repair and Clean Reset: copies every readable
      row into a fresh file in rowid chunks (one bad page costs that chunk, not the table),
      regenerates the FTS index from `spots`, keeps the original as .bak. Logic lives in
      the DAL rather than the Window so it is testable.
- [x] **Quick Repair no longer reverts WAL** â€” `Views/DbRecoveryWindow.cs`
      It opened with `Journal Mode=Delete`, which converted the database back to the
      rollback journal every time someone ran repair. Now stays in WAL and reports
      `PRAGMA quick_check` so the log says whether a Rebuild is needed.
- [x] **Fixed duplicate watermark rows** â€” `Views/DbRecoveryWindow.cs`
      Quick Repair declared `userinfo(field TEXT PRIMARY KEY, ...)` but `IF NOT EXISTS`
      can't add a key to an existing table, so `INSERT OR REPLACE` appended duplicates
      instead of replacing. Now delete-then-insert, which works either way.
- [x] **No automatic VACUUM migration** â€” *decided against.* Existing databases keep
      4096-byte pages. VACUUM on a multi-GB database needs 2x free disk and minutes of
      unattended work at startup; the gain from 8192 does not justify that risk. Users who
      run Rebuild get an 8192-page database for free.

### Phase 2 â€” Hot paths

- [x] **RSA verifier cache** â€” `Helpers/SpotHelper.cs`
      `MakeRsa` caches by modulus (cap 2048, no eviction so `GetRsa`'s long-lived array
      stays valid) and `_rsaParameters` is now a local instead of a mutated static. This
      was a Windows CryptoAPI key container allocated *and leaked* per spot header.
      *Not yet done:* `RSACryptoServiceProvider` â†’ `RSA.Create()` (CNG). Ripples into
      `Worker.Rsa[]`, `GetRsa`, `UserKeyHelper`.
- [x] **Row counts no longer scanned per batch** â€” `DAL/SpotSaver.cs`
      `UpdateDatabaseSettings` runs in the `finally` of *every* save batch and ran two
      `COUNT(1)` scans each time, so import cost grew with database size. MIN/MAX stay
      (index lookups, and DatabaseMax is the import watermark); the counts refresh on a
      30-second throttle and exactly when forced. Retention forces them.
      **Behaviour note:** the displayed spot count can lag by up to 30s mid-import.
- [x] **Send path stops materializing the whole stream** â€” `Phuse/NNTP/Net/SocketBase.cs`
      `InternalSend` copied the outbound stream into a fresh array per send; now streams
      through a reused buffer. Also moved the null check before first use.
      *Correction to the original plan:* the **receive** buffer was already reused â€”
      `SetBuffer` only allocates when null or too small. No ArrayPool needed there.
- [x] **Short-read bugs on the article path** â€” `Phuse/NNTP/Net/Module.cs`
      `Stream.Read` is not required to return everything asked for. `GetBytes` and
      `UnzipResponse` assumed it did, which on any non-MemoryStream would silently produce
      zero-padded or truncated article data. Both now loop; `GetBytes` trims on a short read.
- [x] **SQL injection closed on network-supplied ids** â€” `DAL/SpotProvider.cs`,
      `Model/Favorites.cs`, `Model/Headers.cs`
      Six statements concatenated message ids and moduli â€” values that arrive from Usenet â€”
      straight into SQL. All now parameterized, which also lets SQLite reuse the prepared
      statements. `Headers.UpdateNullModuluses` reuses one command across its loop.
- [ ] **Parameterize the remaining filter-expression builders** â€” `DAL/SpotProvider.cs:365, 400`
      Harder: filters are a user-authored mini-language. Keep building them as text but
      validate against an allowlist of columns/operators and parameterize the literals.
      **Needs the golden-output tests first.**
- [ ] **Parallelize header verification** â€” `Model/Worker.cs`
      *Deliberately not done yet.* The characterization tests now make it safe to attempt,
      and the benchmark says `VerifyHash` is 78% of the verification cost and only comes
      down with threads. But `DoWork` is a decompiled goto-lattice with four pieces of
      shared mutable state (the `SHA1Managed` instance, the reused subcat `List<string>`,
      `_xOutputData`, and the RSA cache â€” whose instance members aren't documented
      thread-safe), *and* the output order is load-bearing (see
      `ReturnsSpotsInAscendingArticleOrder`). That is a lot of risk for ~24 s per million
      spots on a path that is probably network-bound.
      **Measure a real import first** â€” if verification isn't a visible share of it, don't.
- [x] **FTS4 â†’ FTS5** â€” `user_version` 2 â†’ 3, `docid` â†’ `rowid` throughout, `Fts5Module`
      registering the module per connection, legacy filters rewritten on load.
      `Fts5MigrationTests` migrates a seeded 3.0.5-shaped database and asserts the spots,
      the search results and the FTS5 integrity check.
- [ ] **Triage the 33 blocking waits / 47 `Thread.Sleep` calls** â€” UI-thread-reachable ones
      first. Leave deliberate backpressure (the 50 ms pause in the retention delete loop) alone.
- [ ] **`Settings.Default.Save()` per batch** â€” writes the whole user.config after every
      save batch. Worth throttling like the counts, but it carries the import watermark,
      so losing it on a crash costs re-downloading. Measure before changing.
- [ ] **yEnc vectorization** â€” `Spotnet.Enc/SpotnetDecoder.cs`. Measure first; likely not
      the bottleneck. (Note: the README's claim of SIMD decoding is not accurate â€” `Decode`
      is a scalar loop and `Init()` is empty.)

### Phase 3 â€” Dependencies & security

- [x] **TLS protocol and certificate validation** â€” `Phuse/NNTP/Net/SSLSocket.cs`
      `SslProtocols.Default` (SSL 3.0 + TLS 1.0) â†’ `SslProtocols.None` (OS negotiates).
      `EncryptionPolicy.AllowNoEncryption` â†’ `RequireEncryption`. Certificates are now
      validated, with `AllowInvalidServerCertificate` as the opt-out.
      **This is the one behaviour change** â€” see "Watch for" below.
- [x] **App-wide TLS for HTTP paths** â€” `App.cs`, `app.config`
      `ServicePointManager.SecurityProtocol = SystemDefault`, `DefaultConnectionLimit = 32`.
      `app.config`: `supportedRuntime` sku corrected 4.5 â†’ 4.7.2 (it was quietly asking for
      old-framework quirks), plus explicit `AppContextSwitchOverrides` for
      `DontEnableSchUseStrongCrypto` / `DontEnableSystemDefaultTlsVersions`.
- [x] **XXE hardening** â€” `Helpers/SpotnetUpdateVerifier.cs`, `Controls/LeftPanelUserControl.cs`,
      `Helpers/PortableSettingsProvider.cs`
      Three `XmlDocument`s parsed externally-influenced content without `XmlResolver = null`
      â€” most importantly the update manifest, which is parsed *before* its signature is
      checked. The other ~16 CA3075 hits were already mitigated (false positives).
- [x] **System.Data.SQLite 1.0.94 â†’ 1.0.119** â€” `Spotnet.csproj`, `Spotnet.Tests.csproj`,
      `tools/DbDiagnostic`, `tools/DbRepair`
      `System.Data.SQLite.Core` via PackageReference (the `.Core` package, not the
      meta-package, to avoid dragging in EF6). Removed the loose DLL reference and the
      manual `SQLite.Interop.dll` copy from all four projects.
      **This clears one of the two hard x64 blockers:** the package stages
      `x86/SQLite.Interop.dll` and `x64/SQLite.Interop.dll` side by side and resolves the
      right one at runtime â€” verified in the build output, both present and correctly
      typed. No stale root-level interop left to shadow them.
      Validated against the real 1 GB / 2.29M-row database and 61/61 tests, which exercise
      WAL, FTS4 rebuild, ATTACH and chunked copy on the new engine.
      *Insert throughput is only ~10% better and within run-to-run noise* â€” the value here
      is ten years of engine correctness fixes, FTS5 availability, and the x64 unblock,
      not raw speed.
- [ ] **The rest of `lib/` â†’ PackageReference**, one package per commit.
- [x] **Security-relevant upgrades**: SharpZipLib 1.4.2, Ionic.Zip removed in favor of a
      traversal-safe framework ZIP boundary, Newtonsoft.Json 13.0.3.
- [x] **Non-breaking maintenance upgrades**: NLog 5.5.1 and HtmlAgilityPack 1.12.4.
- [ ] **Breaking UI/MVVM decisions**: MvvmLight â†’ CommunityToolkit.Mvvm, AvalonDock â†’
      Dirkster 4.7x, MahApps.Metro 1.0 â†’ 2.4.x (65 XAML files â€” weigh it).
- [x] **Remove Pri.LongPath** â€” framework switches plus `longPathAware` manifest.

### Phase 4 â€” Platform

- [x] **`IPage` decoupled from Awesomium** â€” `Browser/PageReadyEventArgs.cs`, `Browser/IPage.cs`
      The interface exposed `Awesomium.Core.DocumentReadyEventArgs` in its event signature,
      so every consumer of `IPage` named the engine. Consumers only ever read `ReadyState`
      with two values, so it is now a local `PageReadyEventArgs` / `PageReadyState`;
      `AwesomiumPage` translates at its own boundary. `IPage.cs` no longer references
      Awesomium at all. **A `WebView2Page : IPage` can now be added without touching consumers.**
- [x] **`WebView2Page : IPage` added** â€” `Browser/WebView2Page.cs`, `browser/webview2page.xaml`
      `Microsoft.Web.WebView2` 1.0.3351.48 via PackageReference (restores and stages on
      net472; ships x86/x64/arm64 loaders, so it does not pin the build to x86).
      Wired into `PagesFactory` for **generic web pages only** â€” the least entangled page
      type. ReleaseNotes / ResponseSite / AdvancedDownloads still derive from
      `AwesomiumPage` and need porting individually.
      Ported behaviours: navigation, title and address changes, `NewWindowRequested` â†’
      Spotnet tab (was `ShowCreatedWebView`), `DownloadStarting` â†’ NZB queue (was
      `WebCore.Download`), Ctrl+key passthrough, and the `Loading` page type.
      **Runtime fallback:** `PagesFactory.UseWebView2` only honours the setting when
      `CoreWebView2Environment.GetAvailableBrowserVersionString()` succeeds, so enabling
      it on a machine without the Evergreen Runtime silently falls back rather than
      showing a blank tab. Covered by `Spotnet.Tests/WebView2PageTests.cs` (4).
      **Selecting the engine:** `Spotnet.exe --webview2` / `--no-webview2`, since
      `user.config` lives under a per-executable path hash. The choice persists. The log
      states which engine is in use.
- [x] **All three remaining page types ported to WebView2** â€” `ReleaseNotesPage`,
      `ResponsePage`, `AdvancedDownloadsPage` now derive from `WebView2Page`, and
      `PagesFactory` no longer initializes Awesomium's WebCore for them.
      `ResponsePage` needed its JS bridge rebuilt: Awesomium's
      `CreateGlobalJavascriptObject("app").BindAsync("UploadLogs", ...)` became a shim
      injected with `AddScriptToExecuteOnDocumentCreatedAsync` that forwards through
      `chrome.webview.postMessage`, handled in `WebMessageReceived`. The handler treats
      the channel as untrusted (remote page): it accepts one exact literal and ignores
      everything else. New `WebView2Page.OnCoreWebView2ReadyAsync` hook makes that
      possible before first navigation.
- [x] **Awesomium no longer initializes at startup** â€” `Views/MainWindow.cs`
      `AwesomiumPage.InitializeWebCore()` ran unconditionally on every launch, loading the
      32-bit `awesomium.dll` into the process whether a page needed it or not. Now gated
      on `!PagesFactory.UseWebView2`.
- [x] **Dead Awesomium surface removed** â€” `Model/Sys.cs` (`WebSessionProvider
      SessionProvider`, declared but never assigned or read), plus unused
      `using Awesomium.Core;` from `IEWebBrowser.cs` and `SpotNativePage.cs`.
- [x] **Loading spinner restored for WebView2 tabs** â€” `Views/MainWindow.cs:974`
      The gate was `newPage is AwesomiumPage`, so the ported tabs showed no loading
      indicator at all. Now covers both engine-backed page types.
- [x] **Legacy browser removed completely.** WebView2 is the only browser engine; the
      source, settings, command-line fallback, managed references, and native assets are gone.
- [ ] **Retarget to .NET 8/10** â€” expect breaks in `System.Configuration` settings,
      `Microsoft.VisualBasic` (in `SpotParser`, `Worker`), `System.Drawing` (avatars), WCF.
- [x] **Flipped the solution to x64.** Spotnet, Spotnet.Enc, and Spotnet.Tests are AMD64;
      Meta.Vlc was replaced with LibVLCSharp and the official VideoLAN x64 runtime.

---

### Dark theme contrast fixes

Three reports from real use, all the same class of bug â€” a foreground was themed but the
surface behind it was not, leaving light text on a light background.

- [x] **Selected tab unreadable** â€” `Helpers/ThemeHelper.cs`
      The light theme loaded `blueedited.xaml`, which defines only 31 of the 51 keys the
      controls reference. Missing among them: `BackgroundSelected`, `BackgroundNotSelected`,
      `SpotBackgroundBrush` and the entire `GrayBrush1..10` set. After a theme switch the
      selected tab's background resolved to nothing while `tabcontrol.xaml` still set its
      foreground to `IdealForegroundColorBrush` (white) â€” white on near-white.
      `classiclight.xaml` is the complete 51-key counterpart to `moderndark.xaml` and was
      sitting unused; the light theme now loads that instead.
- [x] **Menu dropdown unreadable in dark mode** â€” `style/mainmenustyle.xaml`
      The implicit `MenuItem` style set a foreground but no `Template`, so WPF fell back to
      the *system* menu template, which paints the submenu popup with
      `SystemColors.MenuBrush` â€” always light. Dark foreground on light system surface.
      Added a real `ControlTemplate` (icon column, header, shortcut text, submenu arrow,
      checkmark) whose popup border draws from `WhiteColorBrush`/`GrayBrush7`, so the
      surface follows the theme. Disabled items now use `GrayBrush4` rather than
      `GrayBrush5`, which was invisible on the dark surface.
- [x] **Spot post body renders white in dark mode** â€” `Utilities/SpotParser.cs`
      The injected dark CSS overrode text colour with element selectors
      (`div, span, td, ...`) but the tab templates paint their panels through *ID*
      selectors (`#part-one`, `#part-two`, `#ImdbPanel`, `#wrapper-one`, ...). ID
      selectors outrank element selectors, so the panels stayed `#ffffff` while their text
      turned light â€” the info table, the download panel and the comment form were all
      unreadable. Those IDs are now named explicitly in the dark stylesheet.


---

## 7. Findings that corrected earlier assumptions

Recorded because each one overturned something that had been asserted confidently.
They are the reason section 5 says to verify and measure before acting.

### The x64 blockers were managed assembly references, not SQLite

PE inspection showed that the former browser and media wrappers were managed assemblies
marked 32-bit-only. Avoiding their code paths was insufficient because loading either
assembly in a 64-bit process would throw `BadImageFormatException`. The resolution was to
remove the legacy browser entirely, port its remaining DOM helper to WebView2 scripting,
replace the media wrapper with LibVLCSharp, and then set every project to AMD64. SQLite was
already ready because its package supplies both native architectures. The x64-only output
target now removes unused x86 and ARM64 native payloads after each build.

### What the real database showed

`DbDiagnostic inspect` against the live install (first time any of this work has seen real
data), and it corrected two assumptions:

| | spots (.dbs) | comments (.dbc) |
| :--- | :--- | :--- |
| size | 1,031 MB | 1,340 MB |
| journal_mode | **wal** | **wal** |
| page_size | 4096 | **16384** |
| rows | 2,290,567 spots | â€” |
| quick_check | ok | ok |

- **The WAL change is live and both databases are healthy.** No corruption on either.
- **`COUNT(1)` over 2.29M rows takes 47 ms, not seconds.** SQLite scans the smallest index
  rather than the table. The per-batch throttle is still worth having â€” it ran *twice* per
  batch, so a long import saved seconds, not minutes â€” but the original claim overstated it.
- **The comments store has always used 16 KB pages.** The old import path set
  `page_size = 16384` on every batch; a no-op except on the very first one, which created
  the database. Dropping that pragma would have silently created *new* comments databases
  at the 4096 default. Now set explicitly in `AddComments` before WAL and the first write,
  with both page sizes named in `SpotsSchema`.

### Measured results

From `dotnet run --project tools/DbDiagnostic -c Release -- bench 50000`, on this machine.
Absolute numbers depend on disk and antivirus; the ratios are the point.

**Journalling â€” 50,000 rows in 5,000-row batches**

| Configuration | Time | Crash safe |
| :--- | ---: | :--- |
| `DELETE` + `synchronous=OFF` (was) | ~128 ms | **no** |
| `DELETE` + `synchronous=NORMAL` | ~136 ms | yes |
| `WAL` + `synchronous=NORMAL` (now) | ~119â€“129 ms | yes |

WAL is a touch *faster* than the setting it replaced, and it is crash safe. So the fix
costs nothing â€” the old `synchronous=OFF` was buying essentially no speed in exchange for
the corruption risk. That is the useful finding here.

**Signature verification â€” 20,000 verifications over 500 distinct posters**

| | Time | Per verification |
| :--- | ---: | ---: |
| New provider per spot (was) | ~617 ms | ~31 Âµs |
| Cached by modulus (now) | ~494 ms | ~25 Âµs |
| â€” of which construction | ~122 ms | ~6 Âµs |
| â€” of which `VerifyHash` | ~482 ms | ~24 Âµs |

**This corrects the plan.** The plan called the per-spot key container "the biggest win"
and said it cost "orders of magnitude" more than the verification. It does not:
construction is ~6 Âµs against ~24 Âµs for the `VerifyHash` it enables â€” about a fifth of
the cost. The cache is worth keeping (1.3x, and it stops a real handle leak of one
undisposed CSP handle per spot) but it is not transformative.

`VerifyHash` is 78% of the cost and is irreducible per spot, so the only remaining lever
on this path is running verification on more than one thread. At ~24 Âµs, a million-spot
sync is ~24 seconds of single-core CPU â€” real, but very likely dwarfed by network and
database time, which is why the next step is measuring a real import rather than
parallelizing on spec.


---

## 8. Known issues and what nobody has tested

### Known issue: links in a post open a blank tab

Reported from real use: clicking a link inside a spot opens a new tab, but the page never
loads. **Not caused by any change here** â€” verified rather than assumed:

- `MainWindow` assigns `newTab.Content = newPage` in exactly one place, inside the
  `DocumentReadyEvent` handler, gated on `ReadyState == Ready`. If the engine never
  reaches that state the tab stays empty, which is the symptom.
- The `IPage` decoupling changed the type of that argument. Awesomium's
  `DocumentReadyState` is exactly `{ Ready = 0, Loaded = 1 }` (read from the shipped
  assembly), and the translation maps `Loaded â†’ Loaded`, everything else â†’ `Ready`. That
  is 1:1, so the gate behaves identically before and after.
- The log records no error for the failed tab, consistent with navigation never completing
  rather than an exception.

The likely cause is Awesomium itself: it embeds Chromium from around 2014, whose network
stack predates modern TLS, so most HTTPS sites cannot be negotiated at all. **This is the
problem WebView2 exists to solve** â€” worth re-testing with `--webview2`.

### Watch for

**Certificate validation is now on.** Anyone whose provider uses a self-signed or expired
certificate will fail to connect until they enable *Allow invalid server certificate*.
The log names the exact `SslPolicyErrors` and points at the setting. To revert entirely,
change the `DefaultSettingValue` on `Settings.AllowInvalidServerCertificate` to `True` and
the matching entry in `app.config`.

**The spot count can lag up to 30 seconds mid-import** now that the full-table counts are
throttled. It is exact after retention and whenever forced.

**`DbRecoveryWindow.xaml` hardcodes light colours** (`#333333`, `#555555`, `#F2F2F2`).
That predates this work, but it will look wrong under the Modern Dark theme â€” worth
folding into the theme engine.

**No live-server or real-database testing has happened.** The TLS handshake, a full header
import, and the Rebuild path against a genuinely corrupt file all still want a real
smoke test.

