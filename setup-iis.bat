@echo off
echo Setting up FilePortal in IIS...
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

powershell -ExecutionPolicy Bypass -File "%~dp0setup-iis.ps1"

echo.
echo IIS setup script completed.
echo.
pause