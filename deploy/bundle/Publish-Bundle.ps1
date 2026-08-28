<#
.SYNOPSIS
    Baut StockTV und den StockTV-Kiosk und legt beides verteilfertig unter C:\StockTV ab.

.DESCRIPTION
    Laeuft auf dem Entwicklungsrechner. Das Ergebnis ist ein Ordner, der sich als Ganzes auf
    einen Zielrechner kopieren laesst; dort erledigt Install-StockTV.ps1 den Rest.

    Aufbau des Pakets:

        C:\StockTV\
          win-x64\
            StockTV\             StockTvBlazor, self-contained
            Kiosk\               StockTvKiosk, self-contained
          config\                Vorlagen, siehe Install-StockTV.ps1
          Install-StockTV.ps1
          Test-Installation.ps1
          Install-KioskTask.ps1  aus deploy/windows, wird von der Installation aufgerufen
          VERSION.txt
          README.md

    Die Ebene "win-x64" ist Absicht: ein spaeterer Zweig fuer den Raspberry Pi kommt als
    "linux-arm64" daneben, ohne dass sich am Rest etwas aendert.

.PARAMETER OutputRoot
    Zielordner des Pakets. Wird geleert, sofern er nur aus einem frueheren Lauf stammt.

.PARAMETER StockTvRepo
    Wurzel des StockTV-Repositorys. Vorbelegt mit dem Nachbarordner dieses Repositorys.

.PARAMETER SkipStockTv
    Nur den Kiosk neu bauen und den vorhandenen StockTV-Stand im Paket stehen lassen.

.PARAMETER SkipKiosk
    Umgekehrt.

.EXAMPLE
    .\Publish-Bundle.ps1

.EXAMPLE
    .\Publish-Bundle.ps1 -OutputRoot D:\Verteilung\StockTV -SkipStockTv
#>
[CmdletBinding()]
param(
    [string]$OutputRoot = "C:\StockTV",

    [string]$StockTvRepo,

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$SkipStockTv,

    [switch]$SkipKiosk
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------------------
# Pfade aufloesen
# ---------------------------------------------------------------------------------------

$BundleDir  = $PSScriptRoot
$KioskRepo  = Split-Path -Parent (Split-Path -Parent $BundleDir)
$SourceRoot = Split-Path -Parent $KioskRepo

if (-not $StockTvRepo) { $StockTvRepo = Join-Path $SourceRoot "StockTV" }

$KioskProject   = Join-Path $KioskRepo   "src\StockTvKiosk.App\StockTvKiosk.App.csproj"
$StockTvProject = Join-Path $StockTvRepo "StockTvBlazor\StockTvBlazor.csproj"

foreach ($required in @($KioskProject, $StockTvProject)) {
    if (-not (Test-Path $required)) {
        throw "Projekt nicht gefunden: $required`nMit -StockTvRepo laesst sich der Pfad zum StockTV-Repository setzen."
    }
}

$RuntimeDir = Join-Path $OutputRoot "win-x64"
$StockTvOut = Join-Path $RuntimeDir "StockTV"
$KioskOut   = Join-Path $RuntimeDir "Kiosk"

Write-Host "=========================================="
Write-Host "  StockTV-Verteilpaket"
Write-Host "  Kiosk:   $KioskRepo"
Write-Host "  StockTV: $StockTvRepo"
Write-Host "  Ziel:    $OutputRoot"
Write-Host "=========================================="
Write-Host ""

# ---------------------------------------------------------------------------------------
# Bauen
# ---------------------------------------------------------------------------------------

function Publish-One {
    param([string]$Project, [string]$Output, [string]$Label)

    Write-Host "== $Label ==" -ForegroundColor Cyan

    # Der Zielordner wird vorher geleert: bleiben Dateien aus einem aelteren Stand liegen,
    # laedt die Anwendung womoeglich eine DLL, die es im neuen Build gar nicht mehr gibt.
    # Ausgenommen ist _config - dort liegen die Vorlagen, die spaeter eingesetzt werden.
    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

    & dotnet publish $Project `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $Output `
        -p:DebugType=none `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw "$Label : Publish fehlgeschlagen." }

    # -p:DebugType=none entfernt nur die eigenen Symboldateien. Die nativen aus den
    # NuGet-Paketen kommen trotzdem mit - allein libSkiaSharp.pdb sind 80 MB. Zur Laufzeit
    # braucht sie niemand, und in einem Paket, das ueber das Netz auf Geraete wandert,
    # haben sie nichts verloren.
    $symbols = @(Get-ChildItem $Output -Recurse -File -Filter *.pdb)
    if ($symbols.Count -gt 0) {
        $saved = ($symbols | Measure-Object -Property Length -Sum).Sum
        $symbols | Remove-Item -Force
        Write-Host ("   {0} Symboldateien entfernt ({1:N1} MB)" -f $symbols.Count, ($saved / 1MB))
    }

    $size = (Get-ChildItem $Output -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("   {0} Dateien, {1:N1} MB" -f `
        (Get-ChildItem $Output -Recurse -File).Count, ($size / 1MB))
    Write-Host ""
}

