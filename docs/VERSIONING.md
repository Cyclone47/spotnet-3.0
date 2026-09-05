# Version numbers

One version number is edited by hand. Everything that *can* read it back does so; what is
left over is prose and published data, which no reference can generate for you.

## The single source

```
src/Spotnet/Spotnet/Properties/AssemblyInfo.cs

    [assembly: AssemblyVersion("3.0.8.0")]
    [assembly: AssemblyFileVersion("3.0.8.0")]
```

The project sets `GenerateAssemblyInfo=false`, so this file — not the `.csproj` — is where
the number lives. Both attributes always carry the same four components.

## What follows it on its own

Nothing to do here. These read the version rather than repeating it, and were already built
that way:

| Place | How it gets the version |
| --- | --- |
| `installer/Spotnet3.iss` | `GetVersionNumbersString(PayloadDir + "\Spotnet.exe")` at compile time, feeding `AppVersion`, `VersionInfoVersion` and `VersionInfoProductVersion` |
| Help ▸ About | `AppHelper.AppVersion`, read from the running assembly |
| Release Notes tab | `AppHelper.AppVersion`; `ReleaseNotesFeed` also injects a heading for the running version when the changelog does not contain one |
| Update client | Compares the manifest against the running assembly version |
| Installed Apps entry, file properties | The installer fields above |

If you find yourself typing a version into any of these, something has gone wrong.

## What has to be written

Prose about a release cannot be derived from a number, and the update feed describes what
is *published* rather than what is being built.

1. **`docs/releases/v<version>.md`** — the changelog, English and Dutch, in that file.
2. **`Resources/ReleaseNotes/whatsnew.html`** and **`whatsnew.nl.html`** — a new
   `<section>` on top. Give it the `gh-tag` badge, and replace the previous release's badge
   with its release date: only the newest entry may look current.
3. **`Spotnet.Properties.Resources.resx`**, entries `whatsnew` and `whatsnew_nl` — the same
   two documents, HTML-escaped. **This copy is what ships**; the `.html` files next to it
   are the editable source. Changing only one of the two is the mistake this page exists
   to prevent.
4. **`README.md` and `README_EN.md`** — the version row, the download link and the release
   tag link.
5. **`updates/latest.json`** — only *after* the GitHub release exists, because it needs the
   asset's real size and SHA-256. See [UPDATES.md](UPDATES.md); the `clientUpdate` gate is
   what actually offers the build to users.

## The tooling

```powershell
pwsh ./tools/Sync-Version.ps1
```

Verifies every item above against `AssemblyInfo.cs` and exits non-zero on a mismatch. The
update feed is reported but never failed on — it lags on purpose until a release is
published.

```powershell
pwsh ./tools/Sync-Version.ps1 -Set 3.0.9.0
```

Bumps `AssemblyInfo.cs` and rewrites the README version rows and links, then prints what
still needs writing by hand.

`VersionConsistencyTests` in the regression suite enforces the same rules, so a version
bump that leaves the release notes, the READMEs or the shipped resx behind fails the build
instead of reaching a user.

## Releasing, end to end

1. `pwsh ./tools/Sync-Version.ps1 -Set <version>`.
2. Write the release notes: `docs/releases/`, both `whatsnew` documents, both resx entries.
3. `pwsh ./tools/Sync-Version.ps1` and run the test suite. Both must be clean.
4. `pwsh ./build-installer.ps1` — builds, tests and packages the setup.
5. Create the GitHub release `v<version>` and attach `Spotnet-3.0-x64-Setup.exe`.
6. Update `updates/latest.json` with the published URL, size and SHA-256, set
   `clientUpdate` to `1`, commit and push.

Steps 1–4 can be repeated freely; nothing reaches an installed client until step 6 lands.
