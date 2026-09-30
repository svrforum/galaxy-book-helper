#Requires -RunAsAdministrator
# Opens the UI for a screenshot without clicking any power/fan control.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$image=Join-Path $root 'artifacts/fan-control-ui.png'
$run=Start-Process -FilePath (Join-Path $root 'bin-next/GalaxyHelper.exe') -ArgumentList ('--ui-smoke "'+$image+'"') -WindowStyle Hidden -Wait -PassThru
[ordered]@{TimestampUtc=[DateTime]::UtcNow.ToString('o');ExitCode=$run.ExitCode;Image=$image;HardwareSettingsChanged=$false} | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts/fan-control-ui-check.json') -Encoding UTF8
