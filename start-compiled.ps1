<#
=============================================================
 StockTV - Kompilierte Version starten (kein Build)

 Startet die bereits gebaute StockTvBlazor.exe direkt und
 oeffnet den Browser dazu (Edge/Chrome, randlos im Kiosk-Modus,
 kein Menue/Adressleiste). Anders als test-dualdisplay.ps1 wird
 hier NICHT gebaut.

 Standardmaessig werden ZWEI randlose Fenster geoeffnet (Dual-
 Display, Spiegel auf dem 2. Bildschirm) - wie test-dualdisplay.ps1:
   Fenster 1 : normale Anzeige  -> hier wird getippt (Numpad)
   Fenster 2 : /display2        -> gespiegelte Anzeige
 Mit -Dual:$false gibt es nur EIN Fenster.

 Ist kein Edge/Chrome installiert, faellt das Skript auf den
 Standardbrowser zurueck (dann mit normalem Fenster/Menue).

 Das Skript ist ordnerunabhaengig: es darf irgendwo liegen
 (auch ausserhalb des Repos) und sucht die StockTvBlazor.exe
 selbst - bevorzugt unter bin\Release\net10.0. Falls die exe
 nicht automatisch gefunden wird, kann der Pfad ueber
 -SearchRoot bzw. -ExePath vorgegeben werden.

 Beispiele:
   .\start-compiled.ps1                        # Standard: Dual-Display, Spiegel auf 2. Bildschirm
   .\start-compiled.ps1 -Dual:$false            # nur EIN randloses Fenster
   .\start-compiled.ps1 -SecondScreen:$false    # Dual, aber beide Fenster nebeneinander auf einem Bildschirm
   .\start-compiled.ps1 -MainDisplayIndex 1     # Einzelfenster auf Bildschirm [1] (mit -Dual:$false)
   .\start-compiled.ps1 -MainDisplayIndex 0 -MirrorDisplayIndex 1  # fest per Index, unabhaengig von Primary
   .\start-compiled.ps1 -Configuration Debug    # bin\Debug\net10.0 bevorzugen
   .\start-compiled.ps1 -TargetFramework net9.0 # falls TFM abweicht
   .\start-compiled.ps1 -SearchRoot D:\Projekte # wo gesucht werden soll
   .\start-compiled.ps1 -ExePath "C:\...\StockTvBlazor.exe"  # Pfad direkt angeben
   .\start-compiled.ps1 -StartPage bestof       # andere Startseite
   .\start-compiled.ps1 -Demo                   # mit Demo-Daten
   .\start-compiled.ps1 -NoBrowser              # nur die App starten, kein Browserfenster
   .\start-compiled.ps1 -StopRunning            # laufende Instanz vorher ohne Rueckfrage beenden
   .\start-compiled.ps1 -DetailedErrors         # echte Fehlermeldung statt "An unhandled error..." sehen
=============================================================
#>
[CmdletBinding()]
param(
    [int]$Port                 = 5129,
    [string]$StartPage         = "turnier",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration     = "Release",
    [string]$TargetFramework   = "net10.0",
    [string]$SearchRoot        = $null,   # wo nach der exe gesucht wird (Standard: Skript-Laufwerk)
    [string]$ExePath           = $null,   # optional: Pfad zur exe direkt angeben, ueberspringt die Suche
    [switch]$Demo,
    [switch]$NoBrowser,
    [switch]$StopRunning,
    [switch]$DetailedErrors,    # ASPNETCORE_ENVIRONMENT=Development erzwingen, um echte Fehler zu sehen
    [switch]$Dual                = $true,   # zusaetzliches /display2-Fenster oeffnen (Standard: an; mit -Dual:$false ausschalten)
    [switch]$SecondScreen        = $true,   # /display2-Fenster auf den zweiten Bildschirm legen (Standard: an; mit -SecondScreen:$false ausschalten; nur mit -Dual)
    [int]$MainDisplayIndex   = -1,   # 0-basierter Bildschirm-Index fuers Hauptfenster; -1 = automatisch (Windows-Primary)
    [int]$MirrorDisplayIndex = -1    # 0-basierter Bildschirm-Index fuers Spiegel-Fenster; -1 = automatisch
)

$ErrorActionPreference = "Stop"

