@echo off
echo Installing FilePortal Windows Service...
echo.

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% == 0 (
    echo Running as administrator
) else (
    echo Please run this as Administrator!
    pause
    exit /b 1
)

cd /d "%~dp0"

echo Installing service...
nssm.exe install FilePortal "bin\Release\net8.0\win-x64\publish\FilePortal.exe" --urls "http://0.0.0.0:5251"

echo Setting service description...
nssm.exe set FilePortal Description "FilePortal ASP.NET Core Web Application"

echo Setting up logging...
if not exist "C:\Logs\FilePortal" mkdir "C:\Logs\FilePortal"
nssm.exe set FilePortal AppStdout "C:\Logs\FilePortal\service.log"
nssm.exe set FilePortal AppStderr "C:\Logs\FilePortal\error.log"

echo Starting service...
nssm.exe start FilePortal

echo.
echo Service Status:
sc query FilePortal

echo.
echo ============================================
echo FilePortal Service Installation Complete!
echo ============================================
echo.
echo Your website is now running as a service at:
echo   http://localhost:5251
echo   http://YOUR_IP_ADDRESS:5251
echo.
echo You can now close this window and any PowerShell windows.
echo The website will continue running in the background.
echo.
echo Service Management:
echo   Start:  nssm.exe start FilePortal
echo   Stop:   nssm.exe stop FilePortal
echo   Remove: nssm.exe remove FilePortal
echo.
pause