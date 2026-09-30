#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $root
$result=[ordered]@{Success=$false;Started=(Get-Date).ToString('o')}
$curve=Join-Path $env:LOCALAPPDATA 'GalaxyHelper/fan-curve.json'
$existed=Test-Path $curve;$saved=if($existed){[IO.File]::ReadAllBytes($curve)}else{$null}
try {
 $key='HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
 $state=Get-ItemProperty $key
 if(!$state.FanZeroHoldReadyTime -or $state.FanZeroHoldReadyTime -ne $state.FanControlReadyTime){throw 'Zero hold driver not active.'}
 $apps=@(Get-Process GalaxyHelper -ErrorAction SilentlyContinue)
 foreach($app in $apps){
  if($app.Path -ne (Join-Path $root 'bin\GalaxyHelper.exe')){throw 'Unexpected running app path.'}
  $state=Get-ItemProperty $key
  if((Test-Path "$env:LOCALAPPDATA/GalaxyHelper/rapl-recovery.json") -or $state.FanControlNeedsRestore -ne 0 -or [BitConverter]::ToUInt32($state.FanControlResult,16) -ne 0){throw 'Active controls: exit using tray restore first.'}
  # Only an idle, Auto-mode app without a power recovery obligation is stopped.
  Stop-Process -Id $app.Id
  $app.WaitForExit()
 }
 $result.Tests=@()
 foreach($mode in @('zero','curve')) {
  $shot=Join-Path $root ('artifacts/free-curve-'+$mode+'-live.png')
  $arg=if($mode -eq 'zero'){'--ui-zero-smoke'}else{'--ui-curve-smoke'}
  $p=Start-Process (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList "$arg $shot" -PassThru -Wait
  if($p.ExitCode -ne 0){throw 'UI test process failed.'}
  $test=Get-Content "$shot.json" -Raw -Encoding UTF8|ConvertFrom-Json
  $result.Tests+=@{Mode=$mode;Report=$test}
  if(!$test.Success){throw ($mode+': '+$test.Error)}
 }
 if(Test-Path "$env:LOCALAPPDATA/GalaxyHelper/rapl-recovery.json"){throw 'Power recovery pending.'}
 if((Get-ItemPropertyValue $key FanControlNeedsRestore) -ne 0){throw 'Fan recovery pending.'}
 $layout=Get-Content "$shot.layout.json" -Raw -Encoding UTF8|ConvertFrom-Json
 if(!$layout.Success){throw 'Layout test failed.'}
 $result.Layout=$layout
 if($existed){[IO.File]::WriteAllBytes($curve,$saved)}elseif(Test-Path $curve){Remove-Item -LiteralPath $curve}
 Copy-Item bin-next/GalaxyHelper.exe bin/GalaxyHelper.exe -Force
 Copy-Item bin-next/GalaxyHardware.exe bin/GalaxyHardware.exe -Force
 $app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -ArgumentList '--show-zero' -PassThru
 $result.AppPid=$app.Id;$result.Success=$true
} catch {$result.Error=$_.Exception.Message}
finally {
 if($existed){[IO.File]::WriteAllBytes($curve,$saved)}elseif(Test-Path $curve){Remove-Item -LiteralPath $curve}
 $result|ConvertTo-Json -Depth 10|Set-Content artifacts/free-curve-live-verification.json -Encoding UTF8
}
