# Stand

- [x] Phase 0 Gerüst
- [x] Phase 1 Shell
- [x] Phase 2 Editor
- [x] Phase 3 Roslyn-Workspace
- [x] Phase 4 IntelliSense
- [ ] Phase 5 Solution/Projekte/NuGet
- [ ] Phase 6 Build/Run/Test
- [ ] Phase 7 StyleCop
- [ ] Phase 8 Klassendiagramm
- [ ] Phase 9 Debugger
- [ ] Phase 10 Packaging & Homebrew
- [ ] Phase 11 Linux

Nächster Schritt: Phase 5 (Solution/Projekte/NuGet).

## Wiedereinstieg
- Prüfen: `tools/check.sh` (Build + alle Tests, Exit-Code ≠ 0 bei Fehlern). Commit nur in einem separaten Schritt danach.
- App starten: `artifacts.nosync/bin/VsLike.App/debug/VsLike [Pfad.slnx|Ordner|Datei]`; Fenster-Screenshot: `tools/shot.sh out.png`.
- UI-Texte: `src/VsLike.Core/Localization/strings.tsv` bearbeiten, dann `python3 tools/strings.py`.
- Test-Solution (App → Lib1 → Lib2): `tests/TestAssets/ThreeProjects`.
- Muster: Roslyn-freie Schnittstellen in `VsLike.Core/Code` (`ICodeWorkspace`, `ILanguageFeatures`), Implementierung in `VsLike.Roslyn`; Code-Änderungen als `WorkspaceEdit`, angewendet von `WorkspaceEditApplier`; Panels über `ShellPanels` + `ViewLocator`; Befehle über `CommandRegistry` + `MenuRegistry` (`WorkbenchCommands`, `LanguageCommands`).
- Bekannte Stolpersteine: siehe `docs/DECISIONS.md` (Dock-ViewLocator, NativeMenu, RealPath, iCloud/`artifacts.nosync`).
