#Requires -RunAsAdministrator
# Bounded, authorized real button-flow test; restores fans and power before closing.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$image=Join-Path $root 'artifacts/fan-control-ui-live.png'
$run=Start-Process -FilePath (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList ('--ui-control-smoke "'+$image+'"') -WindowStyle Hidden -Wait -PassThru
if ($run.ExitCode -ne 0) { throw ('UI process exit '+$run.ExitCode) }
$report=Get-Content ($image+'.json') -Raw | ConvertFrom-Json
if (!$report.Success) { throw $report.Error }
