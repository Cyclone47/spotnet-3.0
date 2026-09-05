# macOS-pariteit: wat de Mac-client mist ten opzichte van Windows

Peildatum: 5 september 2026.
Vergeleken: `macos-client` (HEAD `7e4bdc8`) tegen `origin/main` (`be8256e`, Spotnet **3.0.8.0**).

Dit document is de inventarisatie waar de volgende ontwikkelaar of agent mee verder werkt.
Het is geen wensenlijst: elke regel is geverifieerd in de broncode van beide clients.

---

## 1. Uitgangspositie

| | Windows (`origin/main`) | macOS (`macos-client`) |
|---|---|---|
| Versie | 3.0.8.0 | 3.0.0-alpha (`Spotnet.Mac.csproj`) |
| Runtime | .NET 10, meegeleverd in setup | .NET 8, self-contained |
| UI | WPF + MahApps + Edge WebView2 | Avalonia 11.2.5, geen webview |
| Bronmap | `src/Spotnet/` (hernoemd in `be8256e`) | `src/Spotnet/` |
| Regels app-code | ~443 `.cs`/`.xaml` bestanden | 70 bestanden, ~13.100 regels |
| Tests | 470 | 256 (alle groen) |
| Build | — | schoon, 0 fouten, 62 analyzer-warnings |

De branches lopen 15 commits (main) tegen 9 commits (macos-client) uiteen vanaf
merge-base `65a484f`. Alles wat main sinds die basis heeft toegevoegd — 3.0.6.8,
3.0.7.0, 3.0.8.0, de Android-app en de mapstructuurwijziging — zit **niet** in de
macOS-branch.

### Merge-status

`git merge-tree HEAD origin/main` geeft **25 conflicten**, allemaal mechanisch:

* 21× `rename/rename` — de bestanden die deze branch naar `Spotnet.Core` verplaatste
  (`ServerInfo`, `NntpSettings`, `Socks5Client`, `YEnc*`, `SaveSpotsRow`, …) zijn op
  main meeverhuisd naar `src/Spotnet/Spotnet/Spotnet/Model/`.
* 2× `rename/delete` — `ArticleWatermark.cs` en `UnpackPasswordDetector.cs`, op deze
  branch verplaatst naar `Spotnet.Core`, op main gewoon meeverhuisd.
* 2× inhoudelijk — `Spotnet.csproj` en `Spotnet.Enc.csproj`.

Er zijn **geen** inhoudelijke conflicten in logica. De merge is een dagtaak, geen risico.

---

## 2. Wat de macOS-client al kan

Dit is echt af en getest; het hoeft niet opnieuw.

**Data en zoeken**
* SQLite via `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3`, met FTS5.
* Schema binair identiek aan Windows (`spots`, `search`, `spamreports`, `spamgroup`,
  `userinfo`, `userkey`, `comments`, plus een eigen `commentindex`). Een Windows-database
  kan één-op-één worden gekopieerd.
* Filterexpressie-compiler voor de Spotnet-filtertaal, inclusief `[SN:NEW]` en `[SN:DATE]`.
* Database controleren & herstellen (WAL-checkpoint, reindex, integriteitscheck,
  FTS5-rebuild, vacuum).

**Netwerk**
* NNTP over TLS met AUTHINFO, XOVER-sync van `free.pt`, header-decodering via
  `Spotnet.Enc`, poster-identiteit en watermerk-controle.
* Spot-body en cover-afbeelding ophalen.
* Reactie-index op `free.usenet`, reacties lezen **en plaatsen** (UBB-knoppen, smileys,
  kleur, voorbeeldweergave).
* NZB ophalen en parsen.

**Downloaden en nabewerking** — hier is macOS op onderdelen sterker dan Windows:
* Ingebouwde downloader met meerdere parallelle verbindingen en yEnc-decodering.
* Wachtrij: pauzeren, hervatten, annuleren, omhoog/omlaag, verwijderen met of zonder
  bestanden, geschiedenis die de sessie overleeft.
