param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [string]$InstallPath = "D:\Apps\Ground43",
    [string]$ServiceName = "Ground43.Api",
    [string]$DisplayName = "Ground43 Requirement Platform",
    [string]$Urls = "http://0.0.0.0:4433",
    [string]$ServiceAccount = "LocalSystem",
    [string]$ServicePassword = ""
)
$ErrorActionPreference = "Stop"
$source = [IO.Path]::GetFullPath($PackagePath)
$target = [IO.Path]::GetFullPath($InstallPath)
if (!(Test-Path (Join-Path $source "Ground43.Api.exe"))) { throw "Ground43.Api.exe was not found in $source" }
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { Stop-Service $ServiceName -Force; sc.exe delete $ServiceName | Out-Null; Start-Sleep -Seconds 2 }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force
$config = Join-Path $target "appsettings.Production.json"
if (!(Test-Path $config)) { Copy-Item (Join-Path $target "appsettings.Production.example.json") $config; Write-Warning "Edit $config before starting the service." }
$exe = Join-Path $target "Ground43.Api.exe"
$binPath = '"' + $exe + '" --environment Production --urls ' + $Urls
if ($ServiceAccount -eq "LocalSystem") { sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= $DisplayName | Out-Null }
else { sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= $DisplayName obj= $ServiceAccount password= $ServicePassword | Out-Null }
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null
Write-Host "Service installed. Review $config, then run: Start-Service $ServiceName"
