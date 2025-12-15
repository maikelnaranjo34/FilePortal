#Requires -RunAsAdministrator

Write-Host "Setting up FilePortal in IIS..." -ForegroundColor Green

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Please run this script as Administrator!" -ForegroundColor Red
    exit 1
}

Import-Module WebAdministration

$siteName = "FilePortal"
$appPoolName = "FilePortalPool"
$publishPath = "D:\Dev\Fileportal\bin\Release\net8.0\publish"
$port = 80

Write-Host "Creating application pool..." -ForegroundColor Yellow
if (!(Test-Path IIS:\AppPools\$appPoolName)) {
    New-WebAppPool -Name $appPoolName -Force
    Set-ItemProperty IIS:\AppPools\$appPoolName -Name processModel.identityType -Value LocalSystem
    Set-ItemProperty IIS:\AppPools\$appPoolName -Name processModel.idleTimeout -Value "00:00:00"
    Set-ItemProperty IIS:\AppPools\$appPoolName -Name recycling.periodicRestart.time -Value "00:00:00"
}

Write-Host "Creating website..." -ForegroundColor Yellow
if (!(Test-Path IIS:\Sites\$siteName)) {
    New-Website -Name $siteName -ApplicationPool $appPoolName -PhysicalPath $publishPath -Port $port -Force
} else {
    Write-Host "Website already exists. Updating physical path and rebinding to port $port..." -ForegroundColor Yellow
    Set-ItemProperty IIS:\Sites\$siteName -Name physicalPath -Value $publishPath
    Set-ItemProperty IIS:\Sites\$siteName -Name applicationPool -Value $appPoolName
    try {
        Import-Module WebAdministration
        # Free up port 80 by stopping Default Web Site and removing its :80 binding
        if (Test-Path IIS:\Sites\"Default Web Site") {
            Write-Host "Stopping 'Default Web Site' and removing port 80 binding if present..." -ForegroundColor Yellow
            Stop-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
            $defBindings = Get-WebBinding -Name "Default Web Site" -ErrorAction SilentlyContinue
            foreach ($db in $defBindings) {
                if ($db.protocol -eq 'http' -and $db.bindingInformation -like "*:80:*") {
                    Remove-WebBinding -Name "Default Web Site" -IPAddress * -Port 80 -Protocol http -ErrorAction SilentlyContinue
                }
            }
        }

        Stop-Website -Name $siteName -ErrorAction SilentlyContinue
        $existingBindings = Get-WebBinding -Name $siteName -ErrorAction SilentlyContinue
        # Remove any old 5251/8080 bindings
        foreach ($b in $existingBindings) {
            if ($b.protocol -eq 'http' -and ($b.bindingInformation -like "*:5251:*" -or $b.bindingInformation -like "*:8080:*")) {
                $portToRemove = [int]($b.bindingInformation.Split(':')[1])
                Remove-WebBinding -Name $siteName -IPAddress * -Port $portToRemove -Protocol http -ErrorAction SilentlyContinue
            }
        }
        # Ensure port 80 binding exists
        $has80 = ($existingBindings | Where-Object { $_.protocol -eq 'http' -and $_.bindingInformation -like "*:80:*" })
        if (-not $has80) { New-WebBinding -Name $siteName -IPAddress * -Port $port -Protocol http }
        Start-Website -Name $siteName
    } catch {
        Write-Host "Failed to update bindings: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "Setting permissions..." -ForegroundColor Yellow
$acl = Get-Acl $publishPath
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($accessRule)
Set-Acl -Path $publishPath -AclObject $acl

Write-Host "Starting website..." -ForegroundColor Yellow
Start-Website -Name $siteName

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "FilePortal IIS Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Website URL: http://localhost:$port" -ForegroundColor Cyan
Write-Host "IIS Manager: Open IIS Manager and check Sites > $siteName" -ForegroundColor Cyan
Write-Host ""
Write-Host "Management Commands:" -ForegroundColor Yellow
Write-Host "  Start Site:  Start-Website -Name $siteName" -ForegroundColor White
Write-Host "  Stop Site:   Stop-Website -Name $siteName" -ForegroundColor White
Write-Host "  Restart:     Restart-WebAppPool -Name $appPoolName" -ForegroundColor White
Write-Host ""
Write-Host "Logs Location: $publishPath\logs\" -ForegroundColor Cyan
Write-Host ""

# Test the site
Write-Host "Testing site..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:$port" -TimeoutSec 10
    if ($response.StatusCode -eq 200) {
        Write-Host "✅ Site is responding correctly!" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Site returned status code: $($response.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "❌ Site is not responding. Check IIS logs for errors." -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}