@echo off
REM ============================================================
REM  MillaRemote Client - GPO Startup / Machine deployment
REM  Bu skript kompüter işə düşəndə klienti kopyalayır və
REM  hər istifadəçi girişində avtomatik başlaması üçün qeyd edir.
REM ============================================================

set "SRC=\DOMAIN-SERVER\NETLOGON\MillaRemote"
set "DEST=%ProgramData%\MillaRemote"

if not exist "%DEST%" mkdir "%DEST%"

REM Yalnız dəyişiklik olduqda kopyala.
robocopy "%SRC%" "%DEST%" MillaRemote.Client.exe appsettings.json /XO /R:2 /W:5 >nul

REM Hər istifadəçi girişində avtomatik başlatma (HKLM Run).
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v MillaRemote /t REG_SZ /d "\"%DEST%\MillaRemote.Client.exe\"" /f >nul

exit /b 0
