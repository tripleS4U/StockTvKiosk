<#
.SYNOPSIS
    Prueft eine StockTV-Installation und meldet, was nicht stimmt.

.DESCRIPTION
    Laeuft auf dem Zielrechner, ohne etwas zu veraendern. Geprueft wird in dieser Reihenfolge:

      Dateien        sind beide Anwendungen da, welche Version
      Konfiguration  lesbar, und passen die beiden zueinander (derselbe Port!)
      Dienst         vorhanden, Startart, Zustand
      Autostart      geplanter Task fuer den Kiosk
      Netz           hoert jemand auf den Ports, antwortet StockTV, antwortet die
                     Fernsteuerung des Kiosks

    Der haeufigste stille Fehler ist die Portabweichung: StockTV lauscht auf 8080, der Kiosk
    laedt von 5129, und zu sehen ist nur die Statusseite. Genau darauf zielt die Pruefung
    "Kiosk-Adresse passt zum Dienst".

.PARAMETER ApiKey
    Schluessel der Kiosk-Fernsteuerung. Ohne ihn wird der Endpunkt nur auf Erreichbarkeit
    geprueft (401 gilt dann als Erfolg - die Schnittstelle steht ja).

.EXAMPLE
    .\Test-Installation.ps1

.EXAMPLE
    .\Test-Installation.ps1 -ApiKey "abc..." -InstallDir D:\StockTV
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "C:\StockTV",
    [string]$ServiceName = "StockTV",
    [string]$ApiKey,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Continue"

$AppsDir     = Join-Path $InstallDir "Apps"
$StockTvHome = Join-Path $AppsDir "StockTV"
$KioskDir    = Join-Path $AppsDir "StockTvKiosk"

$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [string]$Area,
        [string]$Check,
        [ValidateSet("OK", "FEHLER", "HINWEIS")]
        [string]$Status,
        [string]$Detail
    )
    $results.Add([PSCustomObject]@{ Bereich = $Area; Pruefung = $Check; Status = $Status; Details = $Detail })
}

function Get-JsonValue {
    <#
        Holt einen Wert aus der Kiosk-Konfiguration. Deren Format verpackt jeden Abschnitt
        der obersten Ebene in { description, value } - deshalb der Umweg ueber .value.
    #>
    param($Node, [string[]]$Path)

    foreach ($name in $Path) {
        if ($null -eq $Node) { return $null }
        if ($Node.PSObject.Properties.Name -contains "value") { $Node = $Node.value }
        if ($Node.PSObject.Properties.Name -notcontains $name) { return $null }
        $Node = $Node.$name
    }
    if ($null -ne $Node -and $Node.PSObject.Properties.Name -contains "value") { $Node = $Node.value }
    return $Node
}

Write-Host "=========================================="
Write-Host "  StockTV-Installation pruefen"
Write-Host "  Verzeichnis: $InstallDir"
Write-Host "=========================================="

# ---------------------------------------------------------------------------------------
# Dateien
# ---------------------------------------------------------------------------------------

$stockTvExe = Join-Path $StockTvHome "StockTvBlazor.exe"
$kioskExe   = Join-Path $KioskDir "StockTvKiosk.exe"

