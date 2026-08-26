<#
.SYNOPSIS
    Erzeugt self-contained Builds des StockTV-Kiosks fuer alle Zielplattformen.

.DESCRIPTION
    Zielplattformen:
      win-x64      Entwicklung und Test unter Windows
      linux-arm64  Raspberry Pi 4 mit 64-bit Raspberry Pi OS (Produktivziel)
      linux-x64    Linux-PC/VM zum Vortesten

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Runtime linux-arm64
    .\publish.ps1 -SingleFile
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64", "linux-arm64", "all")]
    [string]$Runtime = "all",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputRoot = (Join-Path $PSScriptRoot "publish"),

    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\StockTvKiosk.App\StockTvKiosk.App.csproj"

$runtimes = if ($Runtime -eq "all") { @("win-x64", "linux-x64", "linux-arm64") } else { @($Runtime) }

foreach ($rid in $runtimes) {
    $output = Join-Path $OutputRoot $rid
    Write-Host "== Publish $rid -> $output ==" -ForegroundColor Cyan

    $arguments = @(
        "publish", $project,
        "--configuration", $Configuration,
        "--runtime", $rid,
        "--self-contained", "true",
        "--output", $output,
        "-p:DebugType=none"
    )

    if ($SingleFile) {
        # Trimming ist bewusst NICHT aktiv: Avalonia und der WebView laden Typen ueber
        # Reflection nach, ein getrimmtes Build faellt zur Laufzeit um.
        $arguments += "-p:PublishSingleFile=true"
        $arguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish fuer $rid fehlgeschlagen." }
}

Write-Host ""
Write-Host "Fertig. Ergebnisse unter $OutputRoot" -ForegroundColor Green
Write-Host "Linux-Ziele brauchen auf dem Geraet WebKitGTK:" -ForegroundColor DarkGray
Write-Host "  sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0" -ForegroundColor DarkGray
Write-Host "  chmod +x StockTvKiosk" -ForegroundColor DarkGray
