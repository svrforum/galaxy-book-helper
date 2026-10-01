#Requires -RunAsAdministrator
# Identical standalone read-only probe as administrator and SYSTEM.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$run=[Guid]::NewGuid().ToString('N')
$folder=Join-Path $env:ProgramFiles "GalaxyHelper/research/$run"
$task="GalaxyHelper-ReadOnly-$run"
$report=[ordered]@{SettingsWrites=$false;DriverChanges=$false;Started=(Get-Date).ToString('o')}
try {
 New-Item -ItemType Directory -Force $folder|Out-Null
 Copy-Item "$root/artifacts/SamsungReadOnly.exe" "$folder/SamsungReadOnly.exe"
 $report.ProbeHash=(Get-FileHash "$folder/SamsungReadOnly.exe").Hash
 $driver=Get-CimInstance Win32_SystemDriver -Filter "Name='SamsungEventController'"
 $report.Driver=$driver|Select-Object Name,State,PathName
 Start-Process "$folder/SamsungReadOnly.exe" -WindowStyle Hidden -Wait
 $report.Admin=Get-Content "$folder/samsung-fan-standalone.json" -Raw|ConvertFrom-Json
 Move-Item "$folder/samsung-fan-standalone.json" "$folder/admin.json"
 $action=New-ScheduledTaskAction -Execute "$folder/SamsungReadOnly.exe" -WorkingDirectory $folder
 $principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
 Register-ScheduledTask -TaskName $task -Action $action -Principal $principal -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 1))|Out-Null
 Start-ScheduledTask -TaskName $task
 $until=(Get-Date).AddSeconds(45)
 while(!(Test-Path "$folder/samsung-fan-standalone.json") -and (Get-Date) -lt $until){Start-Sleep -Milliseconds 500}
 if(!(Test-Path "$folder/samsung-fan-standalone.json")){throw 'SYSTEM read-only probe timed out.'}
 $report.System=Get-Content "$folder/samsung-fan-standalone.json" -Raw|ConvertFrom-Json
 $report.Completed=$true
} catch {$report.Error=$_.Exception.ToString()}
finally {
 if(Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue){Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $task -Confirm:$false}
 $report|ConvertTo-Json -Depth 8|Set-Content "$root/artifacts/samsung-context-comparison.json" -Encoding UTF8
}
