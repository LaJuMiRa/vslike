# Changelog

## [1.1.0] - 2026-11-22

- Editor: Enter in `void Run() {|}` (oder hinter getipptem `{}`) setzt `{` auf eine eigene Zeile unter die Deklaration, die Schreibmarke eingerückt in die Zeile darunter und `}` in die dritte Zeile. Steht `{` schon allein auf der Zeile oder in einem Kommentar, bleibt es stehen.
- IntelliSense: Tab/Enter ersetzt das bisher getippte Wort. Öffnete sich die Liste nach einem Leerzeichen oder beim ersten Buchstaben, wurde bisher nur der Rest eingefügt (`vo` → `voidvo`).
- Parameterhilfe bleibt nicht mehr hängen: Sie schließt, sobald die Schreibmarke die Argumentliste verlässt (Klick, Pfeiltasten, Enter, Löschen), und eine verspätete Antwort der Engine nach `)` öffnet sie nicht wieder.
- Editor: Enter direkt hinter selbst getipptem `{}` öffnet den Block wie bei `{|}`: `{` bleibt stehen, die Schreibmarke steht eingerückt in der nächsten Zeile, `}` darunter auf eigener Zeile. Bisher entstand nur eine neue Zeile hinter `}`.
- Neue Dateien verwenden einen Block-Namespace (`namespace Demo { … }`) mit eingerücktem Inhalt statt `namespace Demo;`: `Program.cs` und die übrigen Dateien neuer Projekte sowie neue Typen aus dem Klassendiagramm.
- StyleCop-Einstellungsdatei: „Code › StyleCop-Einstellungen …“ (auch per Rechtsklick auf den StyleCop-Knopf der Toolbar) legt neben der Solution eine `stylecop.json` mit Firmenname und Copyright-Text an und öffnet sie. Die Vorgaben dafür stehen unter Einstellungen › StyleCop (Firmenname vorbelegt mit dem macOS-Benutzernamen). Der Fix für den fehlenden Dateikopf (SA1633) schreibt damit z. B. „Copyright (c) Acme. All rights reserved.“ statt „PlaceholderCompany“. Nach dem Speichern der `stylecop.json` gelten die Änderungen sofort für alle Dateien.
- Editor: Der feine graue Strich, der an der Kante der Zeilenleiste bis in Tableiste und Toolbar lief, ist weg (Zeilenleiste und Scrollbereich schneiden wieder an ihren Grenzen ab).
- Klassendiagramm: Für neue Mitglieder genügt der Name. Ohne Typ wird es `int` (Methoden geben weiterhin `void` zurück, Ereignisse `EventHandler`, Parameter ohne Typ `int`), ohne Zugriffszeichen `public` (auch Felder, bisher `private`), Methoden brauchen keine Klammern. Typ und Zugriff können weiterhin in UML-Schreibweise angegeben werden.
- Klassendiagramm: Im Inspektor lässt sich wieder ein neues Mitglied eintippen. Das Eingabefeld wurde in der gruppierten Liste nur als Beschriftung „+ Name: string“ gezeichnet und war nicht anklickbar. Die gewählte Art (Property, Methode …) bleibt nach dem Hinzufügen erhalten statt auf „Property“ zurückzuspringen.
- Klassendiagramm: Rechtsklick auf eine freie Stelle öffnet ein Menü mit „Neue Klasse/Interface/Record/Struct/Enum …“ (an der Klickposition), „Vorhandenen Typ hinzufügen“, „Automatisch anordnen“ und „An Fenster anpassen“.
- Klassendiagramm: Das Kontextmenü einer Box enthält „Basisklasse“ und „Implementiert Interfaces“ (bei Interfaces „Erweitert Interfaces“) zum direkten Setzen der Vererbung.
- Klassendiagramm flüssiger bei vielen Typen: Zoom und Scrollposition werden erst 0,5 s nach der letzten Bewegung in die `.cd`-Datei geschrieben statt bei jedem Trackpad-Ereignis, Boxen und Linien werden nur noch bei Änderungen berechnet, nur die ausgewählte Box hat einen Schatten. Pinch-Zoom funktioniert jetzt auch über den Boxen (bisher nur auf freier Fläche).
- Neue Solutions: Der Speicherort wird gemerkt und als Standard vorgeschlagen; einstellbar unter Einstellungen › Allgemein › „Ordner für neue Solutions“ (Standard weiterhin `~/Developer`).
- Projekteigenschaften: TargetFramework ist ein Auswahlmenü mit einem Eintrag pro installierter SDK-Version (z. B. `net10.0 (.NET SDK 10.0.402, 10.0.401)`); ein abweichender Wert aus der Projektdatei bleibt wählbar.

