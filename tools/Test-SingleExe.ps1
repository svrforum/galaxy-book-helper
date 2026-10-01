$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'artifacts/GalaxyHelper.exe'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyHelperBundleTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temporary | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $report = Join-Path $temporary 'verified.txt'
    $process = Start-Process $app -ArgumentList "--verify-package $report" -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Test-Path $report)) { throw 'The real single EXE package did not verify.' }
    Write-Output 'PASS: real embedded EXE and module package verifies without hardware access.'
    $assembly = [Reflection.Assembly]::LoadFile($app)
    foreach ($case in @('missing-module', 'tampered-app')) {
        $zip = Join-Path $temporary "$case.zip"
        $resource = $assembly.GetManifestResourceStream('payload.zip')
        $file = [IO.File]::Create($zip)
        try { $resource.CopyTo($file) } finally { $resource.Dispose(); $file.Dispose() }
        $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Update)
        try {
            if ($case -eq 'missing-module') { $archive.GetEntry('bin/modules/IntelMSR.bin').Delete() }
            else {
                $archive.GetEntry('bin/GalaxyHelper.exe').Delete()
                $entry = $archive.CreateEntry('bin/GalaxyHelper.exe')
                $stream = $entry.Open()
                try { $bytes = [Text.Encoding]::UTF8.GetBytes('invalid replacement'); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
            }
        } finally { $archive.Dispose() }
        $binary = Join-Path $temporary "$case.exe"
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        & $compiler /nologo /target:winexe /platform:x64 /define:APP_ONLY /codepage:65001 /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$zip,payload.zip" "/out:$binary" (Join-Path $root 'packaging/Bootstrap.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Corrupted bundle test compilation failed.' }
        $failedReport = Join-Path $temporary "$case.txt"
        $process = Start-Process $binary -ArgumentList "--verify-package $failedReport" -Wait -PassThru
        if ($process.ExitCode -eq 0 -or !(Test-Path ($failedReport + '.error.txt'))) { throw "Invalid package was accepted: $case" }
        $expected = if ($case -eq 'missing-module') { 'Incomplete package' } else { 'Payload hash mismatch' }
        if (!(Get-Content ($failedReport + '.error.txt') -Raw).Contains($expected)) { throw "Unexpected rejection: $case" }
        Write-Output "PASS: $case rejected before extraction or hardware access."
    }
} finally { Remove-Item $temporary -Recurse -Force }
