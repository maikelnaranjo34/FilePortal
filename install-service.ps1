#Requires -RunAsAdministrator
param(
    [string]$PublishPath = (Join-Path $PSScriptRoot 'bin\Release\net10.0\win-x64\publish'),
    [string]$Urls = 'http://127.0.0.1:5251',
    [switch]$Start
)

$ErrorActionPreference = 'Stop'
$service = 'FilePortal'
$nssm = Join-Path $PSScriptRoot 'nssm.exe'
$PublishPath = (Resolve-Path -LiteralPath $PublishPath).Path
$appPath = Join-Path $PublishPath 'FilePortal.exe'
if (!(Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw 'Publish FilePortal for win-x64 before installing its service.'
}

function Invoke-Nssm {
    & $nssm @args
    if ($LASTEXITCODE -ne 0) {
        throw "NSSM failed with exit code $LASTEXITCODE."
    }
}

if (!(Get-Service -Name $service -ErrorAction SilentlyContinue)) {
    Invoke-Nssm install $service $appPath
} else {
    if ((Get-Service -Name $service).Status -ne 'Stopped') {
        throw 'Stop the existing FilePortal service before reconfiguring it.'
    }
    Invoke-Nssm set $service Application $appPath
}
Invoke-Nssm set $service ObjectName 'NT AUTHORITY\LocalService'
& sc.exe sidtype $service unrestricted
if ($LASTEXITCODE -ne 0) { throw 'Could not configure the FilePortal service SID.' }
Invoke-Nssm set $service AppDirectory $PublishPath
Invoke-Nssm set $service AppParameters "--urls $Urls"
Invoke-Nssm set $service Description 'FilePortal ASP.NET Core Web Application'

function Grant-FolderAccess([string]$Folder, [string]$Rights) {
    $acl = Get-Acl -LiteralPath $Folder
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        "NT SERVICE\$service", $Rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Folder -AclObject $acl
}

Grant-FolderAccess $PublishPath 'ReadAndExecute'
$data = Join-Path $PublishPath 'data'
$logs = Join-Path $env:ProgramData 'FilePortal\logs'
New-Item -ItemType Directory -Path $data, $logs -Force | Out-Null
Grant-FolderAccess $data 'Modify'
Grant-FolderAccess $logs 'Modify'
Invoke-Nssm set $service AppStdout (Join-Path $logs 'service.log')
Invoke-Nssm set $service AppStderr (Join-Path $logs 'error.log')

if ($Start) { Start-Service -Name $service }
Get-Service -Name $service
