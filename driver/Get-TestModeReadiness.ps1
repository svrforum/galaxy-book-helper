# Elevated, read-only. Never queries or records recovery passwords.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$result = [ordered]@{ CheckedUtc = [DateTime]::UtcNow.ToString('o'); ReadOnly = $true }
try {
    $result.SecureBoot = Confirm-SecureBootUEFI
    $volume = Get-BitLockerVolume -MountPoint $env:SystemDrive
    $result.BitLocker = [ordered]@{
        MountPoint = $volume.MountPoint
        VolumeStatus = $volume.VolumeStatus.ToString()
        ProtectionStatus = $volume.ProtectionStatus.ToString()
        EncryptionPercentage = $volume.EncryptionPercentage
        ProtectorTypes = @($volume.KeyProtector | ForEach-Object { $_.KeyProtectorType.ToString() })
    }
    $boot = & "$env:WINDIR\System32\bcdedit.exe" /enum '{current}' 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Could not read current boot configuration.' }
    $result.BootConfiguration = @($boot | ForEach-Object { $_.ToString() })
    $result.Success = $true
} catch { $result.Success = $false; $result.Error = $_.Exception.Message }
$result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'artifacts\testmode-readiness.json') -Encoding UTF8
