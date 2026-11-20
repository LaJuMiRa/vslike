# Offene Fragen

- **Repo liegt in iCloud Drive** (`~/Documents` wird synchronisiert). iCloud lagert Dateien aus („dataless“); am 2026-11-20 war dadurch die Git-Pack-Datei mit dem ersten Commit unlesbar („far too short to be a packfile“). Behoben durch Zurückladen (`brctl download .git`), aber das kann jederzeit wieder passieren, auch mit `obj/`/`bin/`. Empfehlung: Repo außerhalb von iCloud ablegen (z. B. `~/Developer/vslike`) oder für `Documents` „Mac-Speicher optimieren“ abschalten, und regelmäßig nach `origin` pushen.
- StyleCop 1.3.180 meldet mehrzeilige Collection-Expressions (`[.. x
 .Where()]`) als SA1500; mehrzeilig daher `.ToList()` verwenden.
