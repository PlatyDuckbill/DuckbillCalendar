param([switch]$NoBuild, [ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$pipelineRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }
Push-Location $pipelineRoot
try {
    $publicationArea = Join-Path $pipelineRoot ('.preview/pipeline-checks/' + [Guid]::NewGuid().ToString('N'))
    & dotnet run --project tools/CalendarDataPublish -c $Configuration --no-build -- prepare data/live/kr-calendar.snapshot.json $publicationArea
    if ($LASTEXITCODE -ne 0) { throw 'Pipeline publication preparation failed' }
    & dotnet run --project tests/DuckbillCalendar.Infrastructure.Checks -c $Configuration --no-build -- --publication-smoke $publicationArea
    if ($LASTEXITCODE -ne 0) { throw 'Pipeline consumer/restart validation failed' }
    Write-Host "PASS: isolated offline publication/client pipeline; $publicationArea"
} finally { Pop-Location }
