# Linux-Port (Fedora, Arch) – Arbeitsnotizen

Ziel: `linux/` enthält eine Avalonia-App mit denselben Funktionen und demselben Aussehen wie die macOS-App,
nutzt die unveränderte Engine (`engine/`) über JSON-RPC, baut RPM (Fedora) und PKGBUILD (Arch).
macOS-Code (`Sources/`, `Tests/`, `Package.swift`), `engine/`, `build.sh`, `release.sh`, Cask, bestehende Workflows
bleiben unverändert. Alles Neue liegt in `linux/` (plus ein neuer Workflow `.github/workflows/linux-release.yml`).

## Entscheidungen
- 2026-11-24 Nutzer: Avalonia/C#, gleiches Repo, Ordner `linux/`.
- Avalonia 12.1.6 (12.2.0 erst seit 2026-11-23 draußen), AvaloniaEdit 12.0.0, CommunityToolkit.Mvvm 8.4.2,
  Terminal: Iciclecreek.Avalonia.Terminal 4.1.4 (MIT, XTerm.NET + Porta.Pty mit nativen Shims linux-x64/arm64).
- Tests: xunit.v3 3.2.2 (Avalonia.Headless.XUnit 12.1.6 hängt an xunit.v3.extensibility.core 3.2.2).
- Highlighting wie macOS: eigener Lexer (Port von CSharpLexer) + Roslyn-Klassifizierung der Engine, kein TextMate.

- Paketquellen (geprüft 2026-11-24 in Containern): Fedora 43 `dotnet-sdk-10.0` 10.0.113 (auch `dotnet-sdk-11.0` RC),
  Arch `dotnet-sdk` 10.0.114. Beide sind Feature-Band 10.0.1xx → die Engine muss mit dem MSBuild dieses SDKs laufen
  (Prüfpunkt in Docker). Bauen der Engine braucht wegen `engine/global.json` SDK ≥ 10.0.400 (CI/Build-Container).
- Linux-Releases eigenständig: Tag `linux-vX.Y.Z`, Version in `linux/VERSION` (Start 1.1.1 = Funktionsstand macOS 1.1.1),
  eigener Workflow; der macOS-Workflow reagiert nur auf `v*.*.*` und bleibt unberührt.
- Pakete: Tarball mit fertigem `usr/`-Baum (`linux/build-linux.sh`), daraus RPM (`packaging/vslike.spec`, binär,
  `AutoReqProv: no`) und Arch-Paket `vslike-bin` (`packaging/PKGBUILD.in`, für das AUR).

## Portierungsregeln (für alle Teilaufgaben verbindlich)
- Quelle ist der Swift-Code. Jede Swift-Datei bekommt ein C#-Gegenstück mit gleichem Namen und gleicher Ordnerstruktur:
  `Sources/VsLikeKit/X/Y.swift` → `linux/src/VsLike.Kit/X/Y.cs` (Namespace `VsLike.Kit`),
  `Sources/VsLike/X/Y.swift` → `linux/src/VsLike.Desktop/X/Y.cs` (Namespace `VsLike.Desktop`).
  Tests: `Tests/VsLikeKitTests` → `linux/tests/VsLike.Kit.Tests`, `Tests/VsLikeTests` → `linux/tests/VsLike.Desktop.Tests`.
- Typnamen bleiben, Member in PascalCase (`func openSolution(at:)` → `OpenSolution(string path)`), Enum-Fälle PascalCase.
  `async` → `Task`, `actor` → Klasse mit `SemaphoreSlim`/`lock`, `Codable` → System.Text.Json mit denselben
  JSON-Namen wie die Engine (camelCase), `@Observable`/`@MainActor` → `ObservableObject` (CommunityToolkit) auf dem UI-Thread
  (`Dispatcher.UIThread`). Records/`init`-Properties statt Mutation, wo das Swift-Original `struct`/`let` nutzt.
- Verhalten 1:1 übernehmen, auch Konstanten (Debounce-Zeiten, Farben, Abstände, Schriftgrößen). Nichts weglassen,
  nichts dazuerfinden. Wo Linux zwingend abweicht, im Code kurz kommentieren und hier unter „Abweichungen“ eintragen.
