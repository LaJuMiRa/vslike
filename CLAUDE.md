# VsLike – Projektgedächtnis

Native C#-IDE für macOS (arm64, macOS 26). Funktional auf VS-Niveau für C#/.NET, eigenes Design.
Anzeigename „VsLike“, Cask `vslike`, Lizenz MIT, UI Deutsch + Englisch (Localizable.strings).
`VsLike.*` ist nur der interne Namespace der IDE; Nutzerprojekte bekommen ihre eigenen Namen/Namespaces.
Keine Microsoft-Marken/Assets/Farben/Icons, keine Telemetrie, keine Secrets.

Stand und nächster Schritt: `docs/STATUS.md`. Entscheidungen: `docs/DECISIONS.md`. Offene Punkte: `docs/OPEN_QUESTIONS.md`.

## Tech-Stack (verbindlich, seit 2026-11-21)
- Native macOS-App (nur macOS 26, arm64): Swift 6.2, SwiftUI + AppKit (Editor: NSTextView/TextKit 1), SwiftPM ohne Xcode-Projekt
- Terminal: SwiftTerm 1.11.2 (gepinnt)
- C#-Analyse: .NET-10-Prozess `engine/` (Roslyn als Bibliothek, StyleCop.Analyzers, Diagramm-Writer) hinter JSON-RPC (stdin/stdout)
- Debugger: netcoredbg via DAP, im App-Paket. **vsdbg verboten.**
- Tests: Swift Testing (`swift test`), xUnit für die Engine (`dotnet test engine/VsLike.Engine.slnx`)
- Build/Release: `./build.sh`, `./release.sh` (wie Projekt Seasons), Workflow auf macos-26
- Nutzer braucht installiertes .NET SDK 10; der Startbildschirm prüft das.

## Architektur
```
Sources/VsLike      macOS-App (App, Workspace-Modell, Editor, Sidebar, Panels, Diagram, Sheets)
Sources/VsLikeKit   Swift ohne AppKit: Engine-Client, dotnet-CLI, Solution/Projekt, DAP, Lexer, Diagramm-Layout
engine/             VsLike.Core/Roslyn/Diagram/Engine (C#) + Tests + TestAssets
Tests/              VsLikeKitTests, VsLikeTests
packaging/ scripts/ docs/
```

## Arbeitsregeln
- Swift: strict concurrency (Swift 6), UI-Klassen `@MainActor`, keine Warnungen. C# in `engine/`: Nullable, TreatWarningsAsErrors, StyleCop.
- UI-Thread nie blockieren: Roslyn im Hintergrund, CancellationToken, debounced. Ziel Completion < 150 ms.
- Pro Schritt: planen → umsetzen → Tests → `./release.sh` bzw. `swift test` + `dotnet test` grün → `CHANGELOG.md` → Commit (Conventional Commits).
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
- Layout nur in `<Name>.cd` (Format wie in VS) im Projektordner neben Program.cs; alte `.cdiagram.json` werden weiter geöffnet.
- Rename/Löschen über Roslyn inkl. Referenzen, mit Bestätigung. Fremdtypen „extern“ (read-only, grau).
- Gemeinsames Undo/Redo Diagramm+Code. Zoom/Pan/Raster/Snap/Auto-Layout, Export SVG/PNG.
- Writer zuerst per Snapshot-Tests (Eingabecode + Aktion → erwarteter Code).

### StyleCop
- Schalter pro Solution (`.vslike/settings.json`) + globaler Default; live umschaltbar.
- An: feste StyleCop.Analyzers-Version aus dem NuGet-Cache in Roslyn-Diagnostics, Kategorie „StyleCop“. Aus: nicht geladen.
- `.editorconfig` und `stylecop.json` respektieren; csproj wird nicht verändert (optional „Als PackageReference übernehmen“).
- Code-Fixes inkl. Fix all in Document/Project.

## Phasen
Avalonia-Version (Phasen 0–11) abgeschlossen und ersetzt durch den nativen Neubau (Branch `native-macos`). Homebrew: Tap `LaJuMiRa/homebrew-tap`, nur `Casks/vslike.rb` ergänzen.
