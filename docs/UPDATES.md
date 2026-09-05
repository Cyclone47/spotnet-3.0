# Automatic updates

An installed Spotnet checks this repository for a newer build, offers it, downloads it and
lets Setup replace itself. Everything the client needs is one file on the default branch:

    updates/latest.json

The installer itself is far too large for the repository and lives as an asset on the
matching GitHub release.

## The manifest

```json
{
  "schema": 1,
  "clientUpdate": 1,
  "version": "3.0.7.0",
  "minimumVersion": "3.0.0.0",
  "forced": 0,
  "url": "https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.7.0/Spotnet-3.0-x64-Setup.exe",
  "size": 103175160,
  "sha256": "7ffda174337b40ed794dc887fa3d549ae7749ad05894aa56ddf546f542a392a2",
  "releaseNotesUrl": "https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.7.0"
}
```

| Field | Meaning |
| --- | --- |
| `schema` | Manifest format. A client ignores anything newer than it understands. |
| `clientUpdate` | **The release gate.** `0` means clients read the entry and do nothing. Nobody is offered the build until this is `1`. `true`/`false` work too. |
| `version` | The version being published. Three components are accepted; `3.0.7` and `3.0.7.0` mean the same thing. |
| `minimumVersion` | Anything older than this is told the update is required. |
| `forced` | `1` marks this release required for everyone: the prompt loses its Skip button and the release is offered again even to someone who skipped it. `0`, or absent, is the default. |
| `url` | The installer. Must be `https` on GitHub; anything else is refused. |
| `size` | Exact byte count of the installer. |
| `sha256` | Its SHA-256. A download that does not match is deleted, never run. |
| `releaseNotesUrl` | Optional. Opens the browser during startup, or Spotnet's Release Notes tab once the main window is available. |

## Publishing a release

1. `pwsh .\build-installer.ps1`
   It builds, runs the tests, packages `artifacts/installer/Spotnet-3.0-x64-Setup.exe` and
   writes `artifacts/installer/latest.json` with the real version, size and hash filled in.
2. Create the GitHub release `v<version>` and attach that `Spotnet-3.0-x64-Setup.exe`.
3. Copy `artifacts/installer/latest.json` over `updates/latest.json`.
4. Check the URL matches the tag you just created, then **set `clientUpdate` to `1`**.
5. Commit and push to the default branch.

The gate exists so that steps 1 to 3 can happen in any order and at any pace. A build can
be uploaded, installed by hand and lived with for a week; no client is offered it until the
commit in step 5 lands. Setting `clientUpdate` back to `0` stops the offer for anyone who
has not taken it yet.

## What the client does

- Only an **installed** copy updates itself. A build running out of a development output
  has no Setup that could replace it, and is left alone.
- With automatic updates enabled, the first check runs on the splash screen under
  "Checking for updates…", before constructing the main window, opening databases or
  asking for provider details. The lookup is asynchronous and cancelled after three
  seconds if unavailable. An available update opens a modal decision over the splash;
  startup waits for that decision, with no timeout on the user or their download.
  Installing hands control to Setup without starting the main window. Later/Skip
  continue startup. A failed/cancelled download leaves the decision open for retry
  or Later. Failed checks retry after one minute once the app starts; successful
  checks repeat after four hours. Help ▸ *Check for updates* still works on demand.
- `raw.githubusercontent.com` serves the manifest with a five minute CDN lifetime, and no
  request header or query parameter shortens it. A freshly pushed release can therefore
  take up to five minutes to become visible to clients. Nothing is wrong when that
  happens; wait for the next check.
- The prompt shows the new version, the download size and release notes. At startup
  the notes open in the default browser while the decision remains open. After startup
  the prompt closes and opens Spotnet's Release Notes tab.
  *Update now* downloads, *Later* asks again next time, *Skip this version* suppresses that
  exact version until a newer one appears. A `forced` release has no Skip.
- The download shows progress and can be cancelled. A partial file is resumed on the next
  attempt rather than started over, and a finished file that still verifies is not fetched
  twice.
- The size and SHA-256 are checked before anything is run. A mismatch deletes the file and
  reports it; nothing unverified is ever executed.
- Setup then runs as
  `/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH /LOG=...`
  and Spotnet closes so its files can be replaced. `/SILENT` keeps Setup's own progress
  window on screen, and `/RELAUNCH` — Spotnet's own switch — starts the application again
  when the install finishes. The install log lands next to the download, in
  `%LOCALAPPDATA%\Spotnet3\Updates`.

## Rehearsing a release locally

The client accepts one address that is not GitHub: loopback. It can only ever reach the
machine it is already running on, so it grants nothing to anyone, and it lets the whole
flow be tried before a real release exists.

```
cd artifacts/installer
python -m http.server 8080
```

Point one client at it, in `%LOCALAPPDATA%\Spotnet3\Data\user.config`:

```xml
<setting name="UpdateManifestUrl" serializeAs="String"><value>http://127.0.0.1:8080/latest.json</value></setting>
```

with that manifest's `url` pointing at `http://127.0.0.1:8080/Spotnet-3.0-x64-Setup.exe`,
its `version` set above the running one, and `clientUpdate` set to `1`. Clear the setting
afterwards to go back to the published manifest.

## Signing

The installer is unsigned today, so Windows SmartScreen warns about it on first run —
during an automatic update that warning appears with no window to explain it. Signing the
installer (`build-installer.ps1 -SignThumbprint <thumbprint>`) removes that, and is worth
doing before updates go out to anyone but you.

## Turning it off

`AutoUpdateEnabled` in the profile's `user.config` stops the periodic check. Help ▸ *Check
for updates* still works.
