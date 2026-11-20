# Entscheidungen

| Datum | Entscheidung | Begründung |
|---|---|---|
| 2026-11-20 | Avalonia 11.3.23, Dock.Avalonia 11.3.12.1, AvaloniaEdit 11.4.1 statt der aktuellen 12.x-Linie | Prompt legt Avalonia 11.x verbindlich fest; 12.x (Avalonia 12.1.6, AvaloniaEdit 12.0.0) existiert und wäre ein späteres Upgrade. |
| 2026-11-20 | Roslyn 5.12.0 (Microsoft.CodeAnalysis.*) | Neueste stabile Version, unterstützt C# 14 / .NET 10 SDK. |
| 2026-11-20 | xUnit 2.9.3 + xunit.runner.visualstudio 3.1.5 | Avalonia.Headless.XUnit 11.x hängt an xunit.core 2.x. |
| 2026-11-20 | netcoredbg: osx-arm64 aus 3.2.0-1092, osx-x64 aus 3.1.3-1062, Linux amd64/arm64 aus 3.2.0-1092 | Release 3.2.0 enthält keinen osx-amd64-Build mehr (GitHub-Releases geprüft am 2026-11-20). |
| 2026-11-20 | StyleCop.Analyzers 1.3.180 als feste Version (eigener Code und IDE-Analyzer) | Neueste stabile Version, im NuGet-Cache vorhanden; 1.2.0-beta.556 ist eine Beta. |
| 2026-11-20 | Kein `NuGet.Protocol`; NuGet-v3-REST-API direkt per `HttpClient`, Installation über `dotnet add/remove package` | `NuGet.Protocol` 8.0.0 bringt `NuGet.Frameworks` mit, das mit dem MSBuild-Locator kollidiert (MSBL001), und zieht `System.Security.Cryptography.Pkcs` 8.0.1 mit GHSA-846f-f764-76mx. Die CLI hält csproj-Änderungen konsistent mit dem SDK. |
| 2026-11-20 | `Microsoft.Build.Framework` und `Microsoft.NET.StringTools` mit `ExcludeAssets="runtime"` (Property `UsesMSBuildLocator`) | Pflicht für MSBuildLocator: MSBuild-Assemblies müssen aus dem installierten SDK geladen werden. |
| 2026-11-20 | StyleCop-Regeln SA0001, SA1600–SA1602, SA1633, SA1101, SA1309, SA1413, SA1000/1009/1010 aus | Keine Pflicht-XML-Doku pro Element und kein Datei-Header; `_feld` statt `this.feld`; C#-12-Collection-Expressions kollidieren mit SA1000/1009/1010 in StyleCop 1.3.180. |