- Tastenkürzel: ⌘ → Ctrl, ⌥ → Alt, ⇧ bleibt Shift, ⌃⌘R → Ctrl+Alt+R (Kollisionen hier eintragen).
- Pfade: `~/Library/Caches/VsLike` → `$XDG_CACHE_HOME/vslike` (Standard `~/.cache/vslike`),
  Einstellungen (UserDefaults) → `$XDG_CONFIG_HOME/vslike/settings.json`.
- Installationslayout: `/usr/lib/vslike/` enthält `vslike.dll` (App), `engine/` (VsLikeEngine.dll), `debughook/`,
  `netcoredbg/netcoredbg`; Starter `/usr/bin/vslike` = `exec dotnet /usr/lib/vslike/vslike.dll "$@"`.
  Entwicklungs-Fallback für die Engine: `<repo>/engine/artifacts.nosync/bin/VsLike.Engine/debug/VsLikeEngine.dll`,
  netcoredbg: `$XDG_CACHE_HOME/vslike/netcoredbg/3.2.0-1092/netcoredbg/netcoredbg`.
- Texte: `Sources/VsLike/Resources/{en,de}.lproj/Localizable.strings` → `linux/src/VsLike.Desktop/Resources/Strings.resx`
  und `Strings.de.resx` mit denselben Schlüsseln; Sprache folgt der Systemsprache wie auf macOS.
- Code-Stil: Nullable, TreatWarningsAsErrors, StyleCop (Regeln in `linux/.editorconfig`), Dateien < 800 Zeilen,
  Funktionen möglichst < 50 Zeilen. Keine Warnungen. Build: `dotnet build linux/VsLike.Linux.slnx`,
  Tests: `dotnet test linux/VsLike.Linux.slnx` (Engine vorher bauen: `dotnet build engine/VsLike.Engine.slnx`).
- Nichts außerhalb von `linux/` und dieser Datei ändern.

## Referenz-Screenshots macOS
`/private/tmp/claude-501/-Users-laurenzrauscher-Projects-vslike/237701e8-c973-479b-8d97-0a4aa1208385/scratchpad/ref/editor.png`

## Abweichungen Linux ↔ macOS
Kernschicht (2026-11-24):
- Pfade: Engine/netcoredbg/Debughook über `AppPaths` (neue Datei `Kit/Process/AppPaths.cs`, ersetzt Bundle und `#filePath`):
  Installationsordner = `AppContext.BaseDirectory`, Repo-Wurzel = erster Ordner darüber mit `engine/VsLike.Engine.slnx`.
- `DotnetLocator`: /usr/bin, /usr/lib64/dotnet, /usr/lib/dotnet, /usr/share/dotnet, /usr/local/bin, ~/.dotnet (statt Homebrew-Pfaden).
- `JSONValue`: kein eigener JSON-Enum; `JSONValue.Options` (= RpcJson der Engine) + `JsonNode`/anonyme Objekte als Parameter.
- Notifications/Events: `Channel<T>` (`EngineClient.Notifications`, `DebugSession.Events`) statt `AsyncStream`.
- CoreGraphics-Typen: eigene `CGPoint/CGSize/CGRect` (`Kit/Diagram/Geometry.cs`), NSRange → `TextRange` (in `TextEditing.cs`).
- `ProjectFile.SetProperty` gibt eine neue Instanz zurück (Immutabilität) statt zu mutieren.
- Prozess-Abbruch: `Kill(entireProcessTree)` (SIGKILL) statt SIGTERM.
- `CodeDocument.Storage` ist ein AvaloniaEdit `TextDocument`; Undo über `UndoStack` (keine Aktionsnamen, `actionName` wird ignoriert);
  `TextChanged` löst `TextDidChange` aus. Ein `Apply` mit mehreren Edits zählt die Version nur einmal hoch.
- `Preferences`: JSON-Datei statt UserDefaults (gleiche Schlüssel); Properties `AppearanceMode`/`UiLanguage` (Swift: `appearance`/`language`,
  Namenskonflikt mit den Enums); Firmenname aus GECOS (/etc/passwd) statt `NSFullUserName`; `EditorFontFamily` als Font-Stack
  (SF Mono gibt es nicht); keine Systemliste „Zuletzt benutzt“ (NSDocumentController), nur die eigene Liste.
