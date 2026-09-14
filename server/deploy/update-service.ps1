param(
    [string]$InstallRoot = 'C:\ProgramData\Ground43',
    [string]$ServiceName = 'Ground43.Api',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$serverRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishRoot = Join-Path $serverRoot 'artifacts\publish'
$resolvedInstall = [IO.Path]::GetFullPath($InstallRoot)
$backupParent = Join-Path (Split-Path -Parent $resolvedInstall) 'Ground43-update-backups'
$backupPath = Join-Path $backupParent ((Split-Path -Leaf $resolvedInstall) + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$protectedEntries = @('appsettings.Production.json', 'Data', 'Backups')

function Assert-DirectChild([string]$Path) {
    if ([IO.Path]::GetFullPath((Split-Path -Parent $Path)) -ne $resolvedInstall) {
        throw "Unsafe application target: $Path"
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in Administrator PowerShell.'
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'package.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Release package build failed.' }
}

$resolvedPublish = (Resolve-Path -LiteralPath $publishRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublish 'Ground43.Api.dll'))) { throw 'Publish package is incomplete.' }
if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublish 'wwwroot\index.html'))) { throw 'Client package is incomplete.' }
if (-not (Test-Path -LiteralPath $resolvedInstall -PathType Container)) { throw "Install directory does not exist: $resolvedInstall" }
if ((Get-Item -LiteralPath $resolvedInstall).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Install directory must not be a link.' }

$configPath = Join-Path $resolvedInstall 'appsettings.Production.json'
$databasePath = Join-Path $resolvedInstall 'Data\ground43-lan.db'
foreach ($required in @($configPath, $databasePath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required production file is missing: $required" }
}
$configHash = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash
$productionConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$storageSetting = [string]$productionConfig.Storage.RootPath
if ([string]::IsNullOrWhiteSpace($storageSetting)) { $storageSetting = 'Data' }
$storagePath = if ([IO.Path]::IsPathRooted($storageSetting)) { [IO.Path]::GetFullPath($storageSetting) } else { [IO.Path]::GetFullPath((Join-Path $resolvedInstall $storageSetting)) }
if (-not (Test-Path -LiteralPath $storagePath -PathType Container)) { throw 'Production attachment storage is missing.' }
if ($backupParent.StartsWith($storagePath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $backupParent -eq $storagePath) { throw 'Attachment storage must not contain the backup directory.' }
if ($storagePath.StartsWith($resolvedInstall + '\', [StringComparison]::OrdinalIgnoreCase)) {
    $protectedEntries += $storagePath.Substring($resolvedInstall.Length + 1).Split('\')[0]
} elseif ($storagePath -eq $resolvedInstall) { throw 'Attachment storage must not be the application root.' }

function Backup-VerifiedDirectory([string]$Source, [string]$Destination) {
    $entries = @(Get-Item -LiteralPath $Source) + @(Get-ChildItem -LiteralPath $Source -Recurse -Force)
    if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Data backup does not support linked files or directories.' }
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
    $manifest = foreach ($file in $entries | Where-Object { -not $_.PSIsContainer }) {
        $relative = $file.FullName.Substring($Source.TrimEnd('\').Length + 1)
        $copy = Join-Path $Destination $relative
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if (-not (Test-Path -LiteralPath $copy -PathType Leaf) -or (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash) { throw "Data backup verification failed: $relative" }
        [pscustomobject]@{ Path = $relative; Length = $file.Length; SHA256 = $hash }
    }
    @($manifest) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath ($Destination + '-manifest.json') -Encoding UTF8
}
$sourceIndex = Get-Content -LiteralPath (Join-Path $resolvedPublish 'wwwroot\index.html') -Raw
$sourceScript = [regex]::Match($sourceIndex, '<script[^>]+src="(?<src>[^"]+\.js)"').Groups['src'].Value
if ([string]::IsNullOrWhiteSpace($sourceScript)) { throw 'Cannot identify client asset in publish package.' }
$sourceAssemblyHash = (Get-FileHash -LiteralPath (Join-Path $resolvedPublish 'Ground43.Api.dll') -Algorithm SHA256).Hash

$serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
$expectedExe = Join-Path $resolvedInstall 'Ground43.Api.exe'
if ($null -eq $serviceInfo -or -not $serviceInfo.PathName.TrimStart('"').StartsWith($expectedExe, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Windows service does not point to the selected installation.'
}

$packageEntries = @(Get-ChildItem -LiteralPath $resolvedPublish -Force | Where-Object { $_.Name -notin $protectedEntries })
$deploymentStarted = $false
$service = Get-Service -Name $ServiceName
try {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
    foreach ($entry in $packageEntries) {
        $installedEntry = Join-Path $resolvedInstall $entry.Name
        Assert-DirectChild $installedEntry
        if (Test-Path -LiteralPath $installedEntry) {
            Copy-Item -LiteralPath $installedEntry -Destination (Join-Path $backupPath $entry.Name) -Recurse -Force
        }
    }
    Copy-Item -LiteralPath $configPath -Destination (Join-Path $backupPath 'appsettings.Production.json')
    $databaseBackup = Join-Path $backupPath 'database-before-update'
    New-Item -ItemType Directory -Path $databaseBackup -Force | Out-Null
    foreach ($sqliteFile in @($databasePath, ($databasePath + '-wal'), ($databasePath + '-shm'))) {
        if (Test-Path -LiteralPath $sqliteFile) { Copy-Item -LiteralPath $sqliteFile -Destination $databaseBackup -Force }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $databaseBackup 'ground43-lan.db'))) { throw 'Database backup failed.' }
    Backup-VerifiedDirectory (Join-Path $resolvedInstall 'Data') (Join-Path $backupPath 'data-before-update')
    if ($storagePath.TrimEnd('\') -ne (Join-Path $resolvedInstall 'Data')) {
        Backup-VerifiedDirectory $storagePath (Join-Path $backupPath 'attachments-before-update')
    }

    $deploymentStarted = $true
    foreach ($entry in $packageEntries) {
        $destination = Join-Path $resolvedInstall $entry.Name
        Assert-DirectChild $destination
        if ($entry.PSIsContainer -and (Test-Path -LiteralPath $destination)) { Remove-Item -LiteralPath $destination -Recurse -Force }
        Copy-Item -LiteralPath $entry.FullName -Destination $destination -Recurse -Force
    }
    if ((Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash -ne $configHash) { throw 'Production configuration changed unexpectedly.' }
    if ((Get-FileHash -LiteralPath (Join-Path $resolvedInstall 'Ground43.Api.dll') -Algorithm SHA256).Hash -ne $sourceAssemblyHash) { throw 'Installed server assembly does not match the update package.' }
    if (-not (Test-Path -LiteralPath $databasePath)) { throw 'Production database disappeared.' }

    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $healthy = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri 'http://127.0.0.1:4433/api/health' -TimeoutSec 5
            if ($health.state -eq 'ok' -and $health.database -eq 'ok' -and $health.storage -eq 'ok') { $healthy = $true; break }
        } catch { Start-Sleep -Milliseconds 750 }
    }
    if (-not $healthy) { throw 'Service did not become healthy on port 4433.' }
    $liveIndex = (Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:4433/' -TimeoutSec 10).Content
    $liveScript = [regex]::Match($liveIndex, '<script[^>]+src="(?<src>[^"]+\.js)"').Groups['src'].Value
    if ($liveScript -ne $sourceScript) { throw "Live client asset mismatch: $liveScript" }
    Write-Host "Update completed. Backup: $backupPath"
    Write-Host "Server SHA256: $sourceAssemblyHash"
} catch {
    $failure = $_.Exception.Message
    try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch {}
    if ($deploymentStarted) {
        foreach ($entry in $packageEntries) {
            $destination = Join-Path $resolvedInstall $entry.Name
            $backupEntry = Join-Path $backupPath $entry.Name
            Assert-DirectChild $destination
            if ((Test-Path -LiteralPath $destination) -and (Get-Item -LiteralPath $destination).PSIsContainer) { Remove-Item -LiteralPath $destination -Recurse -Force }
            if (Test-Path -LiteralPath $backupEntry) { Copy-Item -LiteralPath $backupEntry -Destination $destination -Recurse -Force }
            elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
        }
    }
    try { Start-Service -Name $ServiceName; (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30)) } catch {}
    throw "Update failed and application rollback was attempted: $failure"
}
