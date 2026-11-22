# VsLike

**Eine native C#-IDE für macOS.** Editor mit IntelliSense, Build, Tests, Debugger, StyleCop und ein Klassendiagramm,
das direkt in den Code schreibt. Die Oberfläche ist in Swift geschrieben (SwiftUI und AppKit), die C#-Analyse übernimmt
Roslyn in einem Hintergrundprozess. Keine Telemetrie, Lizenz MIT.

![Editor mit Live-Diagnosen und Probleme-Panel](docs/images/editor.png)

## Funktionen

- **Editor und IntelliSense:** sofortiges Highlighting plus semantische Farben von Roslyn, Vervollständigung mit
  Dokumentation, Parameterhilfe, Hover-Infos, Fehler live beim Tippen, Gehe zu Definition (F12), Verweise suchen
  (⇧F12), Umbenennen (F2), Formatieren (⌥⇧F), Snippets wie `prop` oder `ctor`.
- **Quick-Actions (⇧⌘.):** Korrekturen wie fehlende `using`-Direktiven oder fehlende Methoden, Refactorings und
  „Alle beheben“ für Dokument oder Projekt.
- **StyleCop:** pro Solution in der Toolbar schaltbar, respektiert `.editorconfig` und `stylecop.json`, verändert die
  `.csproj` nicht.
- **Build, Tests, Debugger:** Bauen (⌘B), Test-Explorer (⌘U), Debuggen mit netcoredbg inklusive bedingter
  Haltepunkte, Variablen, Überwachen und Aufrufliste. Programme laufen im eingebauten Terminal und können Eingaben lesen.
- **Projekte:** neue Solutions und Projekte aus Vorlagen, NuGet-Pakete, Projekteigenschaften und -verweise.
- **Navigation:** Schnell öffnen (⌘P), Befehlspalette (⇧⌘P), Suche in allen Dateien (⇧⌘F).

Die Oberfläche gibt es auf Deutsch und Englisch.

### Klassendiagramm

Das Klassendiagramm (⇧⌘D) arbeitet in beide Richtungen: Typen, Mitglieder und Vererbung im Diagramm ändern den Code
über Roslyn, Codeänderungen erscheinen sofort im Diagramm. Das Layout liegt im `.cd`-Format neben dem Projekt, Undo
umfasst Diagramm und Code gemeinsam, Export als SVG oder PNG.

![Klassendiagramm mit Vererbung, Interface-Implementierung und Assoziationen](docs/images/class-diagram.png)

## Installation

Voraussetzungen: Mac mit Apple Silicon, macOS 26 oder neuer und ein .NET SDK ab Version 10
(`brew install --cask dotnet-sdk`). Der Startbildschirm zeigt an, ob ein passendes SDK gefunden wurde.

```sh
brew install --cask LaJuMiRa/tap/vslike
```

VsLike ist ad-hoc signiert, aber nicht von Apple notarisiert, deshalb blockiert macOS den ersten Start. Erlaube ihn
einmal unter *Systemeinstellungen › Datenschutz & Sicherheit › „Dennoch öffnen“* oder entferne die Quarantäne:

```sh
xattr -dr com.apple.quarantine /Applications/VsLike.app
```

## Aus dem Quellcode bauen

Nötig sind Xcode 26 (oder die Command Line Tools mit Swift 6.2) und das .NET SDK 10.

```sh
./build.sh      # baut build/VsLike.app
./release.sh    # führt alle Tests aus, baut und packt dist/VsLike-<version>-arm64.zip
```

| Ordner | Inhalt |
|---|---|
| `Sources/VsLike` | macOS-App: Fenster, Editor, Panels, Terminal, Klassendiagramm |
| `Sources/VsLikeKit` | Swift ohne AppKit: Engine-Client, dotnet-CLI, Solution-Dateien, Debugger-Client (DAP), Lexer |
| `engine/` | C#-Analyseprozess mit Roslyn, StyleCop und Diagramm-Writer hinter JSON-RPC |
| `Tests/` | Swift-Tests (`swift test`); Engine-Tests mit `dotnet test engine/VsLike.Engine.slnx` |

Release-Schritte stehen in [`RELEASING.md`](RELEASING.md), Architekturentscheidungen in
[`docs/DECISIONS.md`](docs/DECISIONS.md), Änderungen in [`CHANGELOG.md`](CHANGELOG.md).

## Lizenz

[MIT](LICENSE)
