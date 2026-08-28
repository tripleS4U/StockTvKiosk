# StockTV-Verteilpaket

Ein Ordner, der beide Anwendungen verteilfertig enthaelt: **StockTV** (die Punkteanzeige)
und den **StockTV-Kiosk** (die Vollbildanzeige davor). Er wird auf dem Entwicklungsrechner
gebaut, als Ganzes auf den Zielrechner kopiert und dort installiert.

```
C:\StockTV-Install\          <- das Paket, auf den Zielrechner kopieren
  StockTvApps\
    StockTV\                 StockTvBlazor, self-contained
    StockTvKiosk\            StockTvKiosk, self-contained
  config\                    Konfigurationsvorlagen
  Install-StockTV.ps1        Installation
  Test-Installation.ps1      Pruefung ohne Seiteneffekte
  Install-KioskTask.ps1      Autostart des Kiosks (ruft die Installation auf)
  VERSION.txt                woraus das Paket gebaut wurde

C:\StockTV\                  <- die Installation
  StockTvApps\
    StockTV\                 + _config\stocktv.config.json, _logs\
    StockTvKiosk\            + _config\AppConfig.json, _logs\
```

Paket und Installation sind bewusst **zwei** Ordner. Laege das Paket in der Installation,
kaemen 260 MB doppelt zu liegen, und spaeter waere nicht mehr zu erkennen, was wovon ist.
Die Installation bricht in dem Fall mit einem Hinweis ab.

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

### Die eine Ausnahme: WebView2

Eine Abhaengigkeit deckt self-contained nicht ab — die **WebView2-Runtime**. Sie ist ein
Windows-Systempaket, kein .NET-Bestandteil. Unter Windows 11 ist sie vorinstalliert, unter
Windows 10 kann sie fehlen; dann startet der Kiosk und zeigt eine leere Flaeche, ohne
Absturz und ohne Fehlermeldung.

Das Paket bringt deshalb einen Installer mit, und `Install-StockTV.ps1` fuehrt ihn aus,
wenn die Runtime fehlt:

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

`Test-Installation.ps1` prueft ihr Vorhandensein ausdruecklich.

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

Fuer den Raspberry Pi spaeter:

```powershell
.\Publish-Bundle.ps1 -Runtime linux-arm64 -OutputRoot C:\StockTV-Install-linux-arm64
```

---

## Installieren (Zielrechner)

Ordner kopieren, PowerShell darin oeffnen, und:

```powershell
.\Install-StockTV.ps1
```

Vorher ansehen, was geschehen wuerde:

```powershell
.\Install-StockTV.ps1 -WhatIfOnly
```

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

```powershell
# Erstinstallation mit Bahnnummer und Firewall-Freigabe
.\Install-StockTV.ps1 -BahnNummer 3 -OpenFirewall

# Ohne Administratorrechte
.\Install-StockTV.ps1 -Mode Managed

# Update: Programmdateien neu, Konfiguration bleibt wie sie ist
.\Install-StockTV.ps1

# Update inklusive Konfiguration auf die Vorlagen zuruecksetzen
.\Install-StockTV.ps1 -ResetConfig
```

| Parameter | Vorgabe | Bedeutung |
|---|---|---|
| `-Mode` | `Service` | siehe oben |
| `-InstallDir` | `C:\StockTV` | beide Anwendungen landen darunter in `StockTvApps\` |
| `-Port` | `8080` | HTTP-Port von StockTV, wird in beide Konfigurationen eingesetzt |
| `-KioskApiPort` | `8099` | Port der Kiosk-Fernsteuerung |
| `-ApiKey` | *(erzeugt)* | Schluessel der Fernsteuerung |
| `-BahnNummer` | *(Vorlage)* | traegt die Bahnnummer in die StockTV-Konfiguration ein |
| `-NoKiosk` | | Dateien kopieren, aber keinen Autostart einrichten |
| `-OpenFirewall` | | eingehende Regeln fuer beide Ports (Administrator) |
| `-ResetConfig` | | vorhandene Konfiguration durch die Vorlagen ersetzen |
| `-SkipWebView2` | | die WebView2-Runtime nicht anfassen, auch wenn sie fehlt |
| `-WhatIfOnly` | | nur anzeigen |

> **Vorhandene Konfigurationsdateien werden nicht ueberschrieben.** Ein Update laesst
> Bahnnummer, Farben und den API-Schluessel damit stehen. Genau deshalb wird der Schluessel
> auch nur bei der Erstinstallation genannt — danach steht er verschluesselt in der Datei
> und ist nicht mehr ablesbar.

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

## Adressen nach der Installation

| | |
|---|---|
| Bedienung | `http://localhost:8080/turnier` |
| Zweite Anzeige | `http://localhost:8080/display2` |
| Aus dem Netz | `http://<rechnername>:8080` |
| Fernsteuerung des Kiosks | `http://<rechnername>:8099/swagger` |

Fuer den Zugriff aus dem Netz muss StockTV auf allen Adressen hoeren — die Installation
setzt dafuer `ASPNETCORE_URLS=http://+:<port>`. In der Betriebsart `Managed` steht derselbe
Wert in der Kiosk-Konfiguration unter `BlazorHost > Environment`; ohne ihn wuerde der Kiosk
`http://localhost:<port>` aus der Primaer-URL ableiten und StockTV waere nur auf dem Geraet
selbst erreichbar.

---

## Deinstallieren

```powershell
.\Uninstall-KioskTask.ps1                          # Autostart des Kiosks
Stop-Service StockTV; sc.exe delete StockTV        # Dienst (Administrator)
Remove-Item C:\StockTV -Recurse -Force             # Dateien samt Konfiguration
```
