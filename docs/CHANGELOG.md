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
