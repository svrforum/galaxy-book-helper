# Offline fixture test. Shadows registry readers; never writes HKLM or calls hardware.
$ErrorActionPreference = 'Stop'
$fixtureState = [pscustomobject]@{ LastProbe=[byte[]]::new(24); RpmQuery=[byte[]]::new(184); SupportQuery=$null; MaxStepQuery=$null; ProbeRequestSequence=123; ProbeCompletedSequence=123; LastStage=40 }
function Get-ItemProperty { param($LiteralPath,$Name,$ErrorAction) return $fixtureState }
function Test-Path { param($LiteralPath) return $true }
function Put-U32($bytes,$offset,[uint32]$value) { [BitConverter]::GetBytes($value).CopyTo($bytes,$offset) }
$fixture = [byte[]](0x43,0x58,0x7a,0,0xaa,0,0,2,0x0a,0x8a,0x0a,0xda,0x0b,0x54,0x0b,0xb8,0,0,0,0,0)
$now = [DateTime]::UtcNow.ToFileTimeUtc()
Put-U32 $fixtureState.LastProbe 0 1
Put-U32 $fixtureState.LastProbe 8 2698
Put-U32 $fixtureState.LastProbe 12 2778
[BitConverter]::GetBytes($now).CopyTo($fixtureState.LastProbe,16)
Put-U32 $fixtureState.RpmQuery 0 1
Put-U32 $fixtureState.RpmQuery 4 1
Put-U32 $fixtureState.RpmQuery 16 37
Put-U32 $fixtureState.RpmQuery 20 123
[BitConverter]::GetBytes($now).CopyTo($fixtureState.RpmQuery,24)
$fixture.CopyTo($fixtureState.RpmQuery,69)
$read = & (Join-Path $PSScriptRoot 'Read-Probe.ps1') -Detailed
if (!$read.Success -or $read.Fan1Rpm -ne 2698 -or $read.Fan2Rpm -ne 2778 -or $read.IsLiveReading) { throw 'RPM/cached-state fixture failed.' }
if ($read.Queries.Count -ne 1 -or $read.Queries[0].Sequence -ne 123 -or $read.Queries[0].PayloadHex -ne [BitConverter]::ToString($fixture).Replace('-',' ')) { throw 'Query layout fixture failed.' }
Put-U32 $fixtureState.LastProbe 4 3221225659
Put-U32 $fixtureState.RpmQuery 8 3221225659
Put-U32 $fixtureState.RpmQuery 12 3221225659
Put-U32 $fixtureState.RpmQuery 16 0
$read = & (Join-Path $PSScriptRoot 'Read-Probe.ps1') -Detailed
if ($read.Success -or $null -ne $read.Fan1Rpm -or $null -ne $read.Fan2Rpm -or $read.Queries[0].TransportStatus -ne '0xC00000BB' -or $null -ne $read.Queries[0].PayloadHex) { throw 'Failure must not expose an RPM/payload fixture failed.' }
'PASS: cached RPM fixture, binary query layout, failed transport preserves unknown RPM (3 checks).'
