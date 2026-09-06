@echo off
setlocal

set "ACTIVATOR=%~dp0EgoNet Revival - F1 2018 Event Activator.exe"

if not exist "%ACTIVATOR%" (
  echo Missing activator executable:
  echo   %ACTIVATOR%
  echo Download the full F1 2018 release package, including the .exe.
  pause
  exit /b 1
)

"%ACTIVATOR%" %*
pause
endlocal
