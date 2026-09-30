# Summarize only fan diagnostics; does not access hardware or change settings.
param([string]$LogPath)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$LogPath) {
 $dir=Join-Path $env:LOCALAPPDATA 'Packages\SAMSUNGELECTRONICSCO.LTD.PCDiagnostics_3c1yjt4zspk6g\LocalCache\Local\SamsungPCDiagnosticsTool\Logs'
 $LogPath=(Get-ChildItem $dir -Filter '*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
$lines=@(Get-Content -LiteralPath $LogPath | Where-Object {$_ -match '\| (CoolingFanTest|SabiHelper|CoolingFanTestViewModel) \|'})
$lines | Set-Content (Join-Path $root 'artifacts\samsung-official-fan-test.log') -Encoding UTF8
$step=$null
$readings=@()
$commands=@()
foreach($line in $lines) {
 if($line -match 'Command : 43 58 2C 00 00 ([0-9A-F]{2}) ') {
  $step=[Convert]::ToInt32($matches[1],16)
  $commands += [pscustomobject]@{Time=$line.Substring(0,23);Step=$step}
 }
 if($line -match 'FAN1 RPM : ([0-9.]+), FAN2 RPM : ([0-9.]+)') {
  $readings += [pscustomobject]@{Time=$line.Substring(0,23);Step=$step;Fan1Rpm=[double]::Parse($matches[1],[cultureinfo]::InvariantCulture);Fan2Rpm=[double]::Parse($matches[2],[cultureinfo]::InvariantCulture)}
 }
}
$report=[pscustomobject]@{
 Source=$LogPath
 ObservedAt=(Get-Date -Format o)
 Commands=$commands
 Readings=$readings
 AutoRestoreAcknowledged=(@($lines | Where-Object {$_ -match 'Response : 43 58 2C 00 AA 80 '}).Count -gt 0)
 OutcomeLines=@($lines | Where-Object {$_ -match 'Current mode|completed successfully|MaxFanSpeed|Fan count =|Fan stopped successfully|FAN1 saved|FAN-[0-9]|GNR-[0-9]'})
 Caveat='Official Samsung diagnostics observations. Independent app access and arbitrary RPM cap remain unverified. Step samples include spin-up transients.'
}
$report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'artifacts\samsung-official-fan-test.json') -Encoding UTF8
$report | Select-Object ObservedAt,AutoRestoreAcknowledged,OutcomeLines | ConvertTo-Json -Depth 3
$readings | Group-Object Step | ForEach-Object { $_.Group | Select-Object -Last 1 } | Format-Table
