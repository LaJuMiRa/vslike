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
