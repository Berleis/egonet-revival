@echo off
setlocal

cd /d "%~dp0..\.."

set "GAME_PATH=%~1"
if "%GAME_PATH%"=="" set "GAME_PATH=D:\SteamLibrary\steamapps\common\DiRT 4"

echo EgoNet Revival - DiRT 4 local patch
echo Game: %GAME_PATH%
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

dotnet run --project "%CD%\src\RaceNetShowdown.Patcher" -- patch --game dirt-4 "%GAME_PATH%"
if errorlevel 1 pause & exit /b %ERRORLEVEL%

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$hosts=Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'; $lines=[IO.File]::ReadAllLines($hosts); $out=New-Object System.Collections.Generic.List[string]; $inside=$false; foreach($line in $lines){ if($line -eq '# EgoNet Revival DiRT 4'){ $inside=$true; continue }; if($inside -and $line -eq '# End EgoNet Revival DiRT 4'){ $inside=$false; continue }; if(-not $inside){ $out.Add($line) } }; $out.Add(''); $out.Add('# EgoNet Revival DiRT 4'); foreach($name in @('prod.egonet.codemasters.com','egonet.codemasters.com','racenet.codemasters.com')){ $out.Add('127.0.0.1 ' + $name) }; $out.Add('# End EgoNet Revival DiRT 4'); [IO.File]::WriteAllLines($hosts,$out,[Text.Encoding]::ASCII)"
if errorlevel 1 (
  echo Failed to update the hosts file. Run this script as Administrator.
  pause
  exit /b %ERRORLEVEL%
)

echo.
echo Patch complete. Start the server, then launch DiRT 4 through Steam.
pause
endlocal
