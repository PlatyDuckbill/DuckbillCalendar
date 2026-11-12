$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resourceDirectory = Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin/Localization'
function Read-Resource([string]$File) {
    [xml]$document = Get-Content -LiteralPath $File -Raw
    $values = @{}
    foreach ($entry in $document.root.data) {
        if ($values.ContainsKey([string]$entry.name)) { throw "Duplicate resource key: $($entry.name)" }
        $values.Add([string]$entry.name,[string]$entry.value)
    }
    return $values
}
$english = Read-Resource (Join-Path $resourceDirectory 'Strings.resx')
$korean = Read-Resource (Join-Path $resourceDirectory 'Strings.ko.resx')
foreach ($key in $english.Keys) {
    if (-not $korean.ContainsKey($key)) { throw "Missing Korean resource: $key" }
    $first = [Text.CompositeFormat]::Parse($english[$key])
    $second = [Text.CompositeFormat]::Parse($korean[$key])
    if ($first.MinimumArgumentCount -ne $second.MinimumArgumentCount) { throw "Resource argument mismatch: $key" }
    $enArguments = @([regex]::Matches($english[$key],'(?<!\{)\{(\d+)(?:[:,][^}]*)?\}(?!\})') | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique)
    $koArguments = @([regex]::Matches($korean[$key],'(?<!\{)\{(\d+)(?:[:,][^}]*)?\}(?!\})') | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique)
    if (($enArguments -join ',') -ne ($koArguments -join ',')) { throw "Resource placeholder mismatch: $key" }
}
foreach ($key in $korean.Keys) { if (-not $english.ContainsKey($key)) { throw "Missing fallback resource: $key" } }
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $calendarRoot 'src/DuckbillCalendar.Plugin') -Recurse -Filter '*.cs' -File | Where-Object {$_.FullName -notmatch '[\\/](obj|bin)[\\/]'}
foreach ($file in $sourceFiles) {
    $body = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($call in [regex]::Matches($body,'UiText\.Get\(\s*"([A-Za-z][A-Za-z0-9]+)"')) {
        $key = $call.Groups[1].Value
        if (-not $english.ContainsKey($key)) { throw "Missing literal UI key: $key in $($file.Name)" }
    }
}
Write-Host "PASS: $($english.Count) Korean/English resource pairs, composite formats and literal UI lookups."
