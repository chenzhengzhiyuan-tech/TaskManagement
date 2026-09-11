param([string]$ServiceName = "Ground43.Api")
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) { if ($service.Status -ne "Stopped") { Stop-Service $ServiceName -Force }; sc.exe delete $ServiceName | Out-Null; Write-Host "Deleted $ServiceName" }