# --- 1. StockTvBlazor.exe finden --------------------------------------------
function Find-StockTvExe {
    param([string]$PreferredRelPath, [string]$Root)

    # a) Falls das Skript (noch) im Repo liegt, klappt der alte, schnelle Weg.
    $guess = Join-Path $PSScriptRoot "..\..\StockTvBlazor\$PreferredRelPath"
    if (Test-Path $guess) {
        return Get-Item (Resolve-Path $guess)
    }

    # b) Von jedem Vorfahren des Skriptordners aus rekursiv nach der exe suchen.
    #    So funktioniert es, egal in welchem Unterordner des Repos das Skript liegt.
    $dir = Get-Item $PSScriptRoot
    while ($dir) {
        $hit = Get-ChildItem -Path $dir.FullName -Filter "StockTvBlazor.exe" -Recurse -File -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending
        if ($hit) { return $hit | Select-Object -First 1 }
        $dir = $dir.Parent
    }

    # c) Letzter Versuch: kompletten angegebenen Suchpfad (Standard: Skript-Laufwerk) durchsuchen.
    if (-not $Root) { $Root = (Get-Item $PSScriptRoot).PSDrive.Root }
    Write-Host "Durchsuche $Root ... (kann etwas dauern)" -ForegroundColor DarkGray
    $hit = Get-ChildItem -Path $Root -Filter "StockTvBlazor.exe" -Recurse -File -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending
    return $hit | Select-Object -First 1
}

if ($ExePath) {
    if (-not (Test-Path $ExePath)) {
        Write-Error "Angegebene -ExePath existiert nicht: $ExePath"
        exit 1
    }
    $exe = Get-Item $ExePath
} else {
    $preferredRel = "bin\$Configuration\$TargetFramework\StockTvBlazor.exe"
    $exe = Find-StockTvExe -PreferredRelPath $preferredRel -Root $SearchRoot

    # Wenn mehrere exen gefunden wurden (Debug/Release, versch. TFMs),
    # die zum gewuenschten Pfad passende bevorzugen.
    if ($exe -and $exe.FullName -notmatch [regex]::Escape("bin\$Configuration\$TargetFramework")) {
        $allHits = Get-ChildItem -Path (Get-Item $PSScriptRoot).PSDrive.Root -Filter "StockTvBlazor.exe" -Recurse -File -ErrorAction SilentlyContinue
        $preferredHit = $allHits | Where-Object { $_.FullName -match [regex]::Escape("bin\$Configuration\$TargetFramework") } |
                        Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($preferredHit) { $exe = $preferredHit }
    }
}

if (-not $exe) {
    Write-Error "StockTvBlazor.exe wurde nicht gefunden (gesucht: bin\$Configuration\$TargetFramework). Bitte zuerst bauen oder -ExePath angeben."
    exit 1
}
Write-Host "Verwende Build: $($exe.FullName)" -ForegroundColor DarkGray

# --- 2. Konflikt pruefen: NetMQ 4747/4748 duerfen nur einmal gebunden sein --
$running = Get-Process -Name "StockTvBlazor" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Es laeuft bereits eine StockTV-Instanz (PID: $($running.Id -join ', '))." -ForegroundColor Yellow

    $stop = $StopRunning
    if (-not $stop) {
        $answer = Read-Host "Jetzt beenden? [J/n]"
        if ($answer -eq "" -or $answer -match "^[JjYy]") { $stop = $true }
    }

    if (-not $stop) {
        Write-Host "Abgebrochen." -ForegroundColor Red
        exit 1
    }

    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# --- 3. App starten ----------------------------------------------------------
$env:ASPNETCORE_URLS        = "http://localhost:$Port"
# Mit -DetailedErrors auf "Development" stellen, damit Blazor die echte
# Exception statt nur "An unhandled error has occurred" zeigt.
$env:ASPNETCORE_ENVIRONMENT = if ($DetailedErrors) { "Development" } else { $Configuration }

