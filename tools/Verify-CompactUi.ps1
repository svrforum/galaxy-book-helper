#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$bin=Join-Path $root 'bin-next'
foreach($old in @(Get-Process GalaxyHelper -ErrorAction SilentlyContinue)) {
    if($old.Path -notin @((Join-Path $root 'bin/GalaxyHelper.exe'),(Join-Path $root 'bin-next/GalaxyHelper.exe'))) { throw 'Unexpected running app path.' }
    if(!$old.CloseMainWindow() -or !$old.WaitForExit(15000)) { throw 'Existing app did not exit normally; no forced termination performed.' }
}
$image=Join-Path $root 'artifacts/compact-ui-monitor.png'
$run=Start-Process (Join-Path $bin 'GalaxyHelper.exe') -ArgumentList ('--ui-monitor-smoke "'+$image+'"') -WindowStyle Hidden -PassThru
$clock=[Diagnostics.Stopwatch]::StartNew()
$rows=@(); $cpu=0; $memory=0
while(!$run.WaitForExit(1000)) {
    $run.Refresh(); $cpu=$run.TotalProcessorTime.TotalSeconds; $memory=[Math]::Max($memory,$run.WorkingSet64)
    $record=Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters' LastProbe
    $rows += [ordered]@{ElapsedSeconds=$clock.Elapsed.TotalSeconds;SampleUtc=[DateTime]::FromFileTimeUtc([BitConverter]::ToInt64($record,16)).ToString('o')}
}
[ordered]@{ExitCode=$run.ExitCode;ElapsedSeconds=$clock.Elapsed.TotalSeconds;CpuSeconds=$cpu;LogicalProcessors=[Environment]::ProcessorCount;PeakObservedWorkingSetMB=[Math]::Round($memory/1MB,1);Samples=$rows}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $root 'artifacts/compact-monitor-cost.json') -Encoding UTF8
if($run.ExitCode -ne 0){throw 'Monitor UI failed.'}
$live=Join-Path $root 'artifacts/compact-tray-live.png'
$test=Start-Process (Join-Path $bin 'GalaxyHelper.exe') -ArgumentList ('--ui-control-smoke "'+$live+'"') -WindowStyle Hidden -Wait -PassThru
if($test.ExitCode -ne 0){throw 'Tray test process failed.'}
$result=Get-Content ($live+'.json') -Raw|ConvertFrom-Json
if(!$result.Success){throw $result.Error}
Copy-Item (Join-Path $bin 'GalaxyHelper.exe') (Join-Path $root 'bin/GalaxyHelper.exe')
Copy-Item (Join-Path $bin 'GalaxyHardware.exe') (Join-Path $root 'bin/GalaxyHardware.exe')
$app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -PassThru
[ordered]@{Success=$true;Pid=$app.Id;TimestampUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/compact-ui-rollout.json') -Encoding UTF8