* Drie downloadmodi (ingebouwd / NZB openen / NZB opslaan).
* Nabewerking volledig in-process, **zonder externe binaries**: split-join,
  par2-verificatie, Reed-Solomon-reparatie (`Galois16`, `Par2Repairer`), uitpakken met
  SharpCompress (rar4/rar5 multi-volume, zip, 7z, tar, met wachtwoord), proactieve
  wachtwoorddetectie uit RAR/ZIP-headers. Windows heeft hiervoor `phpar2.exe`,
  `UnRAR.exe` en `7za.exe` nodig, alle drie nog 32-bits.

**UI en platform**
* Menu, werkbalk, filterboom met de meegeleverde `FiltersAdvanced.xml`, eigen filters,
  spotlijst, tabbladen (Overzicht + Downloads + per spot), statusbalk.
* Drie thema's (Klassiek, Modern Licht, Modern Donker), onboarding-wizard,
  instellingenvenster, release notes-venster.
* Keychain voor wachtwoorden, Notification Center via `osascript`, `StandardAppPaths`
  volgens macOS-conventies.
* `.app`-bundel, DMG en PKG met SHA256SUMS, x64 en arm64.

---

## 3. Wat ontbreekt

Gesorteerd op hoe hard het pijn doet in dagelijks gebruik.

### 3.1 Blokkerend voor normaal gebruik

| Onderwerp | Windows | macOS |
|---|---|---|
| **Datavirtualisatie** | `VirtualList` / `VirtualListItem`, pagineert over de hele database | `QueryByFilterAsync(take: 100)` — hard afgekapt op 100 spots, geen paginering, geen scroll-laden |
| **Meerdere servers** | `ServerList`, `VirtualServer`, prioriteit, fill-servers, `HealthChecker` | één server uit `servers.xml`, geen fallback |
| **Automatische sync** | `DbAutoUpdateEnabled` + `DbAutoUpdateIntervalMin`, timer in de statusbalk | alleen handmatig "Spots Ophalen" |
| **Retentie / opruimen** | `Retention`, `DatabaseMin/Max/Count`, database opnieuw opbouwen | niets; de database groeit onbeperkt |
| **Sorteren en kolommen** | `SortColumn`, `SortDirection`, `Columns`, `ColumnsSize`, per tab bewaard | DataGrid sorteert in-memory over de geladen 100, niets wordt bewaard |
| **SOCKS5-proxy** | `UseSocksProxy` + statusindicator in de statusbalk | `Socks5Client` zit al in `Spotnet.Core` maar is nergens aangesloten |

### 3.2 Vertrouwensmodel — volledig afwezig

Dit is het onderdeel dat Spotnet Spotnet maakt, en er staat op macOS niets van.

* Blacklist en whitelist (`SpotBlacklistURL`, `SpotWhitelistURL`, `BlacklistURL`,
  `WhitelistURL`, `KeysURL`, `DownloadExternalLists`, `ExternalListsUpdateInterval`).
* Handtekeningcontrole (`CheckSignatures`) en de vertrouwde-posterlijst.
* Spamrapporten: ophalen uit de `ReportGroup`, `NumOfSpamReportsToSpotHide`,
  `SpamReportsGrid`. De tabellen `spamreports` en `spamgroup` bestaan in het Mac-schema
  maar worden nooit gevuld; `SpotDetailViewModel.SpamReportCount` geeft hard `0` terug.
* `HideBlacklistedSpots`, `ShowTrustedOnlyEnabled`, `ShowEroticaInSearchResults`.
* Klacht indienen over een spot (`ComplainToTheSpot`).
* Favorieten — bestaat op Windows in de database én in de Remote-API (`/favorites`),
  op macOS nergens.

### 3.3 Spots plaatsen

De hele publicatiekant ontbreekt: `Views/Toevoegen`, `SpotsUploader`, `ThumbsUploader`,
`UserKeyHelper`, `ExternalSigning`, `Nickname`, `Avatar`/`AvatarFolder`. macOS kan
lezen en reageren, niet posten. De sleutelopslag (`userkey`-tabel,
`GetUserKeyXmlAsync`) is er wel al.

### 3.4 Downloader-functies

* Snelheidslimiet (`SpeedLimit`, `ChangeDownloadSpeedLimitWindow`).
* Downloadschema met start- en eindtijd (`DownloaderSchedule`, `DownloaderStartTime`,
  `DownloaderEndTime`).
