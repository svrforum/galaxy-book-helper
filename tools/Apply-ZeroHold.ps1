#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $root
$result=[ordered]@{Success=$false;Started=(Get-Date).ToString('o')}
$curve=Join-Path $env:LOCALAPPDATA 'GalaxyHelper/fan-curve.json'
$existed=Test-Path $curve;$saved=if($existed){[IO.File]::ReadAllBytes($curve)}else{$null}
try {
 if(Get-Process GalaxyHelper,GalaxyHardware -ErrorAction SilentlyContinue){throw 'Exit the existing app using restore and exit.'}
 & ./driver/Install-FanZeroHold.ps1
 $install=Get-Content artifacts/driver-fanzero-hold-install-result.json -Raw -Encoding UTF8|ConvertFrom-Json
 if(!$install.Success){throw 'Driver installation did not complete.'}
 $state=Get-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters
 $ready=($null -ne $state.FanZeroHoldReadyTime -and $state.FanZeroHoldReadyTime -eq $state.FanControlReadyTime)
 $result.DriverReady=$ready;$result.InstallExitCode=$install.InstallExitCode
 if($ready){
  $shot=Join-Path $root 'artifacts/zero-hold-ui-live.png'
  $p=Start-Process (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList "--ui-zero-smoke $shot" -PassThru -Wait
  if($p.ExitCode -ne 0){throw 'Zero UI smoke process failed.'}
  $test=Get-Content "$shot.json" -Raw -Encoding UTF8|ConvertFrom-Json
  $result.LiveTest=$test
  if(!$test.Success){throw ('Zero UI smoke failed: '+$test.Error)}
 }
 if(Test-Path "$env:LOCALAPPDATA/GalaxyHelper/rapl-recovery.json"){throw 'Power restoration pending.'}
 if((Get-ItemPropertyValue HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters FanControlNeedsRestore) -ne 0){throw 'Fan restoration pending.'}
 if($existed){[IO.File]::WriteAllBytes($curve,$saved)}elseif(Test-Path $curve){Remove-Item -LiteralPath $curve}
 Copy-Item bin-next/GalaxyHelper.exe bin/GalaxyHelper.exe -Force
 Copy-Item bin-next/GalaxyHardware.exe bin/GalaxyHardware.exe -Force
 $app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -ArgumentList '--show-zero' -PassThru
 $result.AppPid=$app.Id;$result.Success=$true;$result.NeedsReboot=!$ready
} catch {$result.Error=$_.Exception.Message}
finally {
 if($existed){[IO.File]::WriteAllBytes($curve,$saved)}elseif(Test-Path $curve){Remove-Item -LiteralPath $curve}
 $result|ConvertTo-Json -Depth 10|Set-Content artifacts/zero-hold-rollout.json -Encoding UTF8
}
