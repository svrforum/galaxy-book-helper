param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wdk = Join-Path $root '.tools\driver-packages\Microsoft.Windows.WDK.x64.10.0.28000.2526\c'
$binary = Join-Path $root "artifacts\driver\$Configuration\GalaxyFanRead.sys"
if (!(Test-Path -LiteralPath $binary)) { throw 'Build the driver first.' }
$packageDir = Join-Path $root "artifacts\driver\package-$Configuration"
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -LiteralPath $binary -Destination $packageDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GalaxyFanRead\GalaxyFanRead.inf') -Destination $packageDir
& "$wdk\tools\10.0.28000.0\x64\infverif.exe" /w (Join-Path $packageDir 'GalaxyFanRead.inf')
if ($LASTEXITCODE -ne 0) { throw "INF validation failed: $LASTEXITCODE" }
& "$wdk\bin\10.0.28000.0\x86\Inf2Cat.exe" "/driver:$packageDir" /os:10_CO_X64,10_GE_X64 /uselocaltime /verbose
if ($LASTEXITCODE -ne 0) { throw "Catalog creation failed: $LASTEXITCODE" }
$files = Get-ChildItem -LiteralPath $packageDir -File | Where-Object Extension -In '.inf','.sys','.cat'
$manifest = [pscustomobject]@{
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    Configuration = $Configuration
    HardwareId = 'ACPI\SAM0430'
    Model = 'Galaxy Book6 Pro - PAMB'
    Bios = 'PAMB.1.5.74.371'
    Signed = $false
    Installed = $false
    HardwareValidated = $false
    Scope = 'Read-only ACPI probe; no fan setting or RPM limit'
    Files = @($files | ForEach-Object { [pscustomobject]@{ Name = $_.Name; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packageDir 'manifest.json') -Encoding UTF8
Write-Output "Unsigned package prepared: $packageDir"
