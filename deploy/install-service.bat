@echo off
setlocal EnableExtensions
REM ============================================================
REM  MillaRemote Service - GPO Computer Startup ilə quraşdırma
REM  SYSTEM kimi işləyir: xidməti + agent-i kopyalayır, LocalSystem
REM  Windows xidməti kimi qeydiyyatdan keçirir və başladır.
REM  Xidmət SYSTEM olduğu üçün UAC-siz quraşdırma/idarə mümkün olur.
REM ============================================================

set "SRC=\\milladc01\software_deploy\service-package"
set "DEST=%ProgramData%\MillaRemote"
set "LOG=%DEST%\install.log"

if not exist "%DEST%" mkdir "%DEST%"
echo [%date% %time%] service install basladi, SRC=%SRC%, PC=%COMPUTERNAME%>> "%LOG%"

if not exist "%SRC%\MillaRemote.Service.exe" (
  echo XETA: menbe elcatan deyil -^> %SRC%>> "%LOG%"
  goto :end
)

REM --- Yenileme ucun: isleyen xidmeti dayandir (exe kilidli qalmasin) ---
sc stop MillaRemote >nul 2>&1
timeout /t 3 /nobreak >nul 2>&1

REM --- Fayllari kopyala (xidmet + agent + appsettings) ---
robocopy "%SRC%" "%DEST%" MillaRemote.Service.exe MillaRemote.Client.exe appsettings.json /R:2 /W:3>> "%LOG%" 2>&1
echo robocopy exit=%ERRORLEVEL%>> "%LOG%"

if not exist "%DEST%\MillaRemote.Service.exe" (
  echo XETA: xidmet exe kopyalanmadi.>> "%LOG%"
  goto :end
)

REM --- Firewall: agent viewer-in qosulmasi ucun 7000 (inbound) ---
netsh advfirewall firewall show rule name="MillaRemote Stream 7000" >nul 2>&1
if errorlevel 1 netsh advfirewall firewall add rule name="MillaRemote Stream 7000" dir=in action=allow protocol=TCP localport=7000>> "%LOG%" 2>&1

REM --- Kohne (client-only) autostart-i temizle ---
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v MillaRemote /f >nul 2>&1
schtasks /delete /tn "MillaRemoteKick" /f >nul 2>&1

REM --- Xidmeti qeydiyyatdan kecir (yoxdursa) ve basla ---
sc query MillaRemote >nul 2>&1
if errorlevel 1 (
  sc create MillaRemote binPath= "\"%DEST%\MillaRemote.Service.exe\"" start= auto obj= LocalSystem DisplayName= "MillaRemote Agent">> "%LOG%" 2>&1
  sc description MillaRemote "MillaRemote uzaqdan destek xidmeti">> "%LOG%" 2>&1
)
sc start MillaRemote>> "%LOG%" 2>&1

echo [%date% %time%] UGURLU: xidmet quraşdirildi ve baslatildi.>> "%LOG%"

:end
echo [%date% %time%] bitdi>> "%LOG%"
endlocal
exit /b 0
