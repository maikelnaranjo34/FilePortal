# FilePortal Windows Service Setup

This guide shows how to run FilePortal as a Windows service instead of keeping a PowerShell window open.

## Prerequisites

- Windows 10/11 with administrator privileges
- .NET 8.0 Runtime installed
- FilePortal application published (already done)

## Installation

### Option 1: PowerShell Script (Recommended)

1. Right-click `install-service.ps1` and select "Run with PowerShell"
2. If prompted, allow the script to run as administrator
3. The service will be installed and configured automatically

### Option 2: Batch File

1. Right-click `install-service.bat` and select "Run as administrator"
2. Follow the on-screen instructions

### Option 3: Manual Installation

If you prefer to install manually:

```powershell
# Run PowerShell as Administrator
.\nssm.exe install FilePortal "bin\Release\net8.0\win-x64\publish\FilePortal.exe" --urls "http://0.0.0.0:5251"
.\nssm.exe set FilePortal Description "FilePortal ASP.NET Core Web Application"
```

## Service Management

### Start the Service
```powershell
nssm.exe start FilePortal
# OR
Start-Service FilePortal
```

### Stop the Service
```powershell
nssm.exe stop FilePortal
# OR
Stop-Service FilePortal
```

### Restart the Service
```powershell
nssm.exe restart FilePortal
# OR
Restart-Service FilePortal
```

### Check Service Status
```powershell
Get-Service FilePortal
```

### View Service Logs
- Application logs: `C:\Logs\FilePortal\service.log`
- Error logs: `C:\Logs\FilePortal\error.log`

### Remove the Service
```powershell
nssm.exe remove FilePortal
```

## Accessing the Application

Once the service is running, access FilePortal at:
- **Local**: http://localhost:5251
- **Network**: http://YOUR_IP:5251

## Troubleshooting

### Service Won't Start
1. Check the log files in `C:\Logs\FilePortal\`
2. Ensure port 5251 is not in use by another application
3. Verify the published executable exists: `bin\Release\net8.0\win-x64\publish\FilePortal.exe`

### Permission Issues
- Make sure you're running commands as Administrator
- Check that the service account has access to the application directory

### Port Already in Use
To check what's using port 5251:
```powershell
netstat -ano | findstr :5251
```

To kill a process using the port:
```powershell
# Find PID from netstat output, then:
taskkill /PID <PID> /F
```

## Production Deployment

For production environments, consider deploying to IIS instead of a Windows service:

1. Install IIS and ASP.NET Core Hosting Bundle
2. Publish as framework-dependent: `dotnet publish -c Release`
3. Deploy to IIS site

This provides better process management, automatic restarts, and integration with Windows features.