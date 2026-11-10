@echo off
rem n17 YuHelper - download llama-server and models. Arguments are passed to setup-models.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-models.ps1" %*
echo.
pause
