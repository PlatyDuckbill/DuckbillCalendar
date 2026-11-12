param([switch]$NoBuild, [ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [string]$DalamudPath)
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -DalamudPath $DalamudPath }
Push-Location $calendarRoot
try {
    foreach ($project in @('tests/DuckbillCalendar.Core.Checks','tests/DuckbillCalendar.Infrastructure.Checks','tests/DuckbillCalendar.PluginModel.Checks','tests/DuckbillCalendar.ImGui.Checks')) {
        & dotnet run --project $project --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Check failed: $project" }
    }
    & dotnet run --project tools/CalendarDataCollect --configuration $Configuration --no-build -- check
    if ($LASTEXITCODE -ne 0) { throw 'Collector checks failed' }
    & (Join-Path $PSScriptRoot 'check-tools.ps1') -NoBuild -Configuration $Configuration
    & (Join-Path $PSScriptRoot 'check-resources.ps1')
} finally { Pop-Location }
