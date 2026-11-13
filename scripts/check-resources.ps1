$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$document = Get-Content -LiteralPath (Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/Localization/Strings.resx') -Raw
$values = @{}
foreach ($entry in $document.root.data) {
    if ($values.ContainsKey([string]$entry.name)) { throw "Duplicate resource key: $($entry.name)" }
    $values.Add([string]$entry.name, [string]$entry.value)
    [void][Text.CompositeFormat]::Parse([string]$entry.value)
}
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin') -Recurse -Filter '*.cs' -File | Where-Object {$_.FullName -notmatch '[\\/](obj|bin)[\\/]'}
foreach ($file in $sourceFiles) {
    $body = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($call in [regex]::Matches($body,'UiText\.Get\(\s*"([A-Za-z][A-Za-z0-9]+)"')) {
        $key = $call.Groups[1].Value
        if (-not $values.ContainsKey($key)) { throw "Missing Korean resource: $key in $($file.Name)" }
    }
}
$duckbillFixture = Get-Content -LiteralPath (Join-Path $calendarRoot 'tests/DuckbillCalendar.ImGui.Checks/Fixtures/bitty-duckbill.json') -Raw | ConvertFrom-Json
$duckbillPng = Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/Assets/bitty-duckbill-cutout.png'
$duckbillRgba = Join-Path $calendarRoot 'tests/DuckbillCalendar.ImGui.Checks/Fixtures/bitty-duckbill.rgba'
if ((Get-FileHash -LiteralPath $duckbillPng -Algorithm SHA256).Hash.ToLowerInvariant() -ne $duckbillFixture.pngSha256 -or
    (Get-FileHash -LiteralPath $duckbillRgba -Algorithm SHA256).Hash.ToLowerInvariant() -ne $duckbillFixture.rgbaSha256) { throw 'Ogu Day asset and native preview fixture differ' }
Write-Host "PASS: $($values.Count) Korean resource keys, composite formats, UI lookups and Ogu Day asset hashes."