if (-not $SkipStockTv) { Publish-One $StockTvProject $StockTvOut "StockTV" }
if (-not $SkipKiosk)   { Publish-One $KioskProject   $KioskOut   "StockTV-Kiosk" }

# ---------------------------------------------------------------------------------------
# Beiwerk ins Paket legen
# ---------------------------------------------------------------------------------------

Write-Host "== Skripte und Vorlagen ==" -ForegroundColor Cyan

$configOut = Join-Path $OutputRoot "config"
New-Item -ItemType Directory -Path $configOut -Force | Out-Null
Copy-Item (Join-Path $BundleDir "config\*") $configOut -Force

foreach ($script in @("Install-StockTV.ps1", "Test-Installation.ps1", "README.md")) {
    Copy-Item (Join-Path $BundleDir $script) $OutputRoot -Force
}

# Der Task-Installer stammt aus deploy/windows und wird nicht kopiert, sondern
# weitergereicht - so gibt es ihn weiterhin nur einmal im Repository.
Copy-Item (Join-Path $KioskRepo "deploy\windows\Install-KioskTask.ps1")   $OutputRoot -Force
Copy-Item (Join-Path $KioskRepo "deploy\windows\Uninstall-KioskTask.ps1") $OutputRoot -Force

# ---------------------------------------------------------------------------------------
# Versionsstempel
# ---------------------------------------------------------------------------------------

function Get-Version {
    param([string]$Directory, [string]$Executable)

    $path = Join-Path $Directory $Executable
    if (-not (Test-Path $path)) { return "nicht im Paket" }
    return (Get-Item $path).VersionInfo.FileVersion
}

function Get-GitDescription {
    param([string]$Repo)

    if (-not (Test-Path (Join-Path $Repo ".git"))) { return "kein Repository" }
    $described = & git -C $Repo describe --always --dirty 2>$null
    if ($LASTEXITCODE -ne 0) { return "unbekannt" }
    $branch = & git -C $Repo rev-parse --abbrev-ref HEAD 2>$null
    return "$described ($branch)"
}

$version = @(
    "StockTV-Verteilpaket"
    "Erstellt:  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "Rechner:   $env:COMPUTERNAME"
    "Konfiguration: $Configuration"
    ""
    "StockTV        $(Get-Version $StockTvOut 'StockTvBlazor.exe')"
    "  Quelle       $(Get-GitDescription $StockTvRepo)"
    "StockTV-Kiosk  $(Get-Version $KioskOut 'StockTvKiosk.exe')"
    "  Quelle       $(Get-GitDescription $KioskRepo)"
)

$versionFile = Join-Path $OutputRoot "VERSION.txt"
$version | Set-Content $versionFile -Encoding utf8

Write-Host ""
$version | ForEach-Object { Write-Host "   $_" }
Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "Paket fertig: $OutputRoot" -ForegroundColor Green
Write-Host ""
Write-Host "Naechster Schritt: den Ordner auf den Zielrechner kopieren und dort"
Write-Host "  .\Install-StockTV.ps1              (StockTV als Dienst, Kiosk als Task)"
Write-Host "  .\Install-StockTV.ps1 -Mode Managed (der Kiosk startet StockTV selbst)"
Write-Host "==========================================" -ForegroundColor Green
