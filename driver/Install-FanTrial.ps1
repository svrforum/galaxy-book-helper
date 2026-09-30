# Installs the pinned step-2 / 15-second trial-capable candidate. Requires explicit trial authorization. Never reboots or requests a trial.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'artifacts\driver\package-FanTrial'
$reportPath = Join-Path $root 'artifacts\driver-fantrial-install-result.json'
$instance = 'ACPI\SAM0430\2&DABA3FF&0'
$thumbprint = 'D6E40CB650F5A8F873DB76C7F1DBC258F2CC8145'
$report = [ordered]@{ StartedUtc=[DateTime]::UtcNow.ToString('o'); Stage='Preflight'; Restarted=$false }
$quickSearchWasRunning = $false
function Save-Report { $report | ConvertTo-Json -Depth 6 | Set-Content $reportPath -Encoding UTF8 }
try {
    Save-Report
    if (Confirm-SecureBootUEFI) { throw 'Secure Boot is still enabled.' }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GalaxyCodeIntegrity {
    [StructLayout(LayoutKind.Sequential)] public struct Info { public uint Length; public uint Options; }
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int c, ref Info p, int n, out int r);
    public static uint Read() { Info i = new Info(); i.Length = 8; int r; int s = NtQuerySystemInformation(103, ref i, 8, out r); if(s != 0) throw new Exception("Code integrity query failed: " + s); return i.Options; }
}
'@
    $options = [GalaxyCodeIntegrity]::Read()
    $report.CodeIntegrityOptions = ('0x{0:X}' -f $options)
    if (($options -band 2) -eq 0) { throw 'TESTSIGNING is not active in this boot.' }
    $bios = Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
    if ($bios.SystemProductName -ne 'Galaxy Book6 Pro - PAMB' -or $bios.BIOSVersion -ne 'PAMB.1.5.74.371') { throw 'Model or BIOS differs from the reviewed target.' }
    $device = Get-PnpDevice -InstanceId $instance
    if ($device.Status -ne 'OK') { throw 'Samsung device is not healthy before installation.' }
    $base = (Get-PnpDeviceProperty -InstanceId $instance -KeyName DEVPKEY_Device_Service).Data
    if ($base -ne 'SamsungEventController') { throw 'Unexpected base driver.' }
    $expected = @{
        'GalaxyFanRead.sys' = '51EA9B30DE3DBFCF2C05D23255F915BD5849EEAA65AA7505F71ACB11C5C7AD65'
        'GalaxyFanRead.inf' = 'BC641873F2896AD69519F9302C5101C97AE3DF559DAF4A7D8B2CD9FB83FEFDC4'
        'GalaxyFanRead.cat' = '43B734CB47BAFED507723EECD08C87C30C059B22AF192A45F29FC4A3D8935845'
    }
    foreach ($name in $expected.Keys) {
        $path = Join-Path $package $name
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected[$name]) { throw "Package hash mismatch: $name" }
        if ($name -ne 'GalaxyFanRead.inf') {
            $sig = Get-AuthenticodeSignature -LiteralPath $path
            if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $thumbprint) { throw "Signature validation failed: $name" }
        }
    }
    $signTool = Join-Path $root '.tools\driver-packages\Microsoft.Windows.SDK.CPP.10.0.28000.1721\c\bin\10.0.28000.0\x64\signtool.exe'
    & $signTool verify /pa /c (Join-Path $package 'GalaxyFanRead.cat') (Join-Path $package 'GalaxyFanRead.sys') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Catalog membership verification failed.' }
    $pnp = "$env:WINDIR\System32\pnputil.exe"
    $quickSearchWasRunning = (Get-Service -Name 'Quick Search Service').Status -eq 'Running'
    if ($quickSearchWasRunning) { Stop-Service -Name 'Quick Search Service' }
    $report.Stage = 'Staging'; Save-Report
    $output = @(& $pnp /add-driver (Join-Path $package 'GalaxyFanRead.inf') 2>&1)
    $report.StageExitCode = $LASTEXITCODE
    $report.StageOutput = @($output | ForEach-Object { $_.ToString() })
    Save-Report
    if ($report.StageExitCode -ne 0) { throw 'Driver staging failed.' }
    $entries = @(Get-WindowsDriver -Online -All | Where-Object { $_.ProviderName -eq 'Galaxy Helper Research' -and [IO.Path]::GetFileName($_.OriginalFileName) -ieq 'GalaxyFanRead.inf' } | Where-Object { (Get-FileHash -LiteralPath $_.OriginalFileName -Algorithm SHA256).Hash -eq $expected['GalaxyFanRead.inf'] })
    if ($entries.Count -ne 1 -or $entries[0].Driver -notmatch '^oem\d+\.inf$') { throw 'Cannot identify a unique published research package; installation stopped.' }
    $published = $entries[0].Driver
    $report.PublishedInf = $published
    # Save the exact rollback command before the package can attach to the device.
    ('pnputil.exe /delete-driver ' + $published + ' /uninstall') | Set-Content (Join-Path $root 'artifacts\driver\uninstall-read-probe.txt') -Encoding ASCII
    [ordered]@{ PublishedInf=$published; Provider=$entries[0].ProviderName; OriginalFileName=$entries[0].OriginalFileName; DeviceInstance=$instance } | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts\driver\installed-package.json') -Encoding UTF8
    $report.Stage = 'Installing'; Save-Report
    $output = @(& $pnp /add-driver (Join-Path $package 'GalaxyFanRead.inf') /install 2>&1)
    $report.InstallExitCode = $LASTEXITCODE
    $report.InstallOutput = @($output | ForEach-Object { $_.ToString() })
    $report.Stage = 'InstalledCommandReturned'
    $report.Success = ($report.InstallExitCode -eq 0 -or $report.InstallExitCode -eq 3010)
    $report.DeviceStatus = (Get-PnpDevice -InstanceId $instance).Status
    $report.Service = @(Get-CimInstance Win32_SystemDriver -Filter "Name='GalaxyFanRead'" | Select-Object Name,State,Started,PathName)
    $report.Probe = & (Join-Path $PSScriptRoot 'Read-Probe.ps1')
    Save-Report
} catch { $report.Success=$false; $report.Error=$_.Exception.Message; Save-Report; throw }
finally {
    if ($quickSearchWasRunning) {
        try { Start-Service -Name 'Quick Search Service'; $report.QuickSearchRestored=(Get-Service -Name 'Quick Search Service').Status -eq 'Running' }
        catch { $report.ServiceRestoreError=$_.Exception.Message }
        Save-Report
    }
}
