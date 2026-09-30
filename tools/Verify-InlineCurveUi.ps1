#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(Get-Process GalaxyHelper -ErrorAction SilentlyContinue){throw 'Exit Galaxy Helper through its tray menu first.'}
$curveFile=Join-Path $env:LOCALAPPDATA 'GalaxyHelper/fan-curve.json'
$hadCurve=Test-Path -LiteralPath $curveFile
$curveBytes=if($hadCurve){[IO.File]::ReadAllBytes($curveFile)}else{$null}
$image=Join-Path $root 'artifacts/inline-curve-live.png'
try {
    $test=Start-Process (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList ('--ui-curve-smoke "'+$image+'"') -WindowStyle Hidden -Wait -PassThru
    if($test.ExitCode -ne 0){throw 'Inline curve UI process failed.'}
    $result=Get-Content ($image+'.json') -Raw|ConvertFrom-Json
    if(!$result.Success){throw $result.Error}
} finally {
    if($hadCurve){[IO.File]::WriteAllBytes($curveFile,$curveBytes)}elseif(Test-Path -LiteralPath $curveFile){Remove-Item -LiteralPath $curveFile}
}
Copy-Item (Join-Path $root 'bin-next/GalaxyHelper.exe') (Join-Path $root 'bin/GalaxyHelper.exe')
Copy-Item (Join-Path $root 'bin-next/GalaxyHardware.exe') (Join-Path $root 'bin/GalaxyHardware.exe')
$app=Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -PassThru
[ordered]@{Success=$true;Pid=$app.Id;TimestampUtc=[DateTime]::UtcNow.ToString('o');UserCurvePreserved=$true}|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/inline-curve-rollout.json') -Encoding UTF8
