#Requires -RunAsAdministrator
# User-authorized bounded tests. Does not reboot or persist a running fan setting.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$exe = Join-Path $root 'bin-next/GalaxyHardware.exe'
$key = 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
$reportPath = Join-Path $root 'artifacts/fan-control-verification.json'
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); Stage='Preflight'; Success=$false; Results=@() }
function Save-Report { $report | ConvertTo-Json -Depth 6 | Set-Content $reportPath -Encoding UTF8 }
function Run-Stage([string]$name,[string[]]$arguments) {
    $report.Stage=$name; Save-Report
    $path=Join-Path $root ('artifacts/fan-control-' + $name + '.json')
    $output=@(& $exe @arguments 2>&1)
    $code=$LASTEXITCODE
    $text=$output -join "`n"
    [IO.File]::WriteAllText($path,$text)
    if ($code -ne 0) { throw "Stage $name failed ($code). See $path" }
    $value=$text | ConvertFrom-Json
    if (!$value.Success) { throw "Stage $name did not verify success. See $path" }
    $report.Results += [pscustomobject]@{Stage=$name;Success=$true;File=$path}
    Save-Report
    return $value
}
try {
    Save-Report
    $state=Get-ItemProperty $key
    $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime()
    if (!$state.FanControlReadyTime -or [DateTime]::FromFileTimeUtc($state.FanControlReadyTime) -lt $boot) { throw 'The continuous-control driver is not initialized in this boot. No fan write requested.' }
    $null=Run-Stage 'step2' @('fan-control-trial','2','15')
    $lease=Run-Stage 'lease' @('fan-lease-test')
    if (!$lease.LeaseExpiryVerified) { throw 'Watchdog did not verify lease-expiry recovery.' }
    $cal=Run-Stage 'calibration' @('fan-calibrate')
    $entry=$cal.Calibration.Entries | Where-Object Step -eq 2
    $target=[int]([Math]::Ceiling(([Math]::Max($entry.Fan1Peak,$entry.Fan2Peak)+100)/100)*100)
    $null=Run-Stage 'rpm-target' @('fan-rpm-trial',[string]$target)
    $report.TargetRpm=$target
    $report.Success=$true
    $report.Stage='Complete'
} catch { $report.Error=$_.Exception.Message }
finally { $report.FinishedUtc=[DateTime]::UtcNow.ToString('o'); Save-Report }
[pscustomobject]$report
