param([string]$OutputPath = 'docs/assets/data-update-flow.png')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$diagramPath = [IO.Path]::GetFullPath((Join-Path (Join-Path $PSScriptRoot '..') $OutputPath))
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($diagramPath)) -Force | Out-Null
$canvas = [Drawing.Bitmap]::new(1680,1080)
$graphics = [Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$titleFont = [Drawing.Font]::new('맑은 고딕',32,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$sectionFont = [Drawing.Font]::new('맑은 고딕',24,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$boxFont = [Drawing.Font]::new('맑은 고딕',21,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$bodyFont = [Drawing.Font]::new('맑은 고딕',18,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
$textBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#E7EEF8'))
$mutedBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#B1C0D3'))
$format = [Drawing.StringFormat]::new()
$format.Alignment = [Drawing.StringAlignment]::Center
$format.LineAlignment = [Drawing.StringAlignment]::Center
function Draw-Text([string]$Text,[Drawing.Font]$Font,[Drawing.Brush]$Brush,[float]$X,[float]$Y,[float]$Width,[float]$Height) {
    $graphics.DrawString($Text,$Font,$Brush,[Drawing.RectangleF]::new($X,$Y,$Width,$Height),$format)
}
function Draw-Box([float]$X,[float]$Y,[float]$Width,[string]$Title,[string]$Body,[string]$Color) {
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#19263A'))
    $border = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($Color),2)
    $label = [Drawing.SolidBrush]::new($border.Color)
    try {
        $graphics.FillRectangle($background,$X,$Y,$Width,145)
        $graphics.DrawRectangle($border,$X,$Y,$Width,145)
        Draw-Text $Title $boxFont $label ($X+8) ($Y+12) ($Width-16) 38
        Draw-Text $Body $bodyFont $textBrush ($X+10) ($Y+53) ($Width-20) 78
    } finally { $background.Dispose(); $border.Dispose(); $label.Dispose() }
}
function Draw-Arrow([float]$X,[float]$Y,[float]$EndX) {
    $arrow = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#8296AF'),2.5)
    $cap = [Drawing.Drawing2D.AdjustableArrowCap]::new(4,5)
    try { $arrow.CustomEndCap = $cap; $graphics.DrawLine($arrow,$X,$Y,$EndX,$Y) }
    finally { $arrow.Dispose(); $cap.Dispose() }
}
try {
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#0C1423'))
    Draw-Text '오리부리 달력: 수집부터 사용자 반영까지' $titleFont $textBrush 45 20 1590 60
    Draw-Text '0.2.5 구조 · 일정 데이터 갱신 / 전장 갱신 / 플러그인 기능 업데이트' $bodyFont $mutedBrush 45 82 1590 35
    Draw-Text '① 공개 일정 데이터  —  사용자는 DLL을 다시 설치할 필요가 없습니다' $sectionFont $textBrush 45 140 1590 45
    $steps = @(
        @('공식 사이트',"이벤트·공지·패치`n점검·출시·대회 등"),
        @('수집·정규화',"GitHub Actions`n2시간 간격 예약"),
        @('JSON 게시',"원문·날짜·범위 검증`n버전 파일 + 최신 안내"),
        @('사용자 플러그인',"자동 갱신을 켠 경우`n기본 1시간 / 수동 확인"),
        @('검증·캐시',"SHA-256·크기·버전`n정상 파일만 적용"),
        @('달력에 반영',"주요 일정 분류·묶음`n개인 일정과 함께 표시")
    )
    for ($i=0; $i -lt $steps.Count; $i++) {
        $x = 48 + $i*266
        Draw-Box $x 205 240 $steps[$i][0] $steps[$i][1] '#91B5FF'
        if ($i -lt $steps.Count-1) { Draw-Arrow ($x+244) 278 ($x+261) }
    }
    Draw-Text '실패한 출처는 이전 자료 유지 · 파일 검증 실패 시 이전 정상 캐시 사용 · 변경이 없으면 파일 재사용' $bodyFont $mutedBrush 45 365 1590 40
    Draw-Text '일정 최신 정보 받기: 기본 켜짐 · 주기는 30분 / 1·3·6·12시간 중 선택 · 게임 실행 중에만 동작' $bodyFont $mutedBrush 45 405 1590 28
    Draw-Text '② 오늘·내일 전장  —  공개 일정과 별도로, 제공된 한국 날짜만 사용합니다' $sectionFont $textBrush 45 440 1590 45
    $frontline = @(
        @('에테르웨이 API',"한국 날짜별 전장`n추정 회전은 만들지 않음"),
        @('6시간마다 확인',"게임 실행 중에 확인`n끄면 진행 요청도 취소"),
        @('날짜·맵 검증',"기기에 캐시 저장`n24시간 초과 자료 제외"),
        @('오늘·내일 표시',"상단에 전장 요약`n날짜 칸 표기는 선택")
    )
    for ($i=0; $i -lt $frontline.Count; $i++) {
        $x = 48 + $i*400
        Draw-Box $x 505 374 $frontline[$i][0] $frontline[$i][1] '#7EDBC3'
        if ($i -lt $frontline.Count-1) { Draw-Arrow ($x+378) 578 ($x+395) }
    }
    Draw-Text '③ 플러그인 기능 업데이트  —  현재 개발판은 DLL 세트를 교체하고 다시 로드합니다' $sectionFont $textBrush 45 725 1590 45
    $binary = @(
        @('개발판 ZIP / 로컬 빌드',"Plugin·Core·Infrastructure DLL`n같은 버전의 파일을 함께 준비"),
        @('사용자가 파일 교체',"기존 개발 경로에 DLL 세트 반영`n현재 별도 자동 배포 저장소는 없음"),
        @('플러그인 다시 로드',"새 기능·UI·수집 처리 코드 적용`n개인 저장 자료는 유지")
    )
    for ($i=0; $i -lt $binary.Count; $i++) {
        $x = 48 + $i*536
        Draw-Box $x 790 510 $binary[$i][0] $binary[$i][1] '#E3BE78'
        if ($i -lt $binary.Count-1) { Draw-Arrow ($x+514) 863 ($x+531) }
    }
    Draw-Text '개인 일정·이용권·숨김·색상 설정은 기기에 저장합니다. 공개 일정 서버로 보내지 않습니다.' $bodyFont $mutedBrush 45 980 1590 55
    $canvas.Save($diagramPath,[Drawing.Imaging.ImageFormat]::Png)
    Write-Output $diagramPath
} finally {
    $format.Dispose(); $textBrush.Dispose(); $mutedBrush.Dispose(); $titleFont.Dispose(); $sectionFont.Dispose(); $boxFont.Dispose(); $bodyFont.Dispose(); $graphics.Dispose(); $canvas.Dispose()
}
