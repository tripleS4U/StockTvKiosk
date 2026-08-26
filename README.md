# StockTV-Kiosk

Plattformuebergreifende Kiosk-Anwendung (Windows + Linux), die die bestehende
StockTV-Blazor-App auf einem oder zwei Bildschirmen im randlosen Vollbild anzeigt.

Erkennt beim Start die verfuegbaren Displays und oeffnet
* **zwei** Fenster (Bedienfenster + Spiegel), wenn ein zweiter Bildschirm nutzbar ist,
* **ein** Fenster mit der Primaer-URL, wenn nicht.

## Tech-Stack

| Baustein | Version | Anmerkung |
|---|---|---|
| .NET | 10.0.302 | in `global.json` fixiert |
| Avalonia UI | 12.1.1 | Windows, X11, Wayland |
| `Avalonia.Controls.WebView` | 12.1.0 | offiziell, MIT, seit Avalonia 12 quelloffen |

Der WebView nutzt die **native** Engine der Plattform (WebView2 unter Windows,
WPE bzw. WebKitGTK unter Linux) — es wird kein Chromium mitgeliefert.

## Projektstruktur

```
src/StockTvKiosk.Core/    UI-freie Logik: Bildschirmerkennung, Fensterplanung, Konfiguration
src/StockTvKiosk.App/     Avalonia-Anwendung: Fenster, WebView, Prozess-Management, Logging
tests/StockTvKiosk.Core.Tests/  Unit-Tests der Entscheidungslogik (33 Tests)
```