- Dialoge (NSAlert) über `IWorkspaceDialogs` (async); ohne UI und ohne `ConfirmSaveHandler` antwortet `ConfirmSave` mit Cancel.
  Dadurch sind `Close(tab)`, `CloseOtherTabs`, `MoveToTrash` async.
- `MoveToTrash`: `gio trash`, sonst Freedesktop-Trash von Hand; `RevealInFinder`: `xdg-open` auf den Ordner.
- Terminal: `TerminalSession` startet den Prozess selbst über Porta.Pty (`PtyProvider.SpawnAsync`) und hält `IPtyConnection`;
  die Ansicht hängt sich mit `TerminalControl.AttachConnection` an. Shell: `$SHELL`, sonst `/bin/bash` (macOS: zsh).
  `TerminalController.NewShell/Run` sind async (Prozess-ID erst nach dem Start). `ShowTerminal` ist async.
- `DiagnosticsStore.filter` → `CurrentFilter` (Namenskonflikt mit Enum `Filter`); `Workspace.rootURL/solutionURL` → `RootPath/SolutionPath` (Strings).
- Lokalisierung: Localizable.strings hat Schlüssel, die sich nur in Groß/Kleinschreibung unterscheiden („Base class“/„Base Class“,
  „Run Without/without Debugging“); resx erlaubt das nicht → je einer, `ResourceManager.IgnoreCase = true`.
- Test-Abweichung: stylecop.json-Prüfung erwartet `"companyName": "Acme"` (System.Text.Json) statt `"companyName" : "Acme"`.
- Tests ohne Linux-netcoredbg nutzen auf dem Mac `~/Library/Caches/VsLike/netcoredbg/…` (nur Test-Helfer, setzt `VSLIKE_NETCOREDBG`).
- `linux/.editorconfig`: zusätzlich `xUnit1051` in `tests/` aus (Swift-Tests übergeben keine CancellationTokens).

## Stand
- [x] Versionen geprüft, Gerüst `linux/` (slnx, props, 4 Projekte), Restore grün
- [x] Kernschicht (2026-11-24): Kit (alle 26 Dateien aus VsLikeKit + AppPaths, Geometry), Workspace-Modell (alle Dateien aus
      Sources/VsLike/Workspace, Workspace.swift aufgeteilt in Workspace.cs, WorkspaceTypes.cs, Workspace+Engine.cs, Workspace+Documents.cs),
      App/Preferences.cs, App/L.cs, Resources/Strings{,.de}.resx (386 Schlüssel), Platzhalter App/Program.cs + App.axaml(.cs)
      (leeres Fenster, wird von der Shell ersetzt). Build 0 Warnungen; Tests Kit 46/46, Desktop 9/9 grün (macOS, 2026-11-24).
      Nicht portiert (andere Teilaufgaben): Diagramm- und Editor-Tests aus WorkspaceTests.swift, `AppDelegate.open/enclosingSolution`.
- [x] Editor, Diagramm, Shell vollständig portiert (2026-11-25): Views/ (Sidebar, FileTreeList, SearchSidebar, TestExplorer, BottomPanel mit Problems/Output,
      TerminalPanel, DebugPanel, LogView, WelcomeView), Sheets/ (Prompt, References, NewSolution, NewProject, NuGet, ProjectProperties, Settings),
      App.axaml.cs (Workspace + MainWindow, Kommandozeilen-Dateien, Schließen mit Rückfrage). AvaloniaEdit-Stylesheet in App.axaml ergänzt (fehlte).
      Build 0 Fehler/0 Warnungen; Tests Kit 46/46, Desktop 9/9; App startet auf dem Mac, lädt Demo.slnx (Screenshot geprüft: Sidebar, Toolbar, Panel).
