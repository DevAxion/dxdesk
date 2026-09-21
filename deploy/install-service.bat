@echo off
setlocal EnableExtensions
REM ============================================================
REM  MillaRemote Service quraşdırma (LocalSystem Windows xidməti)
REM  Fayllari OZ folderindan kopyalayir (bu .bat harada olsa,
REM  MillaRemote.Service.exe, MillaRemote.Client.exe, appsettings.json
REM  onunla yanasi olmalidir).
REM  Administrator kimi ishe salin (sag klik -> Run as administrator).
REM  Xidmet SYSTEM oldugu ucun UAC-siz idare/qurashdirma mumkun olur.
REM ============================================================

REM --- Menbe: evvel bu skriptin folderi, tapilmasa paylasim (GPO/SYSVOL ucun) ---
set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
if not exist "%SRC%\MillaRemote.Service.exe" set "SRC=\\milladc01\software_deploy\dist-client"

set "DEST=%ProgramData%\MillaRemote"
set "LOG=%DEST%\install.log"

if not exist "%DEST%" mkdir "%DEST%"
echo [%date% %time%] service install basladi, SRC=%SRC%, PC=%COMPUTERNAME%>> "%LOG%"

if not exist "%SRC%\MillaRemote.Service.exe" (
  echo XETA: menbe tapilmadi -^> %SRC%\MillaRemote.Service.exe
  echo Faylar bu .bat ile yanasi olmalidir.
  echo XETA: menbe tapilmadi -^> %SRC%>> "%LOG%"
  goto :end
)

REM --- Yenileme ucun: isleyen xidmeti dayandir ---
sc stop MillaRemote >nul 2>&1
timeout /t 3 /nobreak >nul 2>&1

REM --- Fayllari kopyala ---
robocopy "%SRC%" "%DEST%" MillaRemote.Service.exe MillaRemote.Client.exe appsettings.json /R:2 /W:3>> "%LOG%" 2>&1
echo robocopy exit=%ERRORLEVEL%>> "%LOG%"

if not exist "%DEST%\MillaRemote.Service.exe" (
  echo XETA: xidmet exe kopyalanmadi. Bax: %LOG%
  goto :end
)

REM --- Firewall: 7000 (inbound) ---
netsh advfirewall firewall show rule name="MillaRemote Stream 7000" >nul 2>&1
if errorlevel 1 netsh advfirewall firewall add rule name="MillaRemote Stream 7000" dir=in action=allow protocol=TCP localport=7000>> "%LOG%" 2>&1

REM --- Kohne (client-only) autostart-i temizle ---
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v MillaRemote /f >nul 2>&1
schtasks /delete /tn "MillaRemoteKick" /f >nul 2>&1

REM --- Xidmeti qeydiyyatdan kecir (yoxdursa) ---
sc query MillaRemote >nul 2>&1
if errorlevel 1 (
  sc create MillaRemote binPath= "\"%DEST%\MillaRemote.Service.exe\"" start= auto obj= LocalSystem DisplayName= "MillaRemote Agent">> "%LOG%" 2>&1
  sc description MillaRemote "MillaRemote uzaqdan destek xidmeti">> "%LOG%" 2>&1
)

REM --- Xidmeti basla ---
sc start MillaRemote>> "%LOG%" 2>&1

echo.
echo UGURLU: xidmet qurashdirildi ve baslatildi.
echo Yoxlamaq ucun:  sc query MillaRemote
echo [%date% %time%] UGURLU>> "%LOG%"

:end
echo [%date% %time%] bitdi>> "%LOG%"
echo.
pause
endlocal
exit /b 0
