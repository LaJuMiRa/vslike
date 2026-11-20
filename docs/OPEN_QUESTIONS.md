# Offene Fragen

- **Repo liegt in iCloud Drive** (`~/Documents` wird synchronisiert). iCloud lagert Dateien aus („dataless“); am 2026-11-20 war dadurch die Git-Pack-Datei mit dem ersten Commit unlesbar („far too short to be a packfile“). Behoben durch Zurückladen (`brctl download .git`), aber das kann jederzeit wieder passieren, auch mit `obj/`/`bin/`. Empfehlung: Repo außerhalb von iCloud ablegen (z. B. `~/Developer/vslike`) oder für `Documents` „Mac-Speicher optimieren“ abschalten, und regelmäßig nach `origin` pushen.
- StyleCop 1.3.180 meldet mehrzeilige Collection-Expressions (`[.. x
 .Where()]`) als SA1500; mehrzeilig daher `.ToList()` verwenden.
- Gestartete Programme bekommen keine Standardeingabe (`Console.ReadLine` liefert sofort `null`). Ein Eingabefeld im Ausgabefenster wäre der nächste Schritt; der Debugger (Phase 9) bringt eine Debug-Konsole.
- Test-Explorer unterstützt VSTest-basierte Projekte; reine Microsoft.Testing.Platform-Projekte (z. B. TUnit ohne VSTest-Brücke) werden erkannt, aber evtl. nicht gefunden.
- Klassendiagramm: Typparameter und Constraints lassen sich beim Anlegen setzen, an bestehenden Typen aber nur im Code ändern. Beziehungen werden als gerade Linien gezeichnet (keine Kantenführung um Boxen herum).
- PNG-Export ist nur in der echten App geprüft (Headless-Tests haben keinen Skia-Renderer).
- netcoredbg 3.2 wertet Member-Ketten auf Property-Ergebnissen nicht aus (`Name.Length` → Fehler, `Name` geht). Betrifft Überwachen/Hover/Konsole; Variablenbaum zeigt die Werte trotzdem.
- Debugger: die Debug-Konsole zeigt Programmausgabe, nimmt aber keine Standardeingabe entgegen. Haltepunkte werden nicht über Neustarts hinweg gespeichert.
- Intel-Macs bekommen netcoredbg 3.1.3 (älter als 3.2.0); nicht auf echter x64-Hardware getestet.
- Homebrew: `Casks/vslike.rb` liegt noch nicht im Tap. Er entsteht beim ersten Release-Tag per Pull Request (Secret `TAP_GITHUB_TOKEN` im vslike-Repo nötig). `brew install --cask LaJuMiRa/tap/vslike` ist deshalb noch nicht Ende-zu-Ende geprüft; lokal geprüft sind Bundle, Signatur und Start.
- Das osx-x64-Bundle wird nur in CI (macos-15-intel) gebaut, nicht lokal getestet.
- Notarisierung braucht eine Apple-Developer-ID (Secrets `MACOS_CERT_P12`, `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY`, `MACOS_NOTARY_*`); ohne sie bleibt der Gatekeeper-Hinweis nötig.

