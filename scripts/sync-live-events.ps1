param(
    [ValidateSet('Collect','Prepare')][string]$Mode = 'Collect',
    [string]$CollectionDirectory,
    [string]$ReviewPath,
    [switch]$BuildPlugin,
    [string]$DalamudPath
)
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $calendarRoot
try {
    & dotnet build tools/CalendarDataCollect --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Collector build failed' }
    if (-not $CollectionDirectory) {
        if ($Mode -eq 'Prepare') { throw 'Prepare requires the exact reviewed CollectionDirectory' }
        $CollectionDirectory = Join-Path $calendarRoot ('.preview/event-collections/' + [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    }
    $CollectionDirectory = [IO.Path]::GetFullPath($CollectionDirectory)
    if ($Mode -eq 'Collect') {
        & dotnet run --project tools/CalendarDataCollect --no-build -- collect $CollectionDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Collection has warnings or failed; no snapshot changed' }
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $CollectionDirectory 'collection.json') | ConvertFrom-Json
        $template = [ordered]@{schemaVersion=1;snapshotSha256=$manifest.snapshotSha256;reviewedAt=$null;reviewedBy='';scope='event-list-first-page';approved=@()}
        $templatePath = Join-Path $CollectionDirectory 'review.template.json'
        [IO.File]::WriteAllText($templatePath,($template | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
        Write-Host "Review source.html and candidates.json in $CollectionDirectory"
        Write-Host 'Fill reviewedAt/reviewedBy and explicitly selected approved records (id, normalizedRecordHash, category).'
        Write-Host 'Then run this script with -Mode Prepare -CollectionDirectory <same-directory> -ReviewPath <review.json> -BuildPlugin.'
        return
    }
    if (-not $ReviewPath) { throw 'Prepare requires explicit ReviewPath; collection alone cannot approve events' }
    $ReviewPath = [IO.Path]::GetFullPath($ReviewPath)
    $destination = Join-Path $calendarRoot 'data/live'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $temporary = Join-Path $destination ('kr-events.prepared-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        & dotnet run --project tools/CalendarDataCollect --no-build -- prepare $CollectionDirectory $ReviewPath $temporary
        if ($LASTEXITCODE -ne 0) { throw 'Review/provenance validation failed; snapshot not changed' }
        # Replace one complete file, never truncate the active snapshot during a build.
        [IO.File]::Move($temporary,(Join-Path $destination 'kr-events.snapshot.json'),$true)
        $reviewDestination = Join-Path $destination 'kr-events.review.json'
        if ($ReviewPath -ne [IO.Path]::GetFullPath($reviewDestination)) { Copy-Item -LiteralPath $ReviewPath -Destination $reviewDestination -Force }
        Copy-Item -LiteralPath (Join-Path $CollectionDirectory 'collection.json') -Destination (Join-Path $destination 'kr-events.collection.json') -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
    if ($BuildPlugin) { & (Join-Path $PSScriptRoot 'build.ps1') -DalamudPath $DalamudPath }
    Write-Host 'Reviewed bundled event snapshot updated. No remote upload. Reload the development plugin after building.'
} finally { Pop-Location }