Die gesamte Entscheidungslogik ("wie viele Fenster, auf welchem Bildschirm, mit welcher
URL") liegt in `KioskLayoutPlanner` und ist ohne Avalonia testbar. Die Bildschirmliste
kommt ueber `IScreenProvider`, den `AvaloniaScreenProvider` fuer die echte App und
`FakeScreenProvider` fuer die Tests implementiert.

## Bauen und starten

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/StockTvKiosk.App
```

### Publish

```powershell
.\publish.ps1
```

Erzeugt self-contained Builds unter `publish/` fuer `win-x64`, `linux-x64` und
`linux-arm64` (Raspberry Pi 4, 64-bit). `-Runtime linux-arm64` baut nur ein Ziel.

## Konfiguration

Alles in `appsettings.json`, Abschnitt `Kiosk`. Jeder Wert laesst sich auf der
Kommandozeile ueberschreiben — per Kurzform oder ueber den vollen Pfad
(`--Kiosk:Retry:IntervalSeconds=10`).

| Kurzform | Bedeutung |
|---|---|
| `--primary-url` | Adresse des Bedienfensters |
| `--secondary-url` | Adresse der Spiegel-Anzeige |
| `--demo true` | haengt `demo=true` an beide URLs |
| `--dual false` | nur ein Fenster |
| `--second-screen false` | beide Fenster nebeneinander auf einem Bildschirm |
| `--main-display 1` | Bildschirmindex fuers Bedienfenster |
| `--mirror-display 0` | Bildschirmindex fuer den Spiegel |
| `--window-mode FullScreen` | zusaetzlich `WindowState=FullScreen` setzen |
| `--dry-run true` | nur erkennen und protokollieren, kein Fenster oeffnen |
| `--manage-host true` | StockTvBlazor selbst starten und mit beenden |
| `--host-exe <Pfad>` | Pfad zur `StockTvBlazor.exe` |

Zusaetzlich werden Umgebungsvariablen mit dem Praefix `STOCKTV_` gelesen
(`STOCKTV_Kiosk__Urls__PrimaryUrl=...`), und `appsettings.Local.json` ueberschreibt
`appsettings.json`, ohne dass die eingecheckte Datei angefasst werden muss.

### Wichtige Einstellungen

`Kiosk:Display:MainDisplayIndex` / `MirrorDisplayIndex`
: `-1` = automatisch (Bedienfenster auf den System-Primaerbildschirm, Spiegel auf den
  ersten anderen). Ein expliziter Index ueberstimmt das — noetig, wenn der Kunde den
  Windows-Primaerbildschirm anders gesetzt hat, als der Kiosk ihn braucht.

`Kiosk:Display:SingleScreenFallback`
: Verhalten mit nur einem nutzbaren Bildschirm. `SideBySide` legt beide Fenster
  nebeneinander (wie `start-compiled.ps1`), `PrimaryOnly` oeffnet nur das Bedienfenster.

`Kiosk:Display:TreatDuplicatedAsSingleScreen`
: Bei Anzeigemodus *Duplizieren* zeigen beide Ausgaenge dasselbe Bild — ein Vollbild-Spiegel
  wuerde das Bedienfenster verdecken. Standard `true` behandelt den Fall wie einen Bildschirm.

`Kiosk:Display:AllowExitShortcut`
: Laesst **Shift+Esc** den Kiosk beenden. Im unbeaufsichtigten Feldbetrieb auf `false`.

`Kiosk:BlazorHost:Enabled`
: `false` (Standard): StockTV wird extern gestartet, der Kiosk wartet nur auf die URL.
  `true`: der Kiosk startet `StockTvBlazor` selbst, beendet eine bereits laufende Instanz
  (NetMQ belegt 4747/4748 exklusiv) und faehrt sie beim Schliessen mit herunter.

## Verhalten bei nicht erreichbarer URL

Jedes Fenster prueft seine URL per HTTP, bevor es sie laedt. Antwortet der Server nicht,
zeigt der Kiosk eine **lokale, vollstaendig eingebettete Statusseite** mit Ziel-URL,
Fensterrolle und Versuchszaehler und probiert es alle `IntervalSeconds` erneut. Sobald
der Server antwortet, wird automatisch geladen — ohne Eingriff vor Ort. Schlaegt eine
Navigation spaeter fehl, faellt das Fenster in dieselbe Schleife zurueck.

Jede HTTP-Antwort gilt als erreichbar, auch 404 oder 500: der Server laeuft dann, und
seine echte Fehlermeldung hilft bei der Diagnose mehr als unsere Statusseite.

> `ProbeTimeoutSeconds` nicht unter 5 s setzen. `localhost` loest zuerst auf `::1` auf;
> der IPv6-Versuch braucht rund 2 s, bevor auf `127.0.0.1` zurueckgefallen wird. Bei zu
> kurzem Timeout meldet der Kiosk "Zeitueberschreitung", obwohl die Verbindung schlicht
> abgelehnt wurde.

## Plattform-Hinweise

### Windows
Braucht die **WebView2-Runtime**. Unter Windows 11 vorinstalliert, unter Windows 10 ggf.
nachzuinstallieren (Evergreen Runtime von Microsoft).

Das Anwendungsmanifest setzt Per-Monitor-DPI-Awareness. Ohne sie skaliert Windows die
Fenster nach, und die Positionierung auf einem zweiten Bildschirm mit abweichender
Skalierung stimmt nicht mehr.

### Linux
```bash
sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0
```
(Fedora: `sudo dnf install gtk3 webkit2gtk4.1 libsoup3`)

**X11 vs. Wayland:** Unter X11 funktioniert die exakte Fensterpositionierung wie unter
Windows. Unter Wayland darf eine Anwendung ihre Position nicht selbst bestimmen — der
Compositor entscheidet, auf welchem Ausgang ein Fenster landet. Der Kiosk erkennt eine
Wayland-Sitzung und protokolliert eine Warnung; die Zuordnung zweier Fenster auf zwei
bestimmte Bildschirme ist dort nicht garantiert.

**Raspberry Pi OS (Bookworm)** startet den Desktop standardmaessig mit Wayland/labwc.
Fuer verlaessliches Dual-Display auf X11 umstellen:
`raspi-config` → *Advanced Options* → *Wayland* → *X11*.

Wird Wayland zwingend gebraucht, muss die Zuordnung ueber den Compositor erfolgen
(z. B. `labwc`-Fensterregeln oder `cage`/Kiosk-Shell pro Ausgang) statt ueber die App.

## Manuelles Testprotokoll

Vor jedem Durchgang: `--dry-run true` ausfuehren und pruefen, ob die protokollierten
Bildschirme und der Fensterplan zur Erwartung passen.

| # | Plattform | Aufbau | Erwartung | Status |
|---|---|---|---|---|
| 1 | Windows | 2 Monitore, erweitert | 2 Fenster, je 1 pro Bildschirm, Fokus im Bedienfenster | ✅ verifiziert |
| 2 | Windows | 1 Monitor | 2 Fenster nebeneinander, Warnung "Kein zweiter Bildschirm" | ⬜ offen |
| 3 | Windows | 2 Monitore, dupliziert | 1 Bildschirm belegt, Warnung "Duplizieren" | ⬜ offen |
| 4 | Windows | `--dual false` | genau 1 Vollbildfenster | ✅ verifiziert |
| 5 | Windows | Server aus, dann an | Statusseite → Auto-Reload ohne Eingriff | ✅ verifiziert |
| 6 | Windows | Monitore unterschiedlich skaliert | beide Fenster fuellen ihren Bildschirm exakt | ⬜ offen |
| 7 | Linux/X11 | 2 Monitore | wie #1 | ⬜ offen |
| 8 | Linux/X11 | 1 Monitor | wie #2 | ⬜ offen |
| 9 | Linux/Wayland | 2 Monitore | Fenster oeffnen, Wayland-Warnung im Log, Zuordnung ggf. abweichend | ⬜ offen |
| 10 | Pi 4 / arm64 | 2 HDMI, X11 | wie #1, WebKitGTK laedt die Blazor-Seiten | ⬜ offen |
| 11 | beide | Numpad-Eingabe | Tastatureingabe landet im Bedienfenster, nicht im Spiegel | ⬜ offen |
| 12 | beide | `--manage-host true` | StockTV wird gestartet und beim Schliessen beendet | ⬜ offen |

Automatisiert abgedeckt sind alle Layout-Entscheidungen aus #1–#4, #6 sowie die
Anzeigemodus-Erkennung — 33 Unit-Tests in `tests/StockTvKiosk.Core.Tests`.

**Verifiziert am 26.08.2026** unter Windows 11 (10.0.26200, x64) mit zwei Monitoren
(`2560x1440` primaer, `1920x1200` links davon bei X = −1920) gegen das laufende
`StockTvBlazor` auf Port 5129.

## Logging

Konsole und Datei (`_logs/kiosk.log`, rotierend, 5 MB, 3 Altdateien). Protokolliert
werden erkannte Bildschirme, Anzeigemodus samt Erkennungsquelle, der Fensterplan mit
Zielbildschirm und URL, jeder Verbindungsversuch sowie Ladefehler.

Unter Windows haengt sich der Kiosk an eine vorhandene Konsole an, ohne selbst eine zu
oeffnen: aus einem Terminal gestartet erscheinen die Logs dort, per Doppelklick gestartet
stoert kein Konsolenfenster.
