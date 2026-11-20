# Offene Punkte

- **Repo liegt in iCloud Drive.** iCloud lagert Dateien aus; am 2026-11-20 war dadurch schon einmal eine Git-Pack-Datei unlesbar. Build-Ausgaben liegen deshalb außerhalb (`$TMPDIR`, `artifacts.nosync`). Empfehlung: Repo nach `~/Developer/vslike` verschieben.
- **Bedienung per Maus und Tastatur nicht automatisiert geprüft.** Ohne Bedienungshilfen-Rechte kann ich in der echten App nicht tippen oder klicken. Geprüft sind die Abläufe über Tests am App-Modell (`WorkspaceTests`) gegen den echten Analyse-Prozess und per Fenster-Screenshots (Editor mit Roslyn-Farben, Toolbar, Klassendiagramm).
- Mehrfach-Cursor und Code-Faltung der Avalonia-Version sind im neuen Editor noch nicht umgesetzt.
- Klassendiagramm: Kanten sind gerade Linien (keine Führung um Boxen); Typparameter bestehender Typen nur im Code änderbar.
- netcoredbg 3.2 wertet Member-Ketten auf Property-Ergebnissen teils nicht aus (`Name.Length`); `car.Name` geht.
- Test-Explorer nutzt VSTest; reine Microsoft.Testing.Platform-Projekte (TUnit ohne VSTest-Brücke) werden evtl. nicht gefunden.
- Homebrew: `Casks/vslike.rb` entsteht beim ersten Release-Tag per Pull Request (Secret `TAP_GITHUB_TOKEN`). `brew install` ist erst danach Ende-zu-Ende prüfbar.
- Keine Notarisierung (kein Apple-Developer-Konto); Gatekeeper-Hinweis im README und im Cask.
- Der Release-Workflow (macos-26-Runner) ist noch nie gelaufen, weil nichts gepusht ist.
