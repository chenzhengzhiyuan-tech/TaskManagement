param([int]$Port = 5081, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$dataRoot = Join-Path $PSScriptRoot 'artifacts\local-preview\Data'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:GROUND43_Database__Provider = 'Sqlite'
$env:GROUND43_ConnectionStrings__Default = "Data Source=$(Join-Path $dataRoot 'preview.db')"
$env:GROUND43_Storage__RootPath = $dataRoot
$env:GROUND43_Jobs__Enabled = 'false'
$env:GROUND43_Logging__LogLevel__Microsoft = 'Warning'
$env:GROUND43_Bootstrap__AdminPassword = 'G43-Preview-Only!'
$env:GROUND43_Platform__PublicBaseUrl = 'http://127.0.0.1:5180'
# Nonempty whitespace overrides inherited settings while keeping the notifier unconfigured.
$env:GROUND43_WeCom__CorpId = ' '
$env:GROUND43_WeCom__AgentId = ' '
$env:GROUND43_WeCom__Secret = ' '
$env:GROUND43_WeCom__AdminWebhook = ' '
$dotnet = Join-Path $workspace '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$arguments = @('run', '--no-launch-profile', '--configuration', 'Release', '--project', (Join-Path $PSScriptRoot 'src\Ground43.Api'), '--urls', "http://127.0.0.1:$Port")
if ($NoBuild) { $arguments += '--no-build' }
& $dotnet @arguments
