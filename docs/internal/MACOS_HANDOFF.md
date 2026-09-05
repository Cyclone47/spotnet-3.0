# Overdracht — macOS-client van Spotnet 3.0

> Dit document is de opdracht voor de volgende ontwikkelaar of AI-agent.
> Peildatum 5 september 2026, branch `macos-client`, HEAD `c3d6f94`.
> Lees dit eerst, en daarna [`MACOS_FEATURE_GAP.md`](MACOS_FEATURE_GAP.md) voor de
> volledige inventarisatie per functie.

---

## 1. Wat dit project is

Spotnet is een Nederlandse Usenet-spotbrowser: gebruikers plaatsen "spots" als
ondertekende NNTP-artikelen, andere gebruikers zoeken erin en downloaden de bijbehorende
binaries. De Windows-client is een WPF-applicatie op .NET 10 en staat op versie **3.0.8.0**.

Deze branch bouwt daarnaast een **native macOS-client** in Avalonia, `Spotnet.Mac`, die
dezelfde database, hetzelfde protocol en dezelfde categorieën gebruikt. Hij is
gefaseerd opgebouwd en staat op **3.0.0-alpha**, dus er ontbreken nog hele
onderdelen — het vertrouwensmodel, Spotnet Remote, spots plaatsen.

### De mappen

```
src/Spotnet/
├── Spotnet/            ← de Windows WPF-client. DIT IS DE REFERENTIE.
├── Spotnet.Mac/        ← de Avalonia macOS-client waar je aan werkt
├── Spotnet.Core/       ← platformneutraal, gedeeld door beide
├── Spotnet.Enc/        ← spot-header-crypto, platformneutraal
├── Spotnet.Tests/      ← 470 tests, Windows
└── Spotnet.Mac.Tests/  ← 324 tests, macOS
android/                ← Android companion-app voor Spotnet Remote
```

### De Windows-client is de norm

Bij elke twijfel over gedrag, tekst, indeling of berekening: **zoek op hoe
`src/Spotnet/Spotnet/` het doet en volg dat.** Niet je eigen ontwerp bedenken. Dat geldt
ook voor dingen die op het eerste gezicht fout lijken — de Age-berekening in
`SpotItem.FormatAge` rapporteert bijvoorbeeld één dag te veel, en dat is bewust
overgenomen met een commentaarregel erbij in plaats van gecorrigeerd.

Nuttige ingangen in de Windows-broncode:

| Onderwerp | Windows-bestand |
|---|---|
| Spotlijst en datavirtualisatie | `Spotnet/DataVirtualization/VirtualList.cs` |
| Query's en filters | `Spotnet/DAL/SpotProvider.cs`, `DAL/Fts5Module.cs` |
| Vertrouwensmodel | `Spotnet/Model/BlackAndWhite.cs`, `Model/SpamReports.cs` |
| Downloader | `Spotnet/Downloader/SpotnetDownloader.cs`, `Downloader/DownloadQueue.cs` |
| Meldingen | `Spotnet/Notifications/NotificationManager.cs` |
| Spotnet Remote | `Spotnet/Remote/` (RemoteServer, RemoteAuthManager, Web/) |
| Spots plaatsen | `Spotnet/Helpers/SpotsUploader.cs`, `Views/Toevoegen.cs` |
| Alle NL-teksten | `Spotnet/Spotnet.Properties.Words.nl.resx` |

Neem UI-labels **letterlijk** over uit `Words.nl.resx`. Verzin geen eigen Nederlandse
termen.

---

## 2. Wat al gedaan is

### Fase 0 — branches samengevoegd ✅ `eacac2c`

`origin/main` (3.0.8.0) is in `macos-client` gemerged. 25 conflicten, allemaal
mechanisch. `Spotnet.Core`, `Spotnet.Mac` en `Spotnet.Mac.Tests` zijn mee verhuisd naar
`src/Spotnet/`. `WindowsTargetFramework` staat op `net10.0-windows`, de platformneutrale
libraries op net8.0.