- [ ] Integration, Build, Tests
- [x] Engine-Smoke auf Fedora 43 (SDK 10.0.113, arm64-Container): initialize, workspace/load, classify OK
- [x] Packaging gelaufen (2026-11-24, mit Platzhalter-UI): Tarball x64/arm64, RPM aarch64/x86_64 (Fedora 43),
      vslike-bin x86_64 (Arch). Installation in frischen Containern zieht dotnet-sdk automatisch, Start unter Xvfb ohne Fehler.
      Dateien: `linux/build-linux.sh`, `linux/release-linux.sh`,
      `linux/packaging/{vslike.spec,PKGBUILD.in,build-rpm.sh,build-arch.sh,vslike.desktop,vslike-mime.xml,vslike.sh,icons/}`,
      `.github/workflows/linux-release.yml` (Release mit `--latest=false`, damit macOS „latest“ bleibt).
      Offen: `linux/CHANGELOG.md`, `linux/README.md`, Testlauf. Testimage `vslike-fedora-test` (Xvfb, xdotool, xwd).
- [ ] Prüfen in Docker (Fedora, Arch), Screenshots unter Xvfb

## Abweichungen Shell (2026-11-25)
- Terminal-Tabtitel bleibt der Shell-Name: `TerminalControl` hat kein Titel-Ereignis (nur `TerminalWindow`).
- BottomPanel: statt `ViewThatFits` feste Breiten-Stufen (≥900 / ≥620 / darunter) für Filter und Icon-only-Tabs; Problems-Tabelle ohne Zebra-Zeilen.
- Welcome-Untertitel „C# for Linux“ (gleicher Schlüssel „C# for the Mac“, Wert geändert).

## API der Kernschicht (für Editor, Diagramm, Shell)
Alles in Namespace `VsLike.Desktop` bzw. `VsLike.Kit`; Listen sind unveränderlich und werden bei Änderung ersetzt
(PropertyChanged feuert dann). Alles läuft auf dem UI-Thread.
- `Workspace` (Workspace*.swift): `Preferences`, `Diagnostics`, `Output`, `Build`, `Tests`, `Debugger`, `Terminals`, `Search`;
  Zustand `RootPath`, `SolutionPath`, `Solution`, `Tree`, `FolderTree`, `Projects`, `EngineStatus`/`IsEngineReady`, `Settings`, `Tabs`,
  `ActiveTabId`, `ActiveTab`, `ActiveDocument`, `OpenDocuments`, `SidebarTab`, `BottomTab`, `IsBottomPanelVisible`, `IsInspectorVisible`,
  `Status`, `StyleCopEnabled`, `IsStyleCopLoading`, `SelectedNodeId`, Sheets: `IsCommandPalettePresented`, `IsQuickOpenPresented`,
  `IsNewSolutionPresented`, `NewProjectTarget`, `NugetProject`, `PropertiesProject`, `Prompt`, `Alert`, `References`; `Engine`, `Title`, `IsOpen`.
  Aktionen: `Open(path)`, `CloseAll()`, `OpenFile(path, line, column, selectLength)`, `OpenDiagram(path)`, `Close(tab)`, `CloseOtherTabs(tab)`,
  `Save(doc)`, `SaveAll()`, `SaveActive()`, `RefreshTree()`, `NotifyCodeChanged()`, `SyncNow(doc)`, `RestartEngine()`, `ToggleStyleCop()`,
  `OpenStyleCopSettings()`, `PersistBreakpoints()`, `AllBreakpoints`, `ProjectContaining(path)`, `Show(tab)`, `ShowTerminal()`,
  `OpenSolutionDiagram()`, `OpenDiagramForFolder(folder)`, `PromptNewFile/PromptNewFolder/PromptRename`, `CreateFile`, `MoveToTrash`,
  `RevealInFinder`, `RemoveProjectReference`, `RemoveProjectFromSolution`, `Apply(edit, actionName)`, `Navigate(location|diagnostic)`,
  `GoToDefinition/FindReferences/Rename(doc, offset)`, `FormatDocument(doc)`, `CodeActions(doc, range)`, `Perform(action)`, `FixAllStyleCop(doc)`.
  Hooks: `DocumentListeners`, `ConfirmSaveHandler` (Tests), `Dialogs` (`IWorkspaceDialogs`, setzt das Hauptfenster), `ActiveEditor` (`ICodeEditor`, setzt der Editor),
  statisch `Workspace.DiagramFactory` (`IDiagramEditorFactory`, registriert der Diagramm-Teil beim Start), `Workspace.EngineDll()`.
- `EditorTab.Code(Document)` / `EditorTab.Diagram(Editor: IDiagramEditor)` mit `Id`, `Title`, `Symbol` (SF-Symbol-Name), `IsDirty`.
  `IDiagramEditor` (Id, Name, Path, ScheduleRefresh(immediately), Undo(), Redo()) implementiert der Port von `DiagramEditorModel`.
