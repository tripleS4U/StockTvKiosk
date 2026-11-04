# StockTV-Verteilpaket

Ein Ordner, der beide Anwendungen verteilfertig enthaelt: **StockTV** (die Punkteanzeige)
und den **StockTV-Kiosk** (die Vollbildanzeige davor). Er wird auf dem Entwicklungsrechner
gebaut, als Ganzes auf den Zielrechner kopiert und dort installiert.

Es gibt zwei Pakete — eines je Zielplattform. Der Aufbau ist derselbe, nur die Skripte
unterscheiden sich.

```
C:\StockTV-Install\          <- Windows, auf den Zielrechner kopieren
  StockTvApps\
    StockTV\                 StockTvBlazor, self-contained win-x64
    StockTvKiosk\            StockTvKiosk, self-contained win-x64
  config\                    Konfigurationsvorlagen
  WebView2\                  Installer der WebView2-Runtime
  Install-StockTV.ps1        Installation
  Test-Installation.ps1      Pruefung ohne Seiteneffekte
  Install-KioskTask.ps1      Autostart des Kiosks (ruft die Installation auf)
  VERSION.txt                woraus das Paket gebaut wurde

C:\StockTvApps\              <- die Installation unter Windows
  StockTV\                   + _config\stocktv.config.json, _logs\
  StockTvKiosk\              + _config\AppConfig.json, _logs\
```

```
C:\StockTV-Install-linux-arm64\   <- Raspberry Pi, per scp/rsync aufs Geraet
  StockTvApps\
    StockTV\                      self-contained linux-arm64
    StockTvKiosk\                 self-contained linux-arm64
  config\                         dieselben Vorlagen, dazu die *.linux.json
  units/                          systemd-Vorlagen
  install-stocktv.sh              Installation
  test-installation.sh            Pruefung ohne Seiteneffekte
  VERSION.txt

/opt/stocktvapps/                 <- die Installation auf dem Pi
  StockTV/                        + _config/stocktv.config.json, _logs/
  StockTvKiosk/                   + _config/AppConfig.json, _logs/
```

