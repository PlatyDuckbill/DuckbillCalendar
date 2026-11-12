param([switch]$NoBuild, [ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $calendarRoot
try {
    if (-not $NoBuild) {
        foreach ($project in @('tools/CalendarDataImport','tools/CalendarDataPublish')) {
            & dotnet build $project --configuration $Configuration --nologo
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
        }
    }
    $caseRoot = Join-Path $calendarRoot ('.preview/tool-checks/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
    $script:toolCheckCount = 0
    function Assert-Tool([bool]$Condition, [string]$Description) {
        if (-not $Condition) { throw "FAIL: $Description" }
        $script:toolCheckCount++
    }
    function Invoke-Tool([string]$Project, [string[]]$ToolArguments, [int]$ExpectedExit) {
        & dotnet run --project $Project --configuration $Configuration --no-build -- @ToolArguments
        if ($LASTEXITCODE -ne $ExpectedExit) { throw "Unexpected exit for $Project : $LASTEXITCODE (expected $ExpectedExit)" }
        $script:toolCheckCount++
    }
    $candidatePath = Join-Path $caseRoot 'candidate.json'
    Invoke-Tool 'tools/CalendarDataImport' @('events','data/fixtures/events.synthetic.html',$candidatePath) 0
    $candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
    Assert-Tool ($candidate.candidates.Count -eq 2 -and $candidate.errors.Count -eq 0) 'two cards, global banner ignored'
    Assert-Tool ($candidate.isSynthetic -and -not $candidate.canPublish -and -not $candidate.completeCoverage) 'candidate never implies approval/coverage'
    Assert-Tool ($candidate.candidates[0].phases.Count -eq 2) 'participation and reward are separate'
    Assert-Tool ($null -eq $candidate.candidates[1].phases[0].endsAt -and $candidate.candidates[1].phases[0].unresolvedEndText.Contains('V8.0')) 'conditional end preserved'
    $candidateBefore = [IO.File]::ReadAllBytes($candidatePath)
    Invoke-Tool 'tools/CalendarDataImport' @('events','data/fixtures/events.synthetic.html',$candidatePath) 2
    Assert-Tool ([Convert]::ToBase64String($candidateBefore) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($candidatePath))) 'existing output preserved'
    $emptyPath = Join-Path $caseRoot 'empty.html'
    [IO.File]::WriteAllText($emptyPath,'<!doctype html><html><body>Error page, HTTP 200</body></html>')
    $emptyCandidate = Join-Path $caseRoot 'empty.json'
    Invoke-Tool 'tools/CalendarDataImport' @('events',$emptyPath,$emptyCandidate) 1
    Assert-Tool ((Get-Content -LiteralPath $emptyCandidate -Raw | ConvertFrom-Json).errors -contains 'UnexpectedEmptyResult') 'HTML error page is not empty success'
    $oversize = Join-Path $caseRoot 'large.html'
    [IO.File]::WriteAllBytes($oversize,[byte[]]::new(2 * 1024 * 1024 + 1))
    Invoke-Tool 'tools/CalendarDataImport' @('events',$oversize,(Join-Path $caseRoot 'large.json')) 2
    Assert-Tool (-not (Test-Path -LiteralPath (Join-Path $caseRoot 'large.json'))) 'oversized input produces no candidate'
    $feedPath = 'data/contracts/v2/revisions/000001.json'
    Invoke-Tool 'tools/CalendarDataPublish' @('validate',$feedPath) 1
    Invoke-Tool 'tools/CalendarDataPublish' @('validate',$feedPath,'--allow-example') 0
    $prepared = Join-Path $caseRoot 'prepared'
    Invoke-Tool 'tools/CalendarDataPublish' @('prepare',$feedPath,$prepared,'--allow-example') 0
    $currentPath = Join-Path $prepared 'current.json'
    $pointer = Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json
    $payloadPath = Join-Path $prepared $pointer.content.path
    Assert-Tool ($pointer.isExample -and $pointer.publicationSequence -eq 1 -and $pointer.content.revision -eq 1) 'first local publication is explicit example'
    Assert-Tool ((Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $pointer.content.sha256) 'pointer SHA matches exact payload'
    Assert-Tool ((Get-Item -LiteralPath $payloadPath).Length -eq $pointer.content.bytes) 'pointer byte count matches'
    $payloadJson = Get-Content -LiteralPath $payloadPath -Raw | ConvertFrom-Json
    $unknown = $payloadJson.events[2].phases[0].end
    Assert-Tool ($unknown.kind -eq 'unknown' -and @($unknown.PSObject.Properties).Count -eq 2) 'unknown boundary uses tagged wire shape only'
    Assert-Tool (@($pointer.health.sources | Where-Object {$_.completeCoverage -or $_.lastResult -ne 'not-run'}).Count -eq 0) 'local preparation never fabricates collection health'
    $originalPayloadHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash
    Invoke-Tool 'tools/CalendarDataPublish' @('prepare',$feedPath,$prepared,'--allow-example') 0
    $next = Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json
    Assert-Tool ($next.content.revision -eq 2 -and $next.publicationSequence -eq 2) 'new immutable revision allocated'
    Assert-Tool ((Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash -eq $originalPayloadHash) 'previous immutable payload untouched'
    $malformed = Join-Path $caseRoot 'duplicate.json'
    [IO.File]::WriteAllText($malformed,'{"schemaVersion":2,"schemaVersion":2}')
    Invoke-Tool 'tools/CalendarDataPublish' @('validate',$malformed,'--allow-example') 2
    Write-Host "PASS: $script:toolCheckCount offline CLI checks. Artifacts: $caseRoot"
} finally { Pop-Location }