### Fase 1 — deels ✅

| Item | Status | Commit |
|---|---|---|
| 1. Datavirtualisatie, `take: 100` weg | ✅ | `2c3bc9b` |
| 2. Sorteren serverside, keuze bewaard | ✅ | `2c3bc9b` |
| 3. Automatische sync-timer + retentie | ❌ **open** | — |
| 4. Rolgebaseerde servers (Headers/Download/Upload) | ✅ | `aad428b` |
| 5. SOCKS5 aansluiten | ⚠️ deels | `4ebbd70` |
| 6. Providercatalogus uit `providers.json` | ❌ **open** | — |

Bij item 5: de proxy werkt en is instelbaar in het instellingenvenster, maar de
**statusindicator in de statusbalk ontbreekt nog**. Windows heeft die wel
(`SocksProxyTooltip`, `SocksProxyTooltipViewModel`).

Wat `2c3bc9b` opleverde en waar je op voortbouwt:

* `Spotnet.Mac/DataVirtualization/VirtualSpotCollection.cs` — meldt het volledige aantal
  treffers, houdt ~24 pagina's van 200 rijen vast, deelt placeholders uit die ter plekke
  gevuld worden zodat selectie en scrollpositie blijven staan.
* `Spotnet.Mac/DAL/SpotSort.cs` — de allow-list die een kolomnaam op een databasekolom
  afbeeldt. **Alles wat in de ORDER BY terechtkomt moet hier langs.**
* `QueryByFilterAsync` sorteert altijd met `rowid` als tweede sleutel. Zonder die
  tiebreak levert OFFSET-paginering dubbele of overgeslagen rijen bij gelijke waarden.

### Uiterlijk — `c3d6f94`

De macOS-client had een werkbalk die Windows niet heeft: "Spots Ophalen" linksboven, een
tweede zoekbalk rechtsboven, een tandwiel. Alle drie waren dubbel. Die rij is weg.

---

## 3. Hoe je werkt

1. **Eén fase-item per commit.** Nederlandse commit-boodschap, in de gebiedende wijs of
   als constatering, met uitleg van het *waarom* — kijk naar `git log` voor de toon.
2. **Bouwen en testen na elke wijziging:**
   ```bash
   dotnet build src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj -c Release
   dotnet test src/Spotnet/Spotnet.Mac.Tests/Spotnet.Mac.Tests.csproj
   ```
   324 tests horen groen te zijn. Loopt het aantal terug, dan heb je iets gesloopt.
3. **Schrijf tests voor logica**, niet voor UI-bindingen. Kijk naar
   `VirtualSpotCollectionTests.cs` en `SpotPagingTests.cs` als voorbeeld: injecteerbare
   loaders, echte SQLite-databases in `Path.GetTempPath()`, en tests die de *werkelijke*
   risico's afdekken (paginering met gelijke sorteerwaarden, injectie via
   voorkeurenbestand) in plaats van triviale getters.
4. **Gebruik `System.Progress<T>` niet om volgorde vast te leggen in een test.** Zonder
   SynchronizationContext post die naar de threadpool en wordt de test wisselvallig.
   Er staat een `InlineProgress<T>` in `PostProcessPipelineTests.cs`.
5. **Verzin geen state die van Windows afwijkt.** Kwam je een functie tegen die
   eigenlijk het vertrouwensmodel nodig heeft, laat hem dan liggen tot fase 2 in plaats
   van een eigen variant te bouwen.
6. **Rapporteer eerlijk.** Wat je niet gecontroleerd hebt, zeg je. Zie de openstaande
   punten hieronder voor hoe dat eruitziet.

### Wat je *niet* kunt verifiëren op een Mac

