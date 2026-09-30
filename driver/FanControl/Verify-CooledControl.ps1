#Requires -RunAsAdministrator
# Executes the authorized fan tests at <=10 W, with power restoration in finally.
param([switch]$TargetOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$result=Join-Path $root 'artifacts/fan-control-cooled-verification.json'
if ($TargetOnly) { $result=Join-Path $root 'artifacts/fan-control-target-cooled-verification.json' }
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=Join-Path $root 'bin-next/GalaxyHardware.exe'
$info.Arguments='fan-verify-cool'
if ($TargetOnly) { $info.Arguments='fan-verify-target-cool' }
$info.UseShellExecute=$false
$info.CreateNoWindow=$true
$info.RedirectStandardOutput=$true
$info.RedirectStandardError=$true
$info.StandardOutputEncoding=[Text.Encoding]::UTF8
$info.StandardErrorEncoding=[Text.Encoding]::UTF8
$process=[Diagnostics.Process]::Start($info)
$stdout=$process.StandardOutput.ReadToEndAsync()
$stderr=$process.StandardError.ReadToEndAsync()
$process.WaitForExit()
[IO.File]::WriteAllText($result,$stdout.Result)
if ($stderr.Result) { [IO.File]::WriteAllText((Join-Path $root 'artifacts/fan-control-cooled-stderr.txt'),$stderr.Result) }
if ($process.ExitCode -ne 0) { throw ('Verification process failed: '+$process.ExitCode) }
if ($TargetOnly) {
    $report=$stdout.Result | ConvertFrom-Json
    if ($report.Success) {
        # Finish the current verification workflow with the latest interactive app.
        $image=Join-Path $root 'artifacts/fan-control-ui-final.png'
        $ui=Start-Process -FilePath (Join-Path $root 'bin/GalaxyHelper.exe') -ArgumentList ('--ui-smoke "'+$image+'"') -WindowStyle Hidden -Wait -PassThru
        if ($ui.ExitCode -ne 0) { throw 'Final UI smoke failed.' }
        $app=Start-Process -FilePath (Join-Path $root 'bin/GalaxyHelper.exe') -PassThru
        [IO.File]::WriteAllText((Join-Path $root 'artifacts/fan-control-app-launch.json'),([ordered]@{Pid=$app.Id;StartedUtc=[DateTime]::UtcNow.ToString('o');UiSmokeExitCode=$ui.ExitCode;Image=$image} | ConvertTo-Json))
    }
}
