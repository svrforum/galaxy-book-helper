# Observe Windows fan telemetry only. Does not invoke ACPI setters or alter cooling policies.
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"') -Verb RunAs -WindowStyle Hidden -Wait
 exit
}
$runDir=Join-Path $projectRoot ('artifacts\fan-etw-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runDir | Out-Null
$sessionName='GalaxyFan-'+[guid]::NewGuid().ToString('N')
$etl=Join-Path $runDir 'fan.etl'
$providerFile=Join-Path $runDir 'providers.txt'
@('{63bca7a1-77ec-4ea7-95d0-98d3f0c0ebf7} 0xffffffffffffffff 5','{C42BBFDB-4140-4ada-81DF-2B9A18AC6A7B} 0xffffffffffffffff 5') | Set-Content $providerFile -Encoding ascii
$started=$false
try {
 # Providers from Microsoft's Fan Noise Signal tracing guide. Unique transient ETW session.
 & logman.exe create trace $sessionName -o $etl -f bincirc -max 16 -pf $providerFile -ets 2>&1 | Out-File (Join-Path $runDir 'start.txt')
 if($LASTEXITCODE -ne 0){throw 'ETW start failed. See start.txt'}
 $started=$true
 Start-Sleep -Seconds 20
} catch { $_.Exception.ToString() | Set-Content (Join-Path $runDir 'error.txt') }
finally {
 if($started){ & logman.exe stop $sessionName -ets 2>&1 | Out-File (Join-Path $runDir 'stop.txt') }
}
if(Test-Path $etl){
 & tracerpt.exe $etl -o (Join-Path $runDir 'events.xml') -of XML -y -summary (Join-Path $runDir 'summary.txt') 2>&1 | Out-File (Join-Path $runDir 'decode.txt')
}
$runDir | Set-Content (Join-Path $projectRoot 'artifacts\fan-etw-latest.txt')
