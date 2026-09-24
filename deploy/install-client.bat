@echo off
setlocal EnableExtensions
REM ============================================================
REM  DXDesk Client - GPO Computer Startup skripti (loglu)
REM  Log: %ProgramData%\DXDesk\install.log
REM ============================================================

set "SRC=\milladc01\software_deploy\dist-client"
set "DEST=%ProgramData%\DXDesk"
set "LOG=%DEST%\install.log"

if not exist "%DEST%" mkdir "%DEST%"
echo ================================================>> "%LOG%"
echo [%date% %time%] install basladi>> "%LOG%"
echo SRC=%SRC%>> "%LOG%"
echo istifadeci: %USERNAME%  komputer: %COMPUTERNAME%>> "%LOG%"

REM --- Menbe elcatandirmi? ---
if not exist "%SRC%\DXDesk.Client.exe" (
  echo XETA: menbe tapilmadi -^> %SRC%\DXDesk.Client.exe>> "%LOG%"
  echo XETA: "%SRC%" elcatan deyil. Paylasim icazeleri / sebeke.
  echo Ətraflı: %LOG%
  goto :end
)

REM --- Kopyala (loga yaz) ---
robocopy "%SRC%" "%DEST%" DXDesk.Client.exe appsettings.json /R:2 /W:3>> "%LOG%" 2>&1
echo robocopy exit=%ERRORLEVEL%>> "%LOG%"

REM --- Yoxla ---
if not exist "%DEST%\DXDesk.Client.exe" (
  echo XETA: kopyalama alinmadi.>> "%LOG%"
  echo XETA: fayl kopyalanmadi. Bax: %LOG%
  goto :end
)

REM --- Firewall: 7000 inbound ---
netsh advfirewall firewall show rule name="DXDesk Stream 7000" >nul 2>&1
if errorlevel 1 netsh advfirewall firewall add rule name="DXDesk Stream 7000" dir=in action=allow protocol=TCP localport=7000>> "%LOG%" 2>&1

REM --- Autostart (HKLM Run) ---
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v DXDesk /t REG_SZ /d "\"%DEST%\DXDesk.Client.exe\"" /f>> "%LOG%" 2>&1

echo [%date% %time%] UGURLU: quraşdırma tamamlandi.>> "%LOG%"
echo UGURLU. Client %DEST% qovlugunda. Log: %LOG%

:end
echo [%date% %time%] bitdi>> "%LOG%"
endlocal
exit /b 0
