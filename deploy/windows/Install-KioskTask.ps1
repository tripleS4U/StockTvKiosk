<#
.SYNOPSIS
    Registriert den StockTV-Kiosk als geplanten Task, der bei der Anmeldung startet.

.DESCRIPTION
    Legt einen Task pro Anwendung an - analog zu den beiden systemd-Units unter Linux:
      StockTV\StockTV         (optional) startet StockTvBlazor
      StockTV\StockTV-Kiosk   startet die Kiosk-Anzeige

    Der Task laeuft unter dem aktuellen Benutzer mit normalen Rechten; Administrator wird
    nicht gebraucht.

    Zum Neustartverhalten: Der Task Scheduler startet von sich aus nur nach einem FEHLER
    neu (RestartCount), nicht nach einem sauberen Beenden - anders als systemd mit
    Restart=always. Damit der Kiosk sich auch nach Shift+Esc wieder faengt, bekommt der
    Trigger zusaetzlich eine Wiederholung im Minutentakt, kombiniert mit
    MultipleInstances=IgnoreNew: laeuft der Kiosk noch, wird der neue Start verworfen;
    ist er weg, startet er erneut. Mit -NoKeepAlive laesst sich das abschalten.

.EXAMPLE
    .\Install-KioskTask.ps1 -KioskPath C:\StockTV\Kiosk\StockTvKiosk.exe

.EXAMPLE
    .\Install-KioskTask.ps1 -KioskPath C:\StockTV\Kiosk\StockTvKiosk.exe `
                            -StockTvPath C:\tmp\StockTV-Blazor\bin\Release\net10.0\win-x64\StockTvBlazor.exe

.EXAMPLE
    .\Install-KioskTask.ps1 -KioskPath C:\StockTV\Kiosk\StockTvKiosk.exe -WhatIfOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$KioskPath,

    [string]$StockTvPath,

    [string]$KioskArguments = "",

    [string]$TaskFolder = "StockTV",

    [string]$KioskTaskName = "StockTV-Kiosk",

    [string]$StockTvTaskName = "StockTV",

    # Wartezeit nach der Anmeldung, bevor der Kiosk startet. Gibt der grafischen
    # Sitzung und - falls mitinstalliert - StockTV einen Vorsprung.
    [int]$KioskDelaySeconds = 20,

    # Wiederholungs-Trigger weglassen: dann bleibt der Kiosk nach Shift+Esc unten,
    # bis der Benutzer sich neu anmeldet.
    [switch]$NoKeepAlive,

    # Nur anzeigen, was registriert wuerde.
    [switch]$WhatIfOnly
)

$ErrorActionPreference = "Stop"

function Resolve-Executable {
    param([string]$Path, [string]$Label)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label nicht gefunden: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.PSIsContainer) {
        throw "$Label muss auf eine .exe zeigen, nicht auf ein Verzeichnis: $Path"
    }

    return $item
}

function New-KioskTriggers {
    param([string]$UserId, [int]$DelaySeconds, [bool]$KeepAlive)

    $triggers = @()

    $logon = New-ScheduledTaskTrigger -AtLogOn -User $UserId
    if ($DelaySeconds -gt 0) {
        # Delay ist im CIM-Objekt nur als ISO-8601-Dauer setzbar.
        $logon.Delay = "PT${DelaySeconds}S"
    }
    $triggers += $logon

    if ($KeepAlive) {
        # Der Keep-Alive braucht einen EIGENEN Trigger. Eine Wiederholung haengt am Feuern
        # ihres Triggers - haengt man sie an den AtLogOn-Trigger, laeuft sie erst ab der
        # naechsten Anmeldung und ein manuelles Start-ScheduledTask aktiviert sie gar nicht.
        #
        # Ein Once-Trigger auf heute 00:00 liegt in der Vergangenheit und wird zusammen mit
        # StartWhenAvailable sofort nachgeholt; ab da wiederholt er sich im Minutentakt.
        #
        # -RepetitionDuration bleibt bewusst weg: leere Duration = unbegrenzt.
        # [TimeSpan]::MaxValue waere naheliegend, erzeugt aber P99999999DT23H59M59S,
        # was der Task Scheduler als ausserhalb des gueltigen Bereichs zurueckweist.
        $triggers += New-ScheduledTaskTrigger -Once -At (Get-Date).Date `
            -RepetitionInterval (New-TimeSpan -Minutes 1)
    }

    return $triggers
}

