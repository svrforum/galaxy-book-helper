# Reads a cached result only. Does not load a driver or send any hardware command.
param([switch]$Detailed)
$ErrorActionPreference = 'Stop'
$key = 'HKLM:\SYSTEM\CurrentControlSet\Enum\ACPI\SAM0430\2&DABA3FF&0\Device Parameters\GalaxyFanRead'
$serviceKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
if (Get-ItemProperty -LiteralPath $serviceKey -Name LastProbe -ErrorAction SilentlyContinue) { $key = $serviceKey }
if (!(Test-Path -LiteralPath $key)) {
    $diagnostic = Get-ItemProperty -LiteralPath $serviceKey -ErrorAction SilentlyContinue
    [pscustomobject]@{ Available = $false; Reason = 'No cached driver probe result. This is not a zero-RPM reading.'; LastStage=$diagnostic.LastStage; LastStatus=$diagnostic.LastStatus }
    return
}
$record = (Get-ItemProperty -LiteralPath $key -Name LastProbe).LastProbe
if ($record -isnot [byte[]] -or $record.Length -ne 24 -or [BitConverter]::ToUInt32($record,0) -ne 1) { throw 'Unsupported or malformed driver result.' }
$status = [BitConverter]::ToUInt32($record,4)
$sampleUtc = [DateTime]::FromFileTimeUtc([BitConverter]::ToInt64($record,16))
$result = [ordered]@{
    Available = $true
    Success = ($status -eq 0)
    NtStatus = ('0x{0:X8}' -f $status)
    SampleUtc = $sampleUtc.ToString('o')
    AgeSeconds = [Math]::Round(([DateTime]::UtcNow - $sampleUtc).TotalSeconds,1)
    Fan1Rpm = $(if ($status -eq 0) { [BitConverter]::ToUInt32($record,8) } else { $null })
    Fan2Rpm = $(if ($status -eq 0) { [BitConverter]::ToUInt32($record,12) } else { $null })
    IsLiveReading = $false
}
if ($Detailed) {
    $state = Get-ItemProperty -LiteralPath $serviceKey -ErrorAction SilentlyContinue
    $result.RequestSequence = $state.ProbeRequestSequence
    $result.CompletedSequence = $state.ProbeCompletedSequence
    $result.LastStage = $state.LastStage
    $result.Queries = @(foreach ($name in 'SupportQuery','MaxStepQuery','RpmQuery') {
        $bytes = $state.$name
        if ($bytes -isnot [byte[]] -or $bytes.Length -ne 184 -or [BitConverter]::ToUInt32($bytes,0) -ne 1) { continue }
        $returned = [BitConverter]::ToUInt32($bytes,16)
        $raw = [byte[]]$bytes[53..180]
        $payload = $null
        if ($returned -ge 37 -and $returned -le 128 -and [BitConverter]::ToUInt32($bytes,12) -eq 0) { $payload = [byte[]]$raw[16..36] }
        [pscustomobject]@{
            Name = $name
            Sequence = [BitConverter]::ToUInt32($bytes,20)
            SampleUtc = [DateTime]::FromFileTimeUtc([BitConverter]::ToInt64($bytes,24)).ToString('o')
            TransportStatus = ('0x{0:X8}' -f [BitConverter]::ToUInt32($bytes,8))
            ParsedStatus = ('0x{0:X8}' -f [BitConverter]::ToUInt32($bytes,12))
            ReturnedBytes = $returned
            InputHex = [BitConverter]::ToString($bytes,32,21).Replace('-',' ')
            OutputHex = $(if ($returned -gt 0) { [BitConverter]::ToString($raw,0,[Math]::Min(128,$returned)).Replace('-',' ') } else { '' })
            PayloadHex = $(if ($null -ne $payload) { [BitConverter]::ToString($payload).Replace('-',' ') } else { $null })
            SupportSignatureValid = $(if ($name -eq 'SupportQuery' -and $null -ne $payload) { $payload[5] -eq 0xdd -and $payload[6] -eq 0xcc } else { $null })
            MaxStep = $(if ($name -eq 'MaxStepQuery' -and $null -ne $payload) { [int]$payload[7] } else { $null })
        }
    })
}
[pscustomobject]$result
