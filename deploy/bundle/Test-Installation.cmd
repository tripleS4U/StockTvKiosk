@echo off
rem ---------------------------------------------------------------------------------------
rem  Startet Test-Installation.ps1 ohne Aenderung der ExecutionPolicy.
rem  Siehe Install-StockTV.cmd fuer die Begruendung.
rem
rem      Test-Installation.cmd -ApiKey "der-schluessel"
rem ---------------------------------------------------------------------------------------

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Test-Installation.ps1" %*

if "%~1"=="" pause
