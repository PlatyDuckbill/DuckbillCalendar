param([string]$DalamudPath)
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -DalamudPath $DalamudPath
& (Join-Path $PSScriptRoot 'check.ps1') -NoBuild -Configuration Release
$output = Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/bin/Release'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $calendarRoot 'artifacts/development'))
$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss',[Globalization.CultureInfo]::InvariantCulture)
$bundleName = "DuckbillCalendar-0.1.0.0-dev-$stamp"
$stage = [IO.Path]::GetFullPath((Join-Path $artifactRoot $bundleName))
if (-not $stage.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid artifact path' }
if (Test-Path -LiteralPath $stage) { throw 'Development bundle already exists; do not overwrite a reviewed artifact.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$required = @('DuckbillCalendar.dll','DuckbillCalendar.json','DuckbillCalendar.deps.json','DuckbillCalendar.Core.dll','DuckbillCalendar.Infrastructure.dll','ko/DuckbillCalendar.resources.dll')
foreach ($relative in $required) {
    $source = Join-Path $output $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing runtime file: $relative" }
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
$readme = @"
DuckbillCalendar 0.1.0.0 개발 확인용

이 폴더 전체를 보존하세요. Core/Infrastructure DLL과 ko 리소스가 필요합니다.
Dalamud 개발 플러그인 위치에 이 폴더의 DuckbillCalendar.dll을 등록해 수동으로 로드합니다.
/dbcal 또는 /duckbillcalendar: 주 화면
/dbcal compact: 작은 화면
/dbcal settings: 설정

개인 일정·이용권은 로컬 저장입니다. 설정의 공개 예시를 켜면 예시 데이터로 화면을 확인할 수 있습니다.
원격 갱신과 로컬 채팅 알림은 기본 꺼짐입니다. 운영 피드 주소는 아직 게시하지 않았습니다.
알림은 선택과 출력 설정을 모두 켜야 하며 게임 실행 중에만 동작합니다.
발송 기록 저장 후 종료되면 알림이 누락될 수 있습니다. 기록된 알림은 재전송하지 않습니다.
전장 실제 기준일은 미검증이며 확인 불가로 표시합니다. PvP 중에는 플러그인 화면을 숨깁니다.

KR/글로벌 참조 컴파일과 오프라인 검사를 수행했지만 인게임 화면·음성·실제 채팅 전달은 확인 대기입니다.
이 파일은 공식 배포판이 아니며 원격 Release·설치 저장소에 게시하지 않았습니다.
"@
[IO.File]::WriteAllText((Join-Path $stage 'README-DEV.txt'),$readme,[Text.UTF8Encoding]::new($false))
$checksums = foreach ($relative in $required) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
$zip = Join-Path $artifactRoot ($bundleName + '.zip')
Compress-Archive -LiteralPath @((Get-ChildItem -LiteralPath $stage -Force).FullName) -DestinationPath $zip -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($relative in $required) {
        $entry = $archive.Entries | Where-Object {$_.FullName.Replace('\','/') -eq $relative}
        if (-not $entry) { throw "Archive missing: $relative" }
        $stream = $entry.Open()
        try { $actual = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        $expected = (Get-FileHash -LiteralPath (Join-Path $stage $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) { throw "Archive hash mismatch: $relative" }
    }
} finally { $archive.Dispose() }
Write-Host "Development DLL: $(Join-Path $stage 'DuckbillCalendar.dll')"
Write-Host "Development ZIP: $zip"
Write-Host "ZIP SHA256: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)"
