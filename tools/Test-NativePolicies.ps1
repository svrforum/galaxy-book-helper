$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$compiler = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC/Tools/MSVC/**/bin/Hostx64/x64/cl.exe' | Select-Object -Last 1
if (!$compiler) { throw 'MSVC x64 compiler was not found.' }
$output = Join-Path $root 'artifacts/policy-tests'
New-Item -ItemType Directory -Force $output | Out-Null
foreach ($variant in @('FanControl', 'FanZero', 'FanZeroHold')) {
    $source = Join-Path $root "driver/$variant/PolicyTests.c"
    $binary = Join-Path $output "$variant.exe"
    & $compiler /nologo /W4 /WX /TC /O2 /GS- $source "/Fo$output/$variant.obj" "/Fe$binary" /link /NODEFAULTLIB /SUBSYSTEM:CONSOLE /ENTRY:mainCRTStartup
    if ($LASTEXITCODE -ne 0) { throw "$variant compilation failed." }
    & $binary
    if ($LASTEXITCODE -ne 0) { throw "$variant policy assertion failed at line $LASTEXITCODE." }
    Write-Output "PASS: $variant native policy tests (no hardware access)."
}
