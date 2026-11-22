Import-Module WebAdministration -ErrorAction Stop
$site = Get-Website -Name 'FilePortal'
if (!$site) { throw 'The FilePortal IIS site does not exist.' }

$site | Select-Object Name, state, physicalPath, applicationPool
Get-WebBinding -Name 'FilePortal' | Select-Object protocol, bindingInformation
Get-WebAppPoolState -Name $site.applicationPool

$directory = [Environment]::ExpandEnvironmentVariables($site.physicalPath)
[pscustomobject]@{
    PublishedAssembly = Test-Path -LiteralPath (Join-Path $directory 'FilePortal.dll')
    WebConfig = Test-Path -LiteralPath (Join-Path $directory 'web.config')
    DefaultUserStore = Test-Path -LiteralPath (Join-Path $directory 'data\users.json')
}
