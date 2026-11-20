# VsLike

Eine eigenständige C#-IDE für macOS (Apple Silicon und Intel) und Linux, gebaut mit .NET 10, Avalonia und Roslyn. Lizenz: MIT.

## Voraussetzungen

- .NET SDK 10.0.400 oder neuer (`dotnet --list-sdks`). VsLike nutzt das installierte SDK für MSBuild, Build, Tests und Templates.

## Installation (macOS)

```sh
brew install --cask LaJuMiRa/tap/vslike
```

VsLike ist nicht von Apple notarisiert. Beim ersten Start blockiert macOS (Gatekeeper) die App. Entweder unter
*Systemeinstellungen › Datenschutz & Sicherheit › „Dennoch öffnen“* erlauben oder einmalig die Quarantäne entfernen:

```sh
xattr -dr com.apple.quarantine /Applications/VsLike.app
```

Alternativ das `.zip` oder `.dmg` von der Release-Seite laden und `VsLike.app` nach `/Programme` ziehen. Der Debugger
(netcoredbg, MIT) ist im App-Paket enthalten. Dateizuordnungen: `.sln`, `.slnx`, `.csproj`, `.cs`.

## Funktionen

Editor mit TextMate- und Roslyn-Highlighting, IntelliSense (Completion, Signaturhilfe, Hover, Code-Fixes, Refactorings,
Rename, Go to Definition, Find References, Snippets), Solution Explorer, NuGet-Manager, Build/Run mit Startprofilen,
Test-Explorer, StyleCop (pro Solution schaltbar), bidirektionaler Klassendiagramm-Editor und Debugger. Bedienung über die
Befehlspalette (⇧⌘P).

## Aus dem Quellcode bauen

```sh
dotnet build VsLike.slnx
dotnet test VsLike.slnx
dotnet run --project src/VsLike.App
```

App-Paket bauen (`.app`, `.zip`, `.dmg`): `packaging/macos/build-app.sh osx-arm64 0.1.0`. Releases entstehen per Tag `v*`
(`.github/workflows/release.yml`), der Cask im Tap wird dabei per Pull Request aktualisiert.

## Projektstruktur

| Ordner | Inhalt |
|---|---|
| `src/VsLike.App` | Avalonia-Host, Views, ViewModels, Shell |
| `src/VsLike.Core` | Interfaces, Services, Settings (ohne Avalonia und Roslyn) |
| `src/VsLike.Editor` | Editor-Integration (AvaloniaEdit, TextMate) |
| `src/VsLike.Roslyn` | Workspace, IntelliSense, Diagnostics, Analyzer |
| `src/VsLike.Projects` | Solution-/Projektmodell, Templates, NuGet, Build/Run |
| `src/VsLike.Diagram` | Klassendiagramm-Modell, Canvas, Codegenerierung |
| `src/VsLike.Debugging` | DAP-Client für netcoredbg |

Entscheidungen stehen in `docs/DECISIONS.md`, der Fortschritt in `docs/STATUS.md`.
