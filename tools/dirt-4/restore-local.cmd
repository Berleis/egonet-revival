@echo off
setlocal

cd /d "%~dp0..\.."

set "GAME_PATH=%~1"
if "%GAME_PATH%"=="" set "GAME_PATH=D:\SteamLibrary\steamapps\common\DiRT 4"

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- restore --game dirt-4 "%GAME_PATH%"
if errorlevel 1 pause & exit /b %ERRORLEVEL%

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$hosts=Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'; $lines=[IO.File]::ReadAllLines($hosts); $out=New-Object System.Collections.Generic.List[string]; $inside=$false; foreach($line in $lines){ if($line -eq '# EgoNet Revival DiRT 4'){ $inside=$true; continue }; if($inside -and $line -eq '# End EgoNet Revival DiRT 4'){ $inside=$false; continue }; if(-not $inside){ $out.Add($line) } }; [IO.File]::WriteAllLines($hosts,$out,[Text.Encoding]::ASCII)"

echo DiRT 4 restored. The local root certificate may remain installed and can be removed from Windows Trusted Root Certification Authorities if desired.
pause
endlocal
