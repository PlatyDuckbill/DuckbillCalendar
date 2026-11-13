param([string]$DalamudPath)
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $calendarRoot
try {
    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify release source commit' }
    $dirty = @(& git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) { throw 'Commit the reviewed source before preparing a stable release' }
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -DalamudPath $DalamudPath
    & (Join-Path $PSScriptRoot 'check.ps1') -NoBuild -Configuration Release
    $output = Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/bin/Release'
    & dotnet run --project tests/DuckbillCalendar.ImGui.Checks --configuration Release --no-build -- --inspect-plugin (Join-Path $output 'DuckbillCalendar.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Release assembly/resource inspection failed' }
    $manifest = Get-Content -LiteralPath (Join-Path $output 'DuckbillCalendar.json') -Raw | ConvertFrom-Json -AsHashtable
    $version = [string]$manifest.AssemblyVersion
    if ($manifest.InternalName -cne 'DuckbillCalendar' -or $version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $manifest.DalamudApiLevel -ne 15) { throw 'Unexpected release identity' }
    $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $stage = Join-Path $calendarRoot "artifacts/releases/$version/$stamp"
    if (Test-Path -LiteralPath $stage) { throw 'Never overwrite a prepared release' }
    $payload = Join-Path $stage 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null
    $required = @('DuckbillCalendar.dll','DuckbillCalendar.json','DuckbillCalendar.deps.json','DuckbillCalendar.Core.dll','DuckbillCalendar.Infrastructure.dll','images/icon.png')
    foreach ($relative in $required) {
        $destination = Join-Path $payload $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $output $relative) -Destination $destination
    }
    foreach ($relative in @('CHANGELOG.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $calendarRoot $relative) -Destination (Join-Path $payload $relative) }
    [ordered]@{version=$version;sourceCommit=$sourceCommit;builtUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $payload 'BUILD-INFO.json') -Encoding utf8NoBOM
    @(
        "DuckbillCalendar $version",
        '설치: Dalamud 커스텀 플러그인 저장소에 아래 주소를 추가한 뒤 DuckbillCalendar를 검색합니다.',
        'https://raw.githubusercontent.com/PlatyDuckbill/DuckbillRepo/main/pluginmaster.json',
        '명령어: /dbcal, /duckbillcalendar, /dbcal settings, /dbcal changelog',
        '개인 일정·이용권은 로컬 저장이며 알림은 기본 꺼짐입니다.',
        '개발 플러그인으로 수동 확인할 때는 이 폴더 전체를 보존합니다. Core/Infrastructure DLL도 필요합니다.'
    ) | Set-Content -LiteralPath (Join-Path $payload 'INSTALL.txt') -Encoding utf8NoBOM
    $payloadFiles = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
    $hashes = @($payloadFiles | ForEach-Object {
        [ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace([char]92,[char]47);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    $hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.json') -Encoding utf8NoBOM
    $zipName = "DuckbillCalendar-$version.zip"
    $zipPath = Join-Path $stage $zipName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zipPath,[IO.Compression.CompressionLevel]::Optimal,$false)
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($archive.Entries.Count -ne $hashes.Count + 1) { throw 'Unexpected release ZIP contents' }
        foreach ($record in $hashes) {
            $entry = $archive.GetEntry($record.path)
            if ($null -eq $entry) { throw "Release ZIP missing $($record.path)" }
            $stream = $entry.Open()
            try { $actual = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($actual -ne $record.sha256) { throw "Release ZIP hash mismatch: $($record.path)" }
        }
    } finally { $archive.Dispose() }
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$zipHash  $zipName" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding utf8NoBOM
    $download = "https://github.com/PlatyDuckbill/DuckbillCalendar/releases/download/v$version/$zipName"
    $manifest.DownloadLinkInstall = $download
    $manifest.DownloadLinkUpdate = $download
    $manifest.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $manifest.IsTestingExclusive = $false
    $manifest.IsHide = $false
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding utf8NoBOM
    $stage | Set-Content -LiteralPath (Join-Path $calendarRoot 'artifacts/releases/CURRENT.txt') -Encoding utf8NoBOM
    Write-Host "Release source: $sourceCommit"
    Write-Host "Release ZIP: $zipPath"
    Write-Host "Release SHA256: $zipHash"
    Write-Host 'Prepared only. Publish and verify public assets before exposing repo/release.json.'
} finally { Pop-Location }
