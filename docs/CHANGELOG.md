# Changelog

Format: je Phase ein Eintrag.

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

