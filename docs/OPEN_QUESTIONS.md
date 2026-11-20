# Offene Fragen

- **Repo liegt in iCloud Drive** (`~/Documents` wird synchronisiert). iCloud lagert Dateien aus („dataless“); am 2026-11-20 war dadurch die Git-Pack-Datei mit dem ersten Commit unlesbar („far too short to be a packfile“). Behoben durch Zurückladen (`brctl download .git`), aber das kann jederzeit wieder passieren, auch mit `obj/`/`bin/`. Empfehlung: Repo außerhalb von iCloud ablegen (z. B. `~/Developer/vslike`) oder für `Documents` „Mac-Speicher optimieren“ abschalten, und regelmäßig nach `origin` pushen.
- StyleCop 1.3.180 meldet mehrzeilige Collection-Expressions (`[.. x
 .Where()]`) als SA1500; mehrzeilig daher `.ToList()` verwenden.
- Gestartete Programme bekommen keine Standardeingabe (`Console.ReadLine` liefert sofort `null`). Ein Eingabefeld im Ausgabefenster wäre der nächste Schritt; der Debugger (Phase 9) bringt eine Debug-Konsole.
- Test-Explorer unterstützt VSTest-basierte Projekte; reine Microsoft.Testing.Platform-Projekte (z. B. TUnit ohne VSTest-Brücke) werden erkannt, aber evtl. nicht gefunden.
- Klassendiagramm: Typparameter und Constraints lassen sich beim Anlegen setzen, an bestehenden Typen aber nur im Code ändern. Beziehungen werden als gerade Linien gezeichnet (keine Kantenführung um Boxen herum).
- PNG-Export ist nur in der echten App geprüft (Headless-Tests haben keinen Skia-Renderer).