## [1.0.1] - 2026-11-21

- Die Analyse-Engine läuft mit jedem installierten .NET ab Version 10. Bisher brauchte sie die .NET-10-Laufzeit und startete auf einem Mac mit nur SDK 11 nicht.
- Tests starten `dotnet` wie die App ohne die MSBuild-Variablen des Testprozesses. Unter SDK 11 scheiterte sonst jeder `dotnet restore` der Testbeispiele mit MSB4216.
- Release-Workflow: Abbruch nach 45 Minuten, netcoredbg wird vor den App-Tests geladen (auf einem frischen Runner fehlte er den Debugger-Tests), Tests auch mit dem vorinstallierten SDK 11, Release-Notes enden an der nächsten `## `-Überschrift.

## [1.0.0] - 2026-11-21

Neubau als native macOS-App (nur macOS 26, Apple Silicon).

- Oberfläche komplett in Swift (SwiftUI und AppKit): neues Fenster mit Seitenleiste (Solution, Dateien, Suche, Tests), schlanke Tabs mit Breadcrumb, Toolbar mit Starten/Stoppen/Bauen links, Zielprojekt, Konfiguration und Status-Kapsel in der Mitte, StyleCop, Klassendiagramm, Terminal, Panel und Inspektor rechts.
- Code-Editor auf NSTextView-Basis: Lexer-Highlighting ohne Verzögerung plus semantische Farben von Roslyn, Zeilenleiste mit Haltepunkten und Fehlermarken, Markierungen in der Scrollleiste, Klammerpaare, automatische Einrückung und Klammern, Snippets, Kommentar umschalten.
- IntelliSense-Popups nativ: Vervollständigung mit Symbolen und Dokumentation, Parameterhilfe, Hover-Karten mit Fehlern.
- Quick-Actions auf ⇧⌘. (Menü an der Schreibmarke: Korrekturen, „Alle beheben“, Refactorings) und „Alle StyleCop-Meldungen der Datei beheben“ (⌥⇧⌘.).
- Eingebautes Terminal (SwiftTerm); „Ohne Debugger starten“ läuft im Terminal, das Programm kann also Eingaben lesen.
- Klassendiagramm als grafische Fläche mit Zoom, Verschieben, Raster, schwebender Werkzeugleiste, Inspektor zum Bearbeiten von Typen und Mitgliedern, Export als SVG/PNG. Öffnet direkt über die Toolbar (⇧⌘D) mit allen Typen der Solution.
- Haltepunkte werden pro Solution gespeichert und wandern beim Bearbeiten mit.
- C#-Analyse als Hintergrundprozess `VsLikeEngine` (Roslyn, StyleCop, Diagramm-Writer) hinter JSON-RPC; Build, Tests, NuGet, Vorlagen, Solution-Dateien und Debugger-Anbindung (netcoredbg) sind neu in Swift.
- `build.sh` und `release.sh` wie im Projekt Seasons; `release.sh` führt alle Tests aus, baut und packt das Zip.
- Debuggen im Terminal: Das Programm läuft in einem Terminal-Tab und kann Eingaben lesen. Ein Starthelfer (`VsLike.DebugStartupHook`, im App-Paket) hält es an, bis netcoredbg verbunden ist und die Haltepunkte gesetzt sind, damit auch ein Haltepunkt in der ersten Zeile greift. Das Ende des Programms wird zuverlässig erkannt; bisher blieb der Tab gelegentlich auf „läuft“ und der Prozess als Zombie zurück.
- Fehler und Warnungen werden als Wellenlinie genau unter den betroffenen Zeichen unterstrichen.
- Klassendiagramme im `.cd`-Format öffnen und speichern, auch per Doppelklick im Finder. Positionen und der eingeklappte Zustand bleiben erhalten, die passende Solution oder das Projekt wird automatisch mitgeöffnet.
- StyleCop SA1600: Der Fix ergänzt Dokumentationskommentare im Wortlaut von StyleCop, z. B. „Gets or sets …“, „Initializes a new instance of …“, `<param>`, `<typeparam>`, `<returns>` und `<inheritdoc/>` bei Überschreibungen. Mit „Alle beheben“ geht das für die ganze Datei oder das ganze Projekt. Quick-Actions, die in anderen IDEs nur einen Optionsdialog öffnen („…“), werden ausgeblendet.
- Debug-Panel: Die Spalten teilen sich die verfügbare Breite und schieben die Seitenleisten nicht mehr aus dem Fenster.
- Neue Konsolenprojekte bekommen in `Program.cs` den Namespace des Projekts, eine Klasse `Program` und `Main` mit „Hello, World!“ statt Top-Level-Anweisungen. Vorlagen ohne diese Option werden unverändert angelegt.
- Klassendiagramme werden als `<Projekt>.cd` (Format wie in Visual Studio) im Ordner des Startprojekts neben `Program.cs` angelegt. Ältere `.cdiagram.json`-Dateien lassen sich weiter öffnen.
- Neue Typen aus dem Klassendiagramm landen im Ordner des Diagramms mit dem Namespace des Projekts. Liegt das Diagramm außerhalb aller Projekte, z. B. ein älteres Diagramm neben der Solution, landen sie im Startprojekt.
- Entfernt: Avalonia-Oberfläche, Linux-Pakete und Intel-Build.