foreach ($app in @(
    @{ Label = "StockTV";       Path = $stockTvExe }
    @{ Label = "StockTV-Kiosk"; Path = $kioskExe }
)) {
    if (Test-Path $app.Path) {
        $item = Get-Item $app.Path
        Add-Result "Dateien" $app.Label "OK" `
            ("Version {0}, {1:yyyy-MM-dd HH:mm}" -f $item.VersionInfo.FileVersion, $item.LastWriteTime)
    }
    else {
        Add-Result "Dateien" $app.Label "FEHLER" "nicht gefunden: $($app.Path)"
    }
}

<#
    Die WebView2-Runtime ist das Einzige, was ein self-contained Build nicht mitbringt: sie
    ist ein Windows-Systempaket, kein .NET-Bestandteil. Unter Windows 11 ist sie da, unter
    Windows 10 kann sie fehlen - dann startet der Kiosk, zeigt aber nur ein leeres Fenster.
#>
$webView2Keys = @(
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
    "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
)
$webView2 = $webView2Keys |
    ForEach-Object { (Get-ItemProperty $_ -Name pv -ErrorAction SilentlyContinue).pv } |
    Where-Object { $_ } | Select-Object -First 1

if ($webView2) {
    Add-Result "Dateien" "WebView2-Runtime" "OK" "Version $webView2"
}
else {
    Add-Result "Dateien" "WebView2-Runtime" "FEHLER" `
        "nicht gefunden - der Kiosk zeigt ohne sie nichts an (Evergreen Runtime von Microsoft installieren)"
}

# ---------------------------------------------------------------------------------------
# Konfiguration
# ---------------------------------------------------------------------------------------

$stockTvConfigPath = Join-Path $StockTvHome "_config\stocktv.config.json"
$kioskConfigPath   = Join-Path $KioskDir "_config\AppConfig.json"

$stockTvConfig = $null
if (Test-Path $stockTvConfigPath) {
    try {
        $stockTvConfig = Get-Content $stockTvConfigPath -Raw | ConvertFrom-Json
        Add-Result "Konfiguration" "stocktv.config.json" "OK" `
            "lesbar, Bahn $($stockTvConfig.General.BahnNummer)"
    }
    catch {
        Add-Result "Konfiguration" "stocktv.config.json" "FEHLER" "kein gueltiges JSON: $($_.Exception.Message)"
    }
}
else {
    Add-Result "Konfiguration" "stocktv.config.json" "HINWEIS" `
        "fehlt - StockTV legt sie beim ersten Start mit Standardwerten an"
}

$kioskConfig = $null
$kioskPort   = $null
$kioskApiPort = $null
$blazorHostEnabled = $null

if (Test-Path $kioskConfigPath) {
    try {
        $kioskConfig = Get-Content $kioskConfigPath -Raw | ConvertFrom-Json
        $primaryUrl  = Get-JsonValue $kioskConfig @("KioskOptions", "Urls", "PrimaryUrl")

        if ($primaryUrl -and [Uri]::TryCreate($primaryUrl, "Absolute", [ref]$null)) {
            $kioskPort = ([Uri]$primaryUrl).Port
        }

        $kioskApiPort      = Get-JsonValue $kioskConfig @("KioskOptions", "RestApi", "Port")
        $blazorHostEnabled = Get-JsonValue $kioskConfig @("KioskOptions", "BlazorHost", "Enabled")
        $hostEnvironment   = Get-JsonValue $kioskConfig @("KioskOptions", "BlazorHost", "Environment")

        # Im Managed-Betrieb bestimmt ASPNETCORE_URLS, worauf StockTV hoert. Ist der Wert
        # nicht gesetzt, leitet der Kiosk ihn aus PrimaryUrl ab und beide koennen gar nicht
        # auseinanderlaufen; steht er da, kann er es sehr wohl.
        if ($hostEnvironment -and
            $hostEnvironment.PSObject.Properties.Name -contains "ASPNETCORE_URLS") {
            $hostUrls = $hostEnvironment.ASPNETCORE_URLS
            if ("$hostUrls" -match ':(\d+)') { $hostPort = [int]$Matches[1] }
        }
        $apiKeySet         = Get-JsonValue $kioskConfig @("KioskOptions", "RestApi", "ApiKey")
        $bindAddress       = Get-JsonValue $kioskConfig @("KioskOptions", "RestApi", "BindAddress")

        Add-Result "Konfiguration" "AppConfig.json" "OK" "lesbar, PrimaryUrl $primaryUrl"

        $mode = if ($blazorHostEnabled) { "Managed - der Kiosk startet StockTV selbst" }
                else { "Service - der Kiosk wartet nur auf die Adresse" }
        Add-Result "Konfiguration" "Betriebsart" "OK" $mode

        # Ohne Schluessel startet die Schnittstelle bei nicht-lokaler Bindung gar nicht.
        $loopback = $bindAddress -in @("127.0.0.1", "localhost", "::1")
        if (-not $apiKeySet -and -not $loopback) {
            Add-Result "Konfiguration" "API-Schluessel" "FEHLER" `
                "leer bei BindAddress $bindAddress - die Fernsteuerung startet so nicht"
        }
        elseif (-not $apiKeySet) {
            Add-Result "Konfiguration" "API-Schluessel" "HINWEIS" "leer, aber nur lokal gebunden"
        }
        else {
            Add-Result "Konfiguration" "API-Schluessel" "OK" `
                $(if ("$apiKeySet".StartsWith("enc:")) { "gesetzt und verschluesselt" }
                  else { "gesetzt, wird beim naechsten Start verschluesselt" })
        }
    }
    catch {
        Add-Result "Konfiguration" "AppConfig.json" "FEHLER" "kein gueltiges JSON: $($_.Exception.Message)"
    }
}
else {
    Add-Result "Konfiguration" "AppConfig.json" "HINWEIS" `
        "fehlt - der Kiosk legt sie beim ersten Start mit Standardwerten an (Port 5129!)"
}

# ---------------------------------------------------------------------------------------
# Dienst
# ---------------------------------------------------------------------------------------

$service     = Get-Service $ServiceName -ErrorAction SilentlyContinue
$servicePort = $null

if ($service) {
    $registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $environment  = (Get-ItemProperty -Path $registryPath -Name Environment -ErrorAction SilentlyContinue).Environment
    $urls         = $environment | Where-Object { $_ -like "ASPNETCORE_URLS=*" } | Select-Object -First 1

    if ($urls -match ':(\d+)') { $servicePort = [int]$Matches[1] }

    $status = if ($service.Status -eq "Running") { "OK" } else { "FEHLER" }
    Add-Result "Dienst" $ServiceName $status `
        "$($service.Status), Start $($service.StartType), $urls"
}
else {
    if ($blazorHostEnabled) {
        Add-Result "Dienst" $ServiceName "OK" "nicht vorhanden - richtig fuer die Betriebsart Managed"
    }
    else {
        Add-Result "Dienst" $ServiceName "HINWEIS" `
            "nicht vorhanden, der Kiosk erwartet aber eine laufende Anwendung"
    }
}

<#
    Der stille Klassiker: beide laufen, reden aber ueber verschiedene Ports - zu sehen ist
    dann nur die Statusseite des Kiosks. Verglichen wird gegen die Stelle, die im jeweiligen
    Betrieb tatsaechlich bestimmt, worauf StockTV hoert.
#>
$expectedPort = if ($servicePort) { $servicePort } else { $hostPort }
$expectedFrom = if ($servicePort) { "der Dienst hoert auf" } else { "ASPNETCORE_URLS sagt" }

if ($kioskPort -and $expectedPort) {
    if ($kioskPort -eq $expectedPort) {
        Add-Result "Konfiguration" "Kiosk-Adresse passt zu StockTV" "OK" "beide Port $kioskPort"
    }
    else {
        Add-Result "Konfiguration" "Kiosk-Adresse passt zu StockTV" "FEHLER" `
            "Kiosk laedt von $kioskPort, $expectedFrom $expectedPort"
    }
}
elseif ($kioskPort -and $blazorHostEnabled) {
    Add-Result "Konfiguration" "Kiosk-Adresse passt zu StockTV" "OK" `
        "ASPNETCORE_URLS nicht gesetzt - der Kiosk leitet Port $kioskPort aus PrimaryUrl ab"
}

# ---------------------------------------------------------------------------------------
# Autostart
# ---------------------------------------------------------------------------------------

$task = Get-ScheduledTask -TaskPath "\StockTV\" -ErrorAction SilentlyContinue |
        Where-Object { $_.TaskName -eq "StockTV-Kiosk" }

if ($task) {
    $info = $task | Get-ScheduledTaskInfo
    Add-Result "Autostart" "Task StockTV-Kiosk" "OK" `
        "$($task.State), zuletzt $($info.LastRunTime) (Ergebnis $($info.LastTaskResult))"
}
else {
    Add-Result "Autostart" "Task StockTV-Kiosk" "HINWEIS" `
        "nicht registriert - der Kiosk startet nach einem Neustart nicht von allein"
}

# ---------------------------------------------------------------------------------------
# Netz
# ---------------------------------------------------------------------------------------

function Test-Endpoint {
    param([string]$Url, [hashtable]$Headers = @{})

    try {
        $response = Invoke-WebRequest -Uri $Url -Headers $Headers -TimeoutSec $TimeoutSeconds `
            -UseBasicParsing -ErrorAction Stop
        return @{ Code = [int]$response.StatusCode; Error = $null }
    }
    catch {
        $status = $_.Exception.Response.StatusCode
        if ($null -ne $status) { return @{ Code = [int]$status; Error = $null } }
        return @{ Code = 0; Error = $_.Exception.Message }
    }
}

$kioskRunning = @(Get-Process StockTvKiosk -ErrorAction SilentlyContinue).Count -gt 0

if ($kioskPort) {
    $probe = Test-Endpoint "http://localhost:$kioskPort/"

    if ($probe.Code -gt 0) {
        # Jede HTTP-Antwort zaehlt: der Server steht dann, und das ist die Frage.
        Add-Result "Netz" "StockTV antwortet" "OK" "http://localhost:$kioskPort/ -> HTTP $($probe.Code)"
    }
    elseif ($blazorHostEnabled -and -not $kioskRunning) {
        # In der Betriebsart Managed startet der Kiosk StockTV. Laeuft er nicht, ist das
        # keine kaputte Installation, sondern nur eine, die gerade niemand gestartet hat.
        Add-Result "Netz" "StockTV antwortet" "HINWEIS" `
            "keine Antwort - der Kiosk laeuft nicht, und in dieser Betriebsart startet er StockTV"
    }
    else {
        Add-Result "Netz" "StockTV antwortet" "FEHLER" "http://localhost:$kioskPort/ -> $($probe.Error)"
    }
}

if ($kioskApiPort) {
    $headers = @{}
    if ($ApiKey) { $headers["X-Api-Key"] = $ApiKey }

    $probe = Test-Endpoint "http://localhost:$kioskApiPort/api/system/capabilities" $headers

    switch ($probe.Code) {
        0   { Add-Result "Netz" "Kiosk-Fernsteuerung" "HINWEIS" `
                "keine Antwort auf Port $kioskApiPort - laeuft der Kiosk gerade?" }
        200 { Add-Result "Netz" "Kiosk-Fernsteuerung" "OK" "antwortet mit Schluessel" }
        401 { Add-Result "Netz" "Kiosk-Fernsteuerung" $(if ($ApiKey) { "FEHLER" } else { "OK" }) `
                $(if ($ApiKey) { "Schluessel abgelehnt" } else { "steht, Schluessel nicht geprueft (-ApiKey angeben)" }) }
        default { Add-Result "Netz" "Kiosk-Fernsteuerung" "HINWEIS" "HTTP $($probe.Code)" }
    }
}

# ---------------------------------------------------------------------------------------
# Ergebnis
# ---------------------------------------------------------------------------------------

Write-Host ""
$results | Format-Table -AutoSize -Property Bereich, Pruefung, Status, Details | Out-Host

$failed = @($results | Where-Object Status -eq "FEHLER")
$hints  = @($results | Where-Object Status -eq "HINWEIS")

Write-Host "=========================================="
if ($failed.Count -eq 0) {
    Write-Host "Keine Fehler. $($results.Count) Pruefungen, $($hints.Count) Hinweise." -ForegroundColor Green
}
else {
    Write-Host "$($failed.Count) von $($results.Count) Pruefungen fehlgeschlagen:" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  - $($_.Pruefung): $($_.Details)" -ForegroundColor Red }
}
Write-Host "=========================================="

exit $failed.Count
