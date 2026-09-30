#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $root
try {
 if(Get-Process GalaxyHelper,GalaxyHardware -ErrorAction SilentlyContinue){throw 'Another hardware session is active.'}
 if(Test-Path artifacts/fan-zero-verification.json){Copy-Item artifacts/fan-zero-verification.json artifacts/fan-zero-before-active-cooling.json -Force}
 $info=New-Object Diagnostics.ProcessStartInfo
 $info.FileName=Join-Path $root 'bin-next/GalaxyHardware.exe';$info.Arguments='fan-zero-verify'
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 $info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
 $p=[Diagnostics.Process]::Start($info);$output=$p.StandardOutput.ReadToEndAsync();$errors=$p.StandardError.ReadToEndAsync();$p.WaitForExit()
 [IO.File]::WriteAllText((Join-Path $root 'artifacts/fan-zero-verification.json'),$output.Result)
 [IO.File]::WriteAllText((Join-Path $root 'artifacts/fan-zero-error.txt'),$errors.Result)
 $report=$output.Result|ConvertFrom-Json
 if(!$report.PowerRestored){throw 'Power restoration requires recovery.'}
 if(Test-Path "$env:LOCALAPPDATA/GalaxyHelper/rapl-recovery.json"){throw 'Power recovery journal remains.'}
 $state=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
 if($state.FanControlNeedsRestore -ne 0){throw 'Fan restoration remains pending.'}
 $shot=Join-Path $root 'artifacts/no-scroll-live.png'
 $ui=Start-Process (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList "--ui-smoke $shot" -PassThru -Wait
 if($ui.ExitCode -ne 0){throw 'Live UI smoke failed.'}
 $layout=Get-Content "$shot.layout.json" -Raw|ConvertFrom-Json
 if(!$layout.Success){throw 'Live UI layout has clipping.'}
 Copy-Item bin-next/GalaxyHelper.exe bin/GalaxyHelper.exe -Force
 Copy-Item bin-next/GalaxyHardware.exe bin/GalaxyHardware.exe -Force
 $app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -PassThru
 @{Success=$true;ZeroVerified=$report.Success;ZeroError=$report.Error;AppPid=$app.Id;Layout=$layout;Timestamp=(Get-Date).ToString('o')}|ConvertTo-Json -Depth 6|Set-Content artifacts/no-scroll-rollout.json -Encoding UTF8
} catch {
 @{Success=$false;Error=$_.Exception.Message;Timestamp=(Get-Date).ToString('o')}|ConvertTo-Json|Set-Content artifacts/no-scroll-rollout.json -Encoding UTF8
}
