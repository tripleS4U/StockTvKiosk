<#
.SYNOPSIS
    Baut StockTV und den StockTV-Kiosk und legt beides verteilfertig unter C:\StockTV ab.

.DESCRIPTION
    Laeuft auf dem Entwicklungsrechner. Das Ergebnis ist ein Ordner, der sich als Ganzes auf
    einen Zielrechner kopieren laesst; dort erledigt Install-StockTV.ps1 den Rest.

    Aufbau des Pakets:

        C:\StockTV-Install\
          StockTvApps\
            StockTV\             StockTvBlazor, self-contained
            StockTvKiosk\        StockTvKiosk, self-contained
          config\                Vorlagen, siehe Install-StockTV.ps1
          WebView2\              Installer der WebView2-Runtime, siehe -WebView2
          Install-StockTV.ps1
          Test-Installation.ps1
          Install-KioskTask.ps1  aus deploy/windows, wird von der Installation aufgerufen
          VERSION.txt
          README.md

    Der Ordner StockTvApps ist genau das, was die Installation nach C:\ legt - Paket und
    Installation sind deckungsgleich aufgebaut.

    Ein spaeterer Zweig fuer den Raspberry Pi bekommt ein eigenes Paket, siehe -Runtime.

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

    Baut beides fuer win-x64 nach C:\StockTV-Install und legt den WebView2-Bootstrapper
    (~2 MB) dazu.

.EXAMPLE
    .\Publish-Bundle.ps1 -WebView2 Standalone

    Mit dem vollstaendigen WebView2-Installer (~150 MB) statt des Bootstrappers - fuer
    Zielrechner ohne Internetzugang.

.EXAMPLE
    .\Publish-Bundle.ps1 -SkipStockTv

    Nur den Kiosk neu bauen; der StockTV-Stand im Paket bleibt, wie er ist.

.EXAMPLE
    .\Publish-Bundle.ps1 -Runtime linux-arm64 -OutputRoot C:\StockTV-Install-linux-arm64

    Der spaetere Zweig fuer den Raspberry Pi. Bewusst ein eigenes Paket: zwei Plattformen
    in einem Ordner hiessen, dass jeder Zielrechner den Ballast der anderen mitkopiert.