$logDir = Join-Path $exe.DirectoryName "_logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$logOut = Join-Path $logDir "stocktv-console.log"
$logErr = Join-Path $logDir "stocktv-console-error.log"
Write-Host "Starte StockTV (kompiliert) auf http://localhost:$Port ..." -ForegroundColor Cyan
Write-Host "Konsolen-Log: $logOut" -ForegroundColor DarkGray
$app = Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName -PassThru `
       -RedirectStandardOutput $logOut -RedirectStandardError $logErr

$ready = $false
for ($i = 0; $i -lt 40; $i++) {
    try {
        $null = Invoke-WebRequest -Uri "http://localhost:$Port" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        $ready = $true
        break
    } catch {
        if ($app.HasExited) { break }
        Start-Sleep -Milliseconds 1500
    }
}

if (-not $ready) {
    Write-Host "App ist nicht hochgekommen." -ForegroundColor Red
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    exit 1
}
Write-Host "App laeuft. (PID: $($app.Id))" -ForegroundColor Green

# --- 4. Browser oeffnen (optional) -------------------------------------------
$suffix = ""
if ($Demo) { $suffix = "?demo=true" }
$mainUrl   = "http://localhost:$Port/$StartPage$suffix"
$mirrorUrl = "http://localhost:$Port/display2$suffix"

if ($NoBrowser) {
    Write-Host "Aufrufbar unter: $mainUrl"
    if ($Dual) { Write-Host "Spiegel unter:   $mirrorUrl" }
} else {
    # Edge/Chrome suchen - fuer randloses Kiosk-Fenster (ohne Menue/Adressleiste)
    # gebraucht, egal ob Einzel- oder Dual-Fenster.
    $browser = $null
    foreach ($c in @(
        "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "C:\Program Files\Google\Chrome\Application\chrome.exe",
        "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe")) {
        if (Test-Path $c) { $browser = $c; break }
    }

    if (-not $browser) {
        Write-Host "Kein Edge/Chrome gefunden - oeffne im Standardbrowser (normales Fenster mit Menue/Rahmen)." -ForegroundColor Yellow
        Start-Process $mainUrl
    } else {
        Add-Type -AssemblyName System.Windows.Forms
        $allScreens = [System.Windows.Forms.Screen]::AllScreens
        $primary    = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $secondary  = $allScreens | Where-Object { -not $_.Primary } | Select-Object -First 1

        Write-Host "Erkannte Bildschirme:" -ForegroundColor DarkGray
        for ($i = 0; $i -lt $allScreens.Count; $i++) {
            $s   = $allScreens[$i]
            $tag = if ($s.Primary) { " <- Windows-Primary" } else { "" }
            Write-Host ("  [{0}] {1}x{2} bei ({3},{4}){5}" -f $i, $s.Bounds.Width, $s.Bounds.Height, $s.Bounds.X, $s.Bounds.Y, $tag) -ForegroundColor DarkGray
        }

        $commonArgs = @(
            "--no-first-run"
            "--no-default-browser-check"
            "--disable-translate"
            "--disable-infobars"
            "--disable-session-crashed-bubble"
            "--disable-extensions"
            "--incognito"
            "--noerrdialogs"
            "--kiosk"    # kein Fensterrahmen/Menue/Adressleiste; respektiert window-position/-size
        )
        $profileRoot = Join-Path $env:TEMP "stocktv-compiled"

        if (-not $Dual) {
            # --- 4a. Nur ein Fenster, randlos --------------------------------
            $mIdx = if ($MainDisplayIndex -ge 0) { $MainDisplayIndex } else { 0 }
            if ($mIdx -ge $allScreens.Count) {
                Write-Error "MainDisplayIndex $mIdx ungueltig (verfuegbar: 0..$($allScreens.Count - 1))."
                exit 1
            }
            $b        = if ($MainDisplayIndex -ge 0) { $allScreens[$mIdx].Bounds } else { [System.Drawing.Rectangle]::new($primary.X, $primary.Y, $primary.Width, $primary.Height) }
            $mainPos  = "$($b.X),$($b.Y)"
            $mainSize = "$($b.Width),$($b.Height)"
            $mainProfile = Join-Path $profileRoot "main"

            Write-Host "Oeffne Fenster: $mainUrl"
            Start-Process -FilePath $browser -ArgumentList (@(
                "--user-data-dir=$mainProfile"
                "--window-position=$mainPos"
                "--window-size=$mainSize"
            ) + $commonArgs + @($mainUrl)) | Out-Null
        } else {
            # --- 4b. Zwei Fenster im Kiosk-Modus, wie test-dualdisplay.ps1 ---
            if ($MainDisplayIndex -ge 0 -or $MirrorDisplayIndex -ge 0) {
                # Explizite Zuordnung ueber Index, unabhaengig von Windows-Primary
                $mIdx = $MainDisplayIndex
                if ($mIdx -lt 0) { $mIdx = 0 }
                if ($mIdx -ge $allScreens.Count) {
                    Write-Error "MainDisplayIndex $mIdx ungueltig (verfuegbar: 0..$($allScreens.Count - 1))."
                    exit 1
                }
                $mainBounds = $allScreens[$mIdx].Bounds
                $mainPos    = "$($mainBounds.X),$($mainBounds.Y)"
                $mainSize   = "$($mainBounds.Width),$($mainBounds.Height)"

                if ($MirrorDisplayIndex -ge 0) {
                    if ($MirrorDisplayIndex -ge $allScreens.Count) {
                        Write-Error "MirrorDisplayIndex $MirrorDisplayIndex ungueltig (verfuegbar: 0..$($allScreens.Count - 1))."
                        exit 1
                    }
                    $mirrBounds = $allScreens[$MirrorDisplayIndex].Bounds
                    $mirrPos    = "$($mirrBounds.X),$($mirrBounds.Y)"
                    $mirrSize   = "$($mirrBounds.Width),$($mirrBounds.Height)"
                } else {
                    # Kein Mirror-Index angegeben -> rechte Haelfte des Hauptbildschirms
                    $half     = [int]($mainBounds.Width / 2)
                    $mainSize = "$half,$($mainBounds.Height)"
                    $mirrPos  = "$($mainBounds.X + $half),$($mainBounds.Y)"
                    $mirrSize = "$half,$($mainBounds.Height)"
                }
                Write-Host "Bildschirmauswahl: Haupt=[$mIdx] Spiegel=$(if ($MirrorDisplayIndex -ge 0) { "[$MirrorDisplayIndex]" } else { "rechte Haelfte von [$mIdx]" })" -ForegroundColor DarkGray
            }
            elseif ($SecondScreen -and $secondary) {
                $b = $secondary.Bounds
                $mainPos  = "$($primary.X),$($primary.Y)"
                $mainSize = "$($primary.Width),$($primary.Height)"
                $mirrPos  = "$($b.X),$($b.Y)"
                $mirrSize = "$($b.Width),$($b.Height)"
            } else {
                if ($SecondScreen) { Write-Host "Kein zweiter Bildschirm erkannt - Fenster werden nebeneinander gelegt." -ForegroundColor Yellow }
                $half     = [int]($primary.Width / 2)
                $mainPos  = "$($primary.X),$($primary.Y)"
                $mainSize = "$half,$($primary.Height)"
                $mirrPos  = "$($primary.X + $half),$($primary.Y)"
                $mirrSize = "$half,$($primary.Height)"
            }

            $mainProfile   = Join-Path $profileRoot "main"
            $mirrorProfile = Join-Path $profileRoot "mirror"

            # Spiegel zuerst, Hauptfenster danach -> das Hauptfenster hat am Ende
            # den Tastatur-Fokus, dort funktioniert die Numpad-Eingabe.
            Write-Host "Oeffne Spiegel-Fenster:  $mirrorUrl"
            Start-Process -FilePath $browser -ArgumentList (@(
                "--user-data-dir=$mirrorProfile"
                "--window-position=$mirrPos"
                "--window-size=$mirrSize"
            ) + $commonArgs + @($mirrorUrl)) | Out-Null

            Start-Sleep -Seconds 2

            Write-Host "Oeffne Haupt-Fenster:    $mainUrl"
            Start-Process -FilePath $browser -ArgumentList (@(
                "--user-data-dir=$mainProfile"
                "--window-position=$mainPos"
                "--window-size=$mainSize"
            ) + $commonArgs + @($mainUrl)) | Out-Null
        }
    }
}

Write-Host ""
Write-Host "App laeuft im Hintergrund (PID $($app.Id))." -ForegroundColor Green
Write-Host "Bei Fehlern im Browser: Konsolen-Log pruefen -> $logOut (bzw. $logErr)" -ForegroundColor DarkGray
Write-Host "Zum Beenden: Stop-Process -Id $($app.Id)  (oder Task-Manager)" -ForegroundColor DarkGray