## Avalonia-Version (Phasen 0–11, nicht veröffentlicht)

## Phase 0 – Gerüst (2026-11-20)
- `VsLike.slnx` mit 7 Quell- und 7 Testprojekten, zentrale Paketversionen, `.editorconfig`, StyleCop 1.3.180 für eigenen Code, `global.json` (SDK 10.0.400+).
- Leeres Avalonia-Fenster, `IPlatformService` (macOS/Linux) mit Tests.
- GitHub-Actions-CI für macos-15 (arm64), macos-15-intel (x64), ubuntu-24.04.
- MIT-Lizenz, README.

## Phase 1 – Shell (2026-11-20)
- Command-Registry mit plattformneutralen Tastenkürzeln (`Cmd` = ⌘ auf macOS, Ctrl auf Linux) und Benutzer-Overrides aus `settings.json`.
- Command Palette (⇧⌘P / Ctrl+Shift+P) mit Fuzzy-Suche, Pfeiltasten, Enter, Esc.
- Menüs aus `MenuRegistry`: native macOS-Menüleiste, Menü im Fenster unter Linux.
- Dock-Layout (Explorer links, Probleme/Ausgabe unten, Eigenschaften rechts, Dokumente Mitte); Panels schließ-, verschieb- und über „Ansicht“ wieder einblendbar.
- Layout-Persistenz in `layout.json` (Panel-Bereich, Reihenfolge, Sichtbarkeit, Größen, Fenstergröße), „Fensterlayout zurücksetzen“.
- Eigenes Dark/Light-Theme (Schiefer + Bernstein), System-Modus, Dock-Theme auf eigene Tokens umgelenkt.
- UI-Texte Deutsch/Englisch (`strings.tsv` → resx via `tools/strings.py`), live umschaltbar.

