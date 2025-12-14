#Requires -RunAsAdministrator

Write-Host "Checking IIS FilePortal setup..." -ForegroundColor Green

try {
    Import-Module WebAdministration -ErrorAction Stop

    # Check if site exists
    $site = Get-Website -Name "FilePortal" -ErrorAction SilentlyContinue
    if ($site) {
        Write-Host "✅ IIS Site 'FilePortal' exists" -ForegroundColor Green
        Write-Host "   State: $($site.State)" -ForegroundColor Cyan
        Write-Host "   Physical Path: $($site.PhysicalPath)" -ForegroundColor Cyan
        Write-Host "   Application Pool: $($site.ApplicationPool)" -ForegroundColor Cyan

        # Check bindings
        $bindings = Get-WebBinding -Name "FilePortal"
        Write-Host "   Bindings:" -ForegroundColor Cyan
        foreach ($binding in $bindings) {
            Write-Host "     $($binding.Protocol)://$($binding.BindingInformation)" -ForegroundColor White
        }

        # Check app pool
        $appPool = Get-Item "IIS:\AppPools\$($site.ApplicationPool)" -ErrorAction SilentlyContinue
        if ($appPool) {
            Write-Host "   App Pool State: $($appPool.State)" -ForegroundColor Cyan
        }

    } else {
        Write-Host "❌ IIS Site 'FilePortal' does not exist" -ForegroundColor Red
        Write-Host "Running setup..." -ForegroundColor Yellow

        # Create app pool
        if (!(Test-Path "IIS:\AppPools\FilePortalPool")) {
            New-WebAppPool -Name "FilePortalPool" -Force
            Set-ItemProperty "IIS:\AppPools\FilePortalPool" -Name processModel.identityType -Value ApplicationPoolIdentity
        }

        # Create site
        New-Website -Name "FilePortal" -ApplicationPool "FilePortalPool" -PhysicalPath "D:\Dev\Fileportal\bin\Release\net8.0\publish" -Port 5251 -Force

        # Set permissions
        $publishPath = "D:\Dev\Fileportal\bin\Release\net8.0\publish"
        if (Test-Path $publishPath) {
            $acl = Get-Acl $publishPath
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
            $acl.SetAccessRule($accessRule)
            Set-Acl -Path $publishPath -AclObject $acl
        }

        # Start site
        Start-Website -Name "FilePortal"

        Write-Host "✅ Setup completed!" -ForegroundColor Green
    }

    # Test the site
    Write-Host "Testing site connectivity..." -ForegroundColor Yellow
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5251" -TimeoutSec 10
        Write-Host "✅ Site is responding! Status: $($response.StatusCode)" -ForegroundColor Green
    } catch {
        Write-Host "❌ Site test failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Check IIS logs and application logs for details." -ForegroundColor Yellow
    }

} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Make sure IIS is properly installed and you're running as Administrator." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Press any key to continue..."
$null = $host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")