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
- **Avalonia UI 12.1.1** als Cross-Platform-UI-Framework (Windows + Linux; v12 bringt native Wayland-Unterstützung)
- **`Avalonia.Controls.WebView` 12.1.0** — entschieden am 26.08.2026. Seit Avalonia 12 ist der WebView offiziell, quelloffen (MIT) und kostenlos; er nutzt die native Engine der Plattform (WebView2 / WPE bzw. WebKitGTK) statt gebündeltem Chromium. Damit sind die früher nötigen Drittanbieter-Pakete (`Avalonia.WebView`, `WebViewControl-Avalonia`/CefGlue) hinfällig.
- Konfiguration über `appsettings.json` (Primär-URL, Sekundär-URL, Fallback-Verhalten) sowie optionale Kommandozeilen-Overrides

## Architektur / Vorgehen

1. **Projekt-Setup**
   - Solution mit Avalonia-App-Projekt (`dotnet new avalonia.app`) auf Basis von .NET 10 anlegen.
   - `global.json` mit fixierter SDK-Version für Reproduzierbarkeit.
   - Publish-Targets für `win-x64`, `linux-x64` und **`linux-arm64`** (self-contained) einrichten. `linux-arm64` ist das eigentliche Produktivziel (Raspberry Pi 4, 64-bit Raspberry Pi OS), `linux-x64` dient dem Vortest auf PC/VM.

2. **Screen-Erkennung**
   - Über Avalonias `Screens`-API (`TopLevel.Screens`, `Screens.All`, `Screens.Primary`) alle Displays auslesen.
   - Logik implementieren:
     - `Screens.All.Count >= 2` → zwei Fenster erzeugen (Primär + Sekundär).
     - Nur 1 Screen → nur ein Fenster mit der Primär-URL, Hinweis loggen, dass kein sekundärer Monitor gefunden wurde.

3. **Fenster-Erstellung**
   - Für jeden erkannten Screen ein randloses Vollbildfenster: `WindowDecorations = None`, Position/Größe = `Bounds`/`WorkingArea` des jeweiligen Screens. In Avalonia 12 ersetzt `WindowDecorations` das obsolete `SystemDecorations`.
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

## Geklärte Punkte (26.08.2026)

- **URLs:** Die Blazor-App ist StockTV (`StockTvBlazor.exe`) auf `http://localhost:5129`.
  Primär `/turnier` (Bedienfenster, Numpad-Eingabe), sekundär `/display2` (Spiegel).
  Beides in `appsettings.json` und per `--primary-url` / `--secondary-url` überschreibbar.
- **Verbindungsabbruch:** Lokale, eingebettete Statusseite + Auto-Retry alle 5 s, automatischer
  Reload, sobald der Server antwortet. Konfigurierbar unter `Kiosk:Retry`.
- **Wayland:** X11 ist die primäre Linux-Zielplattform. Wayland läuft, aber die
  Fensterpositionierung liegt dort beim Compositor — der Kiosk erkennt das und warnt im Log.
  Auf Raspberry Pi OS Bookworm per `raspi-config` auf X11 stellen.
- **Prozess-Management:** Konfigurierbar (`Kiosk:BlazorHost:Enabled`). Aus = StockTV wird
  extern gestartet; ein = der Kiosk startet und beendet StockTV selbst und räumt eine
  laufende Vorinstanz wegen der NetMQ-Ports 4747/4748 ab.

- **Autostart:** Umgesetzt für beide Plattformen unter `deploy/` — systemd-User-Units für Linux,
  Aufgabenplanung für Windows. Jeweils zwei getrennte Einheiten (StockTV und Anzeige), damit sich
  die Anzeige neu starten lässt, ohne die laufende Wertung anzufassen. Deshalb bleibt
  `Kiosk:BlazorHost:Enabled` im Autostart-Betrieb auf `false`.
- **`AllowExitShortcut`:** Bleibt auf `true`. Mit Autostart holt systemd den Kiosk nach 5 s
  zurück, der Windows-Task binnen einer Minute — Shift+Esc ist damit ein „Anzeige neu laden"
  statt eines Ausfalls. Auf `false` nur, wenn der Kiosk StockTV selbst verwaltet oder kein
  Autostart eingerichtet ist (Details im README).

## Offene Punkte / vom Nutzer zu klären

- Ob es weitere Startseiten außer `turnier` und `bestof` gibt, die als Primär-URL vorkommen.
