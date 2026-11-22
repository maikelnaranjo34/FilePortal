@echo off
powershell -NoProfile -File "%~dp0setup-iis.ps1" %*
exit /b %errorlevel%
