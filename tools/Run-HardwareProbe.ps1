$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # User starts this script intentionally. Standard Windows UAC remains in control.
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait
    exit
}
$output = Join-Path $projectRoot 'artifacts\hardware-probe.txt'
New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
$app = Join-Path $projectRoot 'bin\GalaxyHardware.exe'
# Read-only: this command cannot invoke the trial-power or restore code paths.
& $app probe 2>&1 | Out-File -FilePath $output -Encoding utf8
$probeExit = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\hardware-probe.exit.txt'), [string]$probeExit)
exit $probeExit
