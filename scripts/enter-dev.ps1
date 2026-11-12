param([string]$DalamudPath)
$ErrorActionPreference = 'Stop'
$calendarRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $DalamudPath) {
    if ($env:DALAMUD_HOME) { $DalamudPath = $env:DALAMUD_HOME }
    else { $DalamudPath = Join-Path $env:APPDATA 'XIVLauncherKR\addon\Hooks\dev' }
}
$calendarReferences = [IO.Path]::GetFullPath($DalamudPath)
foreach ($name in @('Dalamud.dll','Dalamud.Bindings.ImGui.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $calendarReferences $name))) { throw "Missing inspected reference: $name in $calendarReferences" }
}
$calendarApi = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $calendarReferences 'Dalamud.dll')).Version
if ($calendarApi.Major -ne 15) { throw "Expected Dalamud API 15; found $calendarApi" }
$env:DALAMUD_HOME = $calendarReferences
$env:DUCKBILL_CALENDAR_ROOT = $calendarRoot
Write-Host "DuckbillCalendar: $calendarRoot"
Write-Host "Dalamud references: $calendarReferences ($calendarApi)"
