#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$report=[ordered]@{StartedUtc=[DateTime]::UtcNow.ToString('o');Success=$false;ZeroTestRun=$false}
$path=Join-Path $root 'artifacts/presets-zero-rollout.json'
try {
    if(Get-Process GalaxyHelper -ErrorAction SilentlyContinue){throw 'Exit the app through its tray menu first.'}
    & (Join-Path $root 'driver/Install-FanZero.ps1')
    $install=Get-Content (Join-Path $root 'artifacts/driver-fanzero-install-result.json') -Raw|ConvertFrom-Json
    if(!$install.Success){throw 'Driver installation failed.'}
    $report.PublishedInf=$install.PublishedInf;$report.InstallExitCode=$install.InstallExitCode
    Copy-Item (Join-Path $root 'bin-next/GalaxyHelper.exe') (Join-Path $root 'bin/GalaxyHelper.exe')
    Copy-Item (Join-Path $root 'bin-next/GalaxyHardware.exe') (Join-Path $root 'bin/GalaxyHardware.exe')
    Start-Sleep -Seconds 35
    $key=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
    $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToFileTimeUtc()
    $report.ZeroReady=($key.FanZeroReadyTime -and $key.FanZeroReadyTime -eq $key.FanControlReadyTime -and $key.FanZeroReadyTime -ge $boot)
    if($report.ZeroReady){
        $result=Join-Path $root 'artifacts/fan-zero-verification.json'
        $p=Start-Process (Join-Path $root 'bin/GalaxyHardware.exe') -ArgumentList 'fan-zero-verify' -WindowStyle Hidden -RedirectStandardOutput $result -RedirectStandardError (Join-Path $root 'artifacts/fan-zero-error.txt') -Wait -PassThru
        $report.ZeroTestRun=$true;$report.ZeroTestExitCode=$p.ExitCode
        if($p.ExitCode -eq 0){$zero=Get-Content $result -Raw|ConvertFrom-Json;$report.ZeroSuccess=$zero.Success}
    }
    $image=Join-Path $root 'artifacts/curve-presets-live.png'
    $smoke=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -ArgumentList ('--ui-smoke "'+$image+'"') -WindowStyle Hidden -Wait -PassThru
    $report.UiSmokeExitCode=$smoke.ExitCode
    if($smoke.ExitCode -ne 0){throw 'Preset UI smoke failed.'}
    $app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -PassThru
    $report.AppPid=$app.Id;$report.Success=$true
} catch {$report.Error=$_.Exception.Message;throw}
finally {$report|ConvertTo-Json -Depth 5|Set-Content $path -Encoding UTF8}
