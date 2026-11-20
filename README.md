# VsLike

Eine native C#-IDE für macOS (Apple Silicon, macOS 26 oder neuer). Die Oberfläche ist in Swift geschrieben
(SwiftUI und AppKit). Die C#-Analyse übernimmt Roslyn in einem unsichtbaren Hintergrundprozess. Lizenz: MIT.

## Installation

```sh
brew install --cask LaJuMiRa/tap/vslike
```

Nötig ist ein installiertes .NET SDK 10 oder neuer (`brew install --cask dotnet-sdk`). Der Startbildschirm zeigt an, ob
ein passendes SDK gefunden wurde.

VsLike ist ad-hoc signiert, aber nicht von Apple notarisiert. Deshalb blockiert macOS den ersten Start. Erlaube ihn
einmal unter *Systemeinstellungen › Datenschutz & Sicherheit › „Dennoch öffnen“* oder entferne die Quarantäne:

```sh
xattr -dr com.apple.quarantine /Applications/VsLike.app
```

## Funktionen

| Bereich | Was es gibt |
|---|---|
| Editor | Highlighting sofort beim Tippen, danach semantische Farben von Roslyn, Zeilennummern, Klammerpaare, automatische Einrückung, Snippets (`prop`, `ctor`, `for` … mit Tab), Kommentar umschalten (⌘/), Suchen und Ersetzen (⌘F) |
| IntelliSense | Vervollständigung mit Dokumentation, Parameterhilfe, Hover-Infos, Fehler und Warnungen live beim Tippen, Gehe zu Definition (F12 oder ⌘-Klick), Verweise suchen (⇧F12), Umbenennen (F2), Formatieren (⌥⇧F) |
| Quick-Actions (⇧⌘.) | Menü an der Schreibmarke mit Korrekturen (fehlende `using`-Direktive, Methode, Klasse oder Property erzeugen, StyleCop-Korrekturen), „Alle beheben“ im Dokument oder Projekt und Refactorings |
| StyleCop | Pro Solution schaltbar (Knopf in der Toolbar, gespeichert in `.vslike/settings.json`), dazu ein globaler Standard. „Alle StyleCop-Meldungen der Datei beheben“ mit ⌥⇧⌘. |
| Probleme | Tabelle mit Schweregrad, Code, Meldung, Datei und Projekt, Filter nach Fehlern, Warnungen oder StyleCop, Doppelklick springt zur Stelle |
| Terminal | Eingebaute Terminals mit Login-Shell (⌃\`). „Ohne Debugger starten“ läuft ebenfalls im Terminal, deshalb kann das Programm Eingaben lesen |
| Build und Tests | Bauen (⌘B), Neu bauen, Bereinigen, Startprojekt, Konfiguration und Startprofil in der Toolbar, Test-Explorer mit Baum, Ergebnissen und Sprung zur Fehlerstelle (⌘U) |
| Debugger | netcoredbg (im App-Paket). Haltepunkte (Klick auf die Zeilennummer, Rechtsklick für eine Bedingung), Schritte (F10, F11, ⇧F11), Variablen, Überwachen, Aufrufliste, Threads, Debug-Konsole, Werte beim Hovern |
| Klassendiagramm | Grafische Fläche (⇧⌘D) mit Zoom, Verschieben, Raster und automatischer Anordnung. Typen anlegen, Mitglieder hinzufügen und bearbeiten, Vererbung per Ziehen, Umbenennen und Löschen über Roslyn. Jede Änderung wird direkt in den Code geschrieben und lässt sich zusammen mit dem Layout widerrufen. Export als SVG und PNG |
| Projekte | Neue Solution oder neues Projekt aus Vorlagen, NuGet-Pakete (Durchsuchen, Installiert, Updates), Projekteigenschaften und Projektverweise |
| Navigation | Schnell öffnen (⌘P), Befehlspalette (⇧⌘P), Suche in allen Dateien (⇧⌘F) |

Die Oberfläche gibt es auf Deutsch und Englisch; sie richtet sich nach der Systemsprache oder nach den Einstellungen.

## Aus dem Quellcode bauen

Nötig sind Xcode 26 (oder die Command Line Tools mit Swift 6.2) und das .NET SDK 10.

```sh
./build.sh      # baut build/VsLike.app
./release.sh    # testet alles, baut und packt dist/VsLike-<version>-arm64.zip
```

`release.sh` führt nacheinander aus:

1. die Engine-Tests (`dotnet test`)
2. die Swift-Tests (`swift test`), auch gegen echte Beispiel-Solutions, den Analyse-Prozess und den Debugger
3. den App-Build
4. einen Kurztest des fertigen App-Pakets
5. das Packen des Zips samt SHA-256

Ein Release entsteht über einen Tag `v<version>`; die Schritte stehen in `RELEASING.md`.

## Aufbau

| Ordner | Inhalt |
|---|---|
| `Sources/VsLike` | Die macOS-App: Fenster, Editor (AppKit), Seitenleiste, Panels, Terminal, Klassendiagramm, Dialoge |
| `Sources/VsLikeKit` | Swift ohne AppKit: Client für den Analyse-Prozess, dotnet-CLI (Build, Tests, Vorlagen, NuGet), Solution- und Projektdateien, Debugger-Client, C#-Lexer, Diagramm-Layout und SVG-Export |
| `engine/` | Der C#-Analyse-Prozess `VsLikeEngine`: Roslyn, StyleCop und der Klassendiagramm-Writer hinter JSON-RPC auf stdin/stdout |
| `Tests/` | Swift-Tests (`swift test`) |
| `packaging/` | `Info.plist` und Homebrew-Cask |

Entscheidungen stehen in `docs/DECISIONS.md`, der Stand in `docs/STATUS.md`.
