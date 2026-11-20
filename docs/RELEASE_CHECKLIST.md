# Release-Checkliste

Stand 2026-11-20, Version 0.1.0 (noch nicht veröffentlicht).

## End-to-End-Test

`tests/VsLike.App.Tests/EndToEndTests.cs` läuft gegen das echte .NET SDK 10.0.402 und netcoredbg 3.2.0 (macOS arm64, headless):

| Schritt | Ergebnis |
|---|---|
| Neue Solution `Hello.slnx` mit Konsolenprojekt anlegen und öffnen | ✅ Startprojekt automatisch erkannt |
| Klasse `Greeter` im Klassendiagramm anlegen, Property `+ Name: string = "World"` ergänzen | ✅ `Greeter.cs` mit file-scoped Namespace `Hello.App`, Property im Code |
| IntelliSense: Completion nach `greeter.` | ✅ enthält `Name` |
| StyleCop an / aus | ✅ StyleCop-Warnungen erscheinen bzw. verschwinden live |
| Bauen | ✅ Build erfolgreich |
| Debuggen: Haltepunkt in `Program.cs`, Variable `greeter`, Fortsetzen | ✅ Halt in Zeile 4, Variable sichtbar, Ausgabe `Hello World` |

Gesamtlauf `tools/check.sh`: Build ohne Warnungen, 219 Tests in 7 Testprojekten grün (Core 38, Diagram 36, Debugging 3, Editor 32, Roslyn 22, Projects 45, App 43).

## Abnahmen je Phase

| Phase | Abnahme | Geprüft durch |
|---|---|---|
| 2 Editor | 50.004-Zeilen-Datei lädt/rendert in 225 ms | Editor-Test |
| 3 Roslyn | Solution mit 3 Projekten lädt in 1,0–1,4 s, Fehler nach 654 ms in der Fehlerliste | App-/Roslyn-Tests |
| 4 IntelliSense | Completion warm im Median 8 ms (< 150 ms) | Roslyn-Test |
| 5 Projekte | neue Solution mit 2 Projekten + Referenz baut außerhalb der IDE | Projects-Integrationstest |
| 6 Build/Run/Test | Console-App baut/läuft, Fehlerklick springt zur Zeile; Test-Explorer | App-Test |
| 7 StyleCop | SAxxxx mit StyleCop, ohne keine; Zustand pro Solution | App- und Roslyn-Tests |
| 8 Klassendiagramm | Klasse ⇒ Datei, Vererbung ⇒ `: Basis`, Methode in richtiger Datei, externe Änderung ⇒ Diagramm, keine Formatverluste, Undo | App-Test, 21 Writer-Snapshots |
| 9 Debugger | bedingter Haltepunkt, Variablen, Step In/Out, Auswertung, Ausgabe | netcoredbg-Integrationstest, App-Test |
| 10 Packaging | `VsLike.app` (arm64) gebaut, Zip-Signatur gültig, App startet und lädt Solution | manuell, Fenster-Screenshot |
| 11 Linux | Tarball linux-arm64 gebaut (Struktur/Rechte); Build+Tests auf ubuntu in CI | lokal + CI-Konfiguration |

## Vor dem ersten Release (offen, braucht dich)

1. Repo aus iCloud Drive verschieben (z. B. `~/Developer/vslike`) und `main` nach `origin` pushen – bisher nichts gepusht.
2. CI auf GitHub einmal grün sehen (macos-15, macos-15-intel, ubuntu-24.04); lokal lief nur macOS arm64.
3. Secret `TAP_GITHUB_TOKEN` im vslike-Repo anlegen (Fine-grained Token mit Schreibrecht auf `LaJuMiRa/homebrew-tap`, nur Contents + Pull requests).
4. Optional Apple-Developer-ID-Secrets für Signierung/Notarisierung: `MACOS_CERT_P12`, `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY`, `MACOS_NOTARY_APPLE_ID`, `MACOS_NOTARY_TEAM_ID`, `MACOS_NOTARY_PASSWORD`. Ohne sie: ad-hoc signiert, Gatekeeper-Hinweis im Cask.
5. Tag setzen: `git tag v0.1.0 && git push origin v0.1.0` → Release mit `.zip`, `.dmg`, Tarballs; danach den PR im Tap prüfen und mergen.
6. Nach dem Merge prüfen: `brew install --cask LaJuMiRa/tap/vslike` (macOS) bzw. `brew install LaJuMiRa/tap/vslike` (Linux), App starten, Solution öffnen, bauen.

## Bekannte Lücken

Siehe `docs/OPEN_QUESTIONS.md` – u. a. keine Standardeingabe für gestartete Programme, netcoredbg wertet keine Member-Ketten
auf Properties aus, Klassendiagramm ohne Kantenführung und ohne nachträgliche Generic-Constraints, Linux nicht auf
echtem Desktop getestet, x64-Builds nur in CI.