Der Ordner `StockTvApps` im Paket ist genau das, was die Installation nach `C:\` legt —
Paket und Installation sind damit deckungsgleich aufgebaut.

Paket und Installation sind bewusst **zwei** Ordner. Laege das Paket in der Installation,
kaemen 260 MB doppelt zu liegen, und spaeter waere nicht mehr zu erkennen, was wovon ist.
Die Installation bricht in dem Fall mit einem Hinweis ab.

Die beiden Anwendungen brauchen getrennte Verzeichnisse: jede liest ihre Konfiguration aus
einem `_config` **neben der Programmdatei**, und die Laufzeitdateien der beiden
self-contained Builds wuerden sich in einem gemeinsamen Ordner gegenseitig ueberschreiben.

---

## Warum self-contained

Beide Anwendungen werden mit `--runtime win-x64 --self-contained` gebaut. Damit muss auf
dem Zielrechner **kein .NET installiert sein**, und es gibt keinen `runtimes`-Ordner: ein
Publish ohne Zielplattform legt die nativen Bibliotheken fuer 17 Plattformen ab
(`linux-arm`, `osx`, `win-x86`, …), von denen auf einem Windows-Rechner 16 totes Gewicht
sind — beim Kiosk waren das 552 MB von 604 MB.

| | |
|---|---|
| Publish ohne Zielplattform | 604 MB |
| self-contained `win-x64` | **135 MB**, alles flach im Ordner |

### Die eine Ausnahme: die Web-Engine

Der WebView nutzt die **native Engine der Plattform** — unter Windows WebView2, unter Linux
WebKitGTK. Beide sind Systempakete und kein .NET-Bestandteil; sie sind das Einzige, was ein
self-contained Build nicht mitbringt. Fehlen sie, startet der Kiosk und zeigt eine leere
Flaeche, ohne Absturz und ohne Fehlermeldung.

Unter Linux uebernimmt das die Paketverwaltung: `install-stocktv.sh` prueft
`libgtk-3-0`, `libwebkit2gtk-4.1-0` und `libsoup-3.0-0` und installiert sie bei Bedarf per
`apt`. Unter Windows liegt ein Installer im Paket:

#### WebView2 (nur Windows)

Unter Windows 11 ist die Runtime vorinstalliert, unter Windows 10 kann sie fehlen.
`Install-StockTV.ps1` fuehrt den beiliegenden Installer aus, wenn sie fehlt:

| `-WebView2` | im Paket | Zielrechner braucht Internet |
|---|---|---|
| `Bootstrapper` (Vorgabe) | ~2 MB | **ja**, beim Installieren |
| `Standalone` | ~150 MB | nein |
| `None` | — | — |

Fuer Hallen ohne Netz also `-WebView2 Standalone`. Ohne Administratorrechte installiert
WebView2 von sich aus benutzerbezogen — das genuegt, weil der Kiosk unter demselben
Benutzer laeuft.

Eine dritte Moeglichkeit waere die **Fixed Version**: WebView2 liegt dann im
Anwendungsordner, ohne Installation und ohne Adminrechte. Sie ist hier bewusst nicht
vorgesehen, denn sie bekommt **keine Sicherheitsupdates** — auf einem Geraet, das jahrelang
in einer Halle steht und eine Weboberflaeche rendert, ist das der schlechtere Tausch. Die
beiden Installer oben tragen die Runtime ins System ein, wo Edge sie selbst aktuell haelt.

Beide Pruefskripte melden eine fehlende Engine ausdruecklich als Fehler.

---

## Paket bauen (Entwicklungsrechner)

```powershell
.\deploy\bundle\Publish-Bundle.ps1
```

Baut beide Anwendungen nach `C:\StockTV-Install` und legt Skripte, Vorlagen und einen
Versionsstempel dazu.

| Parameter | Vorgabe | Bedeutung |
|---|---|---|
| `-OutputRoot` | `C:\StockTV-Install` | Zielordner des Pakets |
| `-Runtime` | `win-x64` | Zielplattform |
| `-StockTvRepo` | Nachbarordner | Wurzel des StockTV-Repositorys |
| `-WebView2` | `Bootstrapper` | welcher WebView2-Installer beiliegt, siehe oben |
| `-SkipStockTv` / `-SkipKiosk` | | eine Seite stehen lassen |
| `-PackageVersion` | leer | Paketversion fuer `VERSION.txt` (setzt die Build-Pipeline) |

Fuer den Raspberry Pi:

```powershell
.\Publish-Bundle.ps1 -Runtime linux-arm64 -OutputRoot C:\StockTV-Install-linux-arm64
```

Bewusst ein eigenes Paket statt zwei Plattformen in einem: sonst kopierte jedes Zielgeraet
den Ballast der jeweils anderen mit. Welche Skripte beiliegen, richtet sich nach `-Runtime`
— Windows bekommt die `.ps1`-Dateien und den WebView2-Installer, Linux die `.sh`-Dateien
und die systemd-Vorlagen.

---

## Paket bauen (GitHub Actions)

Dasselbe Skript laeuft auch in der Pipeline
[`.github/workflows/bundle.yml`](../../.github/workflows/bundle.yml). Sie checkt dieses
Repository und StockTV nebeneinander aus und baut alle drei Pakete gleichzeitig:

| Datei | Ziel |
|---|---|
| `stocktv-bundle-<version>-win-x64.zip` | Windows x64 |
| `stocktv-bundle-<version>-linux-arm64.tar.gz` | Raspberry Pi 4/5, 64-bit Raspberry Pi OS |
| `stocktv-bundle-<version>-linux-x64.tar.gz` | Linux-PC x64 |

Die Linux-Pakete sind `tar.gz` statt ZIP, weil nur so die Ausfuehrungsrechte mitkommen —
`chmod +x` entfaellt auf dem Geraet. Entpackt wird mit `tar xzf <datei>`.

| Ausloeser | Ergebnis | StockTV-Stand |
|---|---|---|
| Push auf Branch `preview` | Pre-Release **`preview-latest`**, wird bei jedem Lauf ersetzt | `develop` |
| Tag `v*` pushen (z. B. `v1.2.0`) | Release zum Tag | `main` |
| *Run workflow* von einem anderen Branch | nur Artefakte am Lauf (7 Tage) | `develop` |

Bei *Run workflow* (Actions → Verteilpaket) laesst sich im Feld `stocktv_ref` ein anderer
StockTV-Branch, -Tag oder -Commit eintragen, z. B. `feature/rest-api-security` oder
`v1.9.1`. Die Vorgaben `develop`/`main` lassen sich ohne Commit ueber Repo-Variablen
umstellen (Settings → Secrets and variables → Actions → Variables):

| Variable | Vorgabe |
|---|---|
| `STOCKTV_PREVIEW_REF` | `develop` |
| `STOCKTV_RELEASE_REF` | `main` |
| `STOCKTV_REPOSITORY` | `Trawacho/StockTV` |

Der StockTV-Stand wird zu Beginn **einmal** auf einen Commit festgelegt, damit alle drei
Plattformen denselben bekommen. Welcher es war, steht in den Release-Notes und in
`VERSION.txt` im Paket — wichtig bei `main`, das sich mit jedem StockTV-Release bewegt.

Vorher laufen die Kiosk-Tests; schlagen sie fehl, entsteht kein Paket. StockTV testet sich
in seinem eigenen Repository.

Typischer Ablauf:

```powershell
# Preview: aktuellen Stand testen
# (preview ist nur ein Ausloeser; zeigte er zuletzt auf einen anderen Branch: --force)
git push origin main:refs/heads/preview   # oder ein Feature-Branch: feature/x:refs/heads/preview

