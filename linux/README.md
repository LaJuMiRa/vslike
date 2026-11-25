# VsLike für Linux

Die Linux-Version der C#-IDE VsLike (Fedora, Arch). Sie ist eine Avalonia-App mit demselben Funktionsumfang und Aussehen wie die
macOS-App und nutzt dieselbe Analyse-Engine (`../engine`, Roslyn) über JSON-RPC. Keine Telemetrie, Lizenz MIT.

## Installation

Voraussetzung: ein .NET SDK ab Version 10 (die Pakete ziehen es bei Fedora und Arch automatisch nach). Pakete liegen bei den
GitHub-Releases mit Tag `linux-vX.Y.Z`.

```sh
# Fedora
sudo dnf install ./vslike-<version>-1.fc43.x86_64.rpm

# Arch
sudo pacman -U vslike-bin-<version>-1-x86_64.pkg.tar.zst

# andere Distributionen
sudo tar -xzf vslike-<version>-linux-x64.tar.gz -C /
```

Die Pakete sind nicht signiert. Gestartet wird mit `vslike [Solution, Projekt, Ordner oder Datei]`.

## Aus dem Quellcode bauen

Nötig ist das .NET SDK 10 (für die Engine mindestens 10.0.400, siehe `../engine/global.json`).

```sh
dotnet build ../engine/VsLike.Engine.slnx
dotnet build VsLike.Linux.slnx
dotnet test VsLike.Linux.slnx
./build-linux.sh      # baut dist/vslike-<version>-linux-<arch>.tar.gz
./release-linux.sh    # Tests, Tarball, RPM und Arch-Paket (RPM und Arch-Paket brauchen Docker)
```

| Ordner | Inhalt |
|---|---|
| `src/VsLike.Kit` | Engine-Client, dotnet-CLI, Solution und Projekt, Debugger, Lexer, Diagramm-Layout (Port von `Sources/VsLikeKit`) |
| `src/VsLike.Desktop` | App: Workspace, Editor, Diagramm, Seitenleiste, Panels, Dialoge (Port von `Sources/VsLike`) |
| `tests` | Tests für Kit und Desktop |
| `packaging` | RPM-Spec, PKGBUILD, Desktop-Datei, Symbole |