## Phase 2 – Editor (2026-11-20)
- AvaloniaEdit + TextMate mit eigenen Farbschemata (`vslike-dark`, `vslike-light`); Highlighting für C#, csproj/slnx/props/xml/axaml, JSON, Markdown.
- Folding: Klammern und `#region` für C#/JSON (Strings/Kommentare werden übersprungen, Berechnung im Hintergrund), XML-Folding.
- Suchen/Ersetzen in der Datei (Regex, Groß-/Kleinschreibung, ganzes Wort) über das AvaloniaEdit-Suchpanel; „In Dateien suchen“ mit Ersetzen über den ganzen Ordner.
- Auto-Klammern und -Anführungszeichen (Überschreiben, Paar löschen), Zeilen-/Blockkommentare (⌘/), Mehrfach-Cursor (⌥-Klick, ⌘D) mit einem Undo-Schritt.
- Tabs mit Dirty-Markierung, Neu/Öffnen/Speichern/Speichern unter/Alle speichern/Schließen mit Rückfrage, auch beim Beenden; Kodierung inkl. BOM bleibt erhalten.
- Explorer mit Dateibaum (bin/obj/.git ausgeblendet), FileSystemWatcher mit Debounce; unveränderte Tabs laden externe Änderungen neu, geänderte melden einen Konflikt.
- Statusleiste mit Zeile/Spalte und Sprache; Startargumente öffnen Ordner/Dateien.
- Messung: 50.004-Zeilen-Datei lädt und rendert headless in 225 ms.

## Phase 3 – Roslyn-Workspace (2026-11-20)
- `ICodeWorkspace` in Core (Roslyn-frei: `DiagnosticInfo`, `ClassifiedSpan`, `QuickInfo`, `WorkspaceProject`); Implementierung `RoslynWorkspace` mit MSBuildLocator + MSBuildWorkspace für `.sln`, `.slnx`, `.csproj`.
- Unveränderlicher Solution-Snapshot, Editor-Text per `WithDocumentText` (nie auf die Platte); lose `.cs`-Dateien ohne Projekt bekommen ein „Miscellaneous“-Projekt mit BCL-Referenzen.
- Live-Diagnosen (250 ms Entprellung), Wellenlinien, die beim Tippen mitwandern, semantische Einfärbung des sichtbaren Bereichs, Hover mit Signatur, XML-Doku und Diagnosen.
- Fehlerliste mit Filtern (Fehler/Warnungen/Meldungen, Text), Doppelklick springt zu Datei/Zeile/Spalte, Zähler in der Statusleiste.
- „Solution öffnen“ (⇧⌘O) und Startargument: automatisches `dotnet restore`, wenn `obj/project.assets.json` fehlt oder älter als die csproj ist.
- SDK-Prüfung beim Start (`dotnet --list-sdks`, mindestens 10.0) mit Installationshinweis; `dotnet`-Aufrufe ohne geerbte MSBuild-Umgebungsvariablen.
- Messungen: Test-Solution mit 3 Projekten lädt in 1,0–1,4 s; Fehler erscheint 654 ms nach der Eingabe in der Fehlerliste (App-Test) bzw. < 1 s im Workspace-Test.
- Behoben: Tabs zeigten nach dem ersten Rendern den Inhalt des vorherigen Tabs (eigener `ViewLocator`); Absturz beim Neuaufbau des nativen macOS-Menüs; Fluent-Akzentfarbe.

