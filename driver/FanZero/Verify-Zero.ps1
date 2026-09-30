#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(Get-Process GalaxyHelper -ErrorAction SilentlyContinue){throw 'Exit Galaxy Helper through its tray menu before the trial.'}
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=Join-Path $root 'bin/GalaxyHardware.exe'
$info.Arguments='fan-zero-verify'
$info.UseShellExecute=$false;$info.CreateNoWindow=$true
$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
$info.StandardOutputEncoding=[Text.Encoding]::UTF8;$info.StandardErrorEncoding=[Text.Encoding]::UTF8
$process=[Diagnostics.Process]::Start($info)
$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
$process.WaitForExit()
[IO.File]::WriteAllText((Join-Path $root 'artifacts/fan-zero-verification.json'),$stdout.Result)
[IO.File]::WriteAllText((Join-Path $root 'artifacts/fan-zero-error.txt'),$stderr.Result)
if($process.ExitCode -ne 0){throw ('Zero trial process failed: '+$process.ExitCode)}
$report=$stdout.Result|ConvertFrom-Json
if(!$report.Success){throw ('Zero trial not verified: '+$report.Error+' '+$report.Verification.Error)}
