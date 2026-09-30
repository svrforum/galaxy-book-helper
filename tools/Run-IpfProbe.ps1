$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"') -Verb RunAs -WindowStyle Hidden -Wait
    exit
}
& (Join-Path $projectRoot 'artifacts\IpfProbe.exe') 'C:\Windows\System32\DriverStore\FileRepository\ipf_cpu.inf_amd64_b1b7fa888ee1cf4a\ipfcore.dll' --elevated 2>&1 | Out-File (Join-Path $projectRoot 'artifacts\ipf-probe.txt') -Encoding utf8
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\ipf-probe.exit.txt'),[string]$LASTEXITCODE)
