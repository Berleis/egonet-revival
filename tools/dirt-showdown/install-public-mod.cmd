@echo off
setlocal

cd /d "%~dp0..\.."
call "%CD%\games\dirt-showdown\install-dirt-showdown-mod.cmd" %*
exit /b %ERRORLEVEL%
