# Spotnet 3.0

*[English version](README_EN.md)*

Een gemoderniseerde Windows Usenet-client, gebouwd rond de vertrouwde Spotnet-ervaring:
spots doorbladeren, zoeken in een lokale index, reacties lezen en plaatsen, NZB-downloads
beheren en media bekijken — in één desktopapplicatie.

Spotnet is een *client*, geen Usenet-dienst. Je regelt zelf toegang tot een newsserver.

| | |
| --- | --- |
| **Versie** | 3.0.8.0 |
| **Platform** | Windows 10/11 x64 · C# / WPF · .NET 10 (meegeleverd in de setup) |
| **Tests** | 470 geslaagd op de x64 Release-host |
| **Gebaseerd op** | Spotnet 2.0 (build 2.0.0.284), met Spotnet 1.8.1 als referentie |

---

## Downloaden

**[Download Spotnet 3.0.8.0 Setup](https://github.com/Cyclone47/spotnet-3.0/releases/download/v3.0.8.0/Spotnet-3.0-x64-Setup.exe)**
· [Release notes en SHA-256](https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.8.0)

De setup werkt zowel voor een nieuwe installatie als voor een upgrade vanaf een bestaand
Spotnet 2.x-profiel. Spotnet wordt netjes afgesloten, je gekozen profiel wordt naar een
aparte 3.0-datamap gekopieerd en bestaande snelkoppelingen worden bijgewerkt. De oude
applicatiebestanden en het bronprofiel blijven ongemoeid; actieve downloadwachtrijen
worden niet overgenomen.

**.NET 10 zit in de setup**, in de installatiemap van Spotnet zelf. Je hoeft .NET niet
apart te installeren en het verschijnt niet als losse vermelding bij *Geïnstalleerde apps*.
Ontbreekt Microsoft Edge WebView2, dan haalt de setup dat bij Microsoft op.

> **De installer is niet ondertekend.** Windows kan een waarschuwing over een onbekende
> uitgever tonen. Er bestaat nog geen certificaat voor dit project.

Lees de [installatie- en migratiegids](docs/INSTALLER.md) voordat je upgradet.

---

## Wat kun je ermee

### Spots en downloads

De vertrouwde WPF-schil met spots doorbladeren, categorieën, filters, favorieten en
reacties; lokale SQLite-databases; de Phuse NNTP-engine met het Spotnet-metadata- en
handtekeningprotocol; de geïntegreerde downloader met meerdere verbindingen; en
mediavoorbeelden met dock- en tabgedrag.

Zoeken loopt via **SQLite FTS5**, met een index die bij de eerste start eenmalig wordt
opgebouwd. Spotpagina's en reacties worden overal weergegeven met **Edge WebView2**.

Er zijn drie stijlen — **Klassiek**, **Modern licht** en **Modern donker** — te kiezen
tijdens de setup of via *Bewerken ▸ Stijl*.

### Spotnet Remote — bedien Spotnet vanaf je telefoon

Spotnet draait een eigen webserver waarmee je de applicatie bedient vanaf elke telefoon,
tablet of computer in je netwerk. De pagina is een PWA met service worker, dus je kunt hem
op je beginscherm zetten en als app gebruiken.

- **Koppelen met een QR-code.** Scan de code op je scherm; het apparaat krijgt een
  pairing-token, zodat je op je telefoon geen wachtwoord hoeft in te typen.
- **Inloggen met alleen een wachtwoord** — sinds 3.0.8.0 zonder gebruikersnaam — gehasht
  met PBKDF2-SHA256, met bescherming tegen herhaalde mislukte pogingen en een overzicht
  van gekoppelde apparaten.
- **Volledige bediening:** zoeken, categorieën en filters, spots en posters bekijken,
  reacties lezen en plaatsen, de downloadwachtrij beheren, de snelheidslimiet aanpassen en
  handmatig een Usenet-synchronisatie starten.
- **Meldingen** uit de meldingenmodule zijn ook op je telefoon zichtbaar.
- **Automatisch vinden in je netwerk:** de client zoekt de pc via een UDP-broadcast, dus je
  hoeft geen IP-adres in te typen.
- **Houd computer wakker:** optionele instelling die voorkomt dat Windows in slaapstand
  gaat zolang Remote actief is.

Remote draait standaard op poort **8770**, met netwerkdetectie op UDP-poort **8771**. Het
staat standaard uit; je zet het aan via *Instellingen ▸ Remote*. Wil je Spotnet ook van
buitenshuis bereiken, dan regel je zelf port-forwarding — stel dan zeker een sterk
wachtwoord in.

### Android companion-app

Naast de webpagina is er een Android-app (`nl.spotnet.companion`, Android 8.0 of nieuwer)
die dezelfde Remote-server gebruikt:

- vindt Spotnet automatisch in je netwerk en koppelt via de QR-code;
- native Android-meldingen wanneer een meldingsregel aanslaat of een download klaar is;
- controleert op de achtergrond op nieuwe meldingen, ook als de app niet openstaat;
- pull-to-refresh om direct een synchronisatie te starten;
- de downloadwachtrij bekijken en beheren.

De APK (`SpotnetCompanion.apk`) staat als bijlage bij
[release v3.0.7.0](https://github.com/Cyclone47/spotnet-3.0/releases/tag/v3.0.7.0); de
broncode staat in [`android/`](android/).

### Meldingen en alerts

Met de meldingenmodule laat je Spotnet zelf in de gaten houden of er iets binnenkomt dat
je interesseert. Je beheert het via het **belletje in de titelbalk**, dat het aantal
ongelezen meldingen toont en het meldingencentrum opent.

Er zijn drie soorten regels:

- **Filter-alert** — meldt nieuwe spots die aan een van je opgeslagen filters voldoen.
- **Trefwoord-alert** — meldt spots met bepaalde trefwoorden, eventueel beperkt tot één
  categorie.
- **Download-melding** — meldt wanneer een download klaar is.

Per regel kies je hoe vaak er gecontroleerd wordt: direct bij elke synchronisatie, elke 15
of 30 minuten, elk uur, elke 8 of 24 uur, of een eigen interval van minimaal 5 minuten.
Een regel is los aan en uit te zetten en je kunt hem meteen testen om te zien wat hij zou
opleveren.

Meldingen komen binnen in het meldingencentrum — met de bijbehorende spots erbij — en
optioneel ook als Windows-melding in het systeemvak. Gelezen markeren, per stuk
verwijderen of alles wissen kan vanuit hetzelfde venster. Dezelfde meldingen zie je terug
in Spotnet Remote en in de Android-app.

### Automatische updates

Een geïnstalleerde Spotnet controleert zelf op een nieuwere versie, biedt die aan met de
release notes erbij en laat de setup zichzelf vervangen. De controle draait op het
splashscherm, vóór de databases opengaan; is de updateserver onbereikbaar, dan wacht
Spotnet maximaal drie seconden. Downloads worden op grootte en SHA-256 gecontroleerd
voordat er iets wordt uitgevoerd, en een afgebroken download wordt hervat.

Handmatig kan het via *Help ▸ Controleren op updates*; uitzetten kan met
`AutoUpdateEnabled` in je profiel. Zie [docs/UPDATES.md](docs/UPDATES.md) voor hoe een
release wordt gepubliceerd.

---

## Waar dit project vandaan komt

Het doel is Spotnet bruikbaar en onderhoudbaar houden: de broncode van de applicatie is
teruggehaald, verouderde onderdelen zijn vervangen en de betrouwbaarheid is verbeterd —
zonder de bestaande workflow overboord te gooien of de compatibiliteit met het
Spotnet-netwerk te breken. Het is een stapsgewijze modernisering, geen herbouw vanaf nul.

**Spotnet 3.0** is de naam van de gemoderniseerde applicatie in deze repository, geen
officiële uitgave van het oorspronkelijke project. Verwijzingen naar 1.8.1 of 2.0 gaan
over de originele versies waaruit dit is teruggehaald; het originele releasepakket ligt in
[`reference/`](reference/). De werknotities in `docs/internal/` noemen de broncode nog
`reconstructed/Spotnet2/` — dat is de oude naam van `src/Spotnet/`.

Achtergrond: [herkomst van de broncode](docs/reference/SOURCE_PROVENANCE.md) ·
[inventaris van de originele binaries](docs/reference/INVENTORY.md).

### De belangrijkste vervangingen

| Onderdeel | Oorspronkelijk | Nu |
| --- | --- | --- |
| Platform | x86, vastgezet door native componenten | x64 (`Prefer32Bit=false`), .NET 10 |
| Ingebouwde webweergave | Awesomium / oude Chromium-integratie | **Microsoft Edge WebView2 1.0.3351.48** |
| Mediaweergave | `Meta.Vlc` | **LibVLCSharp.WPF 3.10.1** met **VideoLAN.LibVLC.Windows 3.0.23.1** (x64) |
| SQLite | Losse legacy-provider en interop-DLL's | **System.Data.SQLite.Core 1.0.119** via NuGet |
| yEnc-decoder | Mixed-mode x86 `Spotnet.Enc.dll` | Beheerde C#-implementatie `Spotnet.Enc` (x64) |
| ZIP-archieven | Ionic.Zip / DotNetZip | `System.IO.Compression` achter de padgecontroleerde `SafeZip` |
| Overige bibliotheken | Losse legacy-DLL's | SharpZipLib 1.4.2 · Newtonsoft.Json 13.0.3 · NLog 5.5.1 · HtmlAgilityPack 1.12.4 |

`phpar2.exe`, `UnRAR.exe` en `7za.exe` zijn nog 32-bits hulpprogramma's. Ze draaien als
losse processen en dwingen Spotnet zelf niet naar 32-bits. Dit is een Windows x64-build,
geen native ARM64- of cross-platformversie — WPF bindt de applicatie aan Windows.

### Betrouwbaarheid en beveiliging

De schrijfbare database gebruikt **write-ahead logging (WAL)** met `synchronous=NORMAL` in
plaats van het oude `synchronous=OFF`, met een busy-timeout en respect voor read-only
gebruik. **Database herstellen** kopieert leesbare records naar een verse database en
bewaart het origineel als back-up — een herstelpoging, geen garantie. WAL is geen
vervanging voor je eigen back-ups.

Verder: NNTP vereist versleuteling bij TLS-verbindingen en valideert servercertificaten;
SQL-waarden en -identifiers zijn geparameteriseerd en gecontroleerd; ZIP-extractie weigert
paden die buiten de doelmap wijzen; externe XML-resolutie staat uit. Dat zijn gerichte
verbeteringen, geen bewijs van een doorstane security-audit.

Details: [database en herstel](docs/DATABASE.md) ·
[NNTP, spot-XML en handtekeningen](docs/PROTOCOL.md).

---

## Zelf bouwen

Je hebt Windows x64 nodig, de **.NET 10 SDK** met de Windows desktop-workload, de
**Microsoft Edge WebView2 Evergreen Runtime**, en NuGet-toegang voor package restore.

```powershell
dotnet build src/Spotnet/Spotnet.sln -c Release
dotnet test src/Spotnet/Spotnet.Tests/Spotnet.Tests.csproj -c Release --no-build
& "./src/Spotnet/Spotnet/bin/Release/net10.0-windows/Spotnet.exe"
```

Houd de **hele uitvoermap** bij elkaar. `Spotnet.exe` alleen is geen werkende distributie:
de native runtimes, beheerde afhankelijkheden, configuratie en resources horen erbij.

De installer bouw je met:

```powershell
./build-installer.ps1 -BootstrapCompiler
```

Resultaat: `artifacts/installer/Spotnet-3.0-x64-Setup.exe`. Ondertekenen kan met
`-SignThumbprint <cert>` voor een certificaat in je eigen certificaatarchief, of met
`-SignCommand` voor een HSM of clouddienst; de build weigert te verpakken als iets
onondertekend blijft. Sluit Spotnet en maak een back-up van je configuratie en databases
voordat je een nieuwe build op een bestaande installatie test.

Meer detail: [bouw- en setupgids](docs/BUILDING.md).

### Een nieuwe versie uitbrengen

Het versienummer staat op één plek — `AssemblyInfo.cs` — en alles wat de gebruiker ziet
moet daarmee meelopen. Welke plekken dat zijn en in welke volgorde je ze bijwerkt, staat
in [docs/VERSIONING.md](docs/VERSIONING.md). Controleren of alles klopt:

```powershell
pwsh ./tools/Sync-Version.ps1
```

De regressietests bewaken hetzelfde: een versie ophogen zonder de release notes, de README
of de updatefeed mee te nemen laat `VersionConsistencyTests` falen.

---

## Indeling van de repository

```text
build-installer.ps1           Bouwt de x64-setup
providers.json                Lijst met Usenet-providers, opgehaald bij het starten
updates/latest.json           Updatefeed voor geïnstalleerde clients

src/Spotnet/
    Spotnet.sln               Hoofdsolution
    Spotnet/                  WPF-applicatie, XAML, resources en data
    Spotnet.Enc/              Beheerde yEnc-decoder
    Spotnet.Tests/            xUnit-regressietests

android/                      Android companion-app (Kotlin)
installer/                    Inno Setup-script en rooktest
reference/                    Het originele Spotnet 2.0.0.284-releasepakket
tools/                        Setup-helper, thema-preview, databasetools, bouwscripts
docs/                         Documentatie, release notes en referentiemateriaal
```

Voor ontwikkeling werk je in `src/Spotnet/` en start je de build-uitvoer
daarvan.

---

## Documentatie

- [Bouwen en opzetten](docs/BUILDING.md)
- [Installer, migratie en terugdraaien](docs/INSTALLER.md)
- [Versienummers bijwerken](docs/VERSIONING.md)
- [Automatische updates publiceren](docs/UPDATES.md)
- [Providerlijst bijwerken](docs/PROVIDERS.md)
- [Databaseschema en herstel](docs/DATABASE.md)
- [NNTP, spot-XML en handtekeningen](docs/PROTOCOL.md)
- [Release notes per versie](docs/releases/)

Referentie over de versies waaruit dit is teruggehaald staat in
[`docs/reference/`](docs/reference/), chronologische werknotities in
[`docs/internal/`](docs/internal/). Die notities bevatten tussenstanden die soms
achterhaald zijn — deze README is het actuele overzicht.

---

## Wat nog open staat

De Release-build slaagt met **470 automatische tests** op de x64-host, zonder bouwfouten.
Dat is een lokaal ijkpunt, geen CI-badge, en zegt niets over productiegereedheid.

Nog te doen:

- Live TLS-verbindingen met een newsserver en een volledige header-import.
- Handmatige controles op WebView2-navigatie, downloads en mediaweergave.
- Hersteltests met echt beschadigde databases, verder dan de automatische fixtures.
- De 32-bits hulpprogramma's (`phpar2.exe`, `UnRAR.exe`, `7za.exe`) vervangen.
- Bredere acceptatietests van de x64-installer en de migratie.
- Een echte import profileren voordat parallelle verificatie of SIMD-decodering zin heeft.

---

## Bijdragen en credits

Bruikbare bijdragen zijn reproduceerbare bugmeldingen, compatibiliteitstests met providers
en runtimes, regressietests en gerichte moderniseringen. Vermeld de build of commit, je
Windows-versie, de stappen om het te reproduceren en geschoonde logs. Publiceer geen
inloggegevens, tokens of persoonlijke database-inhoud.

Houd de Spotnet-protocolcompatibiliteit intact en voeg tests toe bij gedragswijzigingen.
Databasewijzigingen horen migratie en herstel mee te wegen; wijzigingen in native
afhankelijkheden controleer je in de echte x64-uitvoer, op een desktop.

De credits liggen bij de oorspronkelijke Spotnet- en Phuse-auteurs, bij de makers van de
meegeleverde bibliotheken en tools, en bij iedereen die aan deze reconstructie meewerkt.
Zie de [herkomstdocumentatie](docs/reference/SOURCE_PROVENANCE.md).

Er is nog geen `LICENSE`-bestand in de repository; deze README kent geen algemene licentie
toe aan de teruggehaalde applicatie of aan de componenten van derden.

---

## macOS-client (alpha)

Er wordt gewerkt aan een macOS-variant op de branch
**[`macos-client`](https://github.com/Cyclone47/spotnet-3.0/tree/macos-client)**.

Die branch verkeert in **alpha**: bedoeld om uit te proberen en aan mee te werken, niet
voor dagelijks gebruik. Verwacht ontbrekende functies, ruwe randjes en wijzigingen zonder
aankondiging. De Windows-build op `main` blijft de versie die je installeert als je gewoon
Spotnet wilt gebruiken.
