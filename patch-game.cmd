@echo off
cd /d "%~dp0"
dotnet run --project "%~dp0src\RaceNetShowdown.Server\RaceNetShowdown.Server.csproj" -- --regenerate-certs
if errorlevel 1 pause & exit /b %errorlevel%
dotnet run --project "%~dp0src\RaceNetShowdown.Patcher" -- patch
pause
