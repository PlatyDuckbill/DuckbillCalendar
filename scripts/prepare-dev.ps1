param([string]$DalamudPath)
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -DalamudPath $DalamudPath
& (Join-Path $PSScriptRoot 'check.ps1') -NoBuild -Configuration Release
$output = Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/bin/Release'
& dotnet run --project (Join-Path $calendarRoot 'tests/DuckbillCalendar.ImGui.Checks') --configuration Release --no-build -- --inspect-plugin (Join-Path $output 'DuckbillCalendar.dll')
if ($LASTEXITCODE -ne 0) { throw 'Plugin artifact resource verification failed' }
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $calendarRoot 'artifacts/development'))
$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss',[Globalization.CultureInfo]::InvariantCulture)
$calendarVersion = ([xml](Get-Content -LiteralPath (Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/DuckbillCalendar.Plugin.csproj') -Raw)).Project.PropertyGroup.Version
$bundleName = "DuckbillCalendar-$calendarVersion-dev-$stamp"
$stage = [IO.Path]::GetFullPath((Join-Path $artifactRoot $bundleName))
if (-not $stage.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid artifact path' }
if (Test-Path -LiteralPath $stage) { throw 'Development bundle already exists; do not overwrite a reviewed artifact.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$required = @('DuckbillCalendar.dll','DuckbillCalendar.json','DuckbillCalendar.deps.json','DuckbillCalendar.Core.dll','DuckbillCalendar.Infrastructure.dll','images/icon.png')
foreach ($relative in $required) {
    $source = Join-Path $output $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing runtime file: $relative" }
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
$readme = @"
DuckbillCalendar $calendarVersion 개발 확인용

이 폴더 전체를 보존하세요. Core/Infrastructure DLL이 필요합니다. 한국어 리소스는 주 DLL에 포함됩니다.
Dalamud 개발 플러그인 위치에 이 폴더의 DuckbillCalendar.dll을 등록해 수동으로 로드합니다.
/dbcal 또는 /duckbillcalendar: 주 화면
/dbcal settings: 설정
/dbcal changelog: 업데이트 내역 (최초 배포)

개인 일정·이용권은 로컬 저장입니다. 공식 이벤트 저장본을 포함하며 예시 모드에서는 '실제 일정 보기'로 전환합니다.
일정 최신 정보 받기와 전장 수신은 새 설치에서 기본 켜짐입니다. 기존에 저장한 끄기 선택은 유지합니다. 끄면 새 일정/변경 사항을 받지 않고 저장된 일정만 표시합니다. 로컬 채팅 알림은 기본 꺼짐입니다.
새 설치의 전체 알림·새 글 감지·분류/일정 알림·채팅은 기본 꺼짐입니다. 기존에 직접 저장한 선택은 유지합니다. 알림은 사용자가 켠 뒤 게임 로그인 중에만 처리합니다.
새 공지·이벤트는 첫 정상 원격 수신 이후 새로운 ID부터 알립니다. 실시간 푸시가 아니라 다음 일정 수신 시 확인합니다.
종 버튼은 메인 화면 안의 내역을 엽니다. 목록에서 알림 시점 확인·켜기·끄기가 가능하고 세부 편집은 배경을 덮지 않는 별도 창에서 합니다. 직접 등록한 일정은 수정 창에서 알림도 편집합니다. 채팅 소비 기록 저장 후 출력 전 종료되면 채팅이 누락될 수 있지만 내역은 남습니다.
설정의 일정 색상 및 아이콘에서 공대별 사용자 분류를 추가하고 개인 일정에 지정합니다. 기본 개인 일정·공격대도 삭제할 수 있습니다. 미분류는 항상 선택할 수 있으며 삭제할 때 이동 위치를 선택합니다.
분류별 기본 시점과 일정별 예외/끄기/상속 복원을 지원하며 개별 설정은 검색·페이지 목록으로 관리합니다. 공격대 반복 등록에서 요일별 시각과 기간/주 간격을 정해 유한한 일정을 한 번에 만듭니다. 삭제는 이번 일정/이후 일정/동일 등록 전체를 선택합니다. 무기한 자동 생성·공대원·출석·공유는 포함하지 않습니다.
이벤트 링크는 날짜의 근거 페이지와 개별 상세 링크를 구분하며 이벤트 목록을 공식 상세 안내로 열지 않습니다. 매년5월9일에는 작은 오리부리 꼬마친구 아이콘과 오구데이를 표시합니다.
첫 유효 상태 저장 전에 state.before-v032.json을 한 번 보존합니다. 덮어쓰지 않습니다. 되돌릴 때 플러그인을 끄고 현재 상태를 별도 보관한 뒤 백업을 복원하세요.
전장은 에테르웨이 공개 API의 한국 날짜별 맵을 표시합니다. 전장 수신은 설정에서 끌 수 있습니다.
전투·임무/PvP 중 자동 갱신은 기본 보류하고 수동 갱신은 기존 제한 안에서 가능합니다.
게임 상태를 이유로 창을 자동으로 숨기지 않습니다. 자동 요청만 미루며 창은 직접 열고 닫습니다.
일·시간·분 단위로 충전합니다. 30분만 또는30일6시간15분처럼 입력하고 결과를 미리 확인합니다.
활성 이용권의 만료 시각이 미등록이면 시간/분 충전 전에 기존 만료 시각을 입력해야 합니다. 일수만 더하면 날짜 정밀도를 유지합니다.
설정의 이용권에서 초기화를 누르고 확인하면 해당 이용권의 등록 내용만 미등록 상태로 되돌립니다.
일정만 숨기기/관련 전체 숨기기를 구분하며 설정에서 복원합니다. 기존 전체 숨김은 그대로 유지됩니다.

KR API15 참조 Release 컴파일과 오프라인 검사를 수행했지만 인게임 화면·게임 글꼴/DPI·실제 채팅 전달은 확인 대기입니다.
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
