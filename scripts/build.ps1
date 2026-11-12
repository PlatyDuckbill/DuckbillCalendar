param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [string]$DalamudPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'enter-dev.ps1') -DalamudPath $DalamudPath
Push-Location $env:DUCKBILL_CALENDAR_ROOT
try {
    & dotnet build DuckbillCalendar.slnx --configuration $Configuration --nologo "-p:DalamudLibPath=$env:DALAMUD_HOME" -m:1
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    Write-Host "Development plugin DLL: $(Join-Path $env:DUCKBILL_CALENDAR_ROOT "src/DuckbillCalendar.Plugin/bin/$Configuration/DuckbillCalendar.dll")"
} finally { Pop-Location }