# Release
git tag v1.2.0
git push origin v1.2.0
```

---

## Installieren auf dem Raspberry Pi

```bash
scp -r C:\StockTV-Install-linux-arm64 pi@kiosk:~/stocktv-install     # vom Windows-Rechner
```

```bash
cd ~/stocktv-install
chmod +x install-stocktv.sh test-installation.sh
./install-stocktv.sh --bahn 3
./test-installation.sh
```

Das Ausfuehrbarkeitsbit muss von Hand gesetzt werden: es geht verloren, wenn das Paket ueber
ein Windows-Dateisystem, SMB oder ein ZIP kommt. Die beiden Programmdateien setzt das
Installationsskript selbst.

| Option | Vorgabe | Bedeutung |
|---|---|---|
| `--mode service\|managed` | `service` | wie unter Windows |
| `--install-dir <Pfad>` | `/opt/stocktvapps` | Zielverzeichnis |
| `--port <N>` | `8080` | HTTP-Port von StockTV |
| `--api-port <N>` | `8099` | Port der Kiosk-Fernsteuerung |
| `--api-key <Wert>` | `Secret4StockTV?` | Schluessel der Fernsteuerung |
| `--generate-api-key` | | stattdessen einen zufaelligen erzeugen |
| `--bahn <N>` | *(Vorlage)* | Bahnnummer |
| `--display <:N>` | `:0` | X-Display fuer den Kiosk |
| `--no-kiosk` | | keine Kiosk-Unit einrichten |
| `--reset-config` | | Konfiguration auf die Vorlagen zuruecksetzen |
| `--skip-deps` | | WebKitGTK nicht pruefen |
| `--skip-firewall` | | `ufw` nicht anfassen |
| `--dry-run` | | nur anzeigen |

Eingerichtet werden **systemd-User-Units** (`~/.config/systemd/user/`) — der Kiosk braucht
ohnehin die grafische Sitzung des angemeldeten Benutzers. `sudo` kommt nur an drei Stellen
zum Einsatz: wenn das Zielverzeichnis dem Benutzer nicht gehoert (`/opt`), zum
Nachinstallieren von WebKitGTK, und fuer `ufw`. Installiert man nach `~/`, fragt gar nichts
nach dem Passwort.

> **X11 statt Wayland.** Auf Raspberry Pi OS Bookworm laeuft der Desktop standardmaessig
> unter Wayland; dort bestimmt der Compositor die Fensterposition, und die Zuordnung auf
> zwei Bildschirme ist nicht garantiert. Umstellen mit `raspi-config` → *Advanced Options*
> → *Wayland* → *X11*.

---

## Installieren (Zielrechner)

Ordner kopieren, dann als **Administrator**:

```
Install-StockTV.cmd
```

Vorher ansehen, was geschehen wuerde:

```
Install-StockTV.cmd -WhatIfOnly
```

Der `.cmd`-Starter ruft das PowerShell-Skript mit `-ExecutionPolicy Bypass` auf. Damit
entfaellt das sonst noetige

```powershell
Set-ExecutionPolicy RemoteSigned
```

— eine systemweite Aenderung fuer eine einmalige Installation. Der Umweg erledigt nebenbei
die Kennzeichnung „aus dem Internet", die Dateien mitbringen, wenn das Paket ueber ein
Netzlaufwerk oder ein ZIP kam; ohne sie verweigert selbst `RemoteSigned` den Dienst.

Wer die Policy ohnehin gesetzt hat, kann `.\Install-StockTV.ps1` direkt aufrufen — die
Parameter sind dieselben.

### Warum Administrator

| | braucht Administrator |
|---|---|
| Windows-Dienst anlegen (`-Mode Service`) | ja |
| **Firewall-Regeln** | ja |
| Dateien nach `C:\StockTvApps` | nein |
| Geplanter Task fuer den Kiosk | nein |

Ohne Administratorrechte laeuft `-Mode Managed` vollstaendig durch, nur die Firewall bleibt
offen — und dann fragt Windows beim ersten Start **jeder der beiden Anwendungen** nach.
Dieser Dialog erscheint mitten auf dem Kiosk-Bildschirm, und bis ihn jemand wegklickt kommt
aus dem Netz niemand an die Bedienoberflaeche.

### Zwei Betriebsarten

| | `-Mode Service` (Vorgabe) | `-Mode Managed` |
|---|---|---|
| StockTV startet | als Windows-Dienst | der Kiosk startet es mit |
| Administrator noetig | ja | nein |
| Kiosk-Neustart | laesst die Wertung laufen | nimmt StockTV mit |
| Autostart-Einheiten | zwei | eine |

`Service` ist die Empfehlung fuer den Turnierbetrieb: **Shift+Esc** oder ein Absturz des
Kiosks kostet dann nur die Anzeige, nicht die laufende Wertung. `Managed` ist einfacher,
wenn niemand Administratorrechte auf dem Geraet hat.

### Haeufige Aufrufe

```
:: Erstinstallation mit Bahnnummer (Dienst, Task und Firewall inbegriffen)
Install-StockTV.cmd -BahnNummer 3

