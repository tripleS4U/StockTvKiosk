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

**Einzige Quelle ist `AppConfig`** — die Datei `_config/AppConfig.json` neben der
Anwendung. Sie wird beim ersten Start mit Standardwerten angelegt; der tatsaechlich
verwendete Pfad steht in der ersten Logzeile:

```
info: StockTvKiosk[0] Konfiguration: …\_config\AppConfig.json
```

Es gibt bewusst **keine zweite Konfigurationsquelle**: kein `appsettings.json`, keine
`STOCKTV_`-Umgebungsvariablen, keine Kommandozeilen-Overrides fuer Einstellungswerte. Zwei
Quellen, die still gegeneinander arbeiten, sind im unbeaufsichtigten Kiosk-Betrieb
schwerer zu diagnostizieren als eine Datei, die man aufmacht und liest.

Der einzige verbliebene Schalter ist:

| Schalter | Bedeutung |
|---|---|
| `--dry-run` | Bildschirme erkennen, Fensterplan protokollieren, beenden — kein Fenster |

Er steht absichtlich nicht in der Datei: er gilt fuer genau einen Programmlauf, und ein
dort vergessenes `true` haette zur Folge, dass der Kiosk gar nichts mehr anzeigt.
`--dry-run`, `--dry-run true` und `--dry-run=true` sind gleichwertig; `--dry-run false`
schaltet ausdruecklich ab.

Enum-Werte stehen als Name in der Datei (`"WindowMode": "FullScreen"`,
`"SingleScreenFallback": "PrimaryOnly"`). Zahlwerte aus aelteren Dateien werden beim Lesen
weiterhin akzeptiert und beim naechsten Speichern durch den Namen ersetzt.

### Wichtige Einstellungen

`KioskOptions > Display > MainDisplayIndex` / `MirrorDisplayIndex`
: `-1` = automatisch (Bedienfenster auf den System-Primaerbildschirm, Spiegel auf den
  ersten anderen). Ein expliziter Index ueberstimmt das — noetig, wenn der Kunde den
  Windows-Primaerbildschirm anders gesetzt hat, als der Kiosk ihn braucht.

`KioskOptions > Display > SingleScreenFallback`
: Verhalten mit nur einem nutzbaren Bildschirm. `SideBySide` legt beide Fenster
  nebeneinander (wie `start-compiled.ps1`), `PrimaryOnly` oeffnet nur das Bedienfenster.

`KioskOptions > Display > TreatDuplicatedAsSingleScreen`
: Bei Anzeigemodus *Duplizieren* zeigen beide Ausgaenge dasselbe Bild — ein Vollbild-Spiegel
  wuerde das Bedienfenster verdecken. Standard `true` behandelt den Fall wie einen Bildschirm.

