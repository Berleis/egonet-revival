@echo off
setlocal

cd /d "%~dp0..\.."
dotnet run --project "%CD%\src\RaceNetShowdown.TlsProbe\RaceNetShowdown.TlsProbe.csproj"
exit /b %ERRORLEVEL%