De WPF-client bouwt alleen op Windows (`net10.0-windows` + `UseWPF`) en er is **geen CI
die het dekt** — `.github/workflows/` bevat alleen `verify-providers.yml`. Raak je code
aan die de Windows-client ook gebruikt (`Spotnet.Core`, `Spotnet.Enc`), controleer dan
statisch: bestaat elk type nog, klopt de namespace, staan er geen expliciete
`Compile`-items naar verwijderde bestanden.

---

## 4. Openstaande punten die geen fase zijn

* **De Windows-build is sinds de merge niet gecompileerd.** Statisch nagelopen, niet
  gebouwd. Moet gebeuren vóór `macos-client` ooit naar `main` teruggaat.
* **Er is geen CI voor de Windows-build.** Toevoegen is goedkope verzekering.
* **`Spotnet.Mac.Tests` hangt niet in CI.**
* **30 commits staan niet gepusht** naar `origin/macos-client`. Bewust: de gebruiker
  beslist over pushen.
* **NLog schrijft niets naar schijf** op macOS — `~/Library/Logs/Spotnet/` blijft leeg.
  Dat maakt fouten opsporen bij gebruikers onmogelijk.
* **De Delete-toets doet niets meer in de spotlijst.** Die verwijderde vroeger de rij uit
  de geladen kopie, wat na een verversing terugkwam. Echt verbergen is de blacklist,
  fase 2.
* **De Mac-versie staat op 3.0.0-alpha** en moet naar 3.0.8.0.
* **Portable libraries staan op net8.0**; het supportvenster sluit 2026-11-10. Naar
  net10.0 is één regel in `src/Spotnet/Directory.Build.props` plus een SDK-installatie.
* **Zichtbare afwijkingen van Windows** die nog niet aangepakt zijn: het ZOEKEN-paneel
  mist de radioknoppen Titel/Afzender/Label en de vinkjes Uitgebreid en Favorieten; de
  filterboom mist het "Aangepast ▾"-menu, het Favorieten-knooppunt en de gekleurde
  stippen; de MELDINGEN-knop onderaan het zijpaneel ontbreekt; de titelbalk mist de bel
  en de Remote-indicatoren.

---

## 5. De rest van het stappenplan

Werk de fases **op volgorde** af. De volgorde is niet willekeurig: fase 2 moet vóór fase
7 omdat spots plaatsen sleutelbeheer nodig heeft, en fase 4 vóór fase 5 omdat de
Remote-API meldingen uitserveert.

### Fase 1 afmaken — begin hier

1. **Automatische sync-timer plus retentie.** Windows: `DbAutoUpdateEnabled`,
   `DbAutoUpdateIntervalMin`, `Retention`, `DatabaseMin/Max/Count`. Zonder retentie
   groeit de database onbeperkt.
2. **Providercatalogus uit `providers.json`** in plaats van de vaste lijst. Windows:
   `Model/ProviderCatalogue.cs`, `ProviderCatalogueSource.cs`.
3. **SOCKS5-statusindicator** in de statusbalk afmaken.

### Fase 2 — vertrouwensmodel *(2–3 weken)*

Dit is het onderdeel dat Spotnet Spotnet maakt, en er staat op macOS niets van.

1. Blacklist, whitelist en sleutellijsten ophalen, cachen en op de query toepassen.
2. Handtekeningcontrole aanzetten — `Spotnet.Enc` kan dit al.
3. Spamrapporten uit de `ReportGroup` synchroniseren, `spamreports` en `spamgroup`
   vullen, drempel om spots te verbergen. Die tabellen bestaan al in het Mac-schema maar
   worden nooit gevuld; `SpotDetailViewModel.SpamReportCount` geeft nu hard `0` terug.
4. Filters "alleen vertrouwd", "blacklist verbergen", "erotiek in zoekresultaten".
5. Favorieten: tabel, ster in de lijst, eigen filterknoop.
6. Klacht indienen over een spot.

### Fase 3 — downloader op Windows-niveau *(1–2 weken)*

