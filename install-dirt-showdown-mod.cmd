@echo off
setlocal
set "LOCAL_INSTALLER=%~dp0games\dirt-showdown\install-dirt-showdown-mod.cmd"
set "REMOTE_INSTALLER=https://raw.githubusercontent.com/Berleis/egonet-revival/main/games/dirt-showdown/install-dirt-showdown-mod.cmd"

if exist "%LOCAL_INSTALLER%" (
    call "%LOCAL_INSTALLER%" %*
    exit /b %ERRORLEVEL%
)

echo EgoNet Revival - DiRT Showdown installer launcher
echo Local game package was not found next to this file.
echo Downloading the DiRT Showdown installer from:
echo %REMOTE_INSTALLER%

set "TEMP_INSTALLER=%TEMP%\egonet-revival-install-dirt-showdown-mod.cmd"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%REMOTE_INSTALLER%' -OutFile '%TEMP_INSTALLER%' -UseBasicParsing"
if errorlevel 1 (
    echo Failed to download the DiRT Showdown installer.
    pause
    exit /b 1
)

call "%TEMP_INSTALLER%" %*
exit /b %ERRORLEVEL%