#>
[CmdletBinding()]
param(
    [string]$OutputRoot = "C:\StockTV-Install",

    # Zielplattform. Fuer den Raspberry Pi spaeter: -Runtime linux-arm64 zusammen mit einem
    # eigenen -OutputRoot, damit sich die beiden Pakete nicht ins Gehege kommen.
    [ValidateSet("win-x64", "linux-x64", "linux-arm64")]
    [string]$Runtime = "win-x64",

    [string]$StockTvRepo,

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    <#
        Die WebView2-Runtime ist das Einzige, was ein self-contained Build nicht mitbringt -
        sie ist ein Windows-Systempaket, kein .NET-Bestandteil. Ohne sie startet der Kiosk,
        zeigt aber nichts an.

          Bootstrapper  ~2 MB im Paket, laedt beim Installieren nach. Braucht Internet auf
                        dem ZIELrechner.
          Standalone    ~150 MB im Paket, installiert ohne Internet. Fuer Hallen ohne Netz.
          None          nichts mitliefern.
    #>
    [ValidateSet("Bootstrapper", "Standalone", "None")]
    [string]$WebView2 = "Bootstrapper",

    [switch]$SkipStockTv,

    [switch]$SkipKiosk,

    # Version des Verteilpakets, z. B. "v1.2.0" oder "preview-17". Landet nur in
    # VERSION.txt; die Versionen der beiden Anwendungen bleiben die aus ihren Projekten.
    # Die Build-Pipeline setzt sie, von Hand gebaut bleibt sie leer.
    [string]$PackageVersion
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------------------
# Pfade aufloesen
# ---------------------------------------------------------------------------------------

$BundleDir  = $PSScriptRoot
$KioskRepo  = Split-Path -Parent (Split-Path -Parent $BundleDir)
$SourceRoot = Split-Path -Parent $KioskRepo

if (-not $StockTvRepo) { $StockTvRepo = Join-Path $SourceRoot "StockTV" }

# Pfade im Skript durchgehend mit "/": Windows versteht beide Trenner, Linux nur diesen.
# Das Skript laeuft auch auf den Linux-Runnern der Build-Pipeline (.github/workflows).
$KioskProject   = Join-Path $KioskRepo   "src/StockTvKiosk.App/StockTvKiosk.App.csproj"
$StockTvProject = Join-Path $StockTvRepo "StockTvBlazor/StockTvBlazor.csproj"

foreach ($required in @($KioskProject, $StockTvProject)) {
    if (-not (Test-Path $required)) {
        throw "Projekt nicht gefunden: $required`nMit -StockTvRepo laesst sich der Pfad zum StockTV-Repository setzen."
    }
}

$AppsDir    = Join-Path $OutputRoot "StockTvApps"
$StockTvOut = Join-Path $AppsDir "StockTV"
$KioskOut   = Join-Path $AppsDir "StockTvKiosk"

Write-Host "=========================================="
Write-Host "  StockTV-Verteilpaket"
Write-Host "  Kiosk:    $KioskRepo"
Write-Host "  StockTV:  $StockTvRepo"
Write-Host "  Plattform: $Runtime"
Write-Host "  Ziel:     $OutputRoot"
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
        --runtime $Runtime `
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

$isWindowsTarget = $Runtime -like "win-*"

# Beide Plattformen bekommen dieselben Vorlagen mit: die Installation greift sich die
# passende (AppConfig.<Mode>.json bzw. .linux.json). Ein paar Kilobyte mehr im Paket sind
# das allemal wert - so laesst sich ein Paket auch nachtraeglich noch einordnen.
$configOut = Join-Path $OutputRoot "config"
New-Item -ItemType Directory -Path $configOut -Force | Out-Null
Copy-Item (Join-Path $BundleDir "config/*") $configOut -Force

Copy-Item (Join-Path $BundleDir "README.md") $OutputRoot -Force

if ($isWindowsTarget) {
    # Die .cmd-Starter rufen die Skripte mit -ExecutionPolicy Bypass auf. Ohne sie
    # braeuchte es auf einem frischen Windows erst ein systemweites
    # Set-ExecutionPolicy RemoteSigned - fuer eine einmalige Installation zu viel.
    foreach ($script in @("Install-StockTV.ps1", "Install-StockTV.cmd",
                          "Test-Installation.ps1", "Test-Installation.cmd")) {
        Copy-Item (Join-Path $BundleDir $script) $OutputRoot -Force
    }

    # Der Task-Installer stammt aus deploy/windows und wird nicht kopiert, sondern
    # weitergereicht - so gibt es ihn weiterhin nur einmal im Repository.
    Copy-Item (Join-Path $KioskRepo "deploy/windows/Install-KioskTask.ps1")   $OutputRoot -Force
    Copy-Item (Join-Path $KioskRepo "deploy/windows/Uninstall-KioskTask.ps1") $OutputRoot -Force

    Write-Host "   Windows-Skripte."
}
else {
    foreach ($script in @("install-stocktv.sh", "test-installation.sh")) {
        $target = Join-Path $OutputRoot $script

        # Zeilenenden: ein Skript mit CRLF scheitert auf Linux an "bad interpreter".
        # Der Umweg ueber WriteAllText ist noetig, weil Copy-Item die Datei
        # unveraendert uebernaehme.
        $text = [IO.File]::ReadAllText((Join-Path $BundleDir $script)) -replace "`r`n", "`n"
        [IO.File]::WriteAllText($target, $text, (New-Object Text.UTF8Encoding $false))
    }

    # Die systemd-Vorlagen stammen wie der Task-Installer unter Windows aus deploy/ und
    # liegen weiterhin nur einmal im Repository.
    $unitsOut = Join-Path $OutputRoot "units"
    New-Item -ItemType Directory -Path $unitsOut -Force | Out-Null

    foreach ($unit in @("stocktv.service", "stocktv-kiosk.service")) {
        $text = [IO.File]::ReadAllText((Join-Path $KioskRepo "deploy/linux/$unit")) -replace "`r`n", "`n"
        [IO.File]::WriteAllText((Join-Path $unitsOut $unit), $text, (New-Object Text.UTF8Encoding $false))
    }

    Write-Host "   Linux-Skripte und systemd-Vorlagen."
}

# ---------------------------------------------------------------------------------------
# WebView2-Installer beilegen
# ---------------------------------------------------------------------------------------

$webView2Dir = Join-Path $OutputRoot "WebView2"
$webView2File = $null

