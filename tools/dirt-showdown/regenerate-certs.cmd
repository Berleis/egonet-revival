@echo off
setlocal

cd /d "%~dp0..\.."
dotnet run --project "%CD%\src\RaceNetShowdown.Server\RaceNetShowdown.Server.csproj" -- --regenerate-certs
exit /b %ERRORLEVEL%