1. Snelheidslimiet, retries, time-outs, cachegrootte.
2. Downloadschema met start- en eindtijd.
3. Par2 opruimen, bestanden verwijderen bij rij-verwijdering, slaapstand of afsluiten na
   afloop.
4. NZBGet/SABnzbd als externe downloader, `.nzb`-associatie via `Info.plist`, en
   "Open spotlink".

### Fase 4 — meldingen *(1 week)*

`NotificationManager` en `NotificationModels` zijn platformneutraal op de
Windows-toast-aanroep na. Verplaats ze naar `Spotnet.Core`, laat
`MacNotificationService` de bestaande interface implementeren, en bouw een
meldingcentrum-venster in Avalonia plus de bel in de titelbalk.

### Fase 5 — Spotnet Remote *(2–3 weken)*

1. `RemoteServer`, `RemoteConfig`, `RemoteAuthManager`, `RemoteCatalogService`,
   `RemoteQueueService`, `RemoteDiscoveryService`, `RemoteDtos` en `Remote/Web/`
   overzetten naar een gedeeld project. ASP.NET Core draait ongewijzigd op macOS.
2. `SleepPreventer` vervangen door `IOPMAssertionCreateWithName`, of `caffeinate` als
   snelle route, achter een interface in `Spotnet.Core`.
3. `QrCodeHelper` de PNG-bytes van `QRCoder` laten teruggeven; Avalonia maakt daar een
   `Bitmap` van.
4. Instellingenpaneel, koppelvenster en statuslampjes in Avalonia.
5. Controleren dat de bestaande Android-app in `android/` de macOS-host vindt via
   UDP 8771.

### Fase 6 — weergave en media *(2–3 weken)*

1. Thumbnail-weergave en de compacte lijstweergave.
2. Spotpagina in `WKWebView` met de bestaande tabthema's uit `Data/TabThemes/`, UBB en
   het IMDB-blok.
3. Geavanceerd zoeken, zoeksuggesties, kleurregels, lettergroottes.
4. Mediavoorbeeld met `LibVLCSharp.Avalonia` of AVFoundation.
5. Tabbladen onthouden en zichtbaarheid van UI-elementen.

### Fase 7 — spots plaatsen *(1–2 weken)*

`SpotsUploader`, `ThumbsUploader`, sleutelbeheer, bijnaam en avatar, en het venster
"Spot toevoegen". Vereist het vertrouwensmodel uit fase 2. De sleutelopslag
(`userkey`-tabel, `GetUserKeyXmlAsync`) staat er al.

### Fase 8 — distributie *(1 week)*

1. Automatische updates: `UpdateClient`/`UpdateManifest` naar `Spotnet.Core`, een
   macOS-kanaal in `updates/latest.json`, vervanging van de `.app`-bundel na
   SHA-256-controle. Sparkle is het alternatief.
2. Developer ID-ondertekening en notarisatie van DMG en PKG.
3. Universal binary.
4. Native menubalk, Dock-badge en Dock-voortgang.
5. Nederlands/Engels omschakelbaar.

---

## 6. Bouwen en draaien

```bash
# bouwen
dotnet build src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj -c Release

# testen
dotnet test src/Spotnet/Spotnet.Mac.Tests/Spotnet.Mac.Tests.csproj

# draaien — let op: praat met de echte database en nieuwsserver
dotnet run --project src/Spotnet/Spotnet.Mac/Spotnet.Mac.csproj -c Release

# .app-bundel, DMG en PKG
./tools/make_app_bundle.sh          # detecteert Intel of Apple Silicon
./tools/make_installer.sh           # bouwt DMG en PKG in artifacts/
```

Paden op macOS volgen `StandardAppPaths`: database in
`~/Library/Application Support/Spotnet/`, caches in `~/Library/Caches/Spotnet/`, logs in
`~/Library/Logs/Spotnet/`. Een Windows-database kan er één-op-één in worden gekopieerd —
schema, WAL en FTS5 zijn binair compatibel.
