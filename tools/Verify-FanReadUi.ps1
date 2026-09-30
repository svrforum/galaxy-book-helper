#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o') }
try {
    $output = & (Join-Path $root 'bin/GalaxyHardware.exe') fan-read
    if ($LASTEXITCODE -ne 0) { throw 'C# fan read failed.' }
    $report.Fan = $output | ConvertFrom-Json
    if (!$report.Fan.Success -or !$report.Fan.FreshRequestCompleted) { throw 'No successful fresh C# fan sample.' }
    $image = Join-Path $root 'artifacts/fan-read-ui.png'
    $run = Start-Process -FilePath (Join-Path $root 'bin/GalaxyHelper.exe') -ArgumentList ('--ui-smoke "' + $image + '"') -WindowStyle Hidden -Wait -PassThru
    $report.UiExitCode = $run.ExitCode
    $report.Success = $run.ExitCode -eq 0
} catch { $report.Success=$false; $report.Error=$_.Exception.Message }
$report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'artifacts/fan-read-ui-verification.json') -Encoding UTF8
