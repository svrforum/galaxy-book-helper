param([ValidateSet('bin','bin-next')][string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { throw '.NET Framework 4.x C# compiler is required.' }
$outputDir = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force $outputDir | Out-Null
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll /out:"$outputDir\GalaxyPolicy.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $outputDir\GalaxyPolicy.exe (legacy Windows policy tool)"
$hardwareSources = @(Get-ChildItem (Join-Path $PSScriptRoot 'hardware') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:exe /main:GalaxyHardware.Program /platform:x64 /optimize+ /utf8output /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll /out:"$outputDir\GalaxyHardware.exe" $hardwareSources
if ($LASTEXITCODE -ne 0) { throw 'Hardware tool build failed.' }
New-Item -ItemType Directory -Force (Join-Path $outputDir 'modules') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'dependencies\pawnio\*.bin') (Join-Path $outputDir 'modules')
Write-Output "Built $outputDir\GalaxyHardware.exe"
& $compiler /nologo /target:winexe /main:GalaxyHardware.ControlProgram /platform:x64 /optimize+ /utf8output /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll /out:"$outputDir\GalaxyHelper.exe" $hardwareSources
if ($LASTEXITCODE -ne 0) { throw 'Watt control app build failed.' }
Write-Output "Built $outputDir\GalaxyHelper.exe (RAPL watt control)"
