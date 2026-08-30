@echo off
rem ---------------------------------------------------------------------------------------
rem  Startet Install-StockTV.ps1, ohne dass die ExecutionPolicy des Rechners angefasst
rem  werden muss.
rem
rem  Ohne diesen Umweg braeuchte es auf einem frischen Windows erst
rem      Set-ExecutionPolicy RemoteSigned
rem  - eine systemweite Aenderung fuer eine einmalige Installation. "-ExecutionPolicy
rem  Bypass" gilt dagegen nur fuer diesen einen Aufruf. Es umgeht ausserdem die
rem  Kennzeichnung "aus dem Internet", die Dateien mitbringen, wenn das Paket ueber ein
rem  Netzlaufwerk oder ein ZIP kam - sonst verweigert selbst RemoteSigned den Dienst.
rem
rem  Alle Parameter werden durchgereicht:
rem      Install-StockTV.cmd -Mode Managed -BahnNummer 3
rem ---------------------------------------------------------------------------------------

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-StockTV.ps1" %*

rem Beim Doppelklick faellt das Fenster sonst zu, bevor irgendjemand die Meldungen liest.
if "%~1"=="" pause