* Retries en retry-interval, `DownloaderCacheSizeMb`, `ConnectionTimeout`,
  `DataReceivingTimeout`, `ConnectionIdleTimeout`.
* PC afsluiten na afloop (`ShutdownComputerDialog`) — op macOS zou dit slaapstand of
  afsluiten via `osascript` worden.
* `RemovePar2FilesAfterDownload`, `RemoveFilesOnDownloadRemove` als instelling.
* Externe downloader: NZBGet/SABnzbd (`NzbGetDownloader`, `NzbGetRarScanner`, plus 16
  `NzbGet*`-instellingen), `MenuNzbFilesAssociate`, "Open NZB", "Open spotlink".

### 3.5 Weergave en spotpagina

* Drie lijstweergaven: zonder details, met details, thumbnails (`SpotsThumbnailsView`,
  `VirtualizingWrapPanel`). macOS heeft alleen de detailtabel.
* Kolomkeuze, lettergrootte lijst en spot, kleurregels (`ColoringSpots`,
  `ColoringFilters`), `AutoShowNewSpotsInTheList`.
* De spotpagina zelf: Windows rendert HTML in WebView2 met acht meegeleverde
  tabthema's (`Data/TabThemes/`), UBB, IMDB-blok, newsreader-informatie,
  `LoadImageOnSpotTab`, `HideCommentsWithLinks`, `IsEnabledBadWordsFilterForComment`.
  macOS toont een eenvoudiger native weergave.
* Geavanceerd zoeken (aparte schakelaars voor subject/sender/tag), zoeksuggesties
  (`GoogleSuggest`), `MaxResults`.
* Tabbladen onthouden (`SaveTabs`), zichtbaarheid van menu/werkbalk/statusbalk/filters,
  linkerpaneelbreedte, `Ctrl+1..9` tabnavigatie.
* Taalkeuze NL/EN (`UserLanguage`). De Mac-client is hardcoded Nederlands.
* Providercatalogus uit `providers.json` met VPN-advies; macOS heeft een vaste lijst
  van providernamen in `SettingsViewModel`.

### 3.6 Media

`PlayerControl`, `VlcPlayer` (LibVLCSharp), playlist, volledig scherm, volumeregeling,
`PlayerVolume`. Op macOS niets. Dit is te doen met `LibVLCSharp.Avalonia` of met
AVFoundation.

### 3.7 Nieuw op Windows sinds de macOS-branch aftakte

**Spotnet Remote (3.0.7.0 / 3.0.8.0)** — de grootste losse module.
`RemoteServer` draait op ASP.NET Core minimal API (poort 8770) en is daarmee vrijwel
volledig portable. 26 endpoints: `/status`, `/spots`, `/spots/{id}`,
`/spots/{id}/comments`, `/spots/{id}/image`, `/spots/{id}/download`, `/spots/sync`,
`/queue` met pauze/hervat/speedlimit, `/favorites`, `/filters`, `/notifications`,
`/auth/login`, `/auth/pair`, `/auth/devices`.
Bijbehorend: `RemoteConfig` (gekoppelde apparaten, PBKDF2-SHA256), `RemoteAuthManager`,
`RemoteCatalogService` (965 regels), `RemoteQueueService`, `RemoteDiscoveryService`
(UDP-broadcast op 8771), de PWA in `Remote/Web/` met service worker, en
`RemotePairingWindow` / `SettingsForRemote` / `RemoteStatusTitleBarControl`.
Alleen twee dingen zijn Windows-gebonden: `SleepPreventer` (`kernel32
SetThreadExecutionState`, op macOS `IOPMAssertionCreateWithName` of `caffeinate`) en
`QrCodeHelper` (`BitmapSource`, op Avalonia een `Bitmap` uit dezelfde `QRCoder`-bytes).

**Meldingsysteem (3.0.7.0)** — `NotificationManager` (576 regels) en
`NotificationModels`. Drie regeltypen (filter, trefwoord, download-gereed), intervallen
van "direct bij elke sync" tot 24 uur of eigen interval vanaf 5 minuten, bundeling per
controle, meldingcentrum, bel met ongelezen-teller, Windows-toasts.
macOS heeft alleen losse banners bij "download klaar" en "database hersteld".

