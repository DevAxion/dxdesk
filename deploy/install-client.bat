@echo off
REM ============================================================
REM  MillaRemote Client - GPO Computer Startup skripti
REM  SYSTEM kimi işləyir: faylı kopyalayır, firewall açır və
REM  hər istifadəçi girişində başlaması üçün HKLM Run-a yazır.
REM ============================================================

set "SRC=\milladc01\software_deploy\dist-client"
set "DEST=%ProgramData%\MillaRemote"

if not exist "%DEST%" mkdir "%DEST%"

REM --- Faylları yalnız dəyişəndə kopyala ---
robocopy "%SRC%" "%DEST%" MillaRemote.Client.exe appsettings.json /XO /R:2 /W:5 >nul

REM --- Firewall: admin viewer-in qoşulması üçün 7000 (inbound) ---
netsh advfirewall firewall show rule name="MillaRemote Stream 7000" >nul 2>&1
if errorlevel 1 (
  netsh advfirewall firewall add rule name="MillaRemote Stream 7000" dir=in action=allow protocol=TCP localport=7000 >nul
)

REM --- Hər istifadəçi girişində avtomatik başlatma (HKLM Run) ---
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v MillaRemote /t REG_SZ /d "\"%DEST%\MillaRemote.Client.exe\"" /f >nul

exit /b 0
