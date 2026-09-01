@echo off
setlocal

cd /d "%~dp0..\.."

set "GAME_PATH=%~1"
if "%GAME_PATH%"=="" set "GAME_PATH=D:\SteamLibrary\steamapps\common\grid 2"

echo EgoNet Revival - GRID 2 local patch
echo Game path: %GAME_PATH%
echo.

dotnet run --project "%CD%\src\RaceNetShowdown.Server\RaceNetShowdown.Server.csproj" -- --regenerate-certs
if errorlevel 1 pause & exit /b %ERRORLEVEL%

certutil -addstore -f Root "%CD%\src\RaceNetShowdown.Server\certs\codemasters-local-root-ca.cer"
if errorlevel 1 (
  echo.
  echo Failed to install the local root certificate. Run this script as Administrator.
  pause
  exit /b %ERRORLEVEL%
)

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- patch --game grid-2 "%GAME_PATH%"
if errorlevel 1 pause & exit /b %ERRORLEVEL%

echo.
echo Done. Start the GRID 2 discovery server, then open GRID 2.
pause
endlocal
