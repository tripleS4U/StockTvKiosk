<#
.SYNOPSIS
    Installiert StockTV und den StockTV-Kiosk auf einem Windows-Rechner.

.DESCRIPTION
    Laeuft auf dem Zielrechner, im Ordner des kopierten Verteilpakets.

    Zwei Betriebsarten:

      -Mode Service   StockTV laeuft als Windows-Dienst auf dem angegebenen Port. Der Kiosk
                      startet es nicht selbst, sondern wartet nur auf die Adresse. Ein
                      Neustart des Kiosks laesst die laufende Wertung damit unangetastet.
                      Braucht Administratorrechte fuer den Dienst.

      -Mode Managed   Kein Dienst. Der Kiosk startet StockTV selbst und faehrt es beim
                      Beenden mit herunter - eine einzige Autostart-Einheit. Kommt ohne
                      Administratorrechte aus, solange die Firewall nicht angefasst wird.

    Vorhandene Konfigurationsdateien werden NICHT ueberschrieben. Ein Update laesst damit
    Bahnnummer, Farben und den API-Schluessel stehen. Mit -ResetConfig ausdruecklich neu.

    Installiert wird nach:
        C:\StockTvApps\StockTV\        StockTvBlazor + _config + _logs
        C:\StockTvApps\StockTvKiosk\   StockTvKiosk  + _config + _logs

.EXAMPLE
    .\Install-StockTV.ps1

    Erstinstallation mit den Vorgaben: StockTV als Windows-Dienst auf Port 8080 nach
    C:\StockTvApps\, Kiosk als geplanter Task. Braucht Administratorrechte.

.EXAMPLE
    .\Install-StockTV.ps1 -WhatIfOnly

    Zeigt nur, was geschehen wuerde - ohne irgendetwas zu veraendern.

.EXAMPLE
    .\Install-StockTV.ps1 -Mode Managed -BahnNummer 3

    Ohne Administratorrechte: kein Dienst, der Kiosk startet StockTV selbst. Die Bahnnummer
    wird in die StockTV-Konfiguration eingetragen.

.EXAMPLE
    .\Install-StockTV.ps1 -BahnNummer 5 -SkipFirewall

    Ohne Firewall-Regeln - etwa wenn eine Gruppenrichtlinie sie ohnehin verteilt. Sonst
    werden sie angelegt, sobald die Installation Administratorrechte hat.

.EXAMPLE
    .\Install-StockTV.ps1 -Port 5129 -ApiKey "eigener-schluessel"

    Abweichender HTTP-Port und ein selbst vergebener Schluessel statt eines erzeugten.

.EXAMPLE
    .\Install-StockTV.ps1 -ResetConfig

    Update, das auch die Konfigurationsdateien auf die Vorlagen zuruecksetzt. Ohne diesen
    Schalter bleiben sie unangetastet - dann behaelt das Geraet auch seinen bisherigen
    API-Schluessel.

.EXAMPLE
    .\Install-StockTV.ps1 -InstallDir D:\StockTvApps -SkipWebView2

    Anderes Zielverzeichnis, und die WebView2-Runtime wird nicht angefasst (etwa weil sie
    ueber die Softwareverteilung kommt).
