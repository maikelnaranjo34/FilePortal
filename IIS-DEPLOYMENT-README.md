# FilePortal IIS Deployment Guide

This guide shows how to deploy FilePortal to Internet Information Services (IIS) for production use.

## Prerequisites

- ✅ IIS installed and running (confirmed)
- ✅ ASP.NET Core Hosting Bundle installed (confirmed)
- ✅ .NET 8.0 Runtime installed
- ✅ Administrator privileges

## Quick Setup

### Option 1: PowerShell Script (Recommended)

1. **Right-click `setup-iis.ps1` → "Run with PowerShell"** (as Administrator)
2. The script will automatically:
   - Create an application pool named "FilePortalPool"
   - Create a website named "FilePortal"
   - Set proper permissions
   - Start the website
   - Test the deployment

### Option 2: Batch File

1. **Right-click `setup-iis.bat` → "Run as administrator"**
2. Follow the prompts

## Manual Setup (Alternative)

If you prefer to set up manually:

### 1. Create Application Pool

```powershell
Import-Module WebAdministration
New-WebAppPool -Name "FilePortalPool" -Force
Set-ItemProperty IIS:\AppPools\FilePortalPool -Name processModel.identityType -Value ApplicationPoolIdentity
```

### 2. Create Website

```powershell
New-Website -Name "FilePortal" -ApplicationPool "FilePortalPool" -PhysicalPath "D:\Dev\Fileportal\bin\Release\net8.0\publish" -Port 5251 -Force
```

### 3. Set Permissions

```powershell
$acl = Get-Acl "D:\Dev\Fileportal\bin\Release\net8.0\publish"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($accessRule)
Set-Acl -Path "D:\Dev\Fileportal\bin\Release\net8.0\publish" -AclObject $acl
```

### 4. Start the Website

```powershell
Start-Website -Name "FilePortal"
```

## Configuration Details

### IIS Site Configuration
- **Site Name**: `FilePortal`
- **Application Pool**: `FilePortalPool`
- **Physical Path**: `D:\Dev\Fileportal\bin\Release\net8.0\publish`
- **Port**: `5251`
- **Binding**: `http://*:5251`

### Application Pool Settings
- **Identity**: `ApplicationPoolIdentity`
- **Idle Timeout**: Disabled
- **Periodic Restart**: Disabled

### Environment Variables
- `ASPNETCORE_ENVIRONMENT`: `Production`
- `ASPNETCORE_URLS`: `http://0.0.0.0:5251`

## Accessing the Application

After setup, access FilePortal at:
- **Local**: http://localhost:5251
- **Network**: http://YOUR_IP:5251

## Management Commands

### PowerShell Commands

```powershell
# Check site status
Get-Website -Name "FilePortal"

# Start site
Start-Website -Name "FilePortal"

# Stop site
Stop-Website -Name "FilePortal"

# Restart application pool
Restart-WebAppPool -Name "FilePortalPool"

# View site bindings
Get-WebBinding -Name "FilePortal"
```

### IIS Manager
1. Open IIS Manager (search for "IIS" in Start menu)
2. Expand server node → Sites
3. Right-click "FilePortal" for management options

## Logging and Troubleshooting

### IIS Logs
- **Location**: `C:\inetpub\logs\LogFiles\W3SVC1\`
- **Application Logs**: `D:\Dev\Fileportal\bin\Release\net8.0\publish\logs\stdout`

### Common Issues

#### 1. HTTP Error 500.19 - Internal Server Error
- **Cause**: Permission issues or missing web.config
- **Solution**: Run setup script as administrator

#### 2. HTTP Error 502.5 - Process Failure
- **Cause**: .NET runtime issues
- **Solution**: Check stdout logs for detailed error messages

#### 3. Access Denied
- **Cause**: IIS_IUSRS permissions missing
- **Solution**: Re-run the permission setup in the script

#### 4. Port Already in Use
- **Cause**: Another service using port 5251
- **Solution**: Change port in web.config and IIS binding

### Log Analysis

```powershell
# View recent IIS logs
Get-Content "C:\inetpub\logs\LogFiles\W3SVC1\*.log" -Tail 20

# View application logs
Get-Content "D:\Dev\Fileportal\bin\Release\net8.0\publish\logs\stdout*.log" -Tail 20
```

## Security Considerations

### Production Deployment Checklist
- [ ] Change default port (5251) to 80 or 443
- [ ] Enable HTTPS with SSL certificate
- [ ] Configure Windows Authentication or external auth
- [ ] Set up proper firewall rules
- [ ] Configure log rotation
- [ ] Set up monitoring and alerts
- [ ] Regular security updates

### File Permissions
The setup script grants `IIS_IUSRS` full control over the publish directory. In production, consider more restrictive permissions:
- Read/Execute for application files
- Write access only for logs and user data directories

## Backup and Recovery

### Backup Commands
```powershell
# Backup IIS configuration
%windir%\system32\inetsrv\appcmd.exe add backup "FilePortal_Backup"

# Backup application files
Compress-Archive -Path "D:\Dev\Fileportal\bin\Release\net8.0\publish" -DestinationPath "D:\Backups\FilePortal_$(Get-Date -Format 'yyyyMMdd_HHmmss').zip"
```

### Restore Commands
```powershell
# Restore IIS configuration
%windir%\system32\inetsrv\appcmd.exe restore backup "FilePortal_Backup"

# Restore application files
Expand-Archive -Path "D:\Backups\FilePortal_latest.zip" -DestinationPath "D:\Dev\Fileportal\bin\Release\net8.0\publish"
```

## Performance Tuning

### Application Pool Settings
```powershell
# Increase worker processes for better performance
Set-ItemProperty IIS:\AppPools\FilePortalPool -Name processModel.maxProcesses -Value 4

# Configure memory limits
Set-ItemProperty IIS:\AppPools\FilePortalPool -Name recycling.periodicRestart.memory -Value 500000
```

### IIS Settings
```powershell
# Enable dynamic compression
Set-WebConfigurationProperty -Filter /system.webServer/httpCompression -Name dynamicCompressionEnable -Value true

# Configure request limits
Set-WebConfigurationProperty -Filter /system.webServer/security/requestFiltering -Name maxAllowedContentLength -Value 104857600
```

## Monitoring

### Basic Monitoring
```powershell
# Check site status
Get-WebsiteState -Name "FilePortal"

# Monitor application pool
Get-WorkerProcess | Where-Object { $_.AppPoolName -eq "FilePortalPool" }
```

### Advanced Monitoring
Consider using:
- IIS Administration API
- Application Insights
- Windows Performance Monitor
- Custom logging and alerting

---

**Need Help?** Check the logs first, then refer to Microsoft's IIS documentation or ASP.NET Core deployment guides.