`KioskOptions > Display > AllowExitShortcut`
: Laesst **Shift+Esc** den Kiosk beenden. Standard `true` — siehe
  [Wann Shift+Esc abschalten?](#wann-shiftesc-abschalten).

`KioskOptions > BlazorHost > Enabled`
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

## Autostart

Auf beiden Plattformen gilt dasselbe Muster: **zwei getrennte Einheiten**, eine fuer
StockTV und eine fuer die Anzeige. Damit laesst sich der Kiosk neu starten, ohne die
laufende Turnierwertung anzufassen — deshalb bleibt `KioskOptions > BlazorHost > Enabled` beim
Autostart-Betrieb auf `false`.

### Linux (systemd)

```bash
./deploy/linux/install-linux.sh --kiosk-dir /opt/stocktv-kiosk --stocktv-dir /opt/stocktv
```

Legt zwei **User-Units** unter `~/.config/systemd/user/` an — kein `sudo` noetig, und der
Kiosk laeuft in der grafischen Sitzung des angemeldeten Benutzers, was er ohnehin muss.
Auf einem Pi mit Autologin ist das genau das richtige Modell.

* `stocktv.service` — `WantedBy=default.target`, `Restart=always`, `KillSignal=SIGINT`
  (gibt StockTV Gelegenheit, die NetMQ-Ports 4747/4748 sauber freizugeben)
* `stocktv-kiosk.service` — `WantedBy=graphical-session.target`, `Wants=stocktv.service`,
  wartet per `ExecStartPre` bis zu 60 s auf den X11-Socket

`install-linux.sh --dry-run` zeigt die fertigen Units, ohne etwas zu installieren. Soll StockTV schon vor
der Anmeldung laufen: `sudo loginctl enable-linger <user>`.

```bash
journalctl --user -u stocktv-kiosk.service -f
```

Deinstallieren: `./deploy/linux/uninstall-linux.sh`

### Windows (Aufgabenplanung)

```powershell
.\deploy\windows\Install-KioskTask.ps1 -KioskPath C:\StockTV\Kiosk\StockTvKiosk.exe -StockTvPath C:\StockTV\App\StockTvBlazor.exe
```

Legt die Tasks `\StockTV\StockTV` und `\StockTV\StockTV-Kiosk` an, die bei der Anmeldung
starten (der Kiosk mit 20 s Vorsprung fuer StockTV). Laeuft unter dem aktuellen Benutzer
mit normalen Rechten — **kein Administrator noetig**. `-WhatIfOnly` zeigt nur, was
registriert wuerde.

Ein Detail, das nicht offensichtlich ist: Die Aufgabenplanung startet von sich aus nur
nach einem **Fehler** neu (`RestartCount`), nicht nach einem sauberen Beenden — anders als
systemd mit `Restart=always`. Damit der Kiosk sich auch nach Shift+Esc wieder faengt,
bekommt der Task einen **zweiten** Trigger: ein `Once`-Trigger auf heute 00:00, der
zusammen mit `StartWhenAvailable` sofort nachgeholt wird und sich dann im Minutentakt
wiederholt, kombiniert mit `MultipleInstances = IgnoreNew`. Laeuft der Kiosk noch, wird
der Start verworfen; ist er weg, startet er erneut.

> Die Wiederholung muss an einem **eigenen** Trigger haengen. Haengt man sie an den
> `AtLogOn`-Trigger, laeuft sie erst ab der naechsten Anmeldung, und ein manuelles
> `Start-ScheduledTask` aktiviert sie ueberhaupt nicht.

Mit `-NoKeepAlive` bleibt es beim reinen Anmelde-Trigger. Deinstallieren:
`.\deploy\windows\Uninstall-KioskTask.ps1`

### Wann Shift+Esc abschalten?

Kurz: **mit dem Autostart oben in aller Regel gar nicht.**

Der urspruengliche Grund fuer `AllowExitShortcut = false` war, dass jemand am Geraet den
Kiosk auf den Desktop beenden kann. Sobald aber Autostart mit Neustart eingerichtet ist,
traegt das Argument kaum noch: systemd holt den Kiosk nach `RestartSec=5` zurueck, der
Windows-Task binnen einer Minute. Shift+Esc wird damit vom Ausfallrisiko zur nuetzlichen
Vor-Ort-Geste „Anzeige neu laden" — praktisch, wenn eine Blazor-Seite haengt und niemand
SSH-Zugang hat.

Ein echtes Risiko bleibt in genau einem Fall: bei `KioskOptions > BlazorHost > Enabled = true` nimmt
der Kiosk StockTV beim Beenden mit. Shift+Esc startet dann mitten im Turnier die Wertung
neu — NetMQ-Verbindungen brechen, Zustand im Arbeitsspeicher geht verloren. Genau deshalb
trennt der Autostart oben beides in zwei Einheiten.

Also:

| Situation | Empfehlung |
|---|---|
| Autostart mit zwei Einheiten (empfohlen) | `true` lassen |
| Kiosk verwaltet StockTV (`BlazorHost.Enabled = true`) | `false` |
| Oeffentlich zugaengliches Geraet **und** Fernwartung vorhanden | `false` |
| Kein Autostart eingerichtet | `false`, sonst bleibt der Kiosk unten |

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
| 4 | Windows | `Display > Dual = false` | genau 1 Vollbildfenster | ✅ verifiziert |
| 5 | Windows | Server aus, dann an | Statusseite → Auto-Reload ohne Eingriff | ✅ verifiziert |
| 6 | Windows | Monitore unterschiedlich skaliert | beide Fenster fuellen ihren Bildschirm exakt | ⬜ offen |
| 7 | Linux/X11 | 2 Monitore | wie #1 | ⬜ offen |
| 8 | Linux/X11 | 1 Monitor | wie #2 | ⬜ offen |
| 9 | Linux/Wayland | 2 Monitore | Fenster oeffnen, Wayland-Warnung im Log, Zuordnung ggf. abweichend | ⬜ offen |
| 10 | Pi 4 / arm64 | 2 HDMI, X11 | wie #1, WebKitGTK laedt die Blazor-Seiten | ⬜ offen |
| 11 | beide | Numpad-Eingabe | Tastatureingabe landet im Bedienfenster, nicht im Spiegel | ⬜ offen |
| 12 | beide | `--manage-host true` | StockTV wird gestartet und beim Schliessen beendet | ⬜ offen |
| 13 | Windows | Task registrieren | Task angelegt, 2 Trigger, `IgnoreNew`, Start unter dem Benutzer | ✅ verifiziert |
| 14 | Windows | Kiosk beendet sich sauber | Task startet ihn binnen einer Minute erneut | ✅ verifiziert |
| 15 | Windows | Kiosk laeuft dauerhaft | Wiederholungstrigger erzeugt **keine** zweite Instanz | ✅ verifiziert |
| 16 | Linux | Units erzeugen | `systemd-analyze verify` ohne Beanstandung | ✅ verifiziert |
| 17 | Linux | Installationsablauf | Units am richtigen Ort, `daemon-reload` vor `enable`, Deinstallation raeumt auf | ✅ verifiziert |
| 18 | Pi 4 | Autostart real | Kiosk startet nach Reboot ohne Anmeldung von Hand | ⬜ offen |

Automatisiert abgedeckt sind alle Layout-Entscheidungen aus #1–#4, #6 sowie die
Anzeigemodus-Erkennung — 33 Unit-Tests in `tests/StockTvKiosk.Core.Tests`.

**Verifiziert am 26.08.2026** unter Windows 11 (10.0.26200, x64) mit zwei Monitoren
(`2560x1440` primaer, `1920x1200` links davon bei X = −1920) gegen das laufende
`StockTvBlazor` auf Port 5129. Die systemd-Units und Installationsskripte wurden in einem
Debian-Bookworm-Container gegen systemd 252 geprueft — dieselbe Version wie auf
Raspberry Pi OS Bookworm. **Nicht** geprueft ist der Lauf auf echter Pi-Hardware (#18).

## Logging

Konsole und Datei (`_logs/kiosk.log`, rotierend, 5 MB, 3 Altdateien). Protokolliert
werden erkannte Bildschirme, Anzeigemodus samt Erkennungsquelle, der Fensterplan mit
Zielbildschirm und URL, jeder Verbindungsversuch sowie Ladefehler.

Unter Windows haengt sich der Kiosk an eine vorhandene Konsole an, ohne selbst eine zu
oeffnen: aus einem Terminal gestartet erscheinen die Logs dort, per Doppelklick gestartet
stoert kein Konsolenfenster.
