# Stand

- [x] Phase 0 Gerüst
- [x] Phase 1 Shell
- [x] Phase 2 Editor
- [x] Phase 3 Roslyn-Workspace
- [x] Phase 4 IntelliSense
- [x] Phase 5 Solution/Projekte/NuGet
- [x] Phase 6 Build/Run/Test
- [x] Phase 7 StyleCop
- [x] Phase 8 Klassendiagramm
- [x] Phase 9 Debugger
- [x] Phase 10 Packaging & Homebrew
- [x] Phase 11 Linux

Alle Phasen abgeschlossen. Nächster Schritt: `docs/RELEASE_CHECKLIST.md` → „Vor dem ersten Release“.

## Wiedereinstieg
- Prüfen: `tools/check.sh` (Build + alle Tests, Exit-Code ≠ 0 bei Fehlern). Commit nur in einem separaten Schritt danach.
- App starten: `artifacts.nosync/bin/VsLike.App/debug/VsLike [Pfad.slnx|Ordner|Datei]`; Fenster-Screenshot: `tools/shot.sh out.png`.
- UI-Texte: `src/VsLike.Core/Localization/strings.tsv` bearbeiten, dann `python3 tools/strings.py`.
- Test-Solution (App → Lib1 → Lib2): `tests/TestAssets/ThreeProjects`.
- Muster: Roslyn-freie Schnittstellen in `VsLike.Core/Code` (`ICodeWorkspace`, `ILanguageFeatures`), Implementierung in `VsLike.Roslyn`; Code-Änderungen als `WorkspaceEdit`, angewendet von `WorkspaceEditApplier`; Panels über `ShellPanels` + `ViewLocator`; Befehle über `CommandRegistry` + `MenuRegistry` (`WorkbenchCommands`, `LanguageCommands`, `SolutionCommands`, `BuildCommands`). Pro-Solution-Einstellungen: `SolutionSettings` (`.vslike/settings.json`, enthält schon `StyleCopEnabled`).
- Bekannte Stolpersteine: siehe `docs/DECISIONS.md` (Dock-ViewLocator, NativeMenu, RealPath, iCloud/`artifacts.nosync`, Headless-Tests: Workbench immer entsorgen).
