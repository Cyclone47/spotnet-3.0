# Spotnet 3.0 x64 installer

The installer is built with Inno Setup 7 and installs **for the current Windows user**. Both Setup and Spotnet are x64 executables. Windows 10/11 on x64 hardware and .NET Framework 4.7.2 or newer are required; this package does not enable native ARM64 support.

## Run Setup

[Download Setup and its checksum from the latest GitHub release](https://github.com/Cyclone47/spotnet-3.0/releases/latest). Local build output: `artifacts/installer/Spotnet-3.0-x64-Setup.exe`.

From 3.0.8.0, .NET 10 is included inside the application folder. It is not installed
as a shared runtime and does not appear separately in Windows Installed Apps.
Existing .NET 8 installations may remain for other applications. Upgrades preserve
the Spotnet profile in place without making full database backups.

1. Run Setup under the Windows account that owns the old Spotnet profile; do not switch to a different administrator account.
2. Confirm the installation folder, normally `%LOCALAPPDATA%\Programs\Spotnet3`.
3. On a clean machine, Setup skips migration questions. If Classic 1.8/2.x is detected, choose **migrate and replace (move)**, **migrate alongside (copy)**, **clean alongside**, or **clean and replace**. Copy/alongside is the non-destructive default. A source-profile question appears only when several data folders were found; Setup never merges profiles or guesses the newest preferences. Normal 3.0 upgrades retain their profile and previous shortcut mode.
4. Review the summary. Setup requests a graceful Spotnet exit and waits up to 30 seconds. It uses the existing tray-safe exit command, with a normal close-window fallback. If Spotnet does not exit, or belongs to another Windows session, Setup stops; it never force-kills Spotnet.
   During shutdown, prerequisite handling, and profile copying/verification, Setup shows a dedicated progress page and the current operation. A large profile can still take several minutes, but the wizard no longer leaves the generic Preparing page blank.
5. Missing WebView2 is installed using Microsoft's signed Evergreen bootstrapper. Internet access is needed for that step. Missing .NET Framework is reported before installation; install it from Microsoft and rerun Setup.
6. Setup prepares/verifies the profile, installs the application, creates or updates the selected shortcuts, then completes any requested move. Desktop and Start Menu shortcuts are checked by default. Moving requires an explicit confirmation and deletes the verified migrated source files; it does not rename them.

A fresh installation gets defaults and the application's provider-selection flow on first launch. Old application files remain available until you have verified the new client. Setup does not change NZB associations or the `spotnet://` handler.

The startup language applies to Inno Setup's standard controls and Spotnet's custom welcome text, migration/profile pages, Ready summary, progress/status text, errors, completion, shortcut messages, and uninstall prompts. English and Dutch are included. Low-level helper reports are replaced with localized safe summaries in the Dutch UI.

### Existing shortcuts

Setup scans this Windows user's Desktop and Start Menu Programs folders (including subfolders, up to six levels) for `.lnk` launchers that target `Spotnet.exe` or the legacy Spotnet Squirrel `Update.exe --processStart Spotnet.exe` command. **Alongside** leaves Classic's existing `Spotnet` launchers unchanged and creates selected new launchers named `Spotnet 3.0`. **Replace** updates Classic launchers in place, so `Spotnet` opens 3.0. A clean machine also gets `Spotnet`, without a version suffix. The Classic application itself is not uninstalled by either choice. Renamed launchers are recognized by their target, not their displayed name.

If a selected location has no matching launcher, Setup creates one using the name for that mode. Replace also consolidates the standard `Spotnet 3.0.lnk` name into `Spotnet.lnk`, journaling the old link for uninstall recovery. User-customized names are otherwise retained. It uses a `(64-bit)` suffix if the chosen name belongs to an unrelated application, never overwriting that unrelated link. An existing launcher inside a Desktop subfolder does not suppress creation of a checked root Desktop icon. Upgrades remember alongside mode instead of taking over Classic's links.

Original links are backed up under `%LOCALAPPDATA%\Spotnet3\ShortcutBackups`, with a hash-checked journal. Repeat upgrades retain the original backup. Uninstall restores replaced links and removes newly created links **only if they still match Setup's last version**; user-edited or deleted links are respected. Backup files are retained for recovery. A shortcut failure is reported visibly and causes Setup exit code `10`; the application remains installed and Setup can be rerun after correcting access/path problems.

After creating, updating, restoring or removing a managed shortcut, Setup sends a synchronous targeted change notification asking Windows Explorer to refresh it.

Scope is deliberately per-user: Public Desktop/all-users Start Menu entries, other users' shortcuts, taskbar/Start pins, and ClickOnce `.appref-ms` launchers are not changed. Network/junction/symlink folders are not followed. Those launchers need to be updated manually; do not point a shared all-users shortcut to one user's private installation.

## Detection and migration scope

Detection reads the current-user and machine uninstall registry entries in both architecture views, plus known data locations:

- `%PROGRAMDATA%\Spotnet`
- `%LOCALAPPDATA%\Spotnet\Data`
- Registered Spotnet installation locations and their `Data` subfolders
- Bounded Spotnet settings folders under Local/Roaming AppData and the ClickOnce data cache

Migration questions require an actual 1.8/2.x executable, discovered through an uninstall entry or the known per-user Squirrel location. Stale registry entries and abandoned data folders alone do not trigger the page. Read/access errors stop a selected migration instead of reporting success after a partial copy. Preferences are selected only when uniquely associated with the chosen data folder; ambiguous preferences use defaults.

The automatic profile format is **Spotnet 2.x / this reconstructed C# client**. It imports standard `Spotnet.Properties.Settings` user.config files and compatible portable `<Settings>` XML. Arbitrary Spotnet 1.x/VB settings or server schemas are not automatically converted; use a fresh profile and configure that provider manually. The historical executable is never run to extract settings.

Copied data includes server configuration/credentials, signing keys, spot/comment database files and their WAL/SHM companions, root XML/CSV/DAT/TXT/OLS profile files, custom filters, themes, and images. The preferences importer copies data values only; it does not load types or executable configuration sections from the old file. DTDs/external XML entities are rejected. Certificate-validation bypass is reset to `False` during import.

**Not copied:** legacy executable/DLL payloads, cached content, logs, active downloader queues, and completed or partial download files outside the profile. Those originals remain where they were. Download-folder preferences can still point to their old location; check them before starting downloads. Finish or export old queued jobs before moving to 3.0.

## Data safety and recovery

The installed application uses a new, stable profile location:

```text
%LOCALAPPDATA%\Spotnet3\
    Data\                   Current profile and user.config
    Backups\<timestamp-id>\ Verified pre-upgrade snapshots
    ShortcutBackups\        Original launch links and replacement journal
    staging-<timestamp-id>\  Incomplete copy, if a migration failed
```

- Copy/alongside and both clean choices never modify Classic profile files. Move/replace first prepares a verified copy without deleting sources. Only after application installation and shortcut creation succeed does a second pass recheck every source and destination hash under file locks, then permanently delete the migrated originals. Excluded files are retained. A failure before that point retains the sources; an interrupted removal can leave some originals behind and produces warning/exit code `11`. The pending operation is recorded in `classic-move.xml` under the new profile.
- All selected source file handles are held exclusively for the snapshot, including SQLite WAL/SHM files. Any sharing conflict aborts preparation. Do not reopen the old client during Setup.
- Copies are checked using SHA-256. Setup estimates required free space for the copy plus a 256 MiB margin; the application payload needs additional space.
- Files are written to a separate staging directory. Only a completed profile is renamed into `Data`. Failures retain staging for diagnosis; they never activate an incomplete profile.
- Existing marked 3.0 profiles are preserved, not overwritten by another import. Every repeat install/upgrade makes a verified backup before replacing application files. Backups exclude cache/log folders and require extra disk space.
- The installer refuses an unrecognized non-empty destination profile, overlapping source/destination paths, network sources, and junction/symlink paths.
- Setup refuses to overwrite an unmarked legacy application directory or downgrade a newer executable in the selected installation folder.
- Uninstall requests a safe application exit, restores eligible original shortcuts, and removes installed application files and unchanged newly created shortcuts. It retains the Spotnet 3 profile by default. An attended uninstall offers an unchecked **Permanently remove my Spotnet profile and all personal data** option, showing the exact profile path before anything is deleted. Selecting it permanently deletes `%LOCALAPPDATA%\Spotnet3` after shortcut restoration, including provider credentials, settings, databases, logs, incomplete migrations and backups. Download folders and older Spotnet profiles stored elsewhere are never removed.
- Silent uninstall also retains the profile by default. Administrators can explicitly request the same permanent deletion with `/REMOVEPERSONALDATA=1`; for example, `unins000.exe /VERYSILENT /REMOVEPERSONALDATA=1`. This switch is intentionally opt-in.

After migration, check provider access, spot/comment counts, filters, preferences, and download paths before retiring the old version. The old and new databases are separate copies; changes are not synchronized between them.

For rollback after copy/alongside, exit 3.0 and launch the untouched old installation. Move/replace removes the migrated originals, so it does not offer this immediate data rollback; make your own backup before choosing move if you need it. For a 3.0 data restore, exit Spotnet, preserve the current `Data` folder, then restore a selected complete backup as `Data`. Never restore only the main database file from a snapshot containing WAL/SHM companions. Profile backups may contain credentials and signing keys: keep them private.

Installed builds are marked by `Spotnet.install`. They use stable per-user preferences and do not initialize or use the old Squirrel update feed. Updates to this installation are delivered by running a newer Setup package. Unmarked developer builds retain their previous data-location behavior.

## Build the installer

From the repository root:

```powershell
# Build application, run tests, build helper, verify payload, and compile Setup.
.\build-installer.ps1 -BootstrapCompiler

# Or use an existing Inno Setup 7.1+ compiler.
.\build-installer.ps1 -CompilerPath 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

The bootstrap option downloads the pinned Inno Setup 7.1.0 x64 compiler installer from the publisher's GitHub release, verifies its Authenticode publisher, and uses its portable mode under `artifacts/installer-tools`. Review Inno Setup's own licensing terms for your use. The Microsoft WebView2 bootstrapper is also signature-checked before packaging.

Output includes the setup EXE and a `.sha256` checksum. `artifacts/` is Git-ignored. Compiler downloads and payload staging are intentionally retained for repeat builds and inspection. `-SkipBuild` skips application build/tests for packaging iteration; do not use it as a release-validation substitute.

Packaging takes binaries from the application Release output and bundled data/resources from Git-tracked source paths. It excludes the old Squirrel executable and obsolete browser/player/ZIP/long-path DLLs, and checks AMD64 headers for the app, decoder, WebView2 loader, SQLite interop, and LibVLC. It must not be run against an output directory containing unrelated executable files.

The Spotnet package is **unsigned** until a publisher code-signing certificate is supplied. Windows may show an unknown-publisher/SmartScreen warning. The signature checks on downloaded prerequisites do not sign Spotnet itself. No GitHub release/upload or production installation is performed by the build script.

## Verification

The x64 regression suite covers fresh profiles, data/sidecar preservation, readable SQLite copies, preferences conversion, safe defaults, upgrade backups, unknown destinations, locked files, malformed XML, overlapping paths, excluded queues/caches, discovery, stable settings, graceful-shutdown timeouts, translation completeness, and preparation progress. Move tests verify deferred deletion, changed source/destination rejection and path bounds. Shortcut tests cover alongside preservation, mode retention, checked root Desktop creation, legacy/current/Squirrel matching, repeat upgrades, unrelated/uninstall links, locked files, backup-path bounds, and uninstall recovery/user edits.

First-launch testing also caught and fixed a second SQLite PRAGMA return-value issue in fresh spots-database creation. That path now accepts successful no-row results, verifies the resulting settings/schema version, and applies the page size only to a verified-empty database. A regression test refuses initialization when user tables already exist.

An isolated Inno smoke-test build can be compiled by supplying `/DSmokeTestRoot=<repo>\artifacts\installer-smoke` in addition to the normal compiler defines. It writes only to that workspace test root, uses synthetic Desktop/Programs folders for shortcuts, creates no uninstall registry entry, never closes real Spotnet, and never installs prerequisites. WebView2 must already be present. Run:

```powershell
.\installer\Test-InstallerSmoke.ps1
```

The compiler define must match the script's `-TestRoot` argument (default: `<repo>\artifacts\installer-smoke`); choose a new directory for repeat runs. This checks actual extraction, fresh installation and both launchers, replacement of old/current/Squirrel links without duplicates, unrelated-link preservation, repeat upgrade, backup integrity, uninstall restoring shortcuts while preserving a synthetic profile by default, and a second uninstall permanently deleting that profile only when `/REMOVEPERSONALDATA=1` is supplied. It retains the smoke-test logs but not the deliberately deleted synthetic profile. Never distribute the `*-smoke.exe` artifact: it is configured for that test directory, not real use.

Real-provider operation, graphics/video behavior, non-admin account variations, and migrations from arbitrary historical profiles still require desktop acceptance testing. Passing tests do not certify every legacy profile as compatible.

For four-choice coverage, run the smoke script with `-ClassicMode 0`, `1`, `2`, or `3` (the order listed above), each against a fresh matching compiled smoke root. It seeds an isolated Classic executable/version resource and synthetic profile, then checks detection, copy/move/clean behavior, shortcut names, upgrade mode retention and explicit uninstall deletion. `-Language english` and `-Language dutch` select the installer language. `/SMOKECLASSICMODE` exists only in smoke builds. Production silent first installs require `/FRESH=1` and always preserve Classic, with no unattended move option.

## Implementation references

- [Inno Setup non-administrative installation](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
- [Inno Setup x64 setup executable](https://jrsoftware.org/ishelp/topic_setup_setuparchitecture.htm)
- [Setup preparation and failure handling](https://jrsoftware.org/ishelp/topic_scriptevents.htm)
- [Verifying Inno Setup downloads](https://jrsoftware.org/isdl-verify.php)
- [Microsoft WebView2 runtime distribution](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution)
