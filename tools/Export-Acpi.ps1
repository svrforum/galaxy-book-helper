param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\acpi'))
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FirmwareTables {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint EnumSystemFirmwareTables(uint provider, byte[] buffer, uint size);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[] buffer, uint size);
}
'@
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $outputRoot | Out-Null
# Provider signature is the C multi-character constant 'ACPI'. Table IDs are little-endian.
$provider = [uint32]0x41435049
$length = [FirmwareTables]::EnumSystemFirmwareTables($provider, $null, 0)
if ($length -eq 0) { throw "Cannot enumerate ACPI: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$ids = [byte[]]::new($length)
[void][FirmwareTables]::EnumSystemFirmwareTables($provider, $ids, $length)
# Windows can omit DSDT from enumeration while still serving an explicit request.
$ids = [byte[]]($ids + [Text.Encoding]::ASCII.GetBytes('DSDT'))
$seen = @{}
$report = for ($offset = 0; $offset -lt $ids.Length; $offset += 4) {
    $id = [BitConverter]::ToUInt32($ids, $offset)
    $name = [Text.Encoding]::ASCII.GetString($ids, $offset, 4)
    if ($seen.ContainsKey($id)) { [pscustomobject]@{ Table=$name; Status='Duplicate signature: Windows API exposes first matching table only' }; continue }
    $seen[$id] = $true
    # Collect only executable ACPI definitions; omit unrelated tables such as licensing keys.
    if ($name -notin @('DSDT','SSDT')) { continue }
    $size = [FirmwareTables]::GetSystemFirmwareTable($provider, $id, $null, 0)
    if ($size -eq 0) { [pscustomobject]@{ Table=$name; Status='Read failed' }; continue }
    $bytes = [byte[]]::new($size)
    $actual = [FirmwareTables]::GetSystemFirmwareTable($provider, $id, $bytes, $size)
    if ($actual -ne $size) { throw "Firmware table changed size: $name" }
    $path = Join-Path $outputRoot ($name + '.dat')
    [IO.File]::WriteAllBytes($path, $bytes)
    [pscustomobject]@{ Table=$name; Status='Saved'; Bytes=$size; SHA256=(Get-FileHash $path -Algorithm SHA256).Hash }
}
$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $outputRoot 'manifest.json') -Encoding utf8
$report | Format-Table
