$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(!$identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"') -Verb RunAs -WindowStyle Hidden -Wait
 exit
}
$output=Join-Path $projectRoot 'artifacts\fan-acpi-probe.json'
try {
 Add-Type -Path (Join-Path $PSScriptRoot 'FanAcpiProbe.cs')
 $report=foreach($deviceId in @('ACPI\PNP0C0B\10','ACPI\PNP0C0B\11')) {
  $pdo=(Get-PnpDeviceProperty -InstanceId $deviceId -KeyName DEVPKEY_Device_PDOName).Data
  [pscustomobject]@{Device=$deviceId;PDO=$pdo;Method='_FST';Result=[FanAcpiProbe]::ReadStatus($pdo)}
 }
 $report | ConvertTo-Json | Set-Content $output -Encoding utf8
} catch {
 [pscustomobject]@{Error=$_.Exception.ToString();Method='_FST';SettingsWrites=$false} | ConvertTo-Json | Set-Content $output -Encoding utf8
 exit 1
}
