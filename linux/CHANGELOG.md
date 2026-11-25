# Changelog (VsLike für Linux)

Linux-Releases sind eigenständig: Tag `linux-vX.Y.Z`, Version in `linux/VERSION`. Die macOS-Releases (Tags `vX.Y.Z`) haben ein eigenes `CHANGELOG.md` im Hauptordner.

## [1.1.1] - 2026-11-25

Erste Linux-Version (Fedora und Arch), Funktionsstand wie VsLike 1.1.1 für macOS.

- Native Avalonia-App mit demselben Aufbau und Aussehen wie auf macOS: Seitenleiste (Solution, Dateien, Suche, Tests), Tabs, Editor, Inspektor, unteres Panel (Probleme, Ausgabe, Terminal, Debug) und Befehlspalette. Die Menüs liegen hinter dem Menü-Knopf oben links.
- Editor mit IntelliSense, Live-Diagnosen, StyleCop und Quick-Actions über die unveränderte Analyse-Engine (Roslyn).
- Klassendiagramm in beide Richtungen, Layout im `.cd`-Format.
- Build, Test-Explorer, Debugger (netcoredbg), eingebautes Terminal, NuGet-Pakete, Projekteigenschaften und neue Solutions aus Vorlagen.
- Oberfläche auf Deutsch und Englisch nach Systemsprache. Tastenkürzel: ⌘ wird zu Strg, ⌥ zu Alt, ⌃⌘R zu Strg+Alt+R.
- Einstellungen unter `~/.config/vslike/settings.json`, Zwischenspeicher unter `~/.cache/vslike`.
- Pakete: Tarball (x64, arm64), RPM für Fedora (x86_64, aarch64) und `vslike-bin` für Arch (x86_64). Alle brauchen ein installiertes .NET SDK ab Version 10.
