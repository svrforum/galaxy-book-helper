# Builds/signs a separate candidate only. Does not install, request a control or reboot.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe'
& $msbuild (Join-Path $PSScriptRoot 'GalaxyFanRead.vcxproj') /t:Rebuild /p:Platform=x64 /p:Configuration=Release /p:GalaxyWdkVersion=10.0.28000.0 /p:RunCodeAnalysis=true
if ($LASTEXITCODE) { throw 'Control build failed.' }
$wdk = Join-Path $root '.tools/driver-packages/Microsoft.Windows.WDK.x64.10.0.28000.2526/c'
$signTool = Join-Path $root '.tools/driver-packages/Microsoft.Windows.SDK.CPP.10.0.28000.1721/c/bin/10.0.28000.0/x64/signtool.exe'
$package = Join-Path $root 'artifacts/driver/package-FanZero'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item (Join-Path $root 'artifacts/driver/FanZero-Release/GalaxyFanRead.sys') $package
$inf = [IO.File]::ReadAllText((Join-Path $root 'driver/GalaxyFanRead/GalaxyFanRead.inf')).Replace('0.1.3.0','0.4.0.0').Replace('09/27/2026','09/29/2026').Replace('fan read-only research filter','fan bounded control research filter').Replace('ACPI read-only probe','ACPI bounded control')
[IO.File]::WriteAllText((Join-Path $package 'GalaxyFanRead.inf'),$inf,[Text.Encoding]::ASCII)
& (Join-Path $wdk 'tools/10.0.28000.0/x64/infverif.exe') /w (Join-Path $package 'GalaxyFanRead.inf')
if ($LASTEXITCODE) { throw 'Control INF validation failed.' }
$thumbprint = 'D6E40CB650F5A8F873DB76C7F1DBC258F2CC8145'
& $signTool sign /fd SHA256 /s My /sha1 $thumbprint (Join-Path $package 'GalaxyFanRead.sys')
if ($LASTEXITCODE) { throw 'SYS signing failed.' }
& (Join-Path $wdk 'bin/10.0.28000.0/x86/Inf2Cat.exe') ('/driver:' + $package) /os:10_CO_X64,10_GE_X64 /uselocaltime
if ($LASTEXITCODE) { throw 'Control catalog creation failed.' }
& $signTool sign /fd SHA256 /s My /sha1 $thumbprint (Join-Path $package 'GalaxyFanRead.cat')
if ($LASTEXITCODE) { throw 'CAT signing failed.' }
& $signTool verify /pa /c (Join-Path $package 'GalaxyFanRead.cat') (Join-Path $package 'GalaxyFanRead.sys')
if ($LASTEXITCODE) { throw 'Catalog membership verification failed.' }
[ordered]@{ Version='0.4.0.0'; CreatedUtc=[DateTime]::UtcNow.ToString('o'); Installed=$false; ControlExecuted=$false; Scope='Steps 1..3; experimental step 0 below 40C, Auto at 50C or 30 seconds; 5-second lease'; Files=@(Get-FileHash (Join-Path $package '*.sys'),(Join-Path $package '*.inf'),(Join-Path $package '*.cat') | Select-Object Hash,Path) } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $package 'manifest.json') -Encoding UTF8
