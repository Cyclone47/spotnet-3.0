; Compile using build-installer.ps1 with Inno Setup 7.1 or newer.
#ifndef PayloadDir
  #error PayloadDir must be supplied by build-installer.ps1
#endif
#define AppVersion GetVersionNumbersString(PayloadDir + "\Spotnet.exe")
#ifndef SetupSuffix
  #define SetupSuffix ""
#endif

[Setup]
AppId={{76851D20-501E-45B0-9869-853F814BE60E}
AppName=Spotnet 3.0 (64-bit)
AppVersion={#AppVersion}
AppPublisher=Spotnet 3.0 contributors
AppPublisherURL=https://github.com/Cyclone47/spotnet-3.0
AppSupportURL=https://github.com/Cyclone47/spotnet-3.0/issues
#ifdef SmokeTestRoot
DefaultDirName={#SmokeTestRoot}\App
#else
DefaultDirName={localappdata}\Programs\Spotnet3
#endif
DefaultGroupName=Spotnet 3.0
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
#ifdef SmokeTestRoot
UsePreviousAppDir=no
#else
UsePreviousAppDir=yes
#endif
OutputDir={#OutputDir}
#ifdef SmokeTestRoot
OutputBaseFilename=Spotnet-3.0-x64-Setup-smoke
CreateUninstallRegKey=no
#else
OutputBaseFilename=Spotnet-3.0-x64-Setup{#SetupSuffix}
#endif
SetupIconFile=..\src\Spotnet\Spotnet\Resources\ImagesInternal\spotnet.ico
UninstallDisplayIcon={app}\Spotnet.exe
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=Spotnet.exe
RestartApplications=no
SetupMutex=Spotnet3Setup
; Complete file provenance. Inno leaves copyright and original file name empty by
; default, and a packer-produced binary with blank provenance fields is one of the
; things generic "bundler" heuristics weigh.
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany=Spotnet 3.0 contributors
VersionInfoProductName=Spotnet 3.0 (64-bit)
VersionInfoDescription=Spotnet 3.0 (64-bit) Setup
VersionInfoCopyright=Copyright (C) 2014-2026 Spotnet 3.0 contributors
#ifdef SmokeTestRoot
VersionInfoOriginalFileName=Spotnet-3.0-x64-Setup-smoke.exe
#else
VersionInfoOriginalFileName=Spotnet-3.0-x64-Setup{#SetupSuffix}.exe
#endif
UninstallDisplayName=Spotnet 3.0 (64-bit)
; Authenticode signing is opt-in: build-installer.ps1 defines SignSetup and supplies the
; named "spotnet" tool only when a certificate was given. Without it the compiler must
; not know about a sign tool at all, or it refuses to build.
#ifdef SignSetup
SignTool=spotnet
; The uninstaller is a separate executable and gets its own warning if left unsigned.
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[CustomMessages]
english.LaunchSpotnet=Launch Spotnet 3.0
dutch.LaunchSpotnet=Spotnet 3.0 starten
english.DotNetRequired=.NET Framework 4.7.2 or later is required. Install it from Microsoft, restart Windows if requested, then run Setup again. No Spotnet files have been changed.
english.DotNetRuntimeFailed=Installing the .NET Desktop Runtime did not complete. Install it from Microsoft and run Setup again. No Spotnet data has been migrated.
english.StatusDotNet=Installing the .NET Desktop Runtime. Microsoft's own installer shows its progress; this usually takes a few minutes...
english.StatusDotNetPrepare=Unpacking the .NET Desktop Runtime installer that ships with Setup...
dutch.DotNetRequired=.NET Framework 4.7.2 of nieuwer is vereist. Installeer dit via Microsoft, start Windows opnieuw op indien gevraagd en voer Setup daarna opnieuw uit. Er zijn geen Spotnet-bestanden gewijzigd.
dutch.DotNetRuntimeFailed=De installatie van de .NET Desktop Runtime is niet voltooid. Installeer deze via Microsoft en voer Setup opnieuw uit. Er zijn geen Spotnet-gegevens gemigreerd.
dutch.StatusDotNet=.NET Desktop Runtime installeren. Microsofts eigen installatieprogramma toont de voortgang; dit duurt meestal enkele minuten...
dutch.StatusDotNetPrepare=Het meegeleverde installatieprogramma voor de .NET Desktop Runtime uitpakken...
english.ShortcutGroup=Shortcuts:
dutch.ShortcutGroup=Snelkoppelingen:
english.ProgramsIcon=Add shortcut to Start Menu
dutch.ProgramsIcon=Snelkoppeling toevoegen aan het Startmenu
english.DesktopIcon=Add shortcut to Desktop
dutch.DesktopIcon=Snelkoppeling toevoegen aan het bureaublad
english.SpaceMemo=Profile copy: %1 of data; %2 free on %3.
dutch.SpaceMemo=Profielkopie: %1 aan gegevens; %2 vrij op %3.
english.SpaceMemoUpgrade=Pre-upgrade backup: %1 of data; %2 free on %3.
dutch.SpaceMemoUpgrade=Back-up vóór upgrade: %1 aan gegevens; %2 vrij op %3.
english.SpaceShort=Not enough free disk space. The profile copy and its safety margin need %1 on %2, and only %3 is free. Free up space or choose a smaller source, then try again. Nothing has been changed.
dutch.SpaceShort=Onvoldoende vrije schijfruimte. De profielkopie en de veiligheidsmarge vragen %1 op %2, en er is slechts %3 vrij. Maak ruimte vrij of kies een kleinere bron en probeer het opnieuw. Er is niets gewijzigd.
english.DetectionFailed=Legacy profile detection failed. Setup cannot safely continue.
dutch.DetectionFailed=Het zoeken naar oudere profielen is mislukt. Setup kan niet veilig doorgaan.
english.NoOldInstall=No registered older installation found. Data folders are checked separately.
dutch.NoOldInstall=Geen geregistreerde oudere installatie gevonden. Gegevensmappen worden afzonderlijk gecontroleerd.
english.DetectedInstalls=Detected installations:
dutch.DetectedInstalls=Gevonden installaties:
english.SourceDescription=Choose one source profile. Setup COPIES data to a separate 3.0 profile and leaves old application/data files unchanged. Your Desktop and Start Menu launch shortcuts will open 3.0. Download queues and caches are not imported.
dutch.SourceDescription=Kies één bronprofiel. Setup KOPIEERT gegevens naar een afzonderlijk 3.0-profiel en laat oude programma- en gegevensbestanden ongewijzigd. Snelkoppelingen op het bureaublad en in het Startmenu openen voortaan 3.0. Downloadwachtrijen en caches worden niet geïmporteerd.
english.DataTitle=Your Spotnet data
dutch.DataTitle=Uw Spotnet-gegevens
english.DataSubtitle=Fresh installation or migrate an existing profile
dutch.DataSubtitle=Nieuwe installatie of een bestaand profiel migreren
english.FreshProfile=Start fresh (or keep the existing Spotnet 3.0 profile)
dutch.FreshProfile=Opnieuw beginnen (of het bestaande Spotnet 3.0-profiel behouden)
english.CopyPrefix=Copy:
dutch.CopyPrefix=Kopiëren:
english.ChooseData=Choose a different data folder...
dutch.ChooseData=Een andere gegevensmap kiezen...
english.LegacyDataTitle=Legacy data folder
dutch.LegacyDataTitle=Gegevensmap van oudere Spotnet-versie
english.LegacyDataSubtitle=Select the folder containing servers.xml and the databases
dutch.LegacyDataSubtitle=Selecteer de map met servers.xml en de databases
english.LegacyDataDescription=Select only the Spotnet data folder, not a drive root or the application installation folder.
dutch.LegacyDataDescription=Selecteer alleen de Spotnet-gegevensmap, niet de hoofdmap van een schijf of de installatiemap van het programma.
english.DataFolder=Data folder:
dutch.DataFolder=Gegevensmap:
english.LanguageTitle=Language
dutch.LanguageTitle=Taal
english.LanguageSubtitle=Choose the language Spotnet starts in
dutch.LanguageSubtitle=Kies de taal waarin Spotnet start
english.LanguageDescription=This sets the language of the application itself. You can change it later from Edit, Language.
dutch.LanguageDescription=Dit stelt de taal van het programma zelf in. U kunt dit later wijzigen via Bewerken, Taal.
english.LanguageDutch=Nederlands
dutch.LanguageDutch=Nederlands
english.LanguageEnglish=English
dutch.LanguageEnglish=Engels
english.StyleTitle=Style
dutch.StyleTitle=Stijl
english.StyleSubtitle=Choose how Spotnet looks
dutch.StyleSubtitle=Kies hoe Spotnet eruitziet
english.StyleDescription=Each preview shows the filter list and its icons. You can change the style later from Edit, Style.
dutch.StyleDescription=Elke voorbeeldweergave toont de filterlijst en de bijbehorende pictogrammen. U kunt de stijl later wijzigen via Bewerken, Stijl.
english.StyleModernLight=Modern (light)
dutch.StyleModernLight=Modern (licht)
english.StyleModernDark=Modern (dark)
dutch.StyleModernDark=Modern (donker)
english.StyleClassic=Classic
dutch.StyleClassic=Klassiek
english.SettingsTitle=Your preferences
dutch.SettingsTitle=Uw voorkeuren
english.SettingsSubtitle=Select the settings belonging to that profile
dutch.SettingsSubtitle=Selecteer de instellingen die bij dit profiel horen
english.SettingsDescription=The legacy .NET user.config may live separately from the databases. Choose the matching file; profiles are never merged automatically. Leave defaults selected if unsure. Server credentials travel with servers.xml.
dutch.SettingsDescription=Het oudere .NET-bestand user.config kan los van de databases staan. Kies het bijbehorende bestand; profielen worden nooit automatisch samengevoegd. Laat bij twijfel de standaardkeuze staan. Servergegevens staan in servers.xml.
english.DefaultSettings=Use 3.0 defaults (or keep the existing 3.0 preferences)
dutch.DefaultSettings=Standaardinstellingen van 3.0 gebruiken (of bestaande 3.0-voorkeuren behouden)
english.ChooseSettings=Choose another user.config or portable settings.xml...
dutch.ChooseSettings=Een ander user.config- of draagbaar settings.xml-bestand kiezen...
english.LegacySettingsTitle=Legacy preferences file
dutch.LegacySettingsTitle=Voorkeurenbestand van oudere Spotnet-versie
english.LegacySettingsSubtitle=Select user.config or settings.xml
dutch.LegacySettingsSubtitle=Selecteer user.config of settings.xml
english.LegacySettingsDescription=Only Spotnet 2.x/3.x settings are supported. Unsupported or malformed files stop migration without changing the original.
dutch.LegacySettingsDescription=Alleen Spotnet 2.x/3.x-instellingen worden ondersteund. Bij een niet-ondersteund of ongeldig bestand stopt de migratie zonder het origineel te wijzigen.
english.SettingsFile=Settings file:
dutch.SettingsFile=Instellingenbestand:
english.SettingsFilter=Settings files|*.config;*.xml|All files|*.*
dutch.SettingsFilter=Instellingenbestanden|*.config;*.xml|Alle bestanden|*.*
english.ClassicChoiceTitle=Choose how to install Spotnet 3.0
dutch.ClassicChoiceTitle=Kies hoe u Spotnet 3.0 wilt installeren
english.ClassicChoiceSubtitle=Spotnet Classic is already installed
dutch.ClassicChoiceSubtitle=Spotnet Classic is al geïnstalleerd
english.ClassicSourceTitle=Choose your Classic profile
dutch.ClassicSourceTitle=Kies uw Classic-profiel
english.ClassicSourceDescription=More than one Classic profile was found. Select the one you use. Matching preferences are imported when identifiable; otherwise Spotnet uses default preferences.
dutch.ClassicSourceDescription=Er zijn meerdere Classic-profielen gevonden. Selecteer het profiel dat u gebruikt. Bijbehorende voorkeuren worden overgenomen als ze herkenbaar zijn; anders gebruikt Spotnet standaardvoorkeuren.
english.ClassicSourceMissing=No usable Classic data folder was found. Choose a clean installation to continue. Classic remains unchanged.
dutch.ClassicSourceMissing=Er is geen bruikbare Classic-gegevensmap gevonden. Kies een schone installatie om door te gaan. Classic blijft ongewijzigd.
english.ClassicUnsupported=This Classic profile format cannot be migrated automatically. Choose a clean installation and enter your provider in Spotnet. No Classic files were changed.
dutch.ClassicUnsupported=Dit Classic-profielformaat kan niet automatisch worden gemigreerd. Kies een schone installatie en voer uw provider in Spotnet in. Er zijn geen Classic-bestanden gewijzigd.
english.ClassicCompatibility=Some older profiles require a clean installation. Setup checks this before changing any data. Replace changes the shortcuts; the Classic application itself is not uninstalled.
dutch.ClassicCompatibility=Sommige oudere profielen vereisen een schone installatie. Setup controleert dit voordat gegevens worden gewijzigd. Vervangen past de snelkoppelingen aan; Classic zelf wordt niet verwijderd.
english.ClassicChoiceDescription=Setup found %1. Alongside keeps the existing Spotnet shortcut for Classic and creates Spotnet 3.0. Replace makes the existing Spotnet shortcut open 3.0.
dutch.ClassicChoiceDescription=Setup heeft %1 gevonden. Naast elkaar behoudt de bestaande Spotnet-snelkoppeling voor Classic en maakt Spotnet 3.0. Vervangen laat de bestaande Spotnet-snelkoppeling voortaan 3.0 openen.
english.MigrateReplace=Migrate and replace Spotnet Classic (move data)
dutch.MigrateReplace=Gegevens migreren en Spotnet Classic vervangen (gegevens verplaatsen)
english.MigrateAlongside=Migrate and use alongside Spotnet Classic (copy data)
dutch.MigrateAlongside=Gegevens migreren en naast Spotnet Classic gebruiken (gegevens kopiëren)
english.CleanAlongside=Clean install and use alongside Spotnet Classic
dutch.CleanAlongside=Schone installatie naast Spotnet Classic gebruiken
english.CleanReplace=Clean install and replace Spotnet Classic
dutch.CleanReplace=Schone installatie en Spotnet Classic vervangen
english.MoveConfirmation=After the copy is verified, migrated profile files will be permanently removed from %1. Spotnet Classic program files, download queues and caches will not be removed. Continue?
dutch.MoveConfirmation=Nadat de kopie is gecontroleerd, worden gemigreerde profielbestanden permanent verwijderd uit %1. Programmabestanden, downloadwachtrijen en caches van Spotnet Classic worden niet verwijderd. Doorgaan?
english.CleanInstallSummary=Create a clean Spotnet 3.0 profile with default settings.
dutch.CleanInstallSummary=Een schoon Spotnet 3.0-profiel met standaardinstellingen maken.
english.MigrateCopySummary=Copy and verify the active Spotnet Classic profile; keep the original data.
dutch.MigrateCopySummary=Het actieve Spotnet Classic-profiel kopiëren en controleren; de oorspronkelijke gegevens behouden.
english.MigrateMoveSummary=Copy and verify the active Spotnet Classic profile; then permanently remove the migrated source files.
dutch.MigrateMoveSummary=Het actieve Spotnet Classic-profiel kopiëren en controleren; daarna de gemigreerde bronbestanden permanent verwijderen.
english.MoveIncomplete=Migration succeeded, but some migrated files could not be removed from the Spotnet Classic profile. Check Spotnet 3.0, then remove the remaining Classic profile manually.
dutch.MoveIncomplete=De migratie is geslaagd, maar sommige gemigreerde bestanden konden niet uit het Spotnet Classic-profiel worden verwijderd. Controleer Spotnet 3.0 en verwijder daarna het resterende Classic-profiel handmatig.
english.ShortcutReplaceNotice=Existing Spotnet shortcuts will open 3.0. New shortcuts are named Spotnet (or Spotnet (64-bit) if the name is occupied by an unrelated shortcut).
dutch.ShortcutReplaceNotice=Bestaande Spotnet-snelkoppelingen openen voortaan 3.0. Nieuwe snelkoppelingen heten Spotnet (of Spotnet (64-bit) als de naam door een andere snelkoppeling wordt gebruikt).
english.ShortcutAlongsideNotice=Existing Spotnet shortcuts keep opening Classic. Selected new shortcuts are named Spotnet 3.0.
dutch.ShortcutAlongsideNotice=Bestaande Spotnet-snelkoppelingen blijven Classic openen. Geselecteerde nieuwe snelkoppelingen heten Spotnet 3.0.
english.ShortcutUpgradeNotice=Setup keeps the shortcut mode selected during the original Spotnet 3.0 installation.
dutch.ShortcutUpgradeNotice=Setup behoudt de snelkoppelingsmodus die tijdens de oorspronkelijke installatie van Spotnet 3.0 is gekozen.
english.Welcome1=Install Spotnet 3.0 for this Windows user.
dutch.Welcome1=Spotnet 3.0 voor deze Windows-gebruiker installeren.
english.Welcome2=If an installed Spotnet Classic 1.8/2.x profile is found, Setup offers four simple migrate/clean and replace/alongside choices. A new system starts clean without migration questions. Existing 3.0 profiles are backed up before an upgrade.
dutch.Welcome2=Als een geïnstalleerd Spotnet Classic 1.8/2.x-profiel wordt gevonden, biedt Setup vier eenvoudige keuzes voor migreren/schoon en vervangen/naast elkaar. Een nieuw systeem start schoon zonder migratievragen. Van bestaande 3.0-profielen wordt vóór een upgrade een back-up gemaakt.
english.Welcome3=Setup will ask Spotnet to exit safely and wait for it to close. Large databases require extra disk space and copying time.
dutch.Welcome3=Setup vraagt Spotnet veilig af te sluiten en wacht tot het programma is gestopt. Grote databases vereisen extra schijfruimte en kopieertijd.
english.Welcome4=Replace reuses the Spotnet shortcuts for 3.0. Alongside keeps Classic as Spotnet and creates selected 3.0 shortcuts as Spotnet 3.0.
dutch.Welcome4=Vervangen gebruikt de Spotnet-snelkoppelingen voortaan voor 3.0. Naast elkaar behoudt Classic als Spotnet en maakt geselecteerde 3.0-snelkoppelingen als Spotnet 3.0.
english.Welcome5=.NET 10 ships with Setup and is placed inside the Spotnet installation folder, so no separate .NET installation is needed; Microsoft Edge WebView2 is fetched from Microsoft if missing (internet access required). Uninstall keeps personal data unless you choose to delete it.
dutch.Welcome5=.NET 10 zit in Setup en wordt in de installatiemap van Spotnet geplaatst, dus .NET apart installeren is niet nodig; Microsoft Edge WebView2 wordt bij Microsoft opgehaald als het ontbreekt (internettoegang vereist). Bij verwijderen blijven persoonlijke gegevens behouden, tenzij u kiest om ze te wissen.
english.SeparateFolder=Choose a separate installation folder. Setup will not overwrite a legacy or portable Spotnet installation.
dutch.SeparateFolder=Kies een afzonderlijke installatiemap. Setup overschrijft geen oudere of draagbare Spotnet-installatie.
english.NoDowngrade=A newer Spotnet version is installed here. Downgrades are not supported.
dutch.NoDowngrade=Hier is een nieuwere Spotnet-versie geïnstalleerd. Downgraden wordt niet ondersteund.
english.ProfileLabel=Profile:
dutch.ProfileLabel=Profiel:
english.KeepProfile=Keep current Spotnet 3.0 profile unchanged.
dutch.KeepProfile=Huidig Spotnet 3.0-profiel ongewijzigd behouden.
english.DataSource=Data source:
dutch.DataSource=Gegevensbron:
english.Preferences=Preferences:
dutch.Preferences=Voorkeuren:
english.EmptySource=Empty source means fresh/default settings. Legacy files remain unchanged.
dutch.EmptySource=Een lege bron betekent een nieuw profiel met standaardinstellingen. Oudere bestanden blijven ongewijzigd.
english.QueueNotice=Active download queues are not imported. Existing downloads remain at their original paths.
dutch.QueueNotice=Actieve downloadwachtrijen worden niet geïmporteerd. Bestaande downloads blijven op hun oorspronkelijke locatie.
english.ShortcutNotice=Update your existing Spotnet shortcuts in place, and add the ones selected above. Originals are backed up.
dutch.ShortcutNotice=Bestaande Spotnet-snelkoppelingen worden bijgewerkt en de hierboven gekozen snelkoppelingen worden toegevoegd. Van originelen wordt een back-up gemaakt.
english.WebViewNotice=.NET 10 ships with Setup, inside the Spotnet installation folder; WebView2 is downloaded from Microsoft if it is missing.
dutch.WebViewNotice=.NET 10 zit in Setup, in de installatiemap van Spotnet; WebView2 wordt bij Microsoft gedownload als het ontbreekt.
english.UninstallNotice=Uninstall keeps your profile and backups by default. You can optionally delete all personal data.
dutch.UninstallNotice=Verwijderen behoudt standaard uw profiel en back-ups. U kunt ervoor kiezen alle persoonlijke gegevens te wissen.
english.StatusClosing=Asking Spotnet to exit safely; waiting for database writes to finish...
dutch.StatusClosing=Spotnet wordt veilig afgesloten; wachten tot databasebewerkingen gereed zijn...
english.ProgressTitle=Preparing Spotnet 3.0
dutch.ProgressTitle=Spotnet 3.0 voorbereiden
english.ProgressDescription=Setup is working. This can take several minutes for a large profile.
dutch.ProgressDescription=Setup is bezig. Bij een groot profiel kan dit enkele minuten duren.
english.ProgressDetail=Please wait; Setup has not stopped responding.
dutch.ProgressDetail=Even geduld; Setup reageert nog en werkt verder.
english.CloseFailed=Spotnet could not be closed safely. Exit it manually and retry.
dutch.CloseFailed=Spotnet kon niet veilig worden afgesloten. Sluit het handmatig af en probeer opnieuw.
english.StatusWebView=Installing Microsoft Edge WebView2 (internet access required)...
dutch.StatusWebView=Microsoft Edge WebView2 installeren (internettoegang vereist)...
english.WebViewFailed=WebView2 installation did not complete. Install the Evergreen Runtime from Microsoft and retry. No Spotnet data has been migrated.
dutch.WebViewFailed=De installatie van WebView2 is niet voltooid. Installeer de Evergreen Runtime via Microsoft en probeer opnieuw. Er zijn geen Spotnet-gegevens gemigreerd.
english.StatusProfile=Copying and verifying your profile. Large databases may take several minutes...
dutch.StatusProfile=Uw profiel kopiëren en controleren. Grote databases kunnen enkele minuten duren...
english.ProfileFailed=Profile preparation failed. Close Spotnet and check available disk space and file permissions, then retry.
dutch.ProfileFailed=Het voorbereiden van het profiel is mislukt. Sluit Spotnet, controleer beschikbare schijfruimte en bestandsrechten en probeer opnieuw.
english.UninstallClose=Exit Spotnet before uninstalling. Your profile and backups will be kept.
dutch.UninstallClose=Sluit Spotnet af voordat u het verwijdert. Uw profiel en back-ups blijven behouden.
english.RestoreShortcutFailed=Some shortcuts could not be restored. Their backups remain in
dutch.RestoreShortcutFailed=Sommige snelkoppelingen konden niet worden hersteld. De back-ups blijven staan in
english.DataRetained=Your personal data is retained.
dutch.DataRetained=Uw persoonlijke gegevens blijven behouden.
english.UninstallOptionsTitle=Uninstall Spotnet 3.0
dutch.UninstallOptionsTitle=Spotnet 3.0 verwijderen
english.UninstallOptionsIntro=By default, Spotnet keeps your profile so a later reinstall can use your provider settings and databases.
dutch.UninstallOptionsIntro=Spotnet bewaart standaard uw profiel, zodat een latere installatie uw providerinstellingen en databases kan gebruiken.
english.RemovePersonalData=Permanently remove my Spotnet profile and all personal data
dutch.RemovePersonalData=Mijn Spotnet-profiel en alle persoonlijke gegevens permanent verwijderen
english.RemovePersonalDataDetails=This deletes provider credentials, settings, databases, logs, incomplete migrations and backups from:
dutch.RemovePersonalDataDetails=Dit verwijdert providergegevens, instellingen, databases, logboeken, onvoltooide migraties en back-ups uit:
english.RemovePersonalDataScope=Download folders and older Spotnet profiles stored elsewhere are not removed.
dutch.RemovePersonalDataScope=Downloadmappen en oudere Spotnet-profielen die elders staan, worden niet verwijderd.
english.ContinueUninstall=&Continue
dutch.ContinueUninstall=&Doorgaan
english.CancelUninstall=Cancel
dutch.CancelUninstall=Annuleren
english.RemovePersonalDataFailed=Spotnet was removed, but some personal data could not be deleted. You can remove the remaining files manually from:
dutch.RemovePersonalDataFailed=Spotnet is verwijderd, maar sommige persoonlijke gegevens konden niet worden gewist. U kunt de resterende bestanden handmatig verwijderen uit:
english.StatusShortcuts=Updating your Spotnet Desktop and Start Menu shortcuts...
dutch.StatusShortcuts=Spotnet-snelkoppelingen op het bureaublad en in het Startmenu bijwerken...
english.ShortcutReportMissing=Shortcut update did not produce a report.
dutch.ShortcutReportMissing=Het bijwerken van snelkoppelingen heeft geen rapport opgeleverd.
english.ShortcutDone=Spotnet shortcuts were updated or created. Originals were stored safely.
dutch.ShortcutDone=Spotnet-snelkoppelingen zijn bijgewerkt of aangemaakt. Originelen zijn veilig opgeslagen.
english.ShortcutAttention=Spotnet is installed, but shortcut setup needs attention. Fix the reported permissions/paths and rerun Setup.
dutch.ShortcutAttention=Spotnet is geïnstalleerd, maar de snelkoppelingen vereisen aandacht. Corrigeer de gemelde rechten of paden en voer Setup opnieuw uit.
english.Installed=Spotnet 3.0 (64-bit) is installed.
dutch.Installed=Spotnet 3.0 (64-bit) is geïnstalleerd.
english.InstalledAttention=Application installed; shortcut setup needs attention.
dutch.InstalledAttention=Programma geïnstalleerd; de snelkoppelingen vereisen aandacht.
english.CheckOld=Keep your old installation until you have checked your provider, databases and downloads in 3.0.
dutch.CheckOld=Behoud uw oude installatie totdat u uw provider, databases en downloads in 3.0 hebt gecontroleerd.

[Files]
Source: "{#HelperDir}\Spotnet.SetupHelper.exe"; Flags: dontcopy
Source: "{#HelperDir}\Spotnet.SetupHelper.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PreviewDir}\style-modern-light.bmp"; Flags: dontcopy
Source: "{#PreviewDir}\style-modern-dark.bmp"; Flags: dontcopy
Source: "{#PreviewDir}\style-classic.bmp"; Flags: dontcopy
Source: "{#WebViewBootstrapper}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
#ifndef SelfContained
Source: "{#DotNetBootstrapper}"; DestName: "windowsdesktop-runtime.exe"; Flags: dontcopy
#endif
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Spotnet.install"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
; Both are checked by default: Setup used to add these launchers unconditionally, and an
; upgrade must not quietly take away the icon the user already launches Spotnet from.
; Unchecking one only declines a NEW shortcut - existing Spotnet launchers are still
; re-pointed at 3.0, wherever the user keeps them.
Name: "programsicon"; Description: "{cm:ProgramsIcon}"; GroupDescription: "{cm:ShortcutGroup}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:ShortcutGroup}"

[Run]
#ifndef SmokeTestRoot
Filename: "{app}\Spotnet.exe"; Description: "{cm:LaunchSpotnet}"; Flags: nowait postinstall skipifsilent unchecked; Check: ShortcutsSucceeded
; The automatic update closed Spotnet to replace it; this opens it again.
Filename: "{app}\Spotnet.exe"; Flags: nowait postinstall; Check: RelaunchRequested and ShortcutsSucceeded
#endif

; No UninstallDelete section: the profile is retained by default. An explicit uninstall-time
; choice removes the complete Spotnet 3 profile only after shortcut restoration has finished.
; Legacy installations and download folders outside that profile are never removed.
; No extension/protocol hijacking: the user can keep their old installation while validating 3.0.

[Code]
var
  MigrationPage, SourcePage, LanguagePage: TInputOptionWizardPage;
  ClassicSources, ClassicSourceSettings: TArrayOfString;
  StylePage: TWizardPage;
  StyleButtons: array[0..2] of TRadioButton;
  StyleCaption: TLabel;
  ProgressPage: TOutputProgressWizardPage;
  ExistingProfile, ClassicAvailable, MoveIncomplete: Boolean;
  Prepared: Boolean;
  ShortcutFailure: Boolean;
  RemovePersonalData: Boolean;
  Helper, DetectionFile, ReportFile, SpaceFile, Summary: String;
  ClassicName, ClassicData, ClassicSettings: String;
  CurrentTheme, CurrentLanguage: String;
  { The pre-flight disk-space measurement of the selected profile, in megabytes. }
  SpaceMeasured, SpaceFits: Boolean;
  SpaceBytesMB, SpaceRequiredMB, SpaceFreeMB: Integer;
  SpaceDrive, SpaceKind: String;

function CM(const Key: String): String;
begin
  Result := ExpandConstant('{cm:' + Key + '}');
end;

{ Microsoft's prerequisite installers report no progress back to Setup, so a step
  bar would stand still for minutes and read as a hang. A marquee says "working"
  without inventing a percentage. }
procedure SetBusy(Busy: Boolean);
begin
  if Busy then ProgressPage.ProgressBar.Style := npbstMarquee
  else ProgressPage.ProgressBar.Style := npbstNormal;
end;

function IsDutch: Boolean;
begin
  Result := CompareText(ActiveLanguage, 'dutch') = 0;
end;

function ProfileRoot: String;
begin
#ifdef SmokeTestRoot
  Result := '{#SmokeTestRoot}\Profile';
#else
  Result := ExpandConstant('{localappdata}\Spotnet3');
#endif
end;

function DesktopRoot: String;
begin
#ifdef SmokeTestRoot
  Result := '{#SmokeTestRoot}\Desktop';
#else
  Result := ExpandConstant('{userdesktop}');
#endif
end;

function ProgramsRoot: String;
begin
#ifdef SmokeTestRoot
  Result := '{#SmokeTestRoot}\Programs';
#else
  Result := ExpandConstant('{userprograms}');
#endif
end;

function ShortcutsSucceeded: Boolean;
begin
  Result := not ShortcutFailure and not MoveIncomplete;
end;

{ Spotnet's updater installs silently and needs the application back afterwards.
  /RELAUNCH is its own switch, so a hand-run silent install stays quiet. }
function RelaunchRequested: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function ReadReport(var Text: String): Boolean;
var
  Lines: TArrayOfString;
  Index: Integer;
begin
  Result := LoadStringsFromFile(ReportFile, Lines);
  Text := '';
  if Result then
    for Index := 0 to GetArrayLength(Lines) - 1 do Text := Text + Lines[Index] + #13#10;
end;

function Quote(const Value: String): String;
begin
  { A trailing slash before a closing quote changes Windows argument parsing. }
  Result := '"' + RemoveBackslashUnlessRoot(Value) + '"';
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net472, 0);
  if not Result then
    SuppressibleMsgBox(CM('DotNetRequired'), mbCriticalError, MB_OK, IDOK);
end;

{ The Windows Desktop shared framework the application runs on. Present when at least
  one 8.x (or newer) version directory exists next to the dotnet host. }
function DotNetDesktopInstalled: Boolean;
var
  Root: String;
  Search: TFindRec;
  Major: Integer;
begin
  Result := False;
  Root := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(Root) then exit;
  if FindFirst(Root + '\*', Search) then begin
    try
      repeat
        if (Search.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then begin
          Major := StrToIntDef(Copy(Search.Name, 1, Pos('.', Search.Name) - 1), 0);
          if Major >= 8 then begin
            Result := True;
            exit;
          end;
        end;
      until not FindNext(Search);
    finally
      FindClose(Search);
    end;
  end;
end;

function WebViewInstalled: Boolean;
var
  Version: String;
begin
  Result := (RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
  if not Result then
    Result := (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;


{ One preview tile: the bitmap rendered from the application's own theme
  dictionaries by Spotnet.ThemePreview, with its radio button underneath. }
procedure AddStyleTile(Page: TWizardPage; Index: Integer; const FileName, Caption: String; Left: Integer);
var
  Image: TBitmapImage;
begin
  ExtractTemporaryFile(FileName);
  Image := TBitmapImage.Create(Page);
  Image.Parent := Page.Surface;
  Image.Bitmap.LoadFromFile(ExpandConstant('{tmp}\') + FileName);
  Image.Left := ScaleX(Left);
  Image.Top := ScaleY(34);
  Image.Width := ScaleX(128);
  Image.Height := ScaleY(132);
  Image.Stretch := True;

  StyleButtons[Index] := TRadioButton.Create(Page);
  StyleButtons[Index].Parent := Page.Surface;
  StyleButtons[Index].Left := ScaleX(Left);
  StyleButtons[Index].Top := ScaleY(172);
  StyleButtons[Index].Width := ScaleX(128);
  StyleButtons[Index].Caption := Caption;
end;

{ The style written into the fresh profile's user.config. Must match the values
  ThemeHelper accepts. }
function SelectedTheme: String;
begin
  if StyleButtons[1].Checked then Result := 'ModernDark'
  else if StyleButtons[2].Checked then Result := 'ClassicLight'
  else Result := 'ModernLight';
end;

function SelectedLanguage: String;
begin
  if LanguagePage.SelectedValueIndex = 1 then Result := 'en' else Result := 'nl';
end;

procedure InitializeWizard;
var
  ExitCode, Count, Index: Integer;
  Description, DetectionParameters: String;
begin
  ExtractTemporaryFile('Spotnet.SetupHelper.exe');
  Helper := ExpandConstant('{tmp}\Spotnet.SetupHelper.exe');
  DetectionFile := ExpandConstant('{tmp}\spotnet-detection.ini');
  ReportFile := ExpandConstant('{tmp}\spotnet-migration.txt');
  SpaceFile := ExpandConstant('{tmp}\spotnet-space.ini');
  { Name the profile this run targets. Without it detection reads whatever profile sits
    in the user's own AppData, which is the wrong one under a smoke-test root. }
  DetectionParameters := 'detect --profile ' + Quote(ProfileRoot) + ' --output ' + Quote(DetectionFile);
#ifdef SmokeTestRoot
  DetectionParameters := DetectionParameters + ' --test-root ' + Quote('{#SmokeTestRoot}');
#endif
  if not Exec(Helper, DetectionParameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
    RaiseException(CM('DetectionFailed'));
  ExistingProfile := FileExists(ProfileRoot + '\Data\profile.ready');
  ClassicAvailable := GetIniInt('Detection', 'ClassicAvailable', 0, 0, 1, DetectionFile) = 1;
  ClassicName := GetIniString('Detection', 'ClassicName', '', DetectionFile);
  ClassicData := GetIniString('Detection', 'ClassicData', '', DetectionFile);
  ClassicSettings := GetIniString('Detection', 'ClassicSettings', '', DetectionFile);
  Description := FmtMessage(CM('ClassicChoiceDescription'), [ClassicName]) + #13#10#13#10 + CM('ClassicCompatibility');
  { Onboarding: language, then style. Both sit between Welcome and the folder
    page so the choices are made before anything is written. }
  LanguagePage := CreateInputOptionPage(wpWelcome, CM('LanguageTitle'), CM('LanguageSubtitle'),
    CM('LanguageDescription'), True, False);
  LanguagePage.Add(CM('LanguageDutch'));
  LanguagePage.Add(CM('LanguageEnglish'));
  CurrentLanguage := GetIniString('Detection', 'CurrentLanguage', '', DetectionFile);
  if CurrentLanguage = 'nl' then LanguagePage.SelectedValueIndex := 0
  else if CurrentLanguage = 'en' then LanguagePage.SelectedValueIndex := 1
  else if IsDutch then LanguagePage.SelectedValueIndex := 0
  else LanguagePage.SelectedValueIndex := 1;

  StylePage := CreateCustomPage(LanguagePage.ID, CM('StyleTitle'), CM('StyleSubtitle'));
  StyleCaption := TLabel.Create(StylePage);
  StyleCaption.Parent := StylePage.Surface;
  StyleCaption.Left := 0;
  StyleCaption.Top := 0;
  StyleCaption.Width := StylePage.SurfaceWidth;
  StyleCaption.WordWrap := True;
  StyleCaption.Caption := CM('StyleDescription');
  AddStyleTile(StylePage, 0, 'style-modern-light.bmp', CM('StyleModernLight'), 0);
  AddStyleTile(StylePage, 1, 'style-modern-dark.bmp', CM('StyleModernDark'), 140);
  AddStyleTile(StylePage, 2, 'style-classic.bmp', CM('StyleClassic'), 280);
  { An upgrade opens on the style it already uses, so clicking straight through Setup
    never repaints an existing install. A fresh install opens on Modern (light). }
  CurrentTheme := GetIniString('Detection', 'CurrentTheme', '', DetectionFile);
  if CurrentTheme = 'ModernDark' then StyleButtons[1].Checked := True
  else if CurrentTheme = 'ClassicLight' then StyleButtons[2].Checked := True
  else StyleButtons[0].Checked := True;

  MigrationPage := CreateInputOptionPage(wpSelectDir, CM('ClassicChoiceTitle'), CM('ClassicChoiceSubtitle'),
    Description, True, False);
  MigrationPage.Add(CM('MigrateReplace'));
  MigrationPage.Add(CM('MigrateAlongside'));
  MigrationPage.Add(CM('CleanAlongside'));
  MigrationPage.Add(CM('CleanReplace'));
  { Copy/alongside is the safe default: nothing in Classic is changed. }
  MigrationPage.SelectedValueIndex := 1;
  { Unattended fresh installs preserve Classic. Set this before measuring space. }
  if WizardSilent and (ExpandConstant('{param:FRESH|0}') = '1') then
    MigrationPage.SelectedValueIndex := 2;
#ifdef SmokeTestRoot
  { Test builds only: exercise all four choices without changing a real profile. }
  MigrationPage.SelectedValueIndex := StrToIntDef(ExpandConstant('{param:SMOKECLASSICMODE|}'), MigrationPage.SelectedValueIndex);
#endif
  SourcePage := CreateInputOptionPage(MigrationPage.ID, CM('ClassicSourceTitle'), '', CM('ClassicSourceDescription'), True, False);
  Count := GetIniInt('Detection', 'DataCount', 0, 0, 1000, DetectionFile);
  SetArrayLength(ClassicSources, Count);
  SetArrayLength(ClassicSourceSettings, Count);
  for Index := 0 to Count - 1 do begin
    ClassicSources[Index] := GetIniString('Detection', 'Data' + IntToStr(Index), '', DetectionFile);
    ClassicSourceSettings[Index] := GetIniString('Detection', 'DataSettings' + IntToStr(Index), '', DetectionFile);
    SourcePage.Add(ClassicSources[Index]);
  end;
  SourcePage.SelectedValueIndex := -1;
  WizardForm.WelcomeLabel2.Caption := CM('Welcome1') + #13#10#13#10 +
    CM('Welcome2') + #13#10#13#10 + CM('Welcome3') + #13#10#13#10 +
    CM('Welcome4') + #13#10#13#10 + CM('Welcome5');
  ProgressPage := CreateOutputProgressPage(CM('ProgressTitle'), CM('ProgressDescription'));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { A fresh machine and a normal 3.0 upgrade never see migration source UI. }
  Result := (PageID = MigrationPage.ID) and (ExistingProfile or not ClassicAvailable);
  if PageID = SourcePage.ID then
    Result := ExistingProfile or not ClassicAvailable or (MigrationPage.SelectedValueIndex > 1) or (GetArrayLength(ClassicSources) <= 1);
end;

function SelectedData: String;
begin
  Result := '';
  if ExistingProfile or not ClassicAvailable then exit;
  if MigrationPage.SelectedValueIndex <= 1 then begin
    if (GetArrayLength(ClassicSources) > 1) and (SourcePage.SelectedValueIndex >= 0) then
      Result := ClassicSources[SourcePage.SelectedValueIndex]
    else Result := ClassicData;
  end;
end;

function SelectedSettings: String;
begin
  Result := '';
  if ExistingProfile or not ClassicAvailable then exit;
  if MigrationPage.SelectedValueIndex <= 1 then begin
    if (GetArrayLength(ClassicSources) > 1) and (SourcePage.SelectedValueIndex >= 0) then
      Result := ClassicSourceSettings[SourcePage.SelectedValueIndex]
    else Result := ClassicSettings;
  end;
end;

function MoveClassicData: Boolean;
begin
  Result := ClassicAvailable and not ExistingProfile and (MigrationPage.SelectedValueIndex = 0);
end;

function UseAlongsideShortcuts: Boolean;
begin
  Result := ClassicAvailable and not ExistingProfile and
    ((MigrationPage.SelectedValueIndex = 1) or (MigrationPage.SelectedValueIndex = 2));
end;

function ClassicShortcutMode: String;
begin
  if ExistingProfile then Result := 'auto'
  else if UseAlongsideShortcuts then Result := 'alongside'
  else Result := 'replace';
end;

function SelectedModeSummary: String;
begin
  if ExistingProfile then Result := CM('KeepProfile')
  else if SelectedData = '' then Result := CM('CleanInstallSummary')
  else if MoveClassicData then Result := CM('MigrateMoveSummary')
  else Result := CM('MigrateCopySummary');
end;

{ Megabytes as a person reads them; Dutch gets its own decimal comma. }
function FormatMB(Megabytes: Integer): String;
var
  Whole, Tenths: Integer;
  Separator: String;
begin
  if Megabytes < 1024 then begin
    Result := IntToStr(Megabytes) + ' MB';
    exit;
  end;
  Whole := Megabytes div 1024;
  Tenths := ((Megabytes mod 1024) * 10) div 1024;
  if IsDutch then Separator := ',' else Separator := '.';
  Result := IntToStr(Whole) + Separator + IntToStr(Tenths) + ' GB';
end;

{ What the profile copy will cost, measured before anything is installed or copied.
  The helper opens no file handles for this, so it is safe while Spotnet still holds
  its database. Migration keeps its own check as the backstop; this one exists so a
  3 GB profile onto a full drive is refused on the Ready page instead of halfway
  through the copy. A measurement that fails leaves Setup free to continue. }
procedure MeasureSpace;
var
  ExitCode: Integer;
  Parameters: String;
begin
  SpaceMeasured := False;
  DeleteFile(SpaceFile);
  Parameters := 'measure --profile ' + Quote(ProfileRoot) + ' --output ' + Quote(SpaceFile);
  if SelectedData <> '' then Parameters := Parameters + ' --source-data ' + Quote(SelectedData);
  if SelectedSettings <> '' then Parameters := Parameters + ' --source-settings ' + Quote(SelectedSettings);
  if not Exec(Helper, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then exit;
  if GetIniInt('Space', 'Measured', 0, 0, 1, SpaceFile) <> 1 then exit;
  SpaceKind := GetIniString('Space', 'Kind', '', SpaceFile);
  SpaceBytesMB := GetIniInt('Space', 'BytesMB', 0, 0, 2000000000, SpaceFile);
  SpaceRequiredMB := GetIniInt('Space', 'RequiredMB', 0, 0, 2000000000, SpaceFile);
  SpaceFreeMB := GetIniInt('Space', 'FreeMB', 0, 0, 2000000000, SpaceFile);
  SpaceDrive := GetIniString('Space', 'Drive', '', SpaceFile);
  SpaceFits := GetIniInt('Space', 'Fits', 1, 0, 1, SpaceFile) = 1;
  SpaceMeasured := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Version: String;
  InstalledVersion, PackageVersion: Int64;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    { Never install over a legacy binary tree. Same-family upgrades require our marker. }
    if FileExists(ExpandConstant('{app}\Spotnet.exe')) and not FileExists(ExpandConstant('{app}\Spotnet.install')) then begin
      SuppressibleMsgBox(CM('SeparateFolder'), mbError, MB_OK, IDOK);
      Result := False;
    end;
    if GetVersionNumbersString(ExpandConstant('{app}\Spotnet.exe'), Version) and
      StrToVersion(Version, InstalledVersion) and StrToVersion('{#AppVersion}', PackageVersion) and
      (ComparePackedVersion(InstalledVersion, PackageVersion) > 0) then begin
      SuppressibleMsgBox(CM('NoDowngrade'), mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
  if (CurPageID = MigrationPage.ID) and (MigrationPage.SelectedValueIndex <= 1) and (GetArrayLength(ClassicSources) = 0) then begin
    SuppressibleMsgBox(CM('ClassicSourceMissing'), mbError, MB_OK, IDOK);
    Result := False;
  end;
  if (CurPageID = SourcePage.ID) and (SourcePage.SelectedValueIndex < 0) then Result := False;
  if (CurPageID = wpReady) and MoveClassicData and not WizardSilent then
    Result := SuppressibleMsgBox(FmtMessage(CM('MoveConfirmation'), [SelectedData]),
      mbConfirmation, MB_YESNO, IDNO) = IDYES;
  { Unattended first installs must opt out of migration explicitly; never guess a profile. }
  if (CurPageID = wpReady) and WizardSilent and not ExistingProfile then begin
    if ExpandConstant('{param:FRESH|0}') <> '1' then begin
      Log('Silent first installation requires /FRESH=1. Use the interactive wizard for migration.');
      Result := False;
    end;
  end;
  { Refuse a copy the drive cannot hold while nothing has been touched yet. }
  if (CurPageID = wpReady) and Result then begin
    if not SpaceMeasured then MeasureSpace;
    if SpaceMeasured and not SpaceFits then begin
      Log('The destination drive cannot hold the profile copy; installation was not started.');
      SuppressibleMsgBox(FmtMessage(CM('SpaceShort'), [FormatMB(SpaceRequiredMB), SpaceDrive, FormatMB(SpaceFreeMB)]),
        mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + CM('ProfileLabel') + ' ' + ProfileRoot + '\Data' + NewLine;
  Result := Result + SelectedModeSummary + NewLine;
  if SelectedData <> '' then Result := Result + CM('DataSource') + ' ' + SelectedData + NewLine;
  { Measured here, where the source and destination are both final, so the memo can
    state the real cost of the copy before the user commits to it. }
  MeasureSpace;
  if SpaceMeasured and (SpaceBytesMB > 0) then begin
    if SpaceKind = 'upgrade' then
      Result := Result + FmtMessage(CM('SpaceMemoUpgrade'), [FormatMB(SpaceBytesMB), FormatMB(SpaceFreeMB), SpaceDrive]) + NewLine
    else
      Result := Result + FmtMessage(CM('SpaceMemo'), [FormatMB(SpaceBytesMB), FormatMB(SpaceFreeMB), SpaceDrive]) + NewLine;
  end;
  Result := Result + NewLine + CM('QueueNotice') + NewLine;
  if ExistingProfile then Result := Result + CM('ShortcutUpgradeNotice') + NewLine
  else if UseAlongsideShortcuts then Result := Result + CM('ShortcutAlongsideNotice') + NewLine
  else Result := Result + CM('ShortcutReplaceNotice') + NewLine;
  Result := Result +
    CM('WebViewNotice') + NewLine + CM('UninstallNotice') + NewLine + MemoTasksInfo;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Parameters, RuntimeArguments: String;
  RuntimeWindow: Integer;
  ShowProgress: Boolean;
begin
  Result := '';
  if Prepared then exit;
  ShowProgress := WizardForm.Visible;
  { An attended run lets Microsoft's runtime installer show its own progress window;
    an unattended one keeps it silent. }
  if not WizardSilent then begin
    RuntimeArguments := '/install /passive /norestart';
    RuntimeWindow := SW_SHOWNORMAL;
  end else begin
    RuntimeArguments := '/install /quiet /norestart';
    RuntimeWindow := SW_HIDE;
  end;
  if ShowProgress then begin
    ProgressPage.SetText(CM('StatusClosing'), CM('ProgressDetail'));
    ProgressPage.SetProgress(0, 4);
    ProgressPage.Show;
  end;
  try
#ifndef SmokeTestRoot
  if ShowProgress then ProgressPage.SetText(CM('StatusClosing'), CM('ProgressDetail'));
  if not Exec(Helper, 'close --report ' + Quote(ReportFile), '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
    if IsDutch or not ReadReport(Result) then Result := CM('CloseFailed');
    exit;
  end;
#endif
#ifndef SelfContained
  if ShowProgress then begin
    ProgressPage.SetProgress(1, 4);
    ProgressPage.SetText(CM('StatusDotNet'), CM('ProgressDetail'));
  end;
  if not DotNetDesktopInstalled then begin
#ifdef SmokeTestRoot
    Result := 'Smoke tests require an already installed .NET Desktop Runtime; they never install prerequisites.';
    exit;
#else
    { Unpacking the bundled 56 MB installer is itself a slow step on a slow disk,
      so it gets its own line before the install starts. }
    if ShowProgress then begin
      SetBusy(True);
      ProgressPage.SetText(CM('StatusDotNetPrepare'), CM('ProgressDetail'));
    end;
    ExtractTemporaryFile('windowsdesktop-runtime.exe');
    if ShowProgress then ProgressPage.SetText(CM('StatusDotNet'), CM('ProgressDetail'));
    if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime.exe'), RuntimeArguments, '', RuntimeWindow, ewWaitUntilTerminated, ExitCode) or
       ((ExitCode <> 0) and (ExitCode <> 3010)) or not DotNetDesktopInstalled then begin
      Result := CM('DotNetRuntimeFailed');
      exit;
    end;
    if ShowProgress then SetBusy(False);
#endif
  end;
#endif
  if ShowProgress then begin
    ProgressPage.SetProgress(2, 4);
    ProgressPage.SetText(CM('StatusWebView'), CM('ProgressDetail'));
  end;
  if not WebViewInstalled then begin
#ifdef SmokeTestRoot
    Result := 'Smoke tests require an already installed WebView2 Runtime; they never install prerequisites.';
    exit;
#else
    if ShowProgress then SetBusy(True);
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or
       (ExitCode <> 0) or not WebViewInstalled then begin
      Result := CM('WebViewFailed');
      exit;
    end;
    if ShowProgress then SetBusy(False);
#endif
  end;
  if ShowProgress and not ExistingProfile then begin
    SetBusy(False);
    ProgressPage.SetProgress(3, 4);
    ProgressPage.SetText(CM('StatusProfile'), CM('ProgressDetail'));
  end;
  Parameters := 'prepare --profile ' + Quote(ProfileRoot) + ' --report ' + Quote(ReportFile);
  { A new profile starts in the language and style picked on the wizard's first two
    pages; an imported profile keeps whatever it already states. }
  Parameters := Parameters + ' --language ' + SelectedLanguage;
  Parameters := Parameters + ' --app-theme ' + SelectedTheme;
  if SelectedData <> '' then Parameters := Parameters + ' --source-data ' + Quote(SelectedData);
  if SelectedSettings <> '' then Parameters := Parameters + ' --source-settings ' + Quote(SelectedSettings);
  if MoveClassicData then Parameters := Parameters + ' --move-source 1';
  if not Exec(Helper, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
    ReadReport(Result);
    if Pos('[UNSUPPORTED-PROFILE]', Result) > 0 then Result := CM('ClassicUnsupported')
    else if IsDutch or (Result = '') then Result := CM('ProfileFailed');
    exit;
  end;
  ReadReport(Summary);
  Summary := SelectedModeSummary;
  if ShowProgress then begin
    ProgressPage.SetProgress(4, 4);
    ProgressPage.SetText(CM('StatusProfile'), CM('ShortcutDone'));
  end;
  Prepared := True;
  finally
    if ShowProgress then begin
      SetBusy(False);
      ProgressPage.Hide;
    end;
  end;
end;

function AskUninstallOptions: Boolean;
var
  Form: TSetupForm;
  Heading, Intro, Details, Scope: TNewStaticText;
  RemoveDataCheck: TNewCheckBox;
  ProfilePath: TNewEdit;
  ContinueButton, CancelButton: TNewButton;
  ButtonWidth: Integer;
begin
  { Unattended removal keeps data unless the caller explicitly opts in. }
  RemovePersonalData := CompareText(ExpandConstant('{param:REMOVEPERSONALDATA|0}'), '1') = 0;
  Result := True;
  if UninstallSilent or not DirExists(ProfileRoot) then exit;

  RemovePersonalData := False;
  Form := CreateCustomForm(ScaleX(560), ScaleY(285), False, False);
  try
    Form.Caption := CM('UninstallOptionsTitle');

    Heading := TNewStaticText.Create(Form);
    Heading.Parent := Form;
    Heading.Left := ScaleX(16);
    Heading.Top := ScaleY(16);
    Heading.AutoSize := False;
    Heading.Width := Form.ClientWidth - ScaleX(32);
    Heading.Caption := CM('UninstallOptionsTitle');
    Heading.Font.Style := [fsBold];
    Heading.AdjustHeight;

    Intro := TNewStaticText.Create(Form);
    Intro.Parent := Form;
    Intro.Left := Heading.Left;
    Intro.Top := Heading.Top + Heading.Height + ScaleY(10);
    Intro.AutoSize := False;
    Intro.WordWrap := True;
    Intro.Width := Heading.Width;
    Intro.Caption := CM('UninstallOptionsIntro');
    Intro.AdjustHeight;

    RemoveDataCheck := TNewCheckBox.Create(Form);
    RemoveDataCheck.Parent := Form;
    RemoveDataCheck.Left := Heading.Left;
    RemoveDataCheck.Top := Intro.Top + Intro.Height + ScaleY(14);
    RemoveDataCheck.Width := Heading.Width;
    RemoveDataCheck.Height := ScaleY(20);
    RemoveDataCheck.Caption := CM('RemovePersonalData');
    RemoveDataCheck.Checked := False;

    Details := TNewStaticText.Create(Form);
    Details.Parent := Form;
    Details.Left := Heading.Left;
    Details.Top := RemoveDataCheck.Top + RemoveDataCheck.Height + ScaleY(8);
    Details.AutoSize := False;
    Details.WordWrap := True;
    Details.Width := Heading.Width;
    Details.Caption := CM('RemovePersonalDataDetails');
    Details.AdjustHeight;

    ProfilePath := TNewEdit.Create(Form);
    ProfilePath.Parent := Form;
    ProfilePath.Left := Heading.Left;
    ProfilePath.Top := Details.Top + Details.Height + ScaleY(5);
    ProfilePath.Width := Heading.Width;
    ProfilePath.Text := ProfileRoot;
    ProfilePath.ReadOnly := True;

    Scope := TNewStaticText.Create(Form);
    Scope.Parent := Form;
    Scope.Left := Heading.Left;
    Scope.Top := ProfilePath.Top + ProfilePath.Height + ScaleY(8);
    Scope.AutoSize := False;
    Scope.WordWrap := True;
    Scope.Width := Heading.Width;
    Scope.Caption := CM('RemovePersonalDataScope');
    Scope.AdjustHeight;

    ContinueButton := TNewButton.Create(Form);
    ContinueButton.Parent := Form;
    ContinueButton.Caption := CM('ContinueUninstall');
    ContinueButton.Top := Form.ClientHeight - ScaleY(39);
    ContinueButton.Height := ScaleY(23);
    ContinueButton.ModalResult := mrOk;
    ContinueButton.Default := True;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := CM('CancelUninstall');
    CancelButton.Top := ContinueButton.Top;
    CancelButton.Height := ContinueButton.Height;
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    ButtonWidth := Form.CalculateButtonWidth([ContinueButton.Caption, CancelButton.Caption]);
    ContinueButton.Width := ButtonWidth;
    CancelButton.Width := ButtonWidth;
    CancelButton.Left := Form.ClientWidth - ScaleX(16) - ButtonWidth;
    ContinueButton.Left := CancelButton.Left - ScaleX(8) - ButtonWidth;
    Form.ActiveControl := RemoveDataCheck;

    Result := Form.ShowModal() = mrOk;
    if Result then RemovePersonalData := RemoveDataCheck.Checked;
  finally
    Form.Free();
  end;
end;

function InitializeUninstall: Boolean;
var
  ExitCode: Integer;
begin
  Result := AskUninstallOptions;
  if not Result then exit;
#ifdef SmokeTestRoot
  Result := True;
#else
  Result := Exec(ExpandConstant('{app}\Spotnet.SetupHelper.exe'), 'close', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then
    SuppressibleMsgBox(CM('UninstallClose'), mbError, MB_OK, IDOK);
#endif
end;

function ShortcutParameters: String;
begin
  Result := ' --profile ' + Quote(ProfileRoot) + ' --desktop ' + Quote(DesktopRoot) + ' --programs ' + Quote(ProgramsRoot);
end;

{ The shortcuts the user asked Setup to add on the tasks page. }
function ShortcutCreation: String;
var
  Wanted: String;
begin
  Wanted := '';
  if WizardIsTaskSelected('programsicon') then Wanted := 'programs';
  if WizardIsTaskSelected('desktopicon') then begin
    if Wanted <> '' then Wanted := Wanted + ',';
    Wanted := Wanted + 'desktop';
  end;
  if Wanted = '' then Wanted := 'none';
  Result := ' --create ' + Wanted;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if CurUninstallStep = usUninstall then begin
    { After confirmation, before application/helper files are removed. }
    if not Exec(ExpandConstant('{app}\Spotnet.SetupHelper.exe'), 'restore-shortcuts' + ShortcutParameters,
        '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      SuppressibleMsgBox(CM('RestoreShortcutFailed') + ' ' + ProfileRoot + '\ShortcutBackups.', mbError, MB_OK, IDOK);
  end;
  if (CurUninstallStep = usPostUninstall) and RemovePersonalData then begin
    { Shortcut backups live inside the profile, so removal must be the final step. }
    Log('Removing the Spotnet 3 profile selected by the user: ' + ProfileRoot);
    if DirExists(ProfileRoot) and not DelTree(ProfileRoot, True, True, True) then
      SuppressibleMsgBox(CM('RemovePersonalDataFailed') + ' ' + ProfileRoot, mbError, MB_OK, IDOK);
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := 0;
  if ShortcutFailure then Result := 10;
  if MoveIncomplete then Result := 11;
end;

// Removes everything a previous version put in the application directory, keeping the
// profile data, the install marker and the uninstaller. The payload is self-contained,
// so anything else is a leftover - and leftovers are not harmless: upgrading from the
// .NET Framework layout left x64\SQLite.Interop.dll behind beside the new copy under
// runtimes, which loaded a second SQLite into the process and corrupted its heap on
// the first query.
procedure CleanApplicationDirectory;
var
  Search: TFindRec;
  Target, Root: String;
begin
  Root := ExpandConstant('{app}');
  if not DirExists(Root) then exit;
  if FindFirst(Root + '\*', Search) then begin
    try
      repeat
        if (Search.Name = '.') or (Search.Name = '..') then continue;
        if CompareText(Search.Name, 'Data') = 0 then continue;
        if CompareText(Search.Name, 'Spotnet.install') = 0 then continue;
        if CompareText(Copy(Search.Name, 1, 5), 'unins') = 0 then continue;
        Target := Root + '\' + Search.Name;
        if (Search.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          DelTree(Target, True, True, True)
        else
          DeleteFile(Target);
      until not FindNext(Search);
    finally
      FindClose(Search);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  ShortcutReport, Heading, MoveParameters: String;
begin
  if CurStep = ssInstall then CleanApplicationDirectory;
  if CurStep = ssPostInstall then begin
    WizardForm.StatusLabel.Caption := CM('StatusShortcuts');
    DeleteFile(ReportFile);
    ShortcutFailure := not Exec(ExpandConstant('{app}\Spotnet.SetupHelper.exe'),
      'shortcuts' + ShortcutParameters + ShortcutCreation + ' --classic-mode ' + ClassicShortcutMode + ' --executable ' + Quote(ExpandConstant('{app}\Spotnet.exe')) + ' --report ' + Quote(ReportFile),
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if ExitCode <> 0 then ShortcutFailure := True;
    if not ReadReport(ShortcutReport) then ShortcutReport := CM('ShortcutReportMissing');
    if IsDutch and not ShortcutFailure then ShortcutReport := CM('ShortcutDone');
    Log(ShortcutReport);
    Summary := Summary + #13#10 + ShortcutReport;
    if ShortcutFailure then
      SuppressibleMsgBox(CM('ShortcutAttention') + #13#10#13#10 + ShortcutReport, mbError, MB_OK, IDOK);
    if MoveClassicData then begin
      MoveIncomplete := True;
      if not ShortcutFailure then begin
        MoveParameters := 'complete-move --profile ' + Quote(ProfileRoot) + ' --source-data ' + Quote(SelectedData);
        if SelectedSettings <> '' then MoveParameters := MoveParameters + ' --source-settings ' + Quote(SelectedSettings);
        MoveIncomplete := not Exec(ExpandConstant('{app}\Spotnet.SetupHelper.exe'), MoveParameters,
          '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
        if ExitCode <> 0 then MoveIncomplete := True;
      end;
      if MoveIncomplete then begin
        Summary := CM('MoveIncomplete');
        SuppressibleMsgBox(Summary, mbError, MB_OK, IDOK);
      end;
    end;
    Heading := CM('Installed');
    if ShortcutFailure or MoveIncomplete then Heading := CM('InstalledAttention');
    WizardForm.FinishedLabel.Caption := Heading + #13#10#13#10 + Summary + #13#10#13#10 +
      CM('ProfileLabel') + ' ' + ProfileRoot + '\Data';
  end;
end;
