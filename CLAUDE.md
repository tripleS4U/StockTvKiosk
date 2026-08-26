# CLAUDE.md

Diese Datei liefert Claude Code den Kontext und die Anweisungen für dieses Projekt.

## Projektziel

Eine plattformübergreifende C#-Anwendung (Windows + Linux), die:

1. beim Start die verfügbaren Bildschirme (Displays) des Systems erkennt,
2. prüft, ob ein **primärer** und ein **sekundärer** Bildschirm vorhanden sind,
3. je nach Erkennung ein oder zwei Fenster öffnet, die jeweils eine URL einer bestehenden Blazor-App via WebView laden,
4. das jeweilige Fenster exakt auf dem zugehörigen Bildschirm positioniert (Vollbild/Kiosk-Modus).

Wenn nur ein Bildschirm erkannt wird, öffnet die App nur ein Fenster mit der Primär-URL. Wenn zwei (oder mehr) Bildschirme erkannt werden, öffnet sie zusätzlich ein zweites Fenster mit der Sekundär-URL auf dem zweiten Bildschirm.

## Tech-Stack

- **.NET 10** (aktuelle Version verwenden — `dotnet --version` prüfen und `global.json` entsprechend anlegen)
- **Avalonia UI** als Cross-Platform-UI-Framework (Windows + Linux, X11 und nach Möglichkeit Wayland)
- **WebView-Integration** zum Laden der Blazor-URLs innerhalb eines nativen Avalonia-Fensters (z. B. `Avalonia.WebView` bzw. aktuell empfohlenes Paket recherchieren, da sich das Avalonia-WebView-Ökosystem schnell weiterentwickelt — bei Unsicherheit im Web nach dem aktuellen Stand für .NET 10 / Avalonia suchen, bevor eine Paketentscheidung getroffen wird)
- Konfiguration über `appsettings.json` (Primär-URL, Sekundär-URL, Fallback-Verhalten) sowie optionale Kommandozeilen-Overrides

## Architektur / Vorgehen

1. **Projekt-Setup**
   - Solution mit Avalonia-App-Projekt (`dotnet new avalonia.app`) auf Basis von .NET 10 anlegen.
   - `global.json` mit fixierter SDK-Version für Reproduzierbarkeit.
   - Publish-Targets für `win-x64` und `linux-x64` (self-contained) einrichten.

2. **Screen-Erkennung**
   - Über Avalonias `Screens`-API (`TopLevel.Screens`, `Screens.All`, `Screens.Primary`) alle Displays auslesen.
   - Logik implementieren:
     - `Screens.All.Count >= 2` → zwei Fenster erzeugen (Primär + Sekundär).
     - Nur 1 Screen → nur ein Fenster mit der Primär-URL, Hinweis loggen, dass kein sekundärer Monitor gefunden wurde.

3. **Fenster-Erstellung**
   - Für jeden erkannten Screen ein randloses Vollbildfenster (`SystemDecorations = None`, Position/Größe = `Bounds`/`WorkingArea` des jeweiligen Screens).
   - Jedes Fenster enthält eine WebView-Komponente, die die zugehörige URL lädt.

4. **Konfiguration**
   - `appsettings.json`: `PrimaryUrl`, `SecondaryUrl`, Fallback-Verhalten.
   - Kommandozeilenargumente können die URLs überschreiben.

5. **Plattform-Besonderheiten**
   - **Linux:** X11 vs. Wayland beachten — auf Wayland ist programmatische Fensterpositionierung eingeschränkt (Compositor-Beschränkung). Falls nötig, Fallback-Strategie dokumentieren.
   - **Windows:** WebView2-Runtime-Verfügbarkeit prüfen (Hinweis/Fehlermeldung, falls nicht installiert).

6. **Logging & Fehlerbehandlung**
   - `Microsoft.Extensions.Logging` verwenden (Konsole + optional Datei).
   - Erkannte Screens, geladene URLs und Fehler beim Laden der WebView protokollieren.
   - Definiertes Verhalten, falls eine URL nicht erreichbar ist (z. B. Retry oder Fehlerseite).

7. **Tests**
   - Manuelles Testprotokoll für: Windows 1 Monitor, Windows 2 Monitore, Linux/X11 1/2 Monitore, Linux/Wayland 1/2 Monitore.
   - Unit-Tests für die Screen-Erkennungslogik so weit wie möglich (z. B. durch Abstraktion der Screens-API hinter einem Interface, das sich mocken lässt).

## Arbeitsweise für Claude Code

- Bitte **inkrementell** vorgehen: zuerst das leere Avalonia-Grundgerüst mit .NET 10 aufsetzen und sicherstellen, dass es unter Windows und Linux baut, bevor die Screen-Logik und WebView-Integration hinzukommen.
- Bei der Auswahl von NuGet-Paketen (insbesondere für WebView-Integration) aktuell im Web recherchieren, da sich das Avalonia-Ökosystem für WebViews schnell ändert und Trainingsdaten hier veraltet sein können.
- Die Screen-Erkennungslogik hinter einem eigenen Interface/Service kapseln, damit sie unabhängig von der UI testbar ist.
- Commits klein und nachvollziehbar halten (Setup → Screen-Erkennung → Fenstererstellung → WebView-Integration → Konfiguration → Logging → Tests).
- Nach jedem größeren Schritt kurz zusammenfassen, was implementiert wurde und was als Nächstes ansteht.

## Offene Punkte / vom Nutzer zu klären

- Exakte Primär- und Sekundär-URLs der Blazor-App (aktuell Platzhalter in `appsettings.json`).
- Gewünschtes Verhalten bei Verbindungsabbruch zur Blazor-App (Retry-Intervall, Fehlerseite).
- Ob Wayland-Unterstützung zwingend erforderlich ist oder ob X11 als primäre Linux-Zielplattform ausreicht.
- Ob die App als Autostart-Dienst (Windows-Task / systemd-Service) für Kiosk-Betrieb eingerichtet werden soll.