## Phase 5 – Solution/Projekte/NuGet (2026-11-20)
- Solution Explorer: Solution → Solution-Ordner → Projekte → Abhängigkeiten (Projekte, Pakete) → Ordner/Dateien; Aufklappzustand bleibt bei Aktualisierung erhalten, bin/obj ausgeblendet.
- Kontextmenü: Neue Klasse (file-scoped Namespace aus RootNamespace + Ordner), Neuer Ordner, Umbenennen, Löschen mit Rückfrage, Verschieben per Ziehen; Workspace und offene Tabs ziehen mit.
- „Neue Solution“ und „Neues Projekt“ mit Vorlagenauswahl aus `dotnet new list` (Konsole, Klassenbibliothek, xUnit, Web …), Ziel-Framework, `.slnx` oder `.sln`; „Vorhandenes Projekt hinzufügen“, „Projekt entfernen“, „Solution neu laden“.
- Projektverweise per Dialog (mehrere auf einmal), Projekteigenschaften als Formular (OutputType, TargetFramework, Nullable, ImplicitUsings, LangVersion, TreatWarningsAsErrors, GenerateDocumentationFile, InvariantGlobalization), XML-Formatierung der csproj bleibt erhalten.
- NuGet-Manager: Suche über die NuGet-v3-API, Versionsauswahl, installierte und aktualisierbare Pakete (`dotnet list package --format json`), Installieren/Entfernen/Aktualisieren über `dotnet add/remove package`.
- Lesen von `.slnx` und `.sln` (inkl. Solution-Ordner) in `SolutionReader`.
- Abnahme (Integrationstest): neue Solution mit 2 Projekten + Projektverweis baut und läuft mit `dotnet run`.
- Behoben: FileSystemWatcher-Ereignisse eines bereits geschlossenen Ordners liefen noch auf dem UI-Thread und brachen ab, wenn die Solution-Datei gelöscht war.

## Phase 6 – Build/Run/Test (2026-11-20)
- Build, Neu erstellen, Bereinigen (Solution oder einzelnes Projekt) über `dotnet build/clean` mit Live-Ausgabe im Ausgabefenster; Abbrechen beendet den ganzen Prozessbaum.
- MSBuild-Diagnosen (`Datei(Zeile,Spalte): error CS…`, Projekt- und NuGet-Meldungen) landen dedupliziert in der Fehlerliste (Quelle „Build“); Ausgabezeilen mit Diagnose sind farbig und per Doppelklick anspringbar. Eine Live-Analyse ersetzt veraltete Compilerfehler des letzten Builds für dieselbe Datei.
- Werkzeugleiste: Konfiguration Debug/Release, Startprojekt (Exe/WinExe/Web/Worker, ohne Testprojekte), Startprofil aus `Properties/launchSettings.json` (Argumente, Umgebungsvariablen, Arbeitsordner), Starten ohne Debugger (⌘R), Beenden (⇧⌘.), Build (⌘B).
- Auswahl pro Solution in `.vslike/settings.json` (relativ, nur bei Änderung geschrieben); „Als Startprojekt festlegen“ im Solution Explorer.
- Test-Explorer: findet Tests aller Testprojekte (`dotnet test --list-tests`), Baum Projekt → Namespace → Klasse → Methode → Datenfall, Alle/Auswahl/Fehlgeschlagene ausführen, Ergebnisse aus TRX mit Dauer, Meldung und Stacktrace; Doppelklick springt zur fehlschlagenden Zeile bzw. zur Testmethode (Roslyn-Symbolsuche).
- Abnahme (App-Test): Build-Fehler in einer Console-App springt per Klick zur Zeile; nach der Korrektur baut und läuft die App mit Ausgabe.
- Behoben: UI-Tests scheiterten zufällig (siehe DECISIONS); das Hauptfenster schließt sich jetzt mit seinem Workbench, und Hintergrund-Diagnosen nach dem Schließen werden verworfen.

