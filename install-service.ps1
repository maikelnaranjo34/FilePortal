#Requires -RunAsAdministrator

Write-Host "Installing FilePortal as a Windows service..." -ForegroundColor Green

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Please run this script as Administrator!" -ForegroundColor Red
    exit 1
}

# Set working directory
Set-Location $PSScriptRoot

# Install the service using NSSM
Write-Host "Installing service..." -ForegroundColor Yellow
& ".\nssm.exe" install FilePortal "bin\Release\net8.0\win-x64\publish\FilePortal.exe" --urls "http://0.0.0.0:5251"

# Set service description
& ".\nssm.exe" set FilePortal Description "FilePortal ASP.NET Core Web Application"

# Set up logging
& ".\nssm.exe" set FilePortal AppStdout "C:\Logs\FilePortal\service.log"
& ".\nssm.exe" set FilePortal AppStderr "C:\Logs\FilePortal\error.log"

# Create log directory
if (!(Test-Path "C:\Logs\FilePortal")) {
    New-Item -ItemType Directory -Path "C:\Logs\FilePortal" -Force
}

Write-Host "Service installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Service Management Commands:" -ForegroundColor Cyan
Write-Host "  Start service:  nssm.exe start FilePortal" -ForegroundColor White
Write-Host "  Stop service:   nssm.exe stop FilePortal" -ForegroundColor White
Write-Host "  Restart service: nssm.exe restart FilePortal" -ForegroundColor White
Write-Host "  Remove service: nssm.exe remove FilePortal" -ForegroundColor White
Write-Host ""
Write-Host "Service Status:" -ForegroundColor Cyan
Get-Service FilePortal -ErrorAction SilentlyContinue | Format-Table -AutoSize