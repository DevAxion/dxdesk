@echo off
REM ============================================================
REM  MillaRemote Client - GPO Startup / Machine deployment
REM  Kompüter işə düşəndə klienti kopyalayır, firewall açır və
REM  hər istifadəçi girişində avtomatik başlaması üçün qeyd edir.
REM ============================================================

REM --- Mənbə paylaşımı (öz NETLOGON/paylaşımınıza uyğunlaşdırın) ---
set "SRC=\DOMAIN-SERVER\NETLOGON\MillaRemote"
set "DEST=%ProgramData%\MillaRemote"

if not exist "%DEST%" mkdir "%DEST%"

REM --- Faylları yalnız dəyişəndə kopyala ---
robocopy "%SRC%" "%DEST%" MillaRemote.Client.exe appsettings.json /XO /R:2 /W:5 >nul

REM --- Firewall: admin viewer-in qoşulması üçün 7000 portu (inbound) ---
netsh advfirewall firewall show rule name="MillaRemote Stream 7000" >nul 2>&1
if errorlevel 1 (
  netsh advfirewall firewall add rule name="MillaRemote Stream 7000" dir=in action=allow protocol=TCP localport=7000 >nul
)

REM --- Hər istifadəçi girişində avtomatik başlatma (HKLM Run) ---
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v MillaRemote /t REG_SZ /d "\"%DEST%\MillaRemote.Client.exe\"" /f >nul

REM --- İndi də başlat (kompüter yenidən başlamadan işləsin) ---
start "" "%DEST%\MillaRemote.Client.exe"

exit /b 0
