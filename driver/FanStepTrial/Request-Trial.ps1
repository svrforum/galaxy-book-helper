#Requires -RunAsAdministrator
# Fixed step 2, approximately 15 seconds, then firmware Auto. No arbitrary inputs.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$key = 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); RequestedStep=2; RequestedSeconds=15; Success=$false; Samples=@() }
try {
    $state = Get-ItemProperty $key
    $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime()
    if (!$state.FanTrialReadyTime -or [DateTime]::FromFileTimeUtc($state.FanTrialReadyTime) -lt $boot) { throw 'The trial-enabled driver has not initialized in this boot. No request was written.' }
    $report.Baseline = & (Join-Path $root 'driver/Request-Probe.ps1')
    if (!$report.Baseline.Success) { throw 'Fresh baseline read failed; no fan setting requested.' }
    do { $sequence = Get-Random -Minimum 1 -Maximum 2147483647 } while ($sequence -eq $state.FanTrialRequestSequence)
    $report.Sequence = $sequence
    New-ItemProperty -Path $key -Name FanTrialRequestSequence -PropertyType DWord -Value $sequence -Force | Out-Null
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $seen = 0L
    do {
        Start-Sleep -Milliseconds 200
        $state = Get-ItemProperty $key
        if ($state.FanTrialRequestSequence -ne $sequence) { throw 'Trial request was replaced. Kernel restoration still owns the original trial.' }
        $record = $state.FanTrialResult
        if ($record -isnot [byte[]] -or $record.Length -ne 56 -or [BitConverter]::ToUInt32($record,0) -ne 1 -or [BitConverter]::ToUInt32($record,4) -ne $sequence) { continue }
        $timestamp = [BitConverter]::ToInt64($record,32)
        if ($timestamp -eq $seen) { continue }
        $seen = $timestamp
        $row = [pscustomobject]@{
            SampleUtc=[DateTime]::FromFileTimeUtc($timestamp).ToString('o')
            State=[BitConverter]::ToUInt32($record,8)
            StopReason=[BitConverter]::ToUInt32($record,48)
            ApplyStatus=('0x{0:X8}' -f [BitConverter]::ToUInt32($record,12))
            RestoreStatus=('0x{0:X8}' -f [BitConverter]::ToUInt32($record,16))
            TemperatureC=[BitConverter]::ToInt32($record,20)
            Fan1Rpm=$(if ([BitConverter]::ToUInt32($record,24) -ne [uint32]::MaxValue) { [BitConverter]::ToUInt32($record,24) } else { $null })
            Fan2Rpm=$(if ([BitConverter]::ToUInt32($record,28) -ne [uint32]::MaxValue) { [BitConverter]::ToUInt32($record,28) } else { $null })
        }
        $report.Samples += $row
        $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $root 'artifacts/fan-step-trial-result.json') -Encoding UTF8
        if ($row.State -eq 4) { $report.AutoRestored=$true; $report.Success=$row.ApplyStatus -eq '0x00000000' -and $row.RestoreStatus -eq '0x00000000' -and $row.StopReason -eq 1; break }
        if ($row.State -eq 6) { throw 'Kernel preconditions rejected this trial; fan write was not attempted.' }
    } while ($clock.Elapsed.TotalSeconds -lt 45)
    if (!$report.AutoRestored) { throw 'Auto restoration was not confirmed. Inspect driver status before any further trial.' }
    $report.After = & (Join-Path $root 'driver/Request-Probe.ps1')
} catch { $report.Error=$_.Exception.Message }
finally { $report.FinishedUtc=[DateTime]::UtcNow.ToString('o'); $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $root 'artifacts/fan-step-trial-result.json') -Encoding UTF8 }
[pscustomobject]$report
