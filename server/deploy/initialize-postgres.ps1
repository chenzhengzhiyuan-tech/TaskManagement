param(
    [Parameter(Mandatory=$true)][string]$PsqlPath,
    [string]$HostName = "127.0.0.1",
    [int]$Port = 5432,
    [string]$AdminUser = "postgres",
    [Parameter(Mandatory=$true)][string]$AdminPassword,
    [string]$DatabaseName = "ground43",
    [string]$AppUser = "ground43_app",
    [Parameter(Mandatory=$true)][string]$AppPassword
)
$ErrorActionPreference = "Stop"
$env:PGPASSWORD = $AdminPassword
try {
    $escapedPassword = $AppPassword.Replace("'", "''")
    $userExists = & $PsqlPath -h $HostName -p $Port -U $AdminUser -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$AppUser'"
    if ($userExists.Trim() -ne "1") { & $PsqlPath -h $HostName -p $Port -U $AdminUser -d postgres -v ON_ERROR_STOP=1 -c "CREATE ROLE $AppUser LOGIN PASSWORD '$escapedPassword'" }
    $dbExists = & $PsqlPath -h $HostName -p $Port -U $AdminUser -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DatabaseName'"
    if ($dbExists.Trim() -ne "1") { & $PsqlPath -h $HostName -p $Port -U $AdminUser -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $DatabaseName OWNER $AppUser ENCODING 'UTF8'" }
    Write-Host "PostgreSQL database and application role are ready."
} finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
