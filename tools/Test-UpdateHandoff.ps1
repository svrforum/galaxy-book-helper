param([string]$Exe=(Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/GalaxyHelper.exe'))
$ErrorActionPreference='Stop'
$folder=Join-Path $env:TEMP ('GalaxyUpdateWait-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $folder | Out-Null
try {
    $report=Join-Path $folder 'wait.txt'
    $shell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $parent=Start-Process $shell -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 3') -WindowStyle Hidden -PassThru
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $helper=Start-Process $Exe -ArgumentList @('--verify-update-wait',$parent.Id,$report) -WindowStyle Hidden -Wait -PassThru
    if($helper.ExitCode -ne 0 -or !(Test-Path $report) -or !$parent.HasExited -or $watch.Elapsed.TotalSeconds -lt 1){throw 'Updater did not wait for graceful parent exit.'}
    $bad=Join-Path $folder 'invalid.txt'
    $helper=Start-Process $Exe -ArgumentList @('--verify-update-wait','0',$bad) -WindowStyle Hidden -Wait -PassThru
    if($helper.ExitCode -eq 0 -or (Test-Path $bad) -or !(Test-Path ($bad+'.error.txt'))){throw 'Invalid parent ID was accepted.'}
    Write-Output 'PASS: updater waits for parent exit and rejects invalid IDs without installing or changing hardware.'
} finally {
    if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($folder)) -eq [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')){Remove-Item -LiteralPath $folder -Recurse -Force}
}