function Register-KioskTask {
    param(
        [System.IO.FileInfo]$Executable,
        [string]$Arguments,
        [string]$FullTaskName,
        [string]$Folder,
        [string]$Name,
        [string]$Description,
        [int]$DelaySeconds,
        [bool]$KeepAlive
    )

    $user = "$env:USERDOMAIN\$env:USERNAME"

    $action = if ([string]::IsNullOrWhiteSpace($Arguments)) {
        New-ScheduledTaskAction -Execute $Executable.FullName -WorkingDirectory $Executable.DirectoryName
    } else {
        New-ScheduledTaskAction -Execute $Executable.FullName -Argument $Arguments -WorkingDirectory $Executable.DirectoryName
    }

    $trigger = New-KioskTriggers -UserId $user -DelaySeconds $DelaySeconds -KeepAlive $KeepAlive

    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

    $settings = New-ScheduledTaskSettingsSet `
        -MultipleInstances IgnoreNew `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -DontStopOnIdleEnd `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1)

    if ($WhatIfOnly) {
        Write-Host "[WhatIf] Task '$FullTaskName'" -ForegroundColor Yellow
        Write-Host "         Programm : $($Executable.FullName) $Arguments"
        Write-Host "         Start    : bei Anmeldung von $user, Verzoegerung ${DelaySeconds}s"
        Write-Host "         KeepAlive: $KeepAlive"
        return
    }

    if (Get-ScheduledTask -TaskName $Name -TaskPath "\$Folder\" -ErrorAction SilentlyContinue) {
        Write-Host "Vorhandenen Task '$FullTaskName' ersetzen ..." -ForegroundColor DarkGray
        Unregister-ScheduledTask -TaskName $Name -TaskPath "\$Folder\" -Confirm:$false
    }

    # -ErrorAction Stop ist noetig: Register-ScheduledTask meldet ungueltige Aufgaben-XML
    # als NICHT-terminierenden CIM-Fehler. Ohne diesen Schalter laeuft das Skript weiter
    # und meldet Erfolg, obwohl gar kein Task angelegt wurde.
    $null = Register-ScheduledTask `
        -TaskName $Name `
        -TaskPath "\$Folder\" `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description $Description `
        -ErrorAction Stop

    if (-not (Get-ScheduledTask -TaskName $Name -TaskPath "\$Folder\" -ErrorAction SilentlyContinue)) {
        throw "Task '$FullTaskName' wurde nicht angelegt."
    }

    Write-Host "Registriert: $FullTaskName" -ForegroundColor Green
}

$kiosk = Resolve-Executable -Path $KioskPath -Label "Kiosk-Anwendung"

if ($StockTvPath) {
    $stocktv = Resolve-Executable -Path $StockTvPath -Label "StockTV-Anwendung"

    Register-KioskTask -Executable $stocktv -Arguments "" `
        -FullTaskName "\$TaskFolder\$StockTvTaskName" -Folder $TaskFolder -Name $StockTvTaskName `
        -Description "StockTV Blazor-Anwendung" `
        -DelaySeconds 0 -KeepAlive (-not $NoKeepAlive)
}

Register-KioskTask -Executable $kiosk -Arguments $KioskArguments `
    -FullTaskName "\$TaskFolder\$KioskTaskName" -Folder $TaskFolder -Name $KioskTaskName `
    -Description "StockTV Kiosk-Anzeige" `
    -DelaySeconds $KioskDelaySeconds -KeepAlive (-not $NoKeepAlive)

if (-not $WhatIfOnly) {
    Write-Host ""
    Write-Host "Sofort starten : Start-ScheduledTask -TaskName '$KioskTaskName' -TaskPath '\$TaskFolder\'"
    Write-Host "Status         : Get-ScheduledTask -TaskPath '\$TaskFolder\' | Get-ScheduledTaskInfo"
    Write-Host "Entfernen      : .\Uninstall-KioskTask.ps1"
    if (-not $NoKeepAlive) {
        Write-Host ""
        Write-Host "KeepAlive ist aktiv: der Kiosk kommt nach einem Beenden binnen einer Minute zurueck." -ForegroundColor DarkGray
    }
}
