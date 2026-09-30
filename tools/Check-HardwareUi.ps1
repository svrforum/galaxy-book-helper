$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait
    exit
}
$app = Join-Path $projectRoot 'bin\GalaxyHelper.exe'
$image = Join-Path $projectRoot 'artifacts\watt-ui.png'
$run = Start-Process -FilePath $app -ArgumentList ('--ui-smoke "' + $image + '"') -WindowStyle Hidden -Wait -PassThru
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\watt-ui.exit.txt'), [string]$run.ExitCode)
exit $run.ExitCode
