param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workspace = [IO.Path]::GetFullPath((Join-Path $root ".."))
$workspaceDotnet = Join-Path $workspace ".dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $workspaceDotnet) { $workspaceDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$pnpmCommand = Get-Command pnpm.cmd -ErrorAction SilentlyContinue
if ($null -eq $pnpmCommand) { $pnpmCommand = Get-Command pnpm -ErrorAction Stop }
$pnpm = $pnpmCommand.Source
$client = Join-Path $workspace "client"
$wwwroot = Join-Path $root "src\Ground43.Api\wwwroot"
$publish = Join-Path $root "artifacts\publish"
$previousDataMode = $env:VITE_DATA_MODE
try {
    $env:VITE_DATA_MODE = 'api'
    & $pnpm --dir $client run build
} finally {
    $env:VITE_DATA_MODE = $previousDataMode
}
if ($LASTEXITCODE -ne 0) { throw 'Client build failed; existing package preserved.' }
foreach ($target in @($wwwroot, $publish)) {
    $resolvedTarget = [IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe package target: $resolvedTarget"
    }
}
if (Test-Path $wwwroot) { Remove-Item -LiteralPath $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $client "dist\*") -Destination $wwwroot -Recurse -Force
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
$args = @("publish", (Join-Path $root "src\Ground43.Api\Ground43.Api.csproj"), "-c", $Configuration, "-r", $Runtime, "-o", $publish, "--self-contained", $SelfContained.ToString().ToLowerInvariant())
& $dotnet @args
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed; do not deploy this package.' }
Copy-Item (Join-Path $root "src\Ground43.Api\appsettings.Production.example.json") (Join-Path $publish "appsettings.Production.example.json") -Force
Write-Host "Publish package: $publish"
