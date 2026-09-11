param(
    [Parameter(Mandatory=$true)][string]$PgDumpPath,
    [Parameter(Mandatory=$true)][string]$ConnectionString,
    [Parameter(Mandatory=$true)][string]$AttachmentPath,
    [string]$BackupRoot = "D:\Ground43Backups",
    [int]$HourlyRetention = 24,
    [int]$DailyRetentionDays = 30
)
$ErrorActionPreference = "Stop"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dbDir = Join-Path $BackupRoot "database\hourly"
$fileDir = Join-Path $BackupRoot "attachments"
$logDir = Join-Path $BackupRoot "logs"
New-Item -ItemType Directory -Force -Path $dbDir,$fileDir,$logDir | Out-Null
$dbFile = Join-Path $dbDir "ground43-$stamp.dump"
& $PgDumpPath --dbname=$ConnectionString --format=custom --compress=6 --file=$dbFile
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE" }
& robocopy $AttachmentPath $fileDir /MIR /Z /FFT /R:2 /W:5 /NFL /NDL /NP | Out-File (Join-Path $logDir "attachments-$stamp.log")
if ($LASTEXITCODE -ge 8) { throw "Attachment backup failed with robocopy exit code $LASTEXITCODE" }
Get-ChildItem $dbDir -Filter *.dump | Sort-Object LastWriteTime -Descending | Select-Object -Skip $HourlyRetention | Remove-Item -Force
$dailyDir = Join-Path $BackupRoot "database\daily"; New-Item -ItemType Directory -Force -Path $dailyDir | Out-Null
if (-not (Test-Path (Join-Path $dailyDir "ground43-$(Get-Date -Format yyyyMMdd).dump"))) { Copy-Item $dbFile (Join-Path $dailyDir "ground43-$(Get-Date -Format yyyyMMdd).dump") }
Get-ChildItem $dailyDir -Filter *.dump | Where-Object LastWriteTime -lt (Get-Date).AddDays(-$DailyRetentionDays) | Remove-Item -Force
Write-Host "Backup completed: $dbFile"
