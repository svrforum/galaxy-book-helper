# Signs a separate research package. Does not trust the certificate, install,
# alter boot options, suspend encryption or reboot.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $root '.tools\driver-packages\Microsoft.Windows.SDK.CPP.10.0.28000.1721\c'
$wdk = Join-Path $root '.tools\driver-packages\Microsoft.Windows.WDK.x64.10.0.28000.2526\c'
$signTool = Join-Path $sdk 'bin\10.0.28000.0\x64\signtool.exe'
$source = Join-Path $root 'artifacts\driver\package-Release'
$dest = Join-Path $root 'artifacts\driver\package-TestSigned'
$certificateFile = Join-Path $root 'artifacts\driver\test-certificate.cer'
$certificateId = Join-Path $root 'artifacts\driver\test-certificate-thumbprint.txt'
$certificate = $null
if (Test-Path -LiteralPath $certificateId) {
    $thumbprint = (Get-Content -LiteralPath $certificateId -Raw).Trim()
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Invalid saved certificate identifier.' }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction Stop
    if (!$certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date).AddDays(7)) { throw 'Existing test certificate is not usable.' }
} else {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=GalaxyFanRead Local Development Only' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddMonths(3)
    $certificate.Thumbprint | Set-Content -LiteralPath $certificateId -Encoding ASCII
}
Export-Certificate -Cert $certificate -FilePath $certificateFile -Force | Out-Null
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'GalaxyFanRead.sys'),(Join-Path $source 'GalaxyFanRead.inf') -Destination $dest
& $signTool sign /fd SHA256 /s My /sha1 $certificate.Thumbprint (Join-Path $dest 'GalaxyFanRead.sys')
if ($LASTEXITCODE -ne 0) { throw 'SYS test signing failed.' }
& "$wdk\tools\10.0.28000.0\x64\infverif.exe" /w (Join-Path $dest 'GalaxyFanRead.inf')
if ($LASTEXITCODE -ne 0) { throw 'INF verification failed.' }
& "$wdk\bin\10.0.28000.0\x86\Inf2Cat.exe" "/driver:$dest" /os:10_CO_X64,10_GE_X64 /uselocaltime
if ($LASTEXITCODE -ne 0) { throw 'Catalog generation failed.' }
& $signTool sign /fd SHA256 /s My /sha1 $certificate.Thumbprint (Join-Path $dest 'GalaxyFanRead.cat')
if ($LASTEXITCODE -ne 0) { throw 'Catalog test signing failed.' }
$files = @('GalaxyFanRead.sys','GalaxyFanRead.inf','GalaxyFanRead.cat') | ForEach-Object {
    $path = Join-Path $dest $_
    if ($_ -ne 'GalaxyFanRead.inf') {
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $signature.Status -eq 'HashMismatch') { throw 'Unexpected signature.' }
    }
    [pscustomobject]@{ Name = $_; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
[ordered]@{ CreatedUtc=[DateTime]::UtcNow.ToString('o'); CertificateThumbprint=$certificate.Thumbprint; CertificateExpires=$certificate.NotAfter.ToUniversalTime().ToString('o'); TestSigned=$true; MicrosoftSigned=$false; Installed=$false; HardwareValidated=$false; Files=@($files) } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dest 'manifest.json') -Encoding UTF8
Write-Output "Test-signed package ready: $dest"
