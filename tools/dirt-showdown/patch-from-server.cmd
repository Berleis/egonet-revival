@echo off
setlocal

cd /d "%~dp0..\.."

set /p RACENET_SERVER=RaceNet server IP or host:
if "%RACENET_SERVER%"=="" (
  echo Server IP/host is required.
  pause
  exit /b 1
)

set "CERT_DIR=%CD%\src\RaceNetShowdown.Server\certs"
set "CERT_PATH=%CERT_DIR%\codemasters-local-root-ca.cer"

if not exist "%CERT_DIR%" mkdir "%CERT_DIR%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri 'http://%RACENET_SERVER%/racenet-root-ca.cer' -OutFile '%CERT_PATH%' -UseBasicParsing"
if errorlevel 1 pause & exit /b %ERRORLEVEL%

certutil -addstore -f Root "%CERT_PATH%"
if errorlevel 1 pause & exit /b %ERRORLEVEL%

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- patch --game dirt-showdown
if errorlevel 1 pause & exit /b %ERRORLEVEL%

echo.
echo Done. Restart DiRT Showdown before testing RaceNet.
pause
endlocal