if (-not $isWindowsTarget) {
    # Unter Linux nutzt der Kiosk WebKitGTK aus der Paketverwaltung. Das Gegenstueck zur
    # WebView2-Runtime installiert deshalb install-stocktv.sh per apt, nicht das Paket.
    Write-Host "WebView2: entfaellt - unter Linux uebernimmt WebKitGTK (siehe install-stocktv.sh)." -ForegroundColor DarkGray
}
elseif ($WebView2 -eq "None") {
    Write-Host "WebView2: nichts beigelegt (-WebView2 None)." -ForegroundColor DarkGray
}
else {
    # Kurzlinks von Microsoft, siehe developer.microsoft.com/microsoft-edge/webview2
    $source = if ($WebView2 -eq "Standalone") {
        @{ Url = "https://go.microsoft.com/fwlink/?linkid=2124701"
           Name = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe" }
    } else {
        @{ Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
           Name = "MicrosoftEdgeWebview2Setup.exe" }
    }

    New-Item -ItemType Directory -Path $webView2Dir -Force | Out-Null
    $webView2File = Join-Path $webView2Dir $source.Name

    if (Test-Path $webView2File) {
        Write-Host ("WebView2: {0} bereits im Paket ({1:N1} MB)." -f `
            $source.Name, ((Get-Item $webView2File).Length / 1MB)) -ForegroundColor DarkGray
    }
    else {
        Write-Host "WebView2: lade $($source.Name) ..." -ForegroundColor Cyan
        try {
            # Der Fortschrittsbalken macht Invoke-WebRequest um ein Vielfaches langsamer.
            $previous = $ProgressPreference
            $ProgressPreference = "SilentlyContinue"
            Invoke-WebRequest -Uri $source.Url -OutFile $webView2File -UseBasicParsing
            $ProgressPreference = $previous

            Write-Host ("   {0:N1} MB" -f ((Get-Item $webView2File).Length / 1MB))
        }
        catch {
            # Kein Grund, den ganzen Build scheitern zu lassen: das Paket ist ohne den
            # Installer brauchbar, es fehlt nur die Bequemlichkeit auf dem Zielrechner.
            Write-Warning ("WebView2-Installer konnte nicht geladen werden: {0}`n" +
                           "Das Paket ist trotzdem vollstaendig; auf Zielrechnern ohne " +
                           "WebView2-Runtime muss sie dann von Hand installiert werden." -f $_.Exception.Message)
            Remove-Item $webView2File -Force -ErrorAction SilentlyContinue
            $webView2File = $null
        }
    }
}

Write-Host ""

# ---------------------------------------------------------------------------------------
# Versionsstempel
# ---------------------------------------------------------------------------------------

<#
    Linux-Builds tragen kein .exe, und ihre Programmdatei hat keine Windows-Versionsangabe.
    Dort wird deshalb die zugehoerige .dll gelesen - die gibt es bei einem self-contained
    Build in beiden Faellen und sie traegt die Version aus dem Projekt.
#>
function Get-Version {
    param([string]$Directory, [string]$Assembly)

    $path = Join-Path $Directory "$Assembly.dll"
    if (-not (Test-Path $path)) { return "nicht im Paket" }

    $version = (Get-Item $path).VersionInfo.FileVersion
    return $(if ($version) { $version } else { "unbekannt" })
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
    "StockTV-Verteilpaket $PackageVersion".TrimEnd()
    "Plattform: $Runtime"
    "Erstellt:  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "Rechner:   $([Environment]::MachineName)"
    "Konfiguration: $Configuration"
)

# In GitHub Actions: Link auf den Lauf, der das Paket gebaut hat.
if ($env:GITHUB_RUN_ID) {
    $version += "Build:     $env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
}

$version += @(
    ""
    "StockTV        $(Get-Version $StockTvOut 'StockTvBlazor')"
    "  Quelle       $(Get-GitDescription $StockTvRepo)"
    "StockTV-Kiosk  $(Get-Version $KioskOut 'StockTvKiosk')"
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
Write-Host "Naechster Schritt: den Ordner auf das Zielgeraet kopieren und dort"

if ($isWindowsTarget) {
    Write-Host "  .\Install-StockTV.ps1               (StockTV als Dienst, Kiosk als Task)"
    Write-Host "  .\Install-StockTV.ps1 -Mode Managed (der Kiosk startet StockTV selbst)"
}
else {
    # chmod, weil das Ausfuehrbarkeitsbit beim Kopieren von einem Windows-Dateisystem
    # verlorengeht - ueber SMB, FAT oder ein ZIP kommt es nicht mit.
    Write-Host "  chmod +x install-stocktv.sh test-installation.sh"
    Write-Host "  ./install-stocktv.sh                (zwei systemd-Units)"
    Write-Host "  ./install-stocktv.sh --mode managed (der Kiosk startet StockTV selbst)"
}
Write-Host "==========================================" -ForegroundColor Green