## Phase 7 – StyleCop (2026-11-20)
- StyleCop.Analyzers 1.3.180 (feste Version) wird aus dem NuGet-Cache per `AnalyzerFileReference` geladen; fehlt sie, lädt ein `dotnet restore` mit `PackageDownload` sie in den Cache. Aus = nicht geladen.
- Live umschaltbar ohne Neustart (⌥⇧⌘S bzw. Build-Menü); Zustand pro Solution in `.vslike/settings.json`, sonst globaler Standard („StyleCop-Standard für neue Solutions umschalten“). Statusleiste zeigt „StyleCop“, wenn aktiv.
- Diagnosen erscheinen mit Kategorie „StyleCop“ in Editor und Fehlerliste; `.editorconfig`-Schweregrade und `stylecop.json` (nächstgelegene Datei bis zum Solution-Ordner, als AdditionalFile nur im Snapshot) werden respektiert, die csproj bleibt unverändert.
- Code-Fixes von StyleCop im ⌘.-Menü, dazu „Alle im Dokument beheben“ und „Alle im Projekt beheben“ (für alle Fixes mit Fix-All-Unterstützung).
- Optional „StyleCop als Paketverweis übernehmen“ (mit Bestätigung, über `dotnet add package`).
- Abnahme: Test-Solution zeigt mit StyleCop SA1200/SA1633, ohne keine StyleCop-Diagnosen; nach erneutem Öffnen ist der Zustand erhalten (App- und Roslyn-Tests).

## Phase 8 – Klassendiagramm (2026-11-20)
- Modell (`VsLike.Diagram/Model`): class, interface, struct, enum, record, record struct, Namespaces, Zugriff, abstract/static/sealed/partial, Generics mit Constraints; Felder, Properties (auto/init/required), Methoden, Konstruktoren, Events, Enum-Werte.
- Roslyn-Reader: partielle Typen über mehrere Dateien, Vererbung/Implementierung, Assoziation und Komposition aus Feld-/Property-Typen (Collections/Arrays → Multiplizität *), Fremdtypen als „extern“ (grau, schreibgeschützt).
- Writer (Snapshot-Tests mit Verify, 21 Fälle): Typdatei mit file-scoped Namespace, Mitglied hinzufügen/ändern/entfernen, Basisliste, Modifizierer, Typ löschen. Roslyn findet den Knoten, ersetzt wird nur dessen Textbereich; Zeilenende (LF/CRLF) und Einrückung (Spaces/Tabs) werden übernommen, Kommentare und Rümpfe bleiben.
- Diagramm-Tab mit eigener Zeichenfläche: Raster, Snap, Zoom (⌘/Ctrl + Scrollen), Pan, Einpassen, Auto-Layout (Basistypen oben), Ein-/Ausklappen; Werkzeugkasten (neue Typen platzieren, Erben/Implementieren ziehen ⇒ `: Basis, IFoo`), Eigenschaften-Bereich (Umbenennen, Zugriff/Modifizierer, Basistypen, Mitglieder in UML-Notation `+ Total(withTax: bool): decimal`).
- Neue Klasse ⇒ `<Typname>.cs` im Ordner des Diagramms, Namespace aus RootNamespace + Ordner; Umbenennen über Roslyn inkl. Referenzen und Dateiname; Löschen mit Bestätigung (Anzahl Verwendungen).
- Codeänderungen (Editor oder extern per FileSystemWatcher) aktualisieren offene Diagramme, Positionen bleiben; Layout nur in `<Name>.cdiagram.json`; „Klassendiagramm anzeigen“ für Projekte und Ordner im Solution Explorer.
- Gemeinsames Undo/Redo (⌘Z/⇧⌘Z im Diagramm) über Code- und Layoutänderungen; Export als SVG und PNG.
- Abnahme (App-Test): Klasse anlegen erzeugt Datei; Vererbung ziehen ergänzt `: Car`; Methode landet nur in `Car.cs`, übriger Text byte-gleich; externe Änderung erscheint im Diagramm; Rückgängig/Wiederholen stellen Code wieder her.

