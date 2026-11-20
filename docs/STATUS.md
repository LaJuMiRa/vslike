# Stand: native macOS-App (Branch `native-macos`)

Neubau abgeschlossen (2026-11-21): Swift-Oberfläche + .NET-Analyse-Prozess, `release.sh` grün.

- [x] C#-Teil auf `engine/` reduziert, `VsLikeEngine` (JSON-RPC) mit End-to-End-Tests
- [x] `VsLikeKit`: Engine-Client, dotnet-CLI, Solution/Projekt, NuGet, DAP-Debugger, Lexer, Snippets, Diagramm-Layout/SVG
- [x] App: Fenster, Seitenleiste, Tabs, Toolbar, Panels, Befehlspalette, Einstellungen, de/en
- [x] Editor: Highlighting, Zeilenleiste, Diagnosen, Completion, Signaturhilfe, Hover, Quick-Actions ⇧⌘., Snippets
- [x] Probleme, Ausgabe, Terminal, Build/Run/Clean, Test-Explorer
- [x] NuGet, neue Solution/Projekt, Verweise, Projekteigenschaften
- [x] Klassendiagramm grafisch, Bearbeiten, Undo, Export
- [x] Debugger
- [x] `build.sh`, `release.sh`, Icon, Cask, Workflow, Doku

## Wiedereinstieg
- Alles prüfen und packen: `./release.sh` (Engine-Tests, `swift test`, Build, Kurztest, Zip)
- Nur bauen: `./build.sh` → `build/VsLike.app`
- Engine allein: `cd engine && dotnet build VsLike.Engine.slnx && dotnet test VsLike.Engine.slnx`
- Swift-Tests brauchen die gebaute Engine (`engine/artifacts.nosync/bin/VsLike.Engine/debug/VsLikeEngine.dll`) und netcoredbg im Cache (legt `build.sh` an)
- Fenster-Screenshot: `scripts/shot.sh out.png`; App mit Dateien starten: `open -a build/VsLike.app Solution.slnx Datei.cs` (Kommandozeilen-Argumente verhindern das Fenster, `open` nicht)
- Offene Punkte: `docs/OPEN_QUESTIONS.md`
