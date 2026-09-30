$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait
    exit
}
New-Item -ItemType Directory -Force (Join-Path $projectRoot 'artifacts') | Out-Null
$app = Join-Path $projectRoot 'bin\GalaxyHardware.exe'
$run = Start-Process -FilePath $app -ArgumentList 'trial-power 15 20 15' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $projectRoot 'artifacts\hardware-trial.txt') -RedirectStandardError (Join-Path $projectRoot 'artifacts\hardware-trial.error.txt')
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\hardware-trial.exit.txt'), [string]$run.ExitCode)
# Always capture a final read-only report, including after a rejected trial.
$probe = Start-Process -FilePath $app -ArgumentList 'probe' -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $projectRoot 'artifacts\hardware-after-trial.json') -RedirectStandardError (Join-Path $projectRoot 'artifacts\hardware-after-trial.error.txt')
exit $run.ExitCode