**Android companion-app** — `android/`, `nl.spotnet.companion`, praat met dezelfde
Remote-server. Zodra Remote op macOS draait werkt de bestaande app er ook mee; er is
geen macOS-werk voor nodig behalve netwerkdetectie testen.

**Automatische updates** — `AppUpdater`, `UpdateClient`, `UpdateManifest`,
`UpdateDecision`, `StartupUpdateGate`, `ReleaseNotesFeed`, `SpotnetUpdateVerifier`
(grootte + SHA-256, hervatbare download), controle op het splashscherm vóór de database
opengaat, met een time-out van drie seconden.
macOS heeft **geen** enkele updatevoorziening. De client blijft op de versie die is
geïnstalleerd.

**Overig van main** — `JsonSpotCache` (vervangt de binaire cache),
`StartupWindowLauncher`, de titelbalkkleuren van 3.0.7.0, en de herstructurering naar
`src/Spotnet/`.

### 3.8 macOS-specifiek werk dat nog openstaat

* Native menubalk. Het menu zit nu ín het venster; `Cmd+,`, `Cmd+W`, `Cmd+Q` horen in
  de macOS-menubalk via `NativeMenu`.
* Code signing en notarisatie. DMG en PKG zijn ongetekend; Gatekeeper blokkeert ze.
* Universal binary (`lipo` van x64 en arm64) in plaats van twee losse artefacten.
* Dock-badge voor ongelezen meldingen, en dock-voortgang tijdens downloaden
  (Windows heeft `TaskbarItemInfo`).
* `OutputType` staat op `WinExe`; werkt, maar hoort `Exe` te zijn.

---

## 4. Stappenplan

De volgorde is gekozen op afhankelijkheid: elke fase heeft de vorige nodig.

### Fase 0 — branches samenvoegen *(1 dag)*

Zonder dit blijft elke nieuwe Windows-release de kloof vergroten.

1. Merge `origin/main` in `macos-client`. Los de 25 conflicten mechanisch op: houd de
   `Spotnet.Core`-versie van elk verplaatst bestand, en verwijder het duplicaat onder
   `src/Spotnet/Spotnet/Spotnet/Model/`.
2. Verplaats `Spotnet.Core`, `Spotnet.Mac` en `Spotnet.Mac.Tests` mee naar `src/Spotnet/`.
3. Werk `Spotnet.sln`, `MACOS_DEVELOPMENT.md`, `tools/make_app_bundle.sh` en
   `tools/make_installer.sh` bij op de nieuwe paden.
4. Laat de Windows-client tegen `Spotnet.Core` bouwen in plaats van tegen zijn eigen
   kopieën, zodat er nog maar één `ServerInfo`, één `Socks5Client` en één `YEncDecoder` is.
5. Zet de Mac-versie op 3.0.8.0 en voeg `Spotnet.Mac.Tests` toe aan CI.

### Fase 1 — de client bruikbaar maken bij echte volumes *(1–2 weken)*

1. Datavirtualisatie: paginering of `IncrementalLoadingCollection` op de spotlijst; de
   `take: 100` moet weg.
2. Sorteren en kolommen serverside, met de keuze bewaard in `preferences.json`.
3. Automatische sync-timer plus retentie/opruimbeleid.
4. Meerdere servers met prioriteit en fallback (`ServerList`-model uit `Spotnet.Core`).
5. `Socks5Client` aansluiten op `UsenetConnection` + statusindicator.
6. Providercatalogus uit `providers.json` in plaats van de vaste lijst.

### Fase 2 — vertrouwensmodel *(2–3 weken)*

1. Blacklist, whitelist en sleutellijsten ophalen, cachen en toepassen op de query.
2. Handtekeningcontrole aanzetten (`Spotnet.Enc` kan dit al).
3. Spamrapporten uit de `ReportGroup` synchroniseren en `spamreports`/`spamgroup` vullen;
   drempel om spots te verbergen.
4. Filters "alleen vertrouwd", "blacklist verbergen", "erotiek in zoekresultaten".
5. Favorieten: tabel, ster in de lijst, eigen filterknoop.
6. Klacht indienen over een spot.

