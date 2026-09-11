param(
    [Parameter(Mandatory=$true)][string]$PgRestorePath,
    [Parameter(Mandatory=$true)][string]$ConnectionString,
    [Parameter(Mandatory=$true)][string]$DatabaseBackup,
    [Parameter(Mandatory=$true)][string]$AttachmentBackup,
    [Parameter(Mandatory=$true)][string]$AttachmentTarget
)
$ErrorActionPreference = "Stop"
& $PgRestorePath --dbname=$ConnectionString --clean --if-exists --no-owner $DatabaseBackup
if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE" }
New-Item -ItemType Directory -Force -Path $AttachmentTarget | Out-Null
& robocopy $AttachmentBackup $AttachmentTarget /MIR /Z /FFT /R:2 /W:5
if ($LASTEXITCODE -ge 8) { throw "Attachment restore failed with robocopy exit code $LASTEXITCODE" }
Write-Host "Restore completed."
