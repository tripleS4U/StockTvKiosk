<#
.SYNOPSIS
    Entfernt die von Install-KioskTask.ps1 angelegten geplanten Tasks.
#>
[CmdletBinding()]
param(
    [string]$TaskFolder = "StockTV",
    [switch]$StopRunning
)

$ErrorActionPreference = "Stop"

$tasks = Get-ScheduledTask -TaskPath "\$TaskFolder\" -ErrorAction SilentlyContinue
if (-not $tasks) {
    Write-Host "Keine Tasks unter \$TaskFolder\ gefunden."
    return
}

foreach ($task in $tasks) {
    if ($StopRunning -and $task.State -eq "Running") {
        Stop-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath
    }

    Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -Confirm:$false
    Write-Host "Entfernt: $($task.TaskPath)$($task.TaskName)" -ForegroundColor Green
}
