# StockTV-Verteilpaket

Ein Ordner, der beide Anwendungen verteilfertig enthaelt: **StockTV** (die Punkteanzeige)
und den **StockTV-Kiosk** (die Vollbildanzeige davor). Er wird auf dem Entwicklungsrechner
gebaut, als Ganzes auf den Zielrechner kopiert und dort installiert.

```
C:\StockTV\
  win-x64\
    StockTV\               StockTvBlazor, self-contained
    Kiosk\                 StockTvKiosk, self-contained
  config\                  Konfigurationsvorlagen
  Install-StockTV.ps1      Installation auf dem Zielrechner
  Test-Installation.ps1    Pruefung ohne Seiteneffekte
  Install-KioskTask.ps1    Autostart des Kiosks (wird von der Installation aufgerufen)
  VERSION.txt              woraus das Paket gebaut wurde
```

Die Ebene `win-x64` ist Absicht: ein Zweig fuer den Raspberry Pi kommt spaeter als
`linux-arm64` daneben, ohne dass sich an Installation oder Pruefung etwas aendert.

---

## Paket bauen (Entwicklungsrechner)

```powershell
.\deploy\bundle\Publish-Bundle.ps1
```

Baut beide Anwendungen als self-contained `win-x64` nach `C:\StockTV` und legt Skripte,
Vorlagen und einen Versionsstempel dazu. Mit `-OutputRoot` geht auch ein anderer Ordner,
mit `-SkipStockTv` bzw. `-SkipKiosk` laesst sich eine Seite stehen lassen.

Das Paket ist rund 260 MB gross — beide Anwendungen bringen ihre eigene .NET-Laufzeit mit,
auf dem Zielrechner muss deshalb nichts vorinstalliert sein.

---

## Installieren (Zielrechner)

Den Ordner auf den Zielrechner kopieren — **nicht** nach `C:\StockTV`, sondern daneben, zum
Beispiel nach `C:\StockTV-Setup`. Nach `C:\StockTV` installiert das Skript naemlich; laege
das Paket am selben Ort, kaeme die Verteilung in sich selbst zu liegen. Das Skript bricht in
dem Fall mit einem Hinweis ab.

Dann PowerShell im Paketordner oeffnen:

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
| `-InstallDir` | `C:\StockTV` | StockTV; der Kiosk landet darunter in `\Kiosk` |
| `-Port` | `8080` | HTTP-Port von StockTV, wird in beide Konfigurationen eingesetzt |
| `-KioskApiPort` | `8099` | Port der Kiosk-Fernsteuerung |
| `-ApiKey` | *(erzeugt)* | Schluessel der Fernsteuerung |
| `-BahnNummer` | *(Vorlage)* | traegt die Bahnnummer in die StockTV-Konfiguration ein |
| `-NoKiosk` | | Dateien kopieren, aber keinen Autostart einrichten |
| `-OpenFirewall` | | eingehende Regeln fuer beide Ports (Administrator) |
| `-ResetConfig` | | vorhandene Konfiguration durch die Vorlagen ersetzen |
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
Zahl der Fehler. Geprueft werden Dateien und Versionen, beide Konfigurationsdateien,
Dienstzustand, der geplante Task und ob StockTV und die Fernsteuerung tatsaechlich antworten.

Die wichtigste Pruefung ist **„Kiosk-Adresse passt zu StockTV"**. Der haeufigste stille
Fehler dieser Installation ist eine Portabweichung: der Dienst hoert auf 8080, der Kiosk
laedt von 5129 (dem Standardwert seiner Konfiguration), und zu sehen ist nur die
Statusseite — kein Absturz, keine Fehlermeldung, nichts.

---

## Was wohin gehoert

| Datei | Ort auf dem Zielrechner |
|---|---|
| StockTV-Konfiguration | `C:\StockTV\_config\stocktv.config.json` |
| StockTV-Logs | `C:\StockTV\_logs\` |
| Kiosk-Konfiguration | `C:\StockTV\Kiosk\_config\AppConfig.json` |
| Kiosk-Logs | `C:\StockTV\Kiosk\_logs\` |

Beide Anwendungen lesen ihre Konfiguration aus einem `_config`-Ordner **neben der
Programmdatei**. Deshalb liegen sie in getrennten Verzeichnissen: nebeneinander wuerden
sich `_config`, `_logs` und die Laufzeitdateien der beiden self-contained Builds
gegenseitig ueberschreiben.

---

## Adressen nach der Installation

| | |
|---|---|
| Bedienung | `http://localhost:8080/turnier` |
| Zweite Anzeige | `http://localhost:8080/display2` |
| Aus dem Netz | `http://<rechnername>:8080` |
| Fernsteuerung des Kiosks | `http://<rechnername>:8099/swagger` |

Fuer den Zugriff aus dem Netz muss der Dienst auf allen Adressen hoeren — die Installation
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
