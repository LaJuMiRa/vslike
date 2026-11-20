# VsLike – Projektgedächtnis

C#-IDE für macOS (arm64 + x64), danach Linux. Funktional auf VS-Niveau für C#/.NET, eigenes Design.
Anzeigename „VsLike“, Cask `vslike`, Lizenz MIT, UI Deutsch + Englisch (resx).
`VsLike.*` ist nur der interne Namespace der IDE; Nutzerprojekte bekommen ihre eigenen Namen/Namespaces.
Keine Microsoft-Marken/Assets/Farben/Icons, keine Telemetrie, keine Secrets.

Stand und nächster Schritt: `docs/STATUS.md`. Entscheidungen: `docs/DECISIONS.md`. Offene Punkte: `docs/OPEN_QUESTIONS.md`.

## Tech-Stack (verbindlich)
- C# 14 / .NET 10 (LTS)
- Avalonia UI 11.x, MVVM mit CommunityToolkit.Mvvm, Docking mit Dock.Avalonia
- Editor: AvaloniaEdit + AvaloniaEdit.TextMate
- Roslyn als Bibliothek (Microsoft.CodeAnalysis.*, Workspaces.MSBuild, Build.Locator, CSharp.Features); instabile Features-API immer hinter eigenen Interfaces in `VsLike.Core`
- StyleCop.Analyzers über `AnalyzerFileReference`
- Debugger: netcoredbg via DAP (`--interpreter=vscode`). **vsdbg verboten.**
- Tests: xUnit, Avalonia.Headless.XUnit, Verify (Snapshots)
- CI: GitHub Actions (macOS arm64, macOS x64, ubuntu)
- Nutzer braucht installiertes .NET SDK; IDE prüft das beim Start.

## Architektur
```
src/VsLike.App        Avalonia-Host, Views, ViewModels, Shell
src/VsLike.Core       Interfaces, Services, Events, Settings (kein Avalonia, kein Roslyn)
src/VsLike.Editor     AvaloniaEdit-Integration, Highlighting, Completion-Popup
src/VsLike.Roslyn     Workspace, Completion, Diagnostics, Refactorings, Analyzer
src/VsLike.Projects   Solution/Projekt-Modell, Templates, NuGet, Build/Run
src/VsLike.Diagram    Modell, Canvas-Control, Roslyn-Codegenerierung
src/VsLike.Debugging  DAP-Client, netcoredbg
tests/                je Projekt ein Testprojekt
packaging/ docs/
```
Abhängigkeiten: App → alle; alle anderen → Core. Plattformspezifisches hinter `IPlatformService`.

## Arbeitsregeln
- Nullable an, TreatWarningsAsErrors, `.editorconfig`, zentrale Versionen in `Directory.Packages.props`, eigener Code folgt StyleCop.
- UI-Thread nie blockieren: Roslyn im Hintergrund, CancellationToken, debounced. Ziel Completion < 150 ms.
- Pro Phase: planen → umsetzen → Tests → `dotnet build` + `dotnet test` grün → `docs/CHANGELOG.md` → Commit (Conventional Commits) → nächste Phase.
- Unsichere APIs: Version/Doku prüfen oder Spike bauen, nicht raten. Offenes nach `docs/OPEN_QUESTIONS.md`.
- Entscheidungen ohne Rückfrage treffen und in `docs/DECISIONS.md` festhalten.

## Spezifikationen
### Klassendiagramm (bidirektional)
- Elemente: class, interface, struct, enum, record (+ record struct), Namespaces, Zugriffsmodifizierer, abstract/static/sealed/partial, Generics mit Constraints.
- Mitglieder: Felder, Properties (auto/init/required), Methoden, Konstruktoren, Events, Enum-Werte.
- Vererbung/Interface-Implementierung per Pfeil ⇒ `: Basis, IFoo`. Assoziation/Komposition aus Feld-/Property-Typen abgeleitet.
- Neue Klasse ⇒ `<Typname>.cs` im gewählten Ordner, file-scoped Namespace aus RootNamespace + Ordner.
- Jede Diagrammänderung per Roslyn in die richtige Datei, nur betroffener Knoten, Formatierung/Kommentare bleiben.
- Codeänderungen (auch extern, FileSystemWatcher) aktualisieren das Diagramm inkrementell, Positionen bleiben.
- Layout nur in `<Name>.cdiagram.json`.
- Rename/Löschen über Roslyn inkl. Referenzen, mit Bestätigung. Fremdtypen „extern“ (read-only, grau).
- Gemeinsames Undo/Redo Diagramm+Code. Zoom/Pan/Raster/Snap/Auto-Layout, Export SVG/PNG.
- Writer zuerst per Snapshot-Tests (Eingabecode + Aktion → erwarteter Code).

### StyleCop
- Schalter pro Solution (`.vslike/settings.json`) + globaler Default; live umschaltbar.
- An: feste StyleCop.Analyzers-Version aus dem NuGet-Cache in Roslyn-Diagnostics, Kategorie „StyleCop“. Aus: nicht geladen.
- `.editorconfig` und `stylecop.json` respektieren; csproj wird nicht verändert (optional „Als PackageReference übernehmen“).
- Code-Fixes inkl. Fix all in Document/Project.

## Phasen
0 Gerüst · 1 Shell · 2 Editor · 3 Roslyn-Workspace · 4 IntelliSense · 5 Solution/Projekte/NuGet · 6 Build/Run/Test · 7 StyleCop · 8 Klassendiagramm · 9 Debugger · 10 Packaging/Homebrew (Tap `LaJuMiRa/homebrew-tap`, nur `Casks/vslike.rb` ergänzen) · 11 Linux (Formula `Formula/vslike.rb`)
