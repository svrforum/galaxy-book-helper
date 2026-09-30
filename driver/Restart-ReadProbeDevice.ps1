# Temporarily release the observed Quick Search veto, then restore the service.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$result = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); ServiceRestored=$false }
$wasRunning = $false
try {
    $service = Get-Service -Name 'Quick Search Service'
    $wasRunning = $service.Status -eq 'Running'
    if ($wasRunning) { Stop-Service -Name 'Quick Search Service' -ErrorAction Stop }
    $output = @(& "$env:WINDIR\System32\pnputil.exe" /restart-device 'ACPI\SAM0430\2&DABA3FF&0' 2>&1)
    $result.ExitCode = $LASTEXITCODE
    $result.Output = @($output | ForEach-Object { $_.ToString() })
    $result.Probe = & (Join-Path $PSScriptRoot 'Read-Probe.ps1')
    $result.Driver = @(Get-CimInstance Win32_SystemDriver -Filter "Name='GalaxyFanRead'" | Select-Object Name,State,Started)
} catch { $result.Error = $_.Exception.Message }
finally {
    if ($wasRunning) {
        try { Start-Service -Name 'Quick Search Service'; $result.ServiceRestored=(Get-Service 'Quick Search Service').Status -eq 'Running' }
        catch { $result.RestoreError=$_.Exception.Message }
    } else { $result.ServiceRestored=$true }
    $result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'artifacts\driver-restart-result.json') -Encoding UTF8
}
