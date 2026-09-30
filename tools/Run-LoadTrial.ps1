param([ValidateRange(5,80)][int]$PL1 = 15, [ValidateRange(5,80)][int]$PL2 = 20)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -PL1 ' + $PL1 + ' -PL2 ' + $PL2
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait
    exit
}
New-Item -ItemType Directory -Force (Join-Path $projectRoot 'artifacts') | Out-Null
$app = Join-Path $projectRoot 'bin\GalaxyHardware.exe'
$prefix = 'artifacts\load-trial-' + $PL1 + '-' + $PL2
$run = Start-Process -FilePath $app -ArgumentList ('load-trial ' + $PL1 + ' ' + $PL2) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $projectRoot ($prefix + '.txt')) -RedirectStandardError (Join-Path $projectRoot ($prefix + '.error.txt'))
[IO.File]::WriteAllText((Join-Path $projectRoot ($prefix + '.exit.txt')), [string]$run.ExitCode)
$probe = Start-Process -FilePath $app -ArgumentList 'probe' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $projectRoot ($prefix + '.after.json')) -RedirectStandardError (Join-Path $projectRoot ($prefix + '.after.error.txt'))
exit $run.ExitCode
