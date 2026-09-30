#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(Get-Process GalaxyHelper -ErrorAction SilentlyContinue){throw 'Exit Galaxy Helper through its tray menu first.'}
$image=Join-Path $root 'artifacts/fan-curve-live.png'
$test=Start-Process (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList ('--ui-curve-smoke "'+$image+'"') -WindowStyle Hidden -Wait -PassThru
if($test.ExitCode -ne 0){throw 'Curve UI process failed.'}
$result=Get-Content ($image+'.json') -Raw|ConvertFrom-Json
if(!$result.Success){throw $result.Error}
Copy-Item (Join-Path $root 'bin-next/GalaxyHelper.exe') (Join-Path $root 'bin/GalaxyHelper.exe')
Copy-Item (Join-Path $root 'bin-next/GalaxyHardware.exe') (Join-Path $root 'bin/GalaxyHardware.exe')
$app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -PassThru
[ordered]@{Success=$true;Pid=$app.Id;TimestampUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/fan-curve-rollout.json') -Encoding UTF8
