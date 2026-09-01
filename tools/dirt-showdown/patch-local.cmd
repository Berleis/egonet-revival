@echo off
setlocal

cd /d "%~dp0..\.."

echo EgoNet Revival - DiRT Showdown local patch
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

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- patch --game dirt-showdown
if errorlevel 1 pause & exit /b %ERRORLEVEL%

echo.
echo Done. Start the local server, then open DiRT Showdown.
pause
endlocal
