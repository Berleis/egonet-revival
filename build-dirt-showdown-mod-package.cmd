@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\BuildDirtShowdownModPackage.ps1" %*
pause
