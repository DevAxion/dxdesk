@echo off
setlocal EnableExtensions
REM ============================================================
REM  SRXDesk Service quraşdırma (LocalSystem Windows xidməti)
REM  Fayllari OZ folderindan kopyalayir (bu .bat harada olsa,
REM  SRXDesk.Service.exe, SRXDesk.Client.exe, appsettings.json
REM  onunla yanasi olmalidir).
REM  Administrator kimi ishe salin (sag klik -> Run as administrator).
REM  Xidmet SYSTEM oldugu ucun UAC-siz idare/qurashdirma mumkun olur.
REM ============================================================

REM --- Menbe: evvel bu skriptin folderi, tapilmasa paylasim (GPO/SYSVOL ucun) ---
set "SRC=%~dp0"
if "%SRC:~-1%"=="\" set "SRC=%SRC:~0,-1%"
if not exist "%SRC%\SRXDesk.Service.exe" set "SRC=\\milladc01\software_deploy\dist-client"

set "DEST=%ProgramData%\SRXDesk"
set "LOG=%DEST%\install.log"

if not exist "%DEST%" mkdir "%DEST%"
echo [%date% %time%] service install basladi, SRC=%SRC%, PC=%COMPUTERNAME%>> "%LOG%"

if not exist "%SRC%\SRXDesk.Service.exe" (
  echo XETA: menbe tapilmadi -^> %SRC%\SRXDesk.Service.exe
  echo Faylar bu .bat ile yanasi olmalidir.
  echo XETA: menbe tapilmadi -^> %SRC%>> "%LOG%"
  goto :end
)

REM --- Yenileme ucun: isleyen xidmeti dayandir ---
sc stop SRXDesk >nul 2>&1
timeout /t 3 /nobreak >nul 2>&1

REM --- Fayllari kopyala ---
robocopy "%SRC%" "%DEST%" SRXDesk.Service.exe SRXDesk.Client.exe appsettings.json /R:2 /W:3>> "%LOG%" 2>&1
echo robocopy exit=%ERRORLEVEL%>> "%LOG%"

if not exist "%DEST%\SRXDesk.Service.exe" (
  echo XETA: xidmet exe kopyalanmadi. Bax: %LOG%
  goto :end
)

REM --- Firewall: 7000 (inbound) ---
netsh advfirewall firewall show rule name="SRXDesk Stream 7000" >nul 2>&1
if errorlevel 1 netsh advfirewall firewall add rule name="SRXDesk Stream 7000" dir=in action=allow protocol=TCP localport=7000>> "%LOG%" 2>&1

REM --- Kohne (client-only) autostart-i temizle ---
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v SRXDesk /f >nul 2>&1
schtasks /delete /tn "SRXDeskKick" /f >nul 2>&1

REM --- Xidmeti qeydiyyatdan kecir (yoxdursa) ---
sc query SRXDesk >nul 2>&1
if errorlevel 1 (
  sc create SRXDesk binPath= "\"%DEST%\SRXDesk.Service.exe\"" start= auto obj= LocalSystem DisplayName= "SRXDesk Agent">> "%LOG%" 2>&1
  sc description SRXDesk "SRXDesk uzaqdan destek xidmeti">> "%LOG%" 2>&1
)

REM --- Xidmeti basla ---
sc start SRXDesk>> "%LOG%" 2>&1

echo.
echo UGURLU: xidmet qurashdirildi ve baslatildi.
echo Yoxlamaq ucun:  sc query SRXDesk
echo [%date% %time%] UGURLU>> "%LOG%"

:end
echo [%date% %time%] bitdi>> "%LOG%"
echo.
pause
endlocal
exit /b 0