:: Ohne Administratorrechte
Install-StockTV.cmd -Mode Managed

:: Update: Programmdateien neu, Konfiguration bleibt wie sie ist
Install-StockTV.cmd

:: Update inklusive Konfiguration auf die Vorlagen zuruecksetzen
Install-StockTV.cmd -ResetConfig
```

| Parameter | Vorgabe | Bedeutung |
|---|---|---|
| `-Mode` | `Service` | siehe oben |
| `-InstallDir` | `C:\StockTvApps` | die beiden Anwendungen landen darunter in `StockTV\` und `StockTvKiosk\` |
| `-Port` | `8080` | HTTP-Port von StockTV, wird in beide Konfigurationen eingesetzt |
| `-KioskApiPort` | `8099` | Port der Kiosk-Fernsteuerung |
| `-ApiKey` | `Secret4StockTV?` | Schluessel der Fernsteuerung |
| `-GenerateApiKey` | | stattdessen einen zufaelligen erzeugen |
| `-BahnNummer` | *(Vorlage)* | traegt die Bahnnummer in die StockTV-Konfiguration ein |
| `-NoKiosk` | | Dateien kopieren, aber keinen Autostart einrichten |
| `-SkipFirewall` | | keine Firewall-Regeln anlegen (sonst automatisch, sofern Administrator) |
| `-ResetConfig` | | vorhandene Konfiguration durch die Vorlagen ersetzen |
| `-SkipWebView2` | | die WebView2-Runtime nicht anfassen, auch wenn sie fehlt |
| `-WhatIfOnly` | | nur anzeigen |

> **Vorhandene Konfigurationsdateien werden nicht ueberschrieben.** Ein Update laesst
> Bahnnummer, Farben und den API-Schluessel damit stehen.

### Der API-Schluessel

Vorbelegt mit **`Secret4StockTV?`**, auf allen so eingerichteten Geraeten derselbe. Das ist
Absicht — niemand muss pro Geraet einen Wert nachhalten. Der Preis: wer ihn von einem Geraet
kennt, kommt auf alle, und ueber `PUT /api/config` liesse sich dort ein beliebiger Befehl
hinterlegen. Er ist damit eine Huerde gegen versehentliche Zugriffe, kein Geraeteschutz. In
einem abgeschotteten Hallennetz vertretbar, in einem geteilten nicht.

Je Geraet einen eigenen Schluessel:

```
Install-StockTV.cmd -GenerateApiKey          :: Windows
./install-stocktv.sh --generate-api-key      #  Linux
```

Oder nachtraeglich im laufenden Betrieb, ohne Neustart:

```bash
curl -X POST -H "X-Api-Key: Secret4StockTV?" -H "Content-Type: application/json" \
     -d '{"newKey":"neuer-schluessel"}' http://kiosk:8099/api/config/api-key
