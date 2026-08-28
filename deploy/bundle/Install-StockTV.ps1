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

.EXAMPLE
    .\Install-StockTV.ps1

.EXAMPLE
    .\Install-StockTV.ps1 -Mode Managed -BahnNummer 3 -OpenFirewall

.EXAMPLE
    .\Install-StockTV.ps1 -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet("Service", "Managed")]
    [string]$Mode = "Service",

    [string]$SourceDir = $PSScriptRoot,

    [string]$InstallDir = "C:\StockTV",

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

    # Eingehende Firewall-Regeln fuer StockTV und die Kiosk-Fernsteuerung anlegen.
    [switch]$OpenFirewall,

    # Vorhandene Konfigurationsdateien durch die Vorlagen ersetzen.
    [switch]$ResetConfig,

    [switch]$WhatIfOnly
)

$ErrorActionPreference = "Stop"

$KioskDir = Join-Path $InstallDir "Kiosk"

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

$stockTvSource  = Join-Path $SourceDir "win-x64\StockTV"
$kioskSource    = Join-Path $SourceDir "win-x64\Kiosk"
$configSource   = Join-Path $SourceDir "config"
$stockTvExe     = Join-Path $InstallDir "StockTvBlazor.exe"
$kioskExe       = Join-Path $KioskDir "StockTvKiosk.exe"
$stockTvConfig  = Join-Path $InstallDir "_config\stocktv.config.json"
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
    Verteilpaket und Installation duerfen nicht derselbe Ordner sein. Sonst kopiert die
    Installation die Anwendungen aus win-x64 in genau das Verzeichnis, das win-x64 enthaelt -
    das laeuft zwar, laesst aber 250 MB doppelt liegen und macht spaeter niemandem klar,
    was davon die Installation ist und was das Paket.
#>
$sourceFull  = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\')
$installFull = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')

if ($sourceFull -eq $installFull -or $sourceFull.StartsWith($installFull + '\', 'OrdinalIgnoreCase')) {
    throw @"
Das Verteilpaket liegt in $sourceFull, installiert werden soll nach $installFull.
Das waere dasselbe Verzeichnis (oder eines darunter).

Das Paket gehoert daneben, nicht hinein - zum Beispiel:
    Paket nach C:\StockTV-Setup kopieren und dort
    .\Install-StockTV.ps1
laufen lassen; installiert wird dann wie gewohnt nach C:\StockTV.

Alternativ mit -InstallDir ein anderes Zielverzeichnis angeben.
"@
}

$isAdmin = Test-Administrator

if ($Mode -eq "Service" -and -not $isAdmin) {
    throw "Fuer -Mode Service werden Administratorrechte gebraucht (Windows-Dienst). " +
          "PowerShell als Administrator starten oder -Mode Managed verwenden."
}
if ($OpenFirewall -and -not $isAdmin) {
    throw "Fuer -OpenFirewall werden Administratorrechte gebraucht."
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

    # Gezaehlt wird die Quelle, nicht das Ziel: unter InstallDir liegt auch der Kiosk, und
    # dessen Dateien haetten in der Meldung fuer StockTV nichts zu suchen.
    Step "$Label nach $To ($((Get-ChildItem $From -Recurse -File).Count) Dateien)."
}

Copy-Application $stockTvSource $InstallDir "StockTV"
Copy-Application $kioskSource   $KioskDir   "StockTV-Kiosk"

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
        $text = $text.Replace('"C:\\StockTV\\StockTvBlazor.exe"', '"' + (ConvertTo-JsonValue $stockTvExe) + '"')
        $text = $text -replace '("RestApi"[\s\S]*?"Port"\s*:\s*)\d+', "`${1}$KioskApiPort"
        $text = $text.Replace('"ApiKey": ""', '"ApiKey": "' + (ConvertTo-JsonValue $ApiKey) + '"')
        return $text
    }

if (-not $kioskWritten -and $generatedKey) {
    $ApiKey = $null
    Warn "Die vorhandene Kiosk-Konfiguration behaelt ihren bisherigen API-Schluessel."
}

# ---------------------------------------------------------------------------------------
# 5. Dienst
# ---------------------------------------------------------------------------------------

Title "5. StockTV"

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
# 6. Kiosk-Autostart
# ---------------------------------------------------------------------------------------

Title "6. Kiosk-Autostart"

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
        & $taskInstaller -KioskPath $kioskExe -WhatIfOnly
    }
    else {
        # StockTV bekommt bewusst keinen eigenen Task: im Dienstbetrieb erledigt das der
        # Dienst, im Managed-Betrieb der Kiosk selbst.
        & $taskInstaller -KioskPath $kioskExe
        Step "Geplanter Task registriert."
    }
}

# ---------------------------------------------------------------------------------------
# 7. Firewall
# ---------------------------------------------------------------------------------------

Title "7. Firewall"

if (-not $OpenFirewall) {
    Step "Uebersprungen. Mit -OpenFirewall werden die Regeln angelegt (Administrator)."
}
else {
    foreach ($rule in @(
        @{ Name = "StockTV ($Port)";              Port = $Port }
        @{ Name = "StockTV-Kiosk ($KioskApiPort)"; Port = $KioskApiPort }
    )) {
        if ($WhatIfOnly) { Step "wuerde Regel '$($rule.Name)' anlegen"; continue }

        Remove-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
        New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Protocol TCP `
            -LocalPort $rule.Port -Action Allow -Profile Any | Out-Null
        Step "Regel '$($rule.Name)' angelegt."
    }
}

# ---------------------------------------------------------------------------------------
# 8. Zusammenfassung
# ---------------------------------------------------------------------------------------

Title "8. Zusammenfassung"

Write-Host ""
Write-Host "  StockTV        $InstallDir"
Write-Host "  Kiosk          $KioskDir"
Write-Host "  Bedienung      http://localhost:$Port/turnier"
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
