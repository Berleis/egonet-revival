@echo off
setlocal
cd /d "%~dp0"

set /p RACENET_SERVER=RaceNet server IP or host:
if "%RACENET_SERVER%"=="" (
  echo Server IP/host is required.
  pause
  exit /b 1
)

set CERT_DIR=%~dp0src\RaceNetShowdown.Server\certs
set CERT_PATH=%CERT_DIR%\codemasters-local-root-ca.cer

if not exist "%CERT_DIR%" mkdir "%CERT_DIR%"

powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri 'http://%RACENET_SERVER%/racenet-root-ca.cer' -OutFile '%CERT_PATH%'"
if errorlevel 1 pause & exit /b %errorlevel%

dotnet run --project "%~dp0src\RaceNetShowdown.Patcher" -- patch
pause
