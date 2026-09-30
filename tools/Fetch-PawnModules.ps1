param([string]$Destination = (Join-Path $PSScriptRoot '../dependencies/pawnio'))
$ErrorActionPreference='Stop'
$destinationPath=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force $destinationPath | Out-Null
$expected=@{
 'IntelMSR.bin'='D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F'
 'IntelMCHBAR.bin'='3F82B832D99B4AAC37D2A20FDB7C9BAA2A3BC0488612C9019C9484EB0E8A6EAE'
}
$complete=$true
foreach($name in $expected.Keys){$path=Join-Path $destinationPath $name;if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $expected[$name]){$complete=$false}}
if($complete){Write-Output 'PawnIO modules already match the pinned hashes.';return}
$cache=Join-Path $PSScriptRoot '../.tools'
New-Item -ItemType Directory -Force $cache | Out-Null
$zip=Join-Path $cache 'pawnio-modules-0.2.11.zip'
Invoke-WebRequest 'https://github.com/namazso/PawnIO.Modules/releases/download/0.2.11/release_0_2_11.zip' -OutFile $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($zip))
try {
 $modules=@{}
 foreach($name in $expected.Keys){
  $entry=@($archive.Entries|Where-Object {$_.Name -ceq $name})
  if($entry.Count -ne 1){throw "Expected one module: $name"}
  $stream=$entry[0].Open();$memory=New-Object IO.MemoryStream
  try {$stream.CopyTo($memory);$bytes=$memory.ToArray()} finally {$stream.Dispose();$memory.Dispose()}
  $sha=[Security.Cryptography.SHA256]::Create()
  try {$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
  if($hash -ne $expected[$name]){throw "Upstream module hash mismatch: $name"}
  $modules[$name]=$bytes
 }
 foreach($name in $modules.Keys){[IO.File]::WriteAllBytes((Join-Path $destinationPath $name),$modules[$name])}
} finally {$archive.Dispose()}
Write-Output 'Downloaded and verified PawnIO.Modules 0.2.11.'