#>
[CmdletBinding()]
param(
    [ValidateSet("Service", "Managed")]
    [string]$Mode = "Service",

    # Vorbelegung steht bewusst NICHT hier, siehe unten.
    [string]$SourceDir,

    [string]$InstallDir = "C:\StockTvApps",

    [ValidateRange(1, 65535)]
    [int]$Port = 8080,

    [ValidateRange(1, 65535)]
    [int]$KioskApiPort = 8099,

    # Schluessel der Kiosk-Fernsteuerung. Ohne Angabe wird einer erzeugt und am Ende genannt.
    [string]$ApiKey,

    # Traegt die Bahnnummer in die StockTV-Konfiguration ein. 0 = Vorlage unveraendert lassen.
    [int]$BahnNummer = 0,

    [string]$ServiceName = "StockTV",

    # Den Kiosk nicht als geplanten Task registrieren; die Dateien werden trotzdem kopiert.
    [switch]$NoKiosk,

    <#
        Die Firewall-Regeln werden von sich aus angelegt, sobald die Installation
        Administratorrechte hat - ohne sie fragt Windows beim ersten Start jeder Anwendung
        nach, mitten auf dem Kiosk-Bildschirm. Dieser Schalter laesst sie aus.
    #>
    [switch]$SkipFirewall,

    # Vorhandene Konfigurationsdateien durch die Vorlagen ersetzen.
    [switch]$ResetConfig,

    # Die WebView2-Runtime nicht anfassen, auch wenn sie fehlt.
    [switch]$SkipWebView2,

    [switch]$WhatIfOnly
)

$ErrorActionPreference = "Stop"

<#
    $SourceDir wird erst hier vorbelegt und nicht im param-Block. In einem Skript mit
    [CmdletBinding()] ist $PSScriptRoot als Parametervorgabe leer, wenn das Skript ueber
    "powershell.exe -File" laeuft - genau so ruft der .cmd-Starter es auf. Im Rumpf steht
    der Wert dagegen in jeder Aufrufart.
#>
if (-not $SourceDir) { $SourceDir = $PSScriptRoot }

# Beide Anwendungen bekommen ihr eigenes Verzeichnis unter InstallDir. Nebeneinander in
# einem Ordner ginge nicht: sie lesen ihre Konfiguration jeweils aus einem _config neben
# der Programmdatei, und die Laufzeitdateien der beiden self-contained Builds wuerden sich
# gegenseitig ueberschreiben.
$StockTvHome = Join-Path $InstallDir "StockTV"
$KioskDir    = Join-Path $InstallDir "StockTvKiosk"

$done     = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Step  { param([string]$Text) $done.Add($Text);     Write-Host "  $Text" }
function Warn  { param([string]$Text) $warnings.Add($Text); Write-Host "  ! $Text" -ForegroundColor Yellow }
function Title { param([string]$Text) Write-Host ""; Write-Host $Text -ForegroundColor Cyan }

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

<#
    JSON-gerechtes Escapen fuer die Werte, die in die Vorlagen eingesetzt werden.
    Betrifft in der Praxis nur Pfade mit ihren Backslashes.
