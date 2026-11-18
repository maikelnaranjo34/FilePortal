#Requires -RunAsAdministrator
param(
    [string]$PublishPath = (Join-Path $PSScriptRoot 'bin\Release\net10.0\publish'),
    [int]$Port = 5251,
    [string]$ListenAddress = '127.0.0.1'
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$siteName = 'FilePortal'
$appPoolName = 'FilePortalPool'
$PublishPath = (Resolve-Path -LiteralPath $PublishPath).Path
if (!(Test-Path -LiteralPath (Join-Path $PublishPath 'web.config')) -or
    !(Test-Path -LiteralPath (Join-Path $PublishPath 'FilePortal.dll'))) {
    throw 'Publish FilePortal before configuring IIS.'
}

$poolPath = "IIS:\AppPools\$appPoolName"
if (!(Test-Path $poolPath)) {
    New-WebAppPool -Name $appPoolName | Out-Null
    Set-ItemProperty $poolPath -Name processModel.identityType -Value ApplicationPoolIdentity
} elseif ((Get-Item $poolPath).processModel.identityType -eq 'LocalSystem') {
    Set-ItemProperty $poolPath -Name processModel.identityType -Value ApplicationPoolIdentity
}
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''

function Grant-FolderAccess([string]$Folder, [string]$Rights) {
    $acl = Get-Acl -LiteralPath $Folder
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        "IIS AppPool\$appPoolName", $Rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Folder -AclObject $acl
}

Grant-FolderAccess $PublishPath 'ReadAndExecute'
$data = Join-Path $PublishPath 'data'
New-Item -ItemType Directory -Path $data -Force | Out-Null
Grant-FolderAccess $data 'Modify'

$sitePath = "IIS:\Sites\$siteName"
if (Test-Path $sitePath) {
    Set-ItemProperty $sitePath -Name physicalPath -Value $PublishPath
    Set-ItemProperty $sitePath -Name applicationPool -Value $appPoolName
} else {
    New-Website -Name $siteName -ApplicationPool $appPoolName -PhysicalPath $PublishPath -IPAddress $ListenAddress -Port $Port | Out-Null
}
if ((Get-WebAppPoolState -Name $appPoolName).Value -eq 'Stopped') {
    Start-WebAppPool -Name $appPoolName
}
if ((Get-Website -Name $siteName).State -eq 'Stopped') {
    Start-Website -Name $siteName
}

Get-WebBinding -Name $siteName | Select-Object protocol, bindingInformation