## Phase 9 – Debugger (2026-11-20)
- DAP-Client (`VsLike.Debugging`) und Sitzung mit netcoredbg (`--interpreter=vscode`); netcoredbg wird gefunden (Umgebung, App-Paket, Cache) oder in der gepinnten Version geladen und per SHA-256 geprüft. Kein vsdbg.
- Haltepunkte per Klick in die Leiste links (F9), bedingte Haltepunkte per Rechtsklick (Raute), wandern beim Bearbeiten mit; Start (F5) baut das Startprojekt und nutzt das Startprofil (Argumente, Umgebung, Arbeitsordner).
- Halt: Ausführungszeile mit Pfeil und Hinterlegung, Aufrufliste (Auswahl wechselt Variablen und springt zum Code), Threads, Variablenbaum mit nachgeladenen Kindern, Überwachen, Debug-Konsole mit Programmausgabe und Auswertung, Hover-Werte im Editor.
- Fortsetzen (F5), Prozedurschritt (F10), Einzelschritt (F11), Rücksprung (⇧F11), Anhalten, Beenden (⇧F5); Debug-Leiste in der Werkzeugleiste.
- Abnahme: Integrationstest gegen echten netcoredbg (bedingter Haltepunkt `i == 3`, Variablen, Step In/Out, Auswertung, Ausgabe) und App-Test (Haltepunkt in `Car.Drive` trifft, Variablen/Überwachen/Hover, Schritt, Programmende).

## Phase 10 – Packaging & Homebrew (2026-11-20)
- `packaging/macos/build-app.sh <rid> <version>`: self-contained Publish (osx-arm64/osx-x64), `VsLike.app` mit `Info.plist` (Bundle-ID `io.github.lajumira.vslike`, Dateizuordnungen `.cs/.sln/.slnx/.csproj`), eigenem Icon (per CoreGraphics gezeichnet), gebündeltem netcoredbg (SHA-256 geprüft), `.zip`, `.dmg` und Prüfsumme.
- Signierung: ohne Secrets ad-hoc (Apple Silicon startet das Bundle), mit Secrets Developer-ID-Signatur mit Hardened Runtime und Entitlements sowie Notarisierung/Stapling.
- Dateien aus dem Finder bzw. per Dateizuordnung öffnen die Solution oder Datei in der laufenden IDE.
- Release-Workflow (`.github/workflows/release.yml`) bei Tag `v*`: Bundles für beide Architekturen, GitHub-Release, danach Pull Request im Tap `LaJuMiRa/homebrew-tap`, der nur `Casks/vslike.rb` ändert (Token `TAP_GITHUB_TOKEN`; ohne Token wird der Schritt übersprungen).
- Cask-Vorlage `packaging/homebrew/vslike.rb.template` (arm/intel, `zap`, Hinweise zu .NET SDK und Gatekeeper); README mit Installation und Gatekeeper-Hinweis.
- Geprüft: arm64-Bundle lokal gebaut, Signatur des ausgelieferten Zips gültig, entpackte App startet und lädt eine Solution über das installierte SDK.

## Phase 11 – Linux (2026-11-20)
- `packaging/linux/build-tarball.sh <linux-x64|linux-arm64> <version>`: self-contained Tarball mit App in `lib/`, gebündeltem netcoredbg (SHA-256 geprüft), Starter `vslike`, `.desktop`-Datei und Icon.
- Homebrew-Formula-Vorlage `packaging/homebrew/vslike-formula.rb.template` (x64/arm64, `depends_on :linux`, Desktop-Eintrag, Test); der Release-Workflow baut beide Tarballs und aktualisiert `Formula/vslike.rb` im selben Tap-PR wie den Cask.
- Plattformunterschiede geprüft: Menü im Fenster (Test), Ctrl statt ⌘ (KeyChord), Dateidialoge über Avalonias StorageProvider, Einstellungen/Cache nach XDG, Schriften mit DejaVu-Rückfall, dotnet-Suche in `/usr/share/dotnet` und `~/.dotnet`; CI baut und testet auf ubuntu-24.04.
- Behoben: Der Starter `vslike` hätte auf case-insensitiven Dateisystemen die App-Binärdatei `VsLike` überschrieben (daher `lib/`).

## Abschluss (2026-11-20)
- End-to-End-Test (`EndToEndTests`): neue Solution → Klasse im Diagramm → IntelliSense → StyleCop an/aus → Build → Debuggen.
- `docs/RELEASE_CHECKLIST.md` mit Abnahmen je Phase und den offenen Schritten bis zum ersten Release.