#>
function ConvertTo-JsonValue {
    param([string]$Text)
    return $Text.Replace('\', '\\').Replace('"', '\"')
}

Write-Host "=========================================="
Write-Host "  StockTV-Installation"
Write-Host "  Betriebsart: $Mode"
Write-Host "  Ziel:        $InstallDir"
Write-Host "=========================================="

# ---------------------------------------------------------------------------------------
# 1. Paket und Rechte pruefen
# ---------------------------------------------------------------------------------------

Title "1. Verteilpaket und Rechte pruefen"

$stockTvSource  = Join-Path $SourceDir "StockTvApps\StockTV"
$kioskSource    = Join-Path $SourceDir "StockTvApps\StockTvKiosk"
$configSource   = Join-Path $SourceDir "config"
$stockTvExe     = Join-Path $StockTvHome "StockTvBlazor.exe"
$kioskExe       = Join-Path $KioskDir "StockTvKiosk.exe"
$stockTvConfig  = Join-Path $StockTvHome "_config\stocktv.config.json"
$kioskConfig    = Join-Path $KioskDir "_config\AppConfig.json"
$kioskTemplate  = Join-Path $configSource "AppConfig.$Mode.json"
$stockTvTemplate = Join-Path $configSource "stocktv.config.json"

foreach ($required in @(
    @{ Path = (Join-Path $stockTvSource "StockTvBlazor.exe"); Label = "StockTV" }
    @{ Path = (Join-Path $kioskSource   "StockTvKiosk.exe");  Label = "StockTV-Kiosk" }
    @{ Path = $stockTvTemplate;                               Label = "StockTV-Konfigurationsvorlage" }
    @{ Path = $kioskTemplate;                                 Label = "Kiosk-Konfigurationsvorlage" }
)) {
    if (-not (Test-Path $required.Path)) {
        throw "$($required.Label) fehlt im Paket: $($required.Path)"
    }
}

Step "Paket vollstaendig."

<#
    Verteilpaket und Installation duerfen nicht derselbe Ordner sein. Sonst kopierte die
    Installation die Anwendungen aus StockTvApps\ in genau das Verzeichnis, das den Ordner
    enthaelt - das laeuft zwar, laesst aber 260 MB doppelt liegen und macht spaeter
    niemandem klar, was davon die Installation ist und was das Paket.
#>
$sourceFull  = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\')
$installFull = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')

if ($sourceFull -eq $installFull -or $sourceFull.StartsWith($installFull + '\', 'OrdinalIgnoreCase')) {
    throw @"
Das Verteilpaket liegt in $sourceFull, installiert werden soll nach $installFull.
Das waere dasselbe Verzeichnis (oder eines darunter).

Das Paket gehoert daneben, nicht hinein - vorgesehen ist:
    Paket nach C:\StockTV-Install kopieren und dort
    .\Install-StockTV.ps1
laufen lassen; installiert wird dann nach C:\StockTvApps.

Alternativ mit -InstallDir ein anderes Zielverzeichnis angeben.
"@
}

$isAdmin = Test-Administrator

if ($Mode -eq "Service" -and -not $isAdmin) {
    throw "Fuer -Mode Service werden Administratorrechte gebraucht (Windows-Dienst). " +
          "PowerShell als Administrator starten oder -Mode Managed verwenden."
}
Step $(if ($isAdmin) { "Laeuft als Administrator." } else { "Laeuft ohne erhoehte Rechte - fuer $Mode ausreichend." })

if ($WhatIfOnly) {
    Write-Host ""
    Write-Host "  -WhatIfOnly: ab hier wird nichts veraendert." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------------------
# 2. Laufendes anhalten
# ---------------------------------------------------------------------------------------

Title "2. Laufende Anwendungen anhalten"

if ($WhatIfOnly) {
    Step "wuerde Dienst und laufende Prozesse anhalten"
} else {
    $service = Get-Service $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne "Stopped") {
        Stop-Service $ServiceName -Force
        # StockTV gibt seine NetMQ-Ports erst beim Beenden frei; ohne das Warten scheitert
        # der anschliessende Start am noch belegten Port.
        (Get-Service $ServiceName).WaitForStatus("Stopped", "00:00:30")
        Step "Dienst '$ServiceName' gestoppt."
    }

    foreach ($name in @("StockTvKiosk", "StockTvBlazor")) {
        $running = @(Get-Process $name -ErrorAction SilentlyContinue)
        if ($running.Count -gt 0) {
            $running | Stop-Process -Force
            Start-Sleep -Milliseconds 500
            Step "$name beendet ($($running.Count) Prozess(e))."
        }
    }
}

# ---------------------------------------------------------------------------------------
# 3. Dateien kopieren
# ---------------------------------------------------------------------------------------

Title "3. Dateien kopieren"

function Copy-Application {
    param([string]$From, [string]$To, [string]$Label)

    if ($WhatIfOnly) { Step "wuerde $Label nach $To kopieren"; return }

    New-Item -ItemType Directory -Path $To -Force | Out-Null

    # _config und _logs bleiben aussen vor: eine vorhandene Konfiguration gehoert dem Geraet
    # und darf ein Update nicht verlieren, die Logs ebenso wenig.
    Get-ChildItem $From -Force |
        Where-Object { $_.Name -notin @("_config", "_logs") } |
        ForEach-Object { Copy-Item $_.FullName -Destination $To -Recurse -Force }

    foreach ($sub in @("_config", "_logs")) {
        New-Item -ItemType Directory -Path (Join-Path $To $sub) -Force | Out-Null
    }

    # Gezaehlt wird die Quelle: das Ziel enthaelt nach einem Update auch _config und _logs,
    # die nicht aus diesem Kopiervorgang stammen.
    Step "$Label nach $To ($((Get-ChildItem $From -Recurse -File).Count) Dateien)."
}

Copy-Application $stockTvSource $StockTvHome   "StockTV"
Copy-Application $kioskSource   $KioskDir      "StockTV-Kiosk"

# Der Task-Installer wird mitgenommen, damit sich der Autostart spaeter auch ohne das
# vollstaendige Paket wieder einrichten laesst.
foreach ($helper in @("Install-KioskTask.ps1", "Uninstall-KioskTask.ps1", "Test-Installation.ps1")) {
    $source = Join-Path $SourceDir $helper
    if ((Test-Path $source) -and -not $WhatIfOnly) {
        Copy-Item $source $InstallDir -Force
    }
}

# ---------------------------------------------------------------------------------------
# 4. Konfiguration
# ---------------------------------------------------------------------------------------

Title "4. Konfiguration"

$generatedKey = $false
if (-not $ApiKey) {
    # Bewusst ueber Create() und die Instanzmethode: das statische GetBytes(int) gibt es
    # erst ab .NET 6, und dieses Skript laeuft ueblicherweise in Windows PowerShell 5.1
    # auf dem .NET Framework.
    $bytes = New-Object byte[] 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }

    $ApiKey = [Convert]::ToBase64String($bytes)
    $generatedKey = $true
}

function Install-ConfigFile {
    param(
        [string]$Template,
        [string]$Target,
        [string]$Label,
        [scriptblock]$Transform
    )

    if ((Test-Path $Target) -and -not $ResetConfig) {
        Step "$Label bleibt unveraendert (schon vorhanden) - mit -ResetConfig ersetzen."
        return $false
    }

    if ($WhatIfOnly) { Step "wuerde $Label schreiben: $Target"; return $true }

    $content = Get-Content $Template -Raw
    if ($Transform) { $content = & $Transform $content }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Target) -Force | Out-Null
    Set-Content -Path $Target -Value $content -Encoding utf8

    # Sofort gegenlesen: eine unlesbare Konfiguration faellt sonst erst beim ersten Start
    # auf, und dort nur als Anwendung, die mit Standardwerten hochkommt.
    try { $null = Get-Content $Target -Raw | ConvertFrom-Json }
    catch { throw "$Label wurde geschrieben, ist aber kein gueltiges JSON: $($_.Exception.Message)" }

    Step "$Label geschrieben: $Target"
    return $true
}

