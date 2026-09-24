@echo off
setlocal EnableExtensions
REM ============================================================
REM  DXDesk Service quraşdırma (LocalSystem Windows xidməti)
REM  Fayllari OZ folderindan kopyalayir (bu .bat harada olsa,
REM  DXDesk.Service.exe, DXDesk.Client.exe, appsettings.json
REM  onunla yanasi olmalidir).
REM  Administrator kimi ishe salin (sag klik -> Run as administrator).
REM  Xidmet SYSTEM oldugu ucun UAC-siz idare/qurashdirma mumkun olur.
REM ============================================================

REM --- Menbe: evvel bu skriptin folderi, tapilmasa paylasim (GPO/SYSVOL ucun) ---
set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
if not exist "%SRC%\DXDesk.Service.exe" set "SRC=\\milladc01\software_deploy\dist-client"

set "DEST=%ProgramData%\DXDesk"
set "LOG=%DEST%\install.log"

if not exist "%DEST%" mkdir "%DEST%"
echo [%date% %time%] service install basladi, SRC=%SRC%, PC=%COMPUTERNAME%>> "%LOG%"

if not exist "%SRC%\DXDesk.Service.exe" (
  echo XETA: menbe tapilmadi -^> %SRC%\DXDesk.Service.exe
  echo Faylar bu .bat ile yanasi olmalidir.
  echo XETA: menbe tapilmadi -^> %SRC%>> "%LOG%"
  goto :end
)

REM --- Yenileme ucun: isleyen xidmeti dayandir ---
sc stop DXDesk >nul 2>&1
timeout /t 3 /nobreak >nul 2>&1

REM --- Fayllari kopyala ---
robocopy "%SRC%" "%DEST%" DXDesk.Service.exe DXDesk.Client.exe appsettings.json /R:2 /W:3>> "%LOG%" 2>&1
echo robocopy exit=%ERRORLEVEL%>> "%LOG%"

if not exist "%DEST%\DXDesk.Service.exe" (
  echo XETA: xidmet exe kopyalanmadi. Bax: %LOG%
  goto :end
)

REM --- Firewall: 7000 (inbound) ---
netsh advfirewall firewall show rule name="DXDesk Stream 7000" >nul 2>&1
if errorlevel 1 netsh advfirewall firewall add rule name="DXDesk Stream 7000" dir=in action=allow protocol=TCP localport=7000>> "%LOG%" 2>&1

REM --- Kohne (client-only) autostart-i temizle ---
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v DXDesk /f >nul 2>&1
schtasks /delete /tn "DXDeskKick" /f >nul 2>&1

REM --- MIQRASIYA: kohne MillaRemote deploymentini tamamile temizle ---
sc stop MillaRemote >nul 2>&1
sc delete MillaRemote >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v MillaRemote /f >nul 2>&1
schtasks /delete /tn "MillaRemoteKick" /f >nul 2>&1
taskkill /im MillaRemote.Client.exe /f >nul 2>&1
taskkill /im MillaRemote.Service.exe /f >nul 2>&1
timeout /t 2 /nobreak >nul 2>&1
rmdir /s /q "%ProgramData%\MillaRemote" >nul 2>&1
netsh advfirewall firewall delete rule name="MillaRemote Stream 7000" >nul 2>&1

REM --- MIQRASIYA: kohne SRXDesk deploymentini tamamile temizle (dxdesk-e kecid) ---
sc stop SRXDesk >nul 2>&1
sc delete SRXDesk >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v SRXDesk /f >nul 2>&1
schtasks /delete /tn "SRXDeskKick" /f >nul 2>&1
taskkill /im SRXDesk.Client.exe /f >nul 2>&1
taskkill /im SRXDesk.Service.exe /f >nul 2>&1
timeout /t 2 /nobreak >nul 2>&1
rmdir /s /q "%ProgramData%\SRXDesk" >nul 2>&1
netsh advfirewall firewall delete rule name="SRXDesk Stream 7000" >nul 2>&1

REM --- Xidmeti qeydiyyatdan kecir (yoxdursa) ---
sc query DXDesk >nul 2>&1
if errorlevel 1 (
  sc create DXDesk binPath= "\"%DEST%\DXDesk.Service.exe\"" start= auto obj= LocalSystem DisplayName= "DXDesk Agent">> "%LOG%" 2>&1
  sc description DXDesk "DXDesk uzaqdan destek xidmeti">> "%LOG%" 2>&1
)

REM --- Xidmeti basla ---
sc start DXDesk>> "%LOG%" 2>&1

echo.
echo UGURLU: xidmet qurashdirildi ve baslatildi.
echo Yoxlamaq ucun:  sc query DXDesk
echo [%date% %time%] UGURLU>> "%LOG%"

:end
echo [%date% %time%] bitdi>> "%LOG%"
echo.
pause
endlocal
exit /b 0
