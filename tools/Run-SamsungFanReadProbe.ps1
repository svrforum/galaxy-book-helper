$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
 $child=Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"') -Verb RunAs -WindowStyle Hidden -Wait -PassThru
 exit $child.ExitCode
}
$output=Join-Path $projectRoot 'artifacts\samsung-fan-read.json'
try {
 Add-Type -Path (Join-Path $PSScriptRoot 'SamsungFanReadProbe.cs')
 $path=[SamsungFanReadProbe]::FindInterface()
 $results=@()
 foreach($query in @('Support','Rpm','MaxStep')) {
  $result=[SamsungFanReadProbe]::Read($path,$query)
  $results += [pscustomobject]@{Query=$query;Result=$result}
  if($result -match 'Error=|InvalidResponse|Supported=False'){break}
 }
 [pscustomobject]@{Time=(Get-Date -Format o);SettingsWrites=$false;Results=$results} | ConvertTo-Json -Depth 5 | Set-Content $output -Encoding UTF8
} catch {
 [pscustomobject]@{Error=$_.Exception.ToString();SettingsWrites=$false} | ConvertTo-Json | Set-Content $output -Encoding UTF8
 exit 1
}