$stockTvWritten = Install-ConfigFile -Template $stockTvTemplate -Target $stockTvConfig `
    -Label "StockTV-Konfiguration" -Transform {
        param([string]$text)
        if ($BahnNummer -gt 0) {
            $text = $text -replace '"BahnNummer"\s*:\s*\d+', "`"BahnNummer`": $BahnNummer"
        }
        return $text
    }

$kioskWritten = Install-ConfigFile -Template $kioskTemplate -Target $kioskConfig `
    -Label "Kiosk-Konfiguration ($Mode)" -Transform {
        param([string]$text)

        # Die Vorlagen sind auf 8080 und C:\StockTV geschrieben; hier wird beides auf die
        # tatsaechlichen Werte gezogen. PrimaryUrl ist dabei die einzige Stelle fuer die
        # Adresse - ASPNETCORE_URLS leitet der Kiosk davon ab.
        $text = $text.Replace('http://localhost:8080/', "http://localhost:$Port/")
        $text = $text.Replace('http://+:8080', "http://+:$Port")
        $text = $text.Replace('"C:\\StockTvApps\\StockTV\\StockTvBlazor.exe"',
                              '"' + (ConvertTo-JsonValue $stockTvExe) + '"')
        $text = $text -replace '("RestApi"[\s\S]*?"Port"\s*:\s*)\d+', "`${1}$KioskApiPort"
        $text = $text.Replace('"ApiKey": ""', '"ApiKey": "' + (ConvertTo-JsonValue $ApiKey) + '"')
        return $text
    }