- `CodeDocument` (CodeDocument.swift): `Id`, `Path`, `Name`, `Directory`, `Storage` (AvaloniaEdit `TextDocument` → `TextEditor.Document`),
  `UndoStack`, `Text`, `Version`, `IsDirty`, `IsCSharp`, `LanguageName`, `Breakpoints` (Zeile → SourceBreakpoint), `ExecutionLine`,
  `PendingSelection` (`TextRange?`), `Replace(range, text)`, `Apply(edits, name)`, `Offset(line, column)`, `Position(offset)`, `ToggleBreakpoint(line)`, `Save()`.
- `DiagnosticsStore`: `Live`, `Build`, `CurrentFilter`, `Search`, `All`, `Visible`, `ErrorCount`, `WarningCount`, `StyleCopCount`, `DiagnosticsFor(path)`.
- `OutputLog`: `Lines`, `SelectedChannel`, `LinesIn(channel)`, `Title(channel)`, `Append`, `Clear`, `Sink`.
- `BuildController`: `Phase`, `IsBusy`, `LastResult`, `Configuration`, `StartupProject`, `StartupProjectName`, `LaunchProfile`, `Profiles`,
  `RunnableProjects`, `Configurations`, `Run(kind)`, `Start(kind)`, `StartWithoutDebugging()`, `Cancel()`.
- `TestController`: `TestProjects`, `Tests`, `Results`, `Running`, `IsBusy`, `SelectedId`, `Tree` (`TestNode`), `PassedCount`, `FailedCount`,
  `TotalCount`, `Discover()`, `Run(nodes, onlyFailed)`, `Cancel()`, `Open(node)`, `ResultFor(node)`.
- `DebugController`: `IsActive`, `IsStarting`, `IsStopped`, `StopReason`, `Threads`, `Frames`, `SelectedFrameId`, `SelectedFrame`, `Variables`
  (`VariableNode`), `Watches` (`WatchExpression`), `Console`; `Start()`, `Resume()`, `StepOver/StepIn/StepOut()`, `Pause()`, `Stop()`,
  `ChildrenOf(node)`, `AddWatch`, `RefreshWatches`, `Evaluate(expr)`, `HoverValue(expr)`, `BreakpointsChanged(doc)`.
- `TerminalController`/`TerminalSession`: `Sessions`, `ActiveId`, `Active`, `HasRunningProgram`, `TerminalFontSize`, `NewShell()`, `Run(...)`,
  `Close(s)`, `StopRuns()`; Session: `Id`, `Title` (setzbar, für OSC-Titel), `IsRun`, `IsRunning`, `ExitCode`, `ProcessId`, `Connection`
  (an `TerminalControl.AttachConnection` geben), Farben `BackgroundDark/Light`, `ForegroundDark/Light`.
- `SearchModel`: `Query`, `Replacement`, `CaseSensitive`, `WholeWord`, `UseRegex`, `Results` (`FileResult`/`Match`), `MatchCount`,
  `IsSearching`, `ErrorText`, `Run()`, `ReplaceAll()`.
- `FileNode`/`FileTreeBuilder`/`FolderWatcher` (FileTree.swift): `FileNode.Kind.Tag`, `Symbol`, `FileTreeBuilder.AllFiles`, `StandardOrder`.
- `Preferences` (Preferences.swift): `Shared`, alle Einstellungen als Properties, `EffectiveLanguage`, `ApplyAppearance()`, `NoteRecent`, `EditorFontFamily`.
- `L.T(key, args)`: Schlüssel wie in Localizable.strings (`"%lld projects loaded"`), `L.Culture` beim Start setzen.

## Nächster Schritt
Ungetestet: Editor-Interaktion (Tippen, Completion), Diagramm, Terminal, Debugger, NuGet/Sheets in der laufenden App; Tests aus WorkspaceTests.swift (Diagramm/Editor) und
Tests für die neuen Views portieren; Docker-Läufe (Fedora/Arch, Xvfb-Screenshots); `linux/CHANGELOG.md`, `linux/README.md`; Commit (noch nichts committet).
