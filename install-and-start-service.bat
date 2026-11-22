@echo off
powershell -NoProfile -File "%~dp0install-service.ps1" -Start %*
exit /b %errorlevel%