if (-not $kioskWritten -and $generatedKey) {
    $ApiKey = $null
    Warn "Die vorhandene Kiosk-Konfiguration behaelt ihren bisherigen API-Schluessel."
}

# ---------------------------------------------------------------------------------------
# 5. WebView2-Runtime
# ---------------------------------------------------------------------------------------

Title "5. WebView2-Runtime"

<#
    Das Einzige, was die self-contained Builds nicht mitbringen: ein Windows-Systempaket,
    kein .NET-Bestandteil. Fehlt es, startet der Kiosk und zeigt eine leere Flaeche - ohne
    Fehlermeldung, ohne Absturz. Unter Windows 11 ist es vorinstalliert.
#>
function Get-WebView2Version {
    $keys = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
        "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
        "HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
    )
    return $keys |
        ForEach-Object { (Get-ItemProperty $_ -Name pv -ErrorAction SilentlyContinue).pv } |
        Where-Object { $_ -and $_ -ne "0.0.0.0" } | Select-Object -First 1
}

$webView2Version = Get-WebView2Version

if ($webView2Version) {
    Step "Vorhanden, Version $webView2Version."
}
elseif ($SkipWebView2) {
    Warn "Fehlt, wird aber uebersprungen (-SkipWebView2). Der Kiosk zeigt ohne sie nichts an."
}
else {
    $installer = Get-ChildItem (Join-Path $SourceDir "WebView2") -Filter *.exe -ErrorAction SilentlyContinue |
                 Select-Object -First 1

    if (-not $installer) {
        Warn ("Fehlt, und im Paket liegt kein Installer (Ordner WebView2). Der Kiosk zeigt " +
              "ohne sie nichts an. Nachholen: Evergreen Runtime von " +
              "https://developer.microsoft.com/microsoft-edge/webview2 installieren, oder das " +
              "Paket mit -WebView2 Standalone neu bauen.")
    }
    elseif ($WhatIfOnly) {
        Step "wuerde $($installer.Name) still installieren"
    }
    else {
        Step "Fehlt - installiere $($installer.Name) ..."

        # Ohne erhoehte Rechte installiert WebView2 von sich aus benutzerbezogen. Das
        # genuegt, solange der Kiosk unter demselben Benutzer laeuft - und genau das tut er,
        # der geplante Task haengt an dessen Anmeldung.
        $process = Start-Process -FilePath $installer.FullName `
            -ArgumentList "/silent", "/install" -Wait -PassThru

        $webView2Version = Get-WebView2Version

        if ($webView2Version) {
            Step "Installiert, Version $webView2Version."
        }
        else {
            Warn ("Der Installer meldete Exitcode $($process.ExitCode), aber die Runtime ist " +
                  "danach nicht auffindbar. Der Kiosk zeigt ohne sie nichts an.")
        }
    }
}

# ---------------------------------------------------------------------------------------
# 6. Dienst
# ---------------------------------------------------------------------------------------

Title "6. StockTV"

$service = Get-Service $ServiceName -ErrorAction SilentlyContinue

if ($Mode -eq "Service") {
    if ($WhatIfOnly) {
        Step "wuerde den Dienst '$ServiceName' auf Port $Port einrichten und starten"
    }
    else {
        $binaryPath = "`"$stockTvExe`""

        if (-not $service) {
            New-Service -Name $ServiceName -BinaryPathName $binaryPath `
                -DisplayName "StockTV Punkteanzeige" `
                -Description "StockTV Blazor Server - Punkteanzeigesystem fuer den Stocksport" `
                -StartupType Automatic | Out-Null
            Step "Dienst '$ServiceName' angelegt."
        }
        else {
            # Der Pfad kann sich seit der letzten Installation geaendert haben.
            & sc.exe config $ServiceName binPath= $binaryPath start= auto | Out-Null
            Step "Dienst '$ServiceName' aktualisiert."
        }

        # http://+:Port statt localhost: die Bedienung laeuft ueber Handys und Tablets im
        # Netz, nicht nur ueber den Kiosk auf demselben Geraet.
        $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
        New-ItemProperty -Path $registryPath -Name "Environment" -PropertyType MultiString `
            -Value @("ASPNETCORE_URLS=http://+:$Port", "ASPNETCORE_ENVIRONMENT=Production") `
            -Force | Out-Null
        Step "Dienst hoert auf http://+:$Port."

        Start-Service $ServiceName
        (Get-Service $ServiceName).WaitForStatus("Running", "00:00:30")
        Step "Dienst gestartet."
    }
}
else {
    Step "Betriebsart Managed - kein Dienst; der Kiosk startet StockTV selbst."

    if ($service) {
        if ($WhatIfOnly) {
            Step "wuerde den vorhandenen Dienst '$ServiceName' anhalten und deaktivieren"
        }
        elseif ($isAdmin) {
            # Stehenlassen waere ein Wettlauf um die Ports: der Dienst startet mit Windows,
            # der Kiosk will dasselbe StockTV starten.
            Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
            Set-Service  $ServiceName -StartupType Disabled
            Warn ("Der vorhandene Dienst '$ServiceName' wurde angehalten und deaktiviert - " +
                  "sonst streiten sich Dienst und Kiosk um die Ports.")
        }
        else {
            Warn ("Es gibt einen Dienst '$ServiceName', der die Ports belegt. Ohne " +
                  "Administratorrechte laesst er sich nicht deaktivieren - der Kiosk wird " +
                  "dann nichts starten und nur auf die Adresse warten.")
        }
    }
}

# ---------------------------------------------------------------------------------------
# 7. Kiosk-Autostart
# ---------------------------------------------------------------------------------------

Title "7. Kiosk-Autostart"

if ($NoKiosk) {
    Step "Uebersprungen (-NoKiosk). Der Kiosk laesst sich von Hand starten: $kioskExe"
}
else {
    $taskInstaller = Join-Path $SourceDir "Install-KioskTask.ps1"

    if (-not (Test-Path $taskInstaller)) {
        Warn "Install-KioskTask.ps1 fehlt im Paket - der Autostart wurde nicht eingerichtet."
    }
    elseif ($WhatIfOnly) {
        Step "wuerde den geplanten Task ueber Install-KioskTask.ps1 registrieren"

        # Der Task-Installer prueft, ob die Programmdatei existiert - im Probelauf wurde aber
        # noch nichts kopiert. Dann wird sein eigener Probelauf mit der Datei aus dem Paket
        # gefuettert; er zeigt so dieselben Triggerdetails, ohne an der leeren Zieldatei zu
        # scheitern.
        $preview = if (Test-Path $kioskExe) { $kioskExe } else { Join-Path $kioskSource "StockTvKiosk.exe" }
        & $taskInstaller -KioskPath $preview -WhatIfOnly

        if ($preview -ne $kioskExe) {
            Step "(der Task zeigt spaeter auf $kioskExe)"
        }
    }
    else {
        # StockTV bekommt bewusst keinen eigenen Task: im Dienstbetrieb erledigt das der
        # Dienst, im Managed-Betrieb der Kiosk selbst.
        & $taskInstaller -KioskPath $kioskExe
        Step "Geplanter Task registriert."
    }
}

# ---------------------------------------------------------------------------------------
# 8. Firewall
# ---------------------------------------------------------------------------------------

Title "8. Firewall"

<#
    Ohne diese Regeln fragt Windows beim ersten Start jeder der beiden Anwendungen nach -
    ein Dialog mitten auf dem Kiosk-Bildschirm, den niemand wegklickt, und bis dahin kommt
    aus dem Netz niemand heran.

    Massgeblich sind die Regeln fuer die ANWENDUNGEN: sie decken alles ab, was die beiden
    oeffnen, und genau sie unterdruecken die Rueckfrage. Die Portregeln kommen dazu, damit
    in der Firewall-Uebersicht steht, worum es geht - und fuer den Fall, dass die
    Programmdatei spaeter woanders liegt.
#>
$firewallRules = @(
    @{ Name = "StockTV (Anwendung)";       Program = $stockTvExe }
    @{ Name = "StockTV-Kiosk (Anwendung)"; Program = $kioskExe }

    # HTTP der Bedienoberflaeche, NetMQ zu den Anzeigen, Fernsteuerung des Kiosks.
    @{ Name = "StockTV (TCP)";      Protocol = "TCP"; Ports = @($Port, 4747, 4748) }
    @{ Name = "StockTV-Kiosk (TCP)"; Protocol = "TCP"; Ports = @($KioskApiPort) }

    # Geraetesuche im Netz. UDP, und ohne sie findet die Gegenstelle den Rechner nicht.
    @{ Name = "StockTV (mDNS)";     Protocol = "UDP"; Ports = @(5353) }
)

if ($SkipFirewall) {
    Step "Uebersprungen (-SkipFirewall)."
}
elseif (-not $isAdmin) {
    Warn ("Ohne Administratorrechte lassen sich keine Firewall-Regeln anlegen. Windows " +
          "fragt dann beim ersten Start jeder Anwendung nach, und aus dem Netz kommt bis " +
          "dahin niemand heran. Nachholen: die Installation einmal als Administrator " +
          "starten, oder -SkipFirewall setzen, wenn das gewollt ist.")
}
else {
    foreach ($rule in $firewallRules) {
        if ($WhatIfOnly) { Step "wuerde Regel '$($rule.Name)' anlegen"; continue }

        # Erst weg, dann neu: sonst sammeln sich bei jedem Update Dubletten an.
        Remove-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue

        $arguments = @{
            DisplayName = $rule.Name
            Direction   = "Inbound"
            Action      = "Allow"
            Profile     = "Any"
        }

        if ($rule.Program) {
            $arguments.Program = $rule.Program
        }
        else {
            $arguments.Protocol  = $rule.Protocol
            $arguments.LocalPort = $rule.Ports
        }

        New-NetFirewallRule @arguments | Out-Null
        Step "Regel '$($rule.Name)' angelegt."
    }
}

# ---------------------------------------------------------------------------------------
# 9. Zusammenfassung
# ---------------------------------------------------------------------------------------

Title "9. Zusammenfassung"

Write-Host ""
Write-Host "  StockTV        $StockTvHome"
Write-Host "  Kiosk          $KioskDir"
Write-Host "  Bedienung      http://localhost:$Port/"
Write-Host "  Im Netz        http://$($env:COMPUTERNAME):$Port"
Write-Host "  Fernsteuerung  http://$($env:COMPUTERNAME):$KioskApiPort/swagger"

if ($ApiKey) {
    Write-Host ""
    Write-Host "  API-Schluessel: $ApiKey" -ForegroundColor Yellow
    Write-Host "  Jetzt notieren - nach dem ersten Start des Kiosks steht er nur noch" -ForegroundColor Yellow
    Write-Host "  verschluesselt in der Datei und laesst sich nicht mehr ablesen." -ForegroundColor Yellow
}

if ($warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "  Hinweise:" -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
}

Write-Host ""
if ($WhatIfOnly) {
    Write-Host "Probelauf beendet - es wurde nichts veraendert." -ForegroundColor Yellow
}
else {
    Write-Host "Fertig. Pruefen mit:  .\Test-Installation.ps1" -ForegroundColor Green
}
Write-Host ""

