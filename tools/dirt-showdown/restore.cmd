@echo off
setlocal

cd /d "%~dp0..\.."

dotnet run --project "%CD%\src\RaceNetShowdown.Server\RaceNetShowdown.Server.csproj" -- --regenerate-certs
if errorlevel 1 pause & exit /b %ERRORLEVEL%

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- restore --game dirt-showdown
pause
endlocal
