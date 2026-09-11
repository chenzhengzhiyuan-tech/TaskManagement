$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dataRoot = Join-Path $PSScriptRoot 'src\Ground43.Api\Data\integration'
$db = Join-Path $dataRoot 'ground43-integration.db'
$resolvedDataRoot = [IO.Path]::GetFullPath($dataRoot)
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\Ground43.Api\Data'))
if (-not $resolvedDataRoot.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe integration data path.' }
if (Test-Path -LiteralPath $resolvedDataRoot) { Remove-Item -LiteralPath $resolvedDataRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:GROUND43_ConnectionStrings__Default = "Data Source=$db"
$env:GROUND43_Storage__RootPath = $dataRoot
$env:GROUND43_Jobs__Enabled = 'false'
$env:GROUND43_Bootstrap__AdminPassword = 'demo123'
$bundledDotnet = Join-Path $root '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $bundledDotnet) { $bundledDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet run --project (Join-Path $PSScriptRoot 'src\Ground43.Api') --urls http://127.0.0.1:5080
