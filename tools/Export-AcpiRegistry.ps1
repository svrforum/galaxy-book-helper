# Read cached AML tables from Windows registry; excludes licensing/identity tables.
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDir=Join-Path $projectRoot 'artifacts\acpi-registry'
New-Item -ItemType Directory -Force $outputDir | Out-Null
$rows=foreach($root in Get-ChildItem 'HKLM:\HARDWARE\ACPI') {
 if($root.PSChildName -notmatch '^(DSDT|SSD[0-9A-Z])$'){continue}
 foreach($key in Get-ChildItem $root.PSPath -Recurse){
  foreach($name in $key.GetValueNames()){
   $bytes=$key.GetValue($name)
   if($bytes -isnot [byte[]] -or $bytes.Length -lt 36){continue}
   $signature=[Text.Encoding]::ASCII.GetString($bytes,0,4)
   if($signature -notin @('DSDT','SSDT')){continue}
   if([BitConverter]::ToUInt32($bytes,4) -ne $bytes.Length){throw 'Table length mismatch'}
   $sum=0;foreach($b in $bytes){$sum=($sum+$b) -band 255};if($sum -ne 0){throw 'Table checksum mismatch'}
   $file=Join-Path $outputDir ($root.PSChildName+'-'+$name+'.dat')
   [IO.File]::WriteAllBytes($file,$bytes)
   [pscustomobject]@{File=[IO.Path]::GetFileName($file);Signature=$signature;Bytes=$bytes.Length;TableId=[Text.Encoding]::ASCII.GetString($bytes,16,8).Trim();SHA256=(Get-FileHash $file).Hash;Source=$key.Name}
  }
 }
}
$rows | ConvertTo-Json | Set-Content (Join-Path $outputDir 'manifest.json') -Encoding utf8
$rows | Select-Object File,TableId,Bytes
