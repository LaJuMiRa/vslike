# Stand: Neubau als native macOS-App (Branch `native-macos`)

Auftrag (2026-11-20): Oberfläche komplett neu und nativ in Swift, nur macOS (arm64), Build/Test per `release.sh` wie im
Projekt Seasons. Funktionen der Avalonia-Version übernehmen. Neu: eingebautes Terminal, sichtbares grafisches
Klassendiagramm, aufgeräumte Toolbar, Quick-Actions per ⌘⇧. (StyleCop-Fixes, Methode/Klasse erzeugen usw.).

Architektur: Swift-App (SwiftUI + AppKit) + unsichtbarer .NET-Prozess `VsLikeEngine` (Roslyn, StyleCop,
Diagramm-Reader/-Writer) über JSON-RPC auf stdin/stdout. Build, Run, Tests, NuGet, Solution-Datei, Debugger-Anbindung
(netcoredbg, DAP) und Terminal sind Swift.

## Schritte
- [x] C#-Teil nach `engine/` verkleinert, `VsLike.Engine` (JSON-RPC-Host) + End-to-End-Tests über den echten Prozess
- [ ] Swift-Paket: `VsLikeKit` (Protokoll-Client, Modelle, Build-/Test-Parser, Solution-Reader, NuGet, DAP, Diagramm-Layout)
- [ ] App-Shell: Fenster, Sidebar, Tabs, Toolbar, unteres Panel, Befehlspalette, Einstellungen, Lokalisierung de/en
- [ ] Editor: NSTextView, Highlighting (lexikalisch + Roslyn), Zeilennummern, Diagnosen, Completion, Signaturhilfe, Hover, Quick-Actions ⌘⇧.
- [ ] Problems, Output, Terminal (SwiftTerm), Build/Run/Clean, Tests
- [ ] NuGet, neue Solution/Projekt, Verweise, Projekteigenschaften
- [ ] Klassendiagramm (Canvas, Bearbeiten, Undo, Export)
- [ ] Debugger
- [ ] `build.sh`, `release.sh`, Icon, Cask, Workflow, Doku

## Wiedereinstieg
- Engine: `cd engine && dotnet build VsLike.Engine.slnx && dotnet test VsLike.Engine.slnx`
