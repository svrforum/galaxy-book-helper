param([string]$WdkVersion, [ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$CheckOnly, [switch]$Analyze)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = $null
if (Test-Path -LiteralPath $vswhere) {
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
}
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$localWdk = Join-Path $PSScriptRoot '..\.tools\driver-packages\Microsoft.Windows.WDK.x64.10.0.28000.2526\c'
if (Test-Path -LiteralPath "$localWdk\Include\10.0.28000.0\km\ntddk.h") { $kits = $localWdk }
$versions = @()
if (Test-Path -LiteralPath "$kits\Include") {
    $versions = @(Get-ChildItem "$kits\Include" -Directory | Where-Object {
        (Test-Path "$($_.FullName)\km\ntddk.h") -and
        (Test-Path "$kits\Lib\$($_.Name)\km\x64\ntoskrnl.lib")
    } | Sort-Object { [version]$_.Name } -Descending | ForEach-Object Name)
}
if (!$WdkVersion -and $versions.Count) { $WdkVersion = $versions[0] }
$ready = [bool]($msbuild -and $WdkVersion -and ($versions -contains $WdkVersion))
[pscustomobject]@{ MSBuild = $msbuild; WdkVersion = $WdkVersion; InstalledWdkVersions = $versions; PreflightReady = $ready; InstallsDriver = $false }
if ($CheckOnly) { return }
if (!$ready) { throw 'MSVC, matching Windows SDK/WDK and WDK Visual Studio integration are required. No driver was built or installed.' }
$buildArgs = @((Join-Path $PSScriptRoot 'GalaxyFanRead\GalaxyFanRead.vcxproj'), '/m', '/p:Platform=x64', "/p:Configuration=$Configuration", "/p:GalaxyWdkVersion=$WdkVersion")
if ($Analyze) { $buildArgs += '/t:Rebuild', '/p:RunCodeAnalysis=true' } else { $buildArgs += '/t:Build' }
& $msbuild @buildArgs
if ($LASTEXITCODE -ne 0) { throw "WDK build failed: $LASTEXITCODE" }
Write-Output 'Unsigned research driver built. This script does not install, sign or load it.'
