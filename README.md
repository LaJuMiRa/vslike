# VsLike

Eine eigenständige C#-IDE für macOS (Apple Silicon und Intel) und Linux, gebaut mit .NET 10, Avalonia und Roslyn. Lizenz: MIT.

## Voraussetzungen

- .NET SDK 10.0.400 oder neuer (`dotnet --list-sdks`). VsLike nutzt das installierte SDK für MSBuild, Build, Tests und Templates.

## Aus dem Quellcode bauen

```sh
dotnet build VsLike.slnx
dotnet test VsLike.slnx
dotnet run --project src/VsLike.App
```

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
