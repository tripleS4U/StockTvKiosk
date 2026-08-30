# StockTV-Kiosk

Plattformuebergreifende Kiosk-Anwendung (Windows + Linux), die die bestehende
StockTV-Blazor-App auf einem oder zwei Bildschirmen im randlosen Vollbild anzeigt.

Erkennt beim Start die verfuegbaren Displays und oeffnet
* **zwei** Fenster (Bedienfenster + Spiegel), wenn ein zweiter Bildschirm nutzbar ist,
* **ein** Fenster mit der Primaer-URL, wenn nicht.

Dazu eine [REST-Schnittstelle](#rest-schnittstelle) mit Swagger, ueber die sich der
Rechner aus der Ferne neu starten, herunterfahren und zwischen *Bildschirme duplizieren*
und *erweitern* umschalten laesst.

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
src/StockTvKiosk.App/     Avalonia-Anwendung: Fenster, WebView, Prozess-Management, Logging, REST-API
tests/StockTvKiosk.Core.Tests/  Unit-Tests der Entscheidungslogik (91 Tests)
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
  `true`: der Kiosk startet `StockTvBlazor` selbst und faehrt es beim Schliessen mit herunter.

Mit `Enabled = true` sieht der Kiosk vor dem Start in drei Stufen nach, ob ein Start
ueberhaupt sinnvoll ist. Das geschieht im Hintergrund — die Fenster erscheinen sofort und
warten wie immer auf die URL.

`KioskOptions > BlazorHost > UrlProbeTimeoutSeconds`
: **Die massgebliche Pruefung.** Antwortet die Primaer-URL, laeuft StockTV bereits — gleich
  ob von Hand gestartet, per Autostart oder **als Dienst installiert**. Dann startet der
  Kiosk keine eigene Instanz. Jede HTTP-Antwort zaehlt, auch 404 oder 500: der Server steht
  dann. `0` schaltet die Pruefung ab.

  Das sticht auch `StopExistingInstance = true`: laeuft StockTV als Dienst, brachte ein
  Abschuss nur den Dienstverwalter zum Neustart — ein Wettlauf, den niemand gewinnt. Und
  noetig waere er nicht, die Adresse liefert ja bereits.

  Nicht unter 5 s setzen, aus demselben Grund wie bei `Retry > ProbeTimeoutSeconds`.

`KioskOptions > BlazorHost > StopExistingInstance`
: Greift nur, wenn die URL **nicht** antwortet, aber ein Prozess laeuft oder ein Port belegt
  ist. `false` (Standard) laesst den Bestand in Ruhe und startet **nichts**. `true` beendet
  die eigenen Instanzen und startet eine neue.

  „Eigene" heisst: aus genau der Datei gestartet, die unter `ExecutablePath` steht. Ein
  Prozess, der nur so **heisst** — eine zweite Installation, ein Dienst —, bleibt
  unangetastet; welcher Pfad stattdessen gefunden wurde, steht im Log. Laesst sich der Pfad
  nicht ermitteln, was bei einem Dienst unter einem anderen Benutzer der Normalfall ist,
  gilt der Prozess ebenfalls als fremd. Im Zweifel nicht anfassen.

  Der Standard ist bewusst `false`: waehrend eines Turniers die laufende Wertung
  abzuschiessen, nur weil der Kiosk neu gestartet wurde, ist der teurere Fehler. Ein
  blinder Start scheitert ohnehin — StockTV belegt NetMQ 4747/4748 und den HTTP-Port
  exklusiv.

  Bleibt danach ein Port belegt, haelt ihn etwas, das der Kiosk nicht beenden darf — dann
  wird auch mit `true` nicht gestartet. Gewartet wird bis zu 5 s auf die Freigabe.

`KioskOptions > BlazorHost > Ports`
: Ports, die vor dem Start frei sein muessen; Vorgabe `[4747, 4748]`. Der HTTP-Port kommt
  automatisch dazu — auf genau den wartet der Kiosk anschliessend.

#### Was nur einmal dasteht

Drei Angaben werden abgeleitet und gehoeren deshalb **nicht** in die Datei:

| Nicht eintragen | Kommt aus |
|---|---|
| `Environment > ASPNETCORE_URLS` | `Urls > PrimaryUrl` (nur Schema, Host und Port) |
| `BlazorHost > WorkingDirectory` | dem Verzeichnis von `ExecutablePath` |
| der HTTP-Port in `BlazorHost > Ports` | ebenfalls `Urls > PrimaryUrl` |

Jede laesst sich weiterhin ausdruecklich setzen und gewinnt dann. Aber zweimal gepflegt
laufen die Werte irgendwann auseinander, und im Fall der Adresse heisst das: der Kiosk
laedt von einem Port, auf dem StockTV gar nicht lauscht — mit der Statusseite als einzigem
Hinweis. Wer `ASPNETCORE_URLS` doch braucht, etwa fuer mehrere Adressen oder eine andere
Bindung, traegt den Schluessel ein; die abgeleitete Angabe tritt dann zurueck.

`ASPNETCORE_ENVIRONMENT` steht aus demselben Grund nicht mehr in den Vorgaben: `Production`
ist ohnehin die Vorgabe von ASP.NET. Fuer die echten Fehlermeldungen statt „An unhandled
error has occurred" hier `Development` eintragen.

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

## REST-Schnittstelle

Der Kiosk bringt eine kleine HTTP-Schnittstelle mit, um den Rechner aus der Ferne neu zu
starten, herunterzufahren und zwischen *Bildschirme duplizieren* und *erweitern*
umzuschalten. Sie laeuft im selben Prozess wie die Fenster — anders ginge es nicht: das
Neuanordnen der Fenster nach einem Moduswechsel muss ueber den UI-Thread laufen.

Bedienoberflaeche: **`http://<geraet>:8099/swagger`**

| Methode | Pfad | Zweck |
|---|---|---|
| `GET` | `/api/system/info` | Betriebssystem, Kiosk-Version, Bildschirme, offene Vormerkung |
| `GET` | `/api/system/capabilities` | Was auf diesem Geraet tatsaechlich geht — und warum nicht |
| `POST` | `/api/system/reboot` | Neustart, `{ "delaySeconds": 30, "force": false }` |
| `POST` | `/api/system/shutdown` | Herunterfahren, gleicher Rumpf |
| `POST` | `/api/system/cancel-shutdown` | Vormerkung zuruecknehmen |
| `GET` | `/api/display/mode` | Anzeigemodus, Bildschirme, Fensteraufteilung |
| `POST` | `/api/display/duplicate` | Auf *Duplizieren* umschalten |
| `POST` | `/api/display/extend` | Auf *Erweitern* umschalten |
| `GET` | `/api/config` | Konfigurationsdatei herunterladen |
| `PUT` | `/api/config` | Konfigurationsdatei ersetzen (Datei-Upload) |
| `POST` | `/api/config/default` | Konfigurationsdatei mit Standardwerten neu anlegen |
| `POST` | `/api/config/api-key` | Schluessel der Fernsteuerung wechseln, `{ "newKey": "…" }` |
| `POST` | `/api/kiosk/reload` | Blazor-Seiten neu laden |
| `POST` | `/api/kiosk/relayout` | Nur die Fenster neu anordnen |

```bash
curl -H "X-Api-Key: $KEY" http://kiosk:8099/api/system/capabilities
curl -X POST -H "X-Api-Key: $KEY" http://kiosk:8099/api/display/extend
curl -X POST -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
     -d '{"delaySeconds":60}' http://kiosk:8099/api/system/reboot
```

### Konfiguration aus der Ferne

```bash
curl -H "X-Api-Key: $KEY" http://kiosk:8099/api/config -o AppConfig.json
# bearbeiten ...
curl -X PUT -H "X-Api-Key: $KEY" -F "file=@AppConfig.json" http://kiosk:8099/api/config
curl -X POST -H "X-Api-Key: $KEY" http://kiosk:8099/api/system/reboot
```

Der Neustart am Ende ist noetig: die laufenden Fenster und Dienste arbeiten mit den Werten,
die sie beim Hochfahren gelesen haben.

Vor dem Ersetzen wird die neue Datei probeweise eingelesen. Scheitert das, bleibt die
bisherige unveraendert und die Antwort nennt die Stelle (400). Das ist keine Formsache:
eine unlesbare Datei legt der Kiosk beim naechsten Start beiseite und faengt mit
Standardwerten an — damit waere auch der API-Schluessel weg und das Geraet aus der Ferne
nicht mehr erreichbar. Die bisherige Fassung wird zusaetzlich als `AppConfig.json.bak`
gesichert.

`POST /api/config/default` legt die Datei mit Standardwerten neu an und **behaelt dabei die
Einstellungen der REST-Schnittstelle** — aus demselben Grund. Wer wirklich alles
zuruecksetzen will, haengt `?keepRestApi=false` an; danach ist das Geraet nur noch vor Ort
erreichbar.

> Die heruntergeladene Datei enthaelt den API-Schluessel als `enc:…`. Das ist Verschleierung,
> keine Verschluesselung mit einem Geheimnis, das nur der Kiosk kennt — behandle die Datei
> wie den Schluessel selbst.

### Schluessel

Sobald die Schnittstelle ueber Loopback hinaus erreichbar ist, ist ein Schluessel
**Pflicht**. Fehlt er, startet sie gar nicht erst und schreibt den Grund ins Log — ohne
diese Sperre koennte jeder im Netz den Rechner neu starten.

Schluessel eintragen: bei stehendem Kiosk `KioskOptions > RestApi > ApiKey` in
`_config/AppConfig.json` im Klartext hinterlegen. Beim naechsten Start wird der Wert
verschluesselt zurueckgeschrieben (`"ApiKey": "enc:…"`) und ist danach nicht mehr im
Klartext lesbar. Das ist Verschleierung gegen versehentliches Mitlesen, kein Schutz vor
jemandem, der Datei **und** Programm hat.

Im laufenden Betrieb geht es ohne Dateizugriff und ohne Neustart:

```bash
curl -X POST -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
     -d '{"newKey":"neuer-schluessel"}' http://kiosk:8099/api/config/api-key
```

Der neue Schluessel gilt ab der Antwort, der bisherige ist damit ungueltig — die Pruefung
liest ihn bei jeder Anfrage neu. Mindestens acht Zeichen. Schlaegt das Speichern fehl,
bleibt der alte gueltig; ein Schluessel, der nur im Arbeitsspeicher steht, waere nach dem
naechsten Start verloren und niemand kaeme mehr auf das Geraet.

> Die Installation belegt den Schluessel mit **`Secret4StockTV?`** vor, damit alle Geraete
> gleich eingerichtet sind. Wer ihn von einem Geraet kennt, kommt damit auf alle — er ist
> eine Huerde gegen versehentliche Zugriffe, kein Geraeteschutz. Fuer je Geraet eigene
> Schluessel: `-GenerateApiKey` bzw. `--generate-api-key` bei der Installation, oder
> nachtraeglich der Aufruf oben.

> Die Swagger-Oberflaeche bleibt ohne Schluessel erreichbar, damit sie sich ueberhaupt
> oeffnen laesst — der Schluessel wird dort oben rechts unter *Authorize* eingetragen.
> Jeder Aufruf aus der Oberflaeche geht wieder durch die Pruefung.

Rein lokaler Betrieb ohne Schluessel geht auch: `BindAddress` auf `127.0.0.1`. Dann kommt
nur heran, wer ohnehin schon am Geraet ist (oder einen SSH-Tunnel hat).

### Einstellungen

`KioskOptions > RestApi > Enabled` / `BindAddress` / `Port`
: Standard `true`, `0.0.0.0`, `8099`. `BindAddress` akzeptiert auch `localhost` und `*`.

`KioskOptions > Power > Mode`
: `Auto` bildet auf das erkannte Betriebssystem ab, `CommandLine` verwendet
  ausschliesslich `RebootCommand`/`ShutdownCommand`, `Disabled` sperrt beide Endpunkte
  (sie antworten dann mit 403).

`KioskOptions > DisplayControl > Mode`
: Genauso fuer das Umschalten. Unter Linux/Wayland ist `CommandLine` der einzige Weg.

`KioskOptions > DisplayControl > ReapplyLayoutAfterSwitch`
: Standard `true` — nach dem Umschalten werden Bildschirme neu erkannt und die Fenster
  nachgezogen. Ohne das haengt ein Vollbildfenster auf einem Bildschirm, den es so nicht
  mehr gibt. `SettleDelayMilliseconds` (1500) ist die Pause davor: das Betriebssystem
  meldet die neue Anordnung nicht sofort.

`KioskOptions > Power > DefaultDelaySeconds` / `MaxDelaySeconds`
: Die Verzoegerung wartet der Kiosk selbst ab, nicht `shutdown /t` — systemd kennt keine.
  Bis zum Ablauf laesst sich die Aktion ueber `cancel-shutdown` zuruecknehmen. Ein
  Neustart, der schon laeuft, ist dagegen nicht mehr aufzuhalten.

### Was die Plattformen koennen

| | Windows | Linux/X11 | Linux/Wayland |
|---|---|---|---|
| Neustart, Herunterfahren | `shutdown.exe` | `systemctl` | `systemctl` |
| Duplizieren, Erweitern | CCD-API, Rueckfall `DisplaySwitch.exe` | `xrandr` | nur ueber eigenen Befehl |

Erhoehte Rechte braucht nichts davon:

* **Windows** — der interaktiv angemeldete Benutzer darf den eigenen Rechner
  herunterfahren, und `SetDisplayConfig` ist genau das, was Win+P ausloest.
* **Linux** — `systemctl reboot`/`poweroff` laeuft ueber logind; polkit erlaubt das der
  aktiven lokalen Sitzung ueblicherweise ohne Passwort. Reicht es nicht (etwa ohne
  Sitzung, per `enable-linger`), einen eigenen Befehl hinterlegen und dafuer eine
  `sudo`-Regel anlegen:

  ```
  # /etc/sudoers.d/stocktv-kiosk  (visudo -f verwenden)
  stocktv ALL=(root) NOPASSWD: /usr/sbin/shutdown
  ```

  ```json
  "Power": { "Mode": "CommandLine",
             "RebootCommand": "sudo", "RebootArguments": "/usr/sbin/shutdown -r now",
             "ShutdownCommand": "sudo", "ShutdownArguments": "/usr/sbin/shutdown -h now" }
  ```

**Wayland** kann das Umschalten nicht: dort bestimmt der Compositor die
Bildschirmanordnung, `xrandr` sieht bestenfalls die XWayland-Sicht. Der Endpunkt antwortet
deshalb mit **501** und einem Hinweis, statt so zu tun, als haette er etwas getan. Ausweg:
auf X11 umstellen (siehe [Linux](#linux)) oder `wlr-randr` installieren und als eigenen
Befehl hinterlegen — die Platzhalter `{primary}`, `{secondary}` und `{direction}` werden
vor dem Aufruf durch die Ausgangsnamen ersetzt.

`GET /api/system/capabilities` beantwortet das alles fuer das laufende Geraet, damit
niemand raten muss.

### Firewall

Die Installationsskripte unter `deploy/` geben den Port **nicht** frei — das braucht unter
Windows Administratorrechte, und sonst verlangt dort nichts welche. Bei Bedarf von Hand:

```powershell
New-NetFirewallRule -DisplayName "StockTV-Kiosk API" -Direction Inbound -Protocol TCP -LocalPort 8099 -Action Allow
```

```bash
sudo ufw allow 8099/tcp
```

## Verteilpaket (Windows)

Fuer den Rollout auf mehrere Geraete gibt es unter [`deploy/bundle/`](deploy/bundle/) ein
Paket, das **StockTV und den Kiosk zusammen** enthaelt — gebaut auf dem
Entwicklungsrechner, als Ganzes auf den Zielrechner kopiert, dort in einem Schritt
installiert und geprueft:

```powershell
.\deploy\bundle\Publish-Bundle.ps1        # Entwicklungsrechner -> C:\StockTV-Install
.\Install-StockTV.ps1                     # Zielrechner -> C:\StockTvApps\
.\Test-Installation.ps1                   # Zielrechner, veraendert nichts
```

Die Installation kann StockTV wahlweise als Windows-Dienst einrichten (`-Mode Service`,
Vorgabe) oder dem Kiosk ueberlassen (`-Mode Managed`) und schreibt beide
Konfigurationsdateien passend zueinander — die haeufigste stille Fehlerquelle ist sonst
eine Portabweichung zwischen `Urls > PrimaryUrl` und dem Port, auf dem StockTV lauscht.
Einzelheiten in [deploy/bundle/README.md](deploy/bundle/README.md).

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
.\deploy\windows\Install-KioskTask.ps1 -KioskPath C:\StockTvApps\StockTvKiosk\StockTvKiosk.exe -StockTvPath C:\StockTvApps\StockTV\StockTvBlazor.exe
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
| 19 | Windows | `GET /api/system/capabilities` | meldet Windows/CCD, `canReboot` und `canSwitchDisplays` | ✅ verifiziert |
| 20 | Windows | Aufruf ohne `X-Api-Key` | 401, Swagger-Oberflaeche bleibt erreichbar | ✅ verifiziert |
| 21 | Windows | Reboot vormerken, zuruecknehmen | 202, `info` zeigt die Vormerkung, zweite ergibt 409, `cancel` 200 | ✅ verifiziert |
| 22 | Windows | `POST /api/display/duplicate` → `extend` | Modus wechselt, Fenster werden nachgezogen | ⬜ offen |
| 23 | Linux/X11 | dito ueber `xrandr` | wie #22 | ⬜ offen |
| 24 | Linux/Wayland | `POST /api/display/extend` | 501 mit Hinweis auf X11 bzw. eigenen Befehl | ⬜ offen |
| 25 | beide | Start ohne `ApiKey` bei `BindAddress = 0.0.0.0` | Schnittstelle startet nicht, Grund im Log | ⬜ offen |
| 26 | Windows | Port belegt, `StopExistingInstance = false` | StockTV wird nicht gestartet, Grund im Log | ✅ verifiziert |
| 27 | Windows | Port von Fremdprozess belegt, `= true` | Fremdprozess bleibt am Leben, kein Start | ✅ verifiziert |
| 28 | Windows | StockTV laeuft bereits, `= true` | alte Instanz beendet, eigene gestartet | ✅ verifiziert |
| 29 | Windows | nichts belegt, `LogDirectory` leer | StockTV startet ohne Konsolenfenster | ✅ verifiziert |
| 30 | Windows | StockTV liefert unter der URL, `StopExistingInstance = true` | kein eigener Start, Bestand unangetastet | ✅ verifiziert |
| 31 | Windows | nichts laeuft | Fenster erscheinen vor den Vorpruefungen, StockTV startet danach | ✅ verifiziert |
| 32 | Windows | zweite Installation haelt 4747/4748, `StopExistingInstance = true` | fremder Prozess bleibt am Leben, Pfad im Log, kein Start | ✅ verifiziert |
| 33 | Windows | `Environment` leer | `ASPNETCORE_URLS` aus `PrimaryUrl` abgeleitet, StockTV lauscht auf 5129 | ✅ verifiziert |
| 34 | Windows | `GET /api/config` | Datei als Download, `AppConfig.json` | ✅ verifiziert |
| 35 | Windows | `PUT /api/config` mit kaputter Datei | 400 mit Fundstelle, Datei auf dem Geraet unveraendert | ✅ verifiziert |
| 36 | Windows | `PUT /api/config` mit gueltiger Datei | uebernommen, `.bak` angelegt | ✅ verifiziert |
| 37 | Windows | `POST /api/config/default`, danach Neustart | Standardwerte, aber Schluessel gilt weiter | ✅ verifiziert |

Automatisiert abgedeckt sind alle Layout-Entscheidungen aus #1–#4, #6, die
Anzeigemodus-Erkennung, die Befehlsermittlung fuer #19–#24 auf beiden Plattformen sowie
die Startentscheidung aus #26–#28, #30, #32 und #33 — 91 Unit-Tests in
`tests/StockTvKiosk.Core.Tests`.

**Verifiziert am 26.08.2026** unter Windows 11 (10.0.26200, x64) mit zwei Monitoren
(`2560x1440` primaer, `1920x1200` links davon bei X = −1920) gegen das laufende
`StockTvBlazor` auf Port 5129. Die systemd-Units und Installationsskripte wurden in einem
Debian-Bookworm-Container gegen systemd 252 geprueft — dieselbe Version wie auf
Raspberry Pi OS Bookworm. **Nicht** geprueft ist der Lauf auf echter Pi-Hardware (#18).

**REST-Schnittstelle verifiziert am 27.08.2026** auf demselben Geraet gegen den laufenden
Kiosk (#19–#21). Das Umschalten selbst (#22–#24) ist noch offen: es aendert die
Bildschirmanordnung des ganzen Rechners, gehoert also auf ein Testgeraet und nicht auf
einen Arbeitsplatz.

**Startentscheidung verifiziert am 28.08.2026** (#26–#33) gegen das echte
`StockTvBlazor`: mit einem Fremdprozess auf Port 4747 startet der Kiosk nichts — weder mit
`StopExistingInstance = false` noch mit `true`, und der Fremdprozess ueberlebt beides. Mit
einer echten Vorinstanz und `true` wird sie beendet und eine eigene gestartet. Liefert die
URL bereits (der Dienst-Fall), bleibt die Vorinstanz auch mit `true` unangetastet. Der
gestartete Prozess hat auch ohne Ausgabeumleitung kein Fenster (`MainWindowHandle = 0`),
und die Fenster erscheinen rund 3 s vor dem Start von StockTV — die Vorpruefungen halten
die Anzeige also nicht auf.

## Logging

Konsole und Datei (`_logs/kiosk.log`, rotierend, 5 MB, 3 Altdateien). Protokolliert
werden erkannte Bildschirme, Anzeigemodus samt Erkennungsquelle, der Fensterplan mit
Zielbildschirm und URL, jeder Verbindungsversuch sowie Ladefehler.

Unter Windows haengt sich der Kiosk an eine vorhandene Konsole an, ohne selbst eine zu
oeffnen: aus einem Terminal gestartet erscheinen die Logs dort, per Doppelklick gestartet
stoert kein Konsolenfenster.
