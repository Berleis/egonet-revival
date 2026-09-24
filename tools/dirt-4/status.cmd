@echo off
setlocal

cd /d "%~dp0..\.."

set "GAME_PATH=%~1"
if "%GAME_PATH%"=="" set "GAME_PATH=D:\SteamLibrary\steamapps\common\DiRT 4"

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- status --game dirt-4 "%GAME_PATH%"
pause
endlocal