### Fase 3 — downloader op Windows-niveau *(1–2 weken)*

1. Snelheidslimiet, retries, time-outs, cachegrootte.
2. Downloadschema met start- en eindtijd.
3. Par2 opruimen, bestanden verwijderen bij rij-verwijdering, slaapstand of afsluiten
   na afloop.
4. NZBGet/SABnzbd als externe downloader, plus `.nzb`-bestandsassociatie via
   `Info.plist` en "Open spotlink".

### Fase 4 — meldingen *(1 week)*

`NotificationManager` en `NotificationModels` zijn platformneutraal op de
Windows-toast-aanroep na. Verplaats ze naar `Spotnet.Core`, laat `MacNotificationService`
de bestaande `ISpotnetNotifier` implementeren, en bouw een meldingcentrum-venster in
Avalonia plus een bel in de werkbalk.
Dit vóór fase 5, omdat de Remote-API meldingen uitserveert.

### Fase 5 — Spotnet Remote *(2–3 weken)*

1. `RemoteServer`, `RemoteConfig`, `RemoteAuthManager`, `RemoteCatalogService`,
   `RemoteQueueService`, `RemoteDiscoveryService`, `RemoteDtos` en `Remote/Web/`
   overzetten naar een gedeeld project. ASP.NET Core draait ongewijzigd op macOS.
2. `SleepPreventer` vervangen door `IOPMAssertionCreateWithName` (of `caffeinate` als
   snelle route) achter een interface in `Spotnet.Core`.
3. `QrCodeHelper` de PNG-bytes van `QRCoder` laten teruggeven; Avalonia maakt daar een
   `Bitmap` van.
4. Instellingenpaneel, koppelvenster en statuslampjes in Avalonia.
5. Controleren dat de bestaande Android-app de macOS-host vindt via UDP 8771.

### Fase 6 — weergave en media *(2–3 weken)*

1. Thumbnail-weergave en de compacte lijstweergave.
2. Spotpagina in `WKWebView` (`Avalonia.WebView` / `Avalonia.Controls.WebView`) met de
   bestaande tabthema's, UBB en IMDB-blok.
3. Geavanceerd zoeken, zoeksuggesties, kleurregels, lettergroottes.
4. Mediavoorbeeld met `LibVLCSharp.Avalonia` of AVFoundation.
5. Tabbladen onthouden en zichtbaarheid van UI-elementen.

### Fase 7 — spots plaatsen *(1–2 weken)*

`SpotsUploader`, `ThumbsUploader`, sleutelbeheer, bijnaam en avatar, en het
"Spot toevoegen"-venster. Vereist een werkend vertrouwensmodel uit fase 2.

### Fase 8 — distributie *(1 week)*

1. Automatische updates: `UpdateClient`/`UpdateManifest` naar `Spotnet.Core`, een
   macOS-kanaal in `updates/latest.json`, en vervanging van de `.app`-bundel na
   SHA-256-controle. Sparkle is het alternatief als een eigen updater te veel wordt.
2. Developer ID-ondertekening en notarisatie van DMG en PKG.
3. Universal binary.
4. Native menubalk, Dock-badge en Dock-voortgang.
5. Nederlands/Engels omschakelbaar.

---

## 5. Beoordeling

Ruwe schatting: **11 tot 16 weken** voltijds voor volledige pariteit met 3.0.8.0,
waarvan fase 0 tot en met 3 (ongeveer vijf weken) de macOS-client van "alpha" naar
"dagelijks bruikbaar" brengt.

Twee dingen zijn urgent en goedkoop, en zouden vóór al het andere moeten:

* **Fase 0**, omdat de branches anders verder uit elkaar lopen.
* **De `take: 100`-limiet**, omdat de client daardoor bij een gevulde database
  simpelweg de verkeerde spots toont.

Eén ding is duurder dan het lijkt: het **vertrouwensmodel**. Zonder blacklist,
handtekeningcontrole en spamrapporten toont de macOS-client spots die de Windows-client
zou verbergen. Dat is geen ontbrekende functie maar een verschil in wat de gebruiker te
zien krijgt, en het verdient voorrang boven Remote.