```

Der neue gilt ab der Antwort, der bisherige ist damit ungueltig.

---

## Pruefen

```powershell
.\Test-Installation.ps1
.\Test-Installation.ps1 -ApiKey "der-schluessel"
```

Veraendert nichts und meldet je Pruefung `OK`, `HINWEIS` oder `FEHLER`; der Exitcode ist die
Zahl der Fehler. Geprueft werden Dateien und Versionen, die WebView2-Runtime, beide
Konfigurationsdateien, der Dienstzustand, der geplante Task und ob StockTV und die
Fernsteuerung tatsaechlich antworten.

Die wichtigste Pruefung ist **„Kiosk-Adresse passt zu StockTV"**. Der haeufigste stille
Fehler dieser Installation ist eine Portabweichung: StockTV lauscht auf 8080, der Kiosk
laedt von 5129 (dem Standardwert seiner Konfiguration), und zu sehen ist nur die
Statusseite — kein Absturz, keine Fehlermeldung, nichts. Verglichen wird gegen die Stelle,
die im jeweiligen Betrieb tatsaechlich bestimmt, worauf StockTV hoert: im Dienstbetrieb
`ASPNETCORE_URLS` des Dienstes, im Managed-Betrieb derselbe Schluessel in der
Kiosk-Konfiguration.

---

## Adressen und Ports

| | |
|---|---|
| Bedienfenster (Intro) | `http://localhost:8080/` |
| Zweite Anzeige | `http://localhost:8080/display2` |
| Aus dem Netz | `http://<rechnername>:8080` |
| Fernsteuerung des Kiosks | `http://<rechnername>:8099/swagger` |

Belegt werden:

| Port | Protokoll | Wofuer |
|---|---|---|
| `8080` | TCP | Bedienoberflaeche von StockTV (`-Port`) |
| `4747`, `4748` | TCP | NetMQ zu den Anzeigegeraeten |
| `5353` | UDP | Geraetesuche im Netz (mDNS) |
| `8099` | TCP | Fernsteuerung des Kiosks (`-KioskApiPort`) |

Die Installation gibt sie frei — unter Windows als Firewall-Regeln (fuer die beiden
Anwendungen und fuer die Ports), unter Linux per `ufw`, sofern der aktiv ist. Raspberry Pi
OS bringt von Haus aus keine eingeschaltete Firewall mit; dann ist nichts zu tun.

Fuer den Zugriff aus dem Netz muss StockTV auf allen Adressen hoeren — die Installation
setzt dafuer `ASPNETCORE_URLS=http://+:<port>`. In der Betriebsart `Managed` steht derselbe
Wert in der Kiosk-Konfiguration unter `BlazorHost > Environment`; ohne ihn wuerde der Kiosk
`http://localhost:<port>` aus der Primaer-URL ableiten und StockTV waere nur auf dem Geraet
selbst erreichbar.

---

## Deinstallieren

Windows:

```powershell
.\Uninstall-KioskTask.ps1                          # Autostart des Kiosks
Stop-Service StockTV; sc.exe delete StockTV        # Dienst (Administrator)
Remove-Item C:\StockTvApps -Recurse -Force         # Dateien samt Konfiguration
```

Raspberry Pi:

```bash
systemctl --user disable --now stocktv-kiosk.service stocktv.service
rm -f ~/.config/systemd/user/stocktv*.service && systemctl --user daemon-reload
sudo rm -rf /opt/stocktvapps
```
