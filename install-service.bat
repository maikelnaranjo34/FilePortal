@echo off
echo Installing FilePortal as a Windows service...
echo.

cd /d "%~dp0"

REM Install the service using NSSM
nssm.exe install FilePortal "bin\Release\net8.0\win-x64\publish\FilePortal.exe" --urls "http://0.0.0.0:5251"

REM Set service description
nssm.exe set FilePortal Description "FilePortal ASP.NET Core Web Application"

REM Set service to restart on failure
nssm.exe set FilePortal AppStdout "C:\Logs\FilePortal\service.log"
nssm.exe set FilePortal AppStderr "C:\Logs\FilePortal\error.log"

REM Create log directory
if not exist "C:\Logs\FilePortal" mkdir "C:\Logs\FilePortal"

echo.
echo Service installed successfully!
echo.
echo To start the service: nssm.exe start FilePortal
echo To stop the service: nssm.exe stop FilePortal
echo To remove the service: nssm.exe remove FilePortal
echo.
pause