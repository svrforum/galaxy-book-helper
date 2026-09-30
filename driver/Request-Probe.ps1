#Requires -RunAsAdministrator
# Requests only the driver's three fixed read commands. No fan/power writes.
param([ValidateRange(5,120)][int]$TimeoutSeconds = 45)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$reportPath = Join-Path $root 'artifacts\driver-request-result.json'
$key = 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); Success=$false; Completed=$false }
try {
    if ((Get-CimInstance Win32_SystemDriver -Filter "Name='GalaxyFanRead'").State -ne 'Running') { throw 'Read-probe driver is not running.' }
    $state = Get-ItemProperty -LiteralPath $key
    if ($null -eq $state.ProbeCompletedSequence) { throw 'The request-capable driver has not completed initialization. It may need the pending reboot or 30 seconds after D0 entry.' }
    do { $sequence = Get-Random -Minimum 1 -Maximum 2147483647 }
    while ($sequence -eq $state.ProbeRequestSequence -or $sequence -eq $state.ProbeCompletedSequence)
    $report.Sequence = $sequence
    $requestedUtc = [DateTime]::UtcNow
    New-ItemProperty -LiteralPath $key -Name ProbeRequestSequence -PropertyType DWord -Value $sequence -Force | Out-Null
    $deadline = $requestedUtc.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $state = Get-ItemProperty -LiteralPath $key
        if ($state.ProbeRequestSequence -ne $sequence) { throw 'Another request replaced this request; no result is attributed to it.' }
        if ($state.ProbeCompletedSequence -eq $sequence) {
            $report.Completed = $true
            $report.Probe = & (Join-Path $PSScriptRoot 'Read-Probe.ps1') -Detailed
            if ([DateTime]::Parse($report.Probe.SampleUtc).ToUniversalTime() -lt $requestedUtc) { throw 'Acknowledgement exists but the result timestamp is stale.' }
            $report.Success = $report.Probe.Success
            break
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$report.Completed) { throw 'Timed out waiting for a fresh probe; cached RPM is not a new reading.' }
} catch { $report.Error = $_.Exception.Message }
finally {
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
[pscustomobject]$report
