$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('GalaxyInstallerEncoding-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temporary | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
 foreach($bom in @($false,$true)){
  $stage=Join-Path $temporary ('payload-'+$bom)
  New-Item -ItemType Directory -Force "$stage/bin/modules","$stage/package"|Out-Null
  # Fixtures are never executed or installed. Verification exits before hardware access.
  foreach($name in @('bin/GalaxyHelper.exe','bin/GalaxyHardware.exe','bin/modules/IntelMSR.bin','bin/modules/IntelMCHBAR.bin','package/GalaxyFanRead.sys','package/GalaxyFanRead.inf','package/GalaxyFanRead.cat','test-certificate.cer','FIRST-START.ko.md','시작안내.ko.md')){[IO.File]::WriteAllText((Join-Path $stage $name),'encoding fixture')}
  [IO.File]::WriteAllText((Join-Path $stage 'Start.ps1'),[IO.File]::ReadAllText((Join-Path $root 'packaging/Start.ps1')),(New-Object Text.UTF8Encoding $true))
  $hashes=[ordered]@{}
  Get-ChildItem $stage -File -Recurse | ForEach-Object {$name=$_.FullName.Substring($stage.Length+1).Replace('\','/');$hashes[$name]=(Get-FileHash $_.FullName).Hash}
  [IO.File]::WriteAllText((Join-Path $stage 'hashes.json'),($hashes|ConvertTo-Json),(New-Object Text.UTF8Encoding $bom))
  $zip=$stage+'.zip';[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
  $binary=$stage+'.exe'
  & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:winexe /platform:x64 /codepage:65001 /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$zip,payload.zip" "/out:$binary" (Join-Path $root 'packaging/Bootstrap.cs')
  if($LASTEXITCODE -ne 0){throw 'Installer test bootstrap compilation failed.'}
  $report=Join-Path $temporary ('report-'+$bom+'.txt')
  $p=Start-Process $binary -ArgumentList @('--verify-installation',$report) -WindowStyle Hidden -Wait -PassThru
  if($p.ExitCode -ne 0 -or !(Test-Path $report)){if(Test-Path ($report+'.error.txt')){Get-Content ($report+'.error.txt')};throw 'Windows PowerShell installer path failed.'}
  Write-Output "PASS: real Bootstrap -> Windows PowerShell -> Start.ps1; UTF8 BOM=$bom, Korean paths; read-only."
 }
} finally {
 if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($temporary)) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')){throw 'Unsafe cleanup path.'}
 Remove-Item -LiteralPath $temporary -Recurse -Force
}
