param([string]$AppDirectory='bin-next',[switch]$AppOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$stage=Join-Path $root ('artifacts/bundle-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$stage/bin/modules","$stage/package"|Out-Null
Copy-Item "$root/$AppDirectory/GalaxyHelper.exe","$root/$AppDirectory/GalaxyHardware.exe" "$stage/bin"
Copy-Item "$root/$AppDirectory/modules/*.bin" "$stage/bin/modules"
if (!$AppOnly) {
 Copy-Item "$root/artifacts/driver/package-FanZeroHold/GalaxyFanRead.inf","$root/artifacts/driver/package-FanZeroHold/GalaxyFanRead.sys","$root/artifacts/driver/package-FanZeroHold/GalaxyFanRead.cat" "$stage/package"
 Copy-Item "$root/artifacts/driver/test-certificate.cer" $stage
 Copy-Item "$root/packaging/FIRST-START.ko.md" "$stage/FIRST-START.ko.md"
 [IO.File]::WriteAllText((Join-Path $stage 'Start.ps1'),[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Start.ps1')),(New-Object Text.UTF8Encoding $true))
} else {
 Copy-Item "$root/packaging/APP-UPDATE.ko.md" "$stage/README.ko.md"
}
Copy-Item "$root/dependencies/pawnio/COPYING","$root/dependencies/pawnio/NOTICE.md" $stage
# Public certificate only; no private keys, user profiles or calibration data.
$hashes=[ordered]@{}
Get-ChildItem $stage -File -Recurse|ForEach-Object {$name=$_.FullName.Substring($stage.Length+1).Replace('\','/');$hashes[$name]=(Get-FileHash $_.FullName -Algorithm SHA256).Hash}
[IO.File]::WriteAllText((Join-Path $stage 'hashes.json'),($hashes|ConvertTo-Json),(New-Object Text.UTF8Encoding $true))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=$stage+'.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
$out=Join-Path $root $(if ($AppOnly) {'artifacts/GalaxyHelper.exe'} else {'artifacts/GalaxyHelper-Setup.exe'})
$defines=@();if ($AppOnly) {$defines=@('/define:APP_ONLY')}
$compiler="$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
& $compiler @defines /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$zip,payload.zip" "/out:$out" (Join-Path $PSScriptRoot 'Bootstrap.cs')
if($LASTEXITCODE -ne 0){throw 'Single EXE build failed.'}
Get-Item $out|Select-Object FullName,Length
Get-FileHash $out -Algorithm SHA256
