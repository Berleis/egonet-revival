@echo off
setlocal

cd /d "%~dp0..\.."

set "PROJECT=%CD%\src\F12018EventActivator\F12018EventActivator.csproj"
set "EXE=%CD%\src\F12018EventActivator\bin\Debug\net10.0\F12018EventActivator.exe"

dotnet build "%PROJECT%" --nologo
if errorlevel 1 (
  pause
  exit /b 1
)

"%EXE%" %*
pause
endlocal
