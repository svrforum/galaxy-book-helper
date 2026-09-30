param([ValidateSet('Prepare','EnableTestMode','Restore')][string]$Action = 'Prepare')
# Run elevated. No driver installation and no automatic restart.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$reportPath = Join-Path $root 'artifacts\development-trust-result.json'
$statePath = Join-Path $root 'artifacts\driver\development-trust-original.json'
$expectedThumbprint = 'D6E40CB650F5A8F873DB76C7F1DBC258F2CC8145'
$certPath = Join-Path $root 'artifacts\driver\test-certificate.cer'
$report = [ordered]@{ Action=$Action; TimeUtc=[DateTime]::UtcNow.ToString('o'); Restarted=$false; DriverInstalled=$false }
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator elevation required.' }
    $bcd = "$env:WINDIR\System32\bcdedit.exe"
    if ($Action -eq 'Prepare') {
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($certPath)
        if ($certificate.Thumbprint -ne $expectedThumbprint) { throw 'Certificate identity mismatch.' }
        if (!(Test-Path -LiteralPath $statePath)) {
            $boot = @(& $bcd /enum '{current}')
            if ($LASTEXITCODE -ne 0) { throw 'Cannot read BCD.' }
            $testEntry = @($boot | Where-Object { $_ -match '^\s*testsigning\s+' })
            if ($testEntry.Count) { throw 'A pre-existing testsigning setting requires separate restoration review.' }
            & $bcd /export (Join-Path $root 'artifacts\driver\boot-before-testmode.bcd') | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'BCD backup failed.' }
            [ordered]@{ Thumbprint=$expectedThumbprint; TestSigningOriginallyAbsent=$true; RootExisted=(Test-Path "Cert:\LocalMachine\Root\$expectedThumbprint"); PublisherExisted=(Test-Path "Cert:\LocalMachine\TrustedPublisher\$expectedThumbprint"); SecureBootOriginallyEnabled=(Confirm-SecureBootUEFI) } | ConvertTo-Json | Set-Content $statePath -Encoding UTF8
        }
        Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
        Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
        $report.CertificateTrusted = $true
        $report.SecureBoot = Confirm-SecureBootUEFI
        $report.NextStep = 'Disable Secure Boot in firmware, return to Windows, then run EnableTestMode.'
    } elseif ($Action -eq 'EnableTestMode') {
        if (Confirm-SecureBootUEFI) { throw 'Secure Boot is still enabled. No boot changes made.' }
        if (!(Test-Path $statePath)) { throw 'Missing original configuration record.' }
        if (!(Test-Path "Cert:\LocalMachine\Root\$expectedThumbprint") -or !(Test-Path "Cert:\LocalMachine\TrustedPublisher\$expectedThumbprint")) { throw 'Test certificate trust is not prepared.' }
        & $bcd /set '{current}' testsigning on | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Setting TESTSIGNING failed.' }
        $report.TestSigningConfigured = $true
        $report.RestartRequired = $true
    } else {
        if (Get-Service GalaxyFanRead -ErrorAction SilentlyContinue) { throw 'Remove the GalaxyFanRead extension package before restoring boot settings.' }
        $original = Get-Content $statePath -Raw | ConvertFrom-Json
        if ($original.Thumbprint -ne $expectedThumbprint -or !$original.TestSigningOriginallyAbsent) { throw 'Original state does not match.' }
        $boot = @(& $bcd /enum '{current}')
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read BCD.' }
        if (@($boot | Where-Object { $_ -match '^\s*testsigning\s+' }).Count) {
            & $bcd /deletevalue '{current}' testsigning | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Restoring TESTSIGNING failed.' }
            $report.RestartRequired = $true
        }
        if (!$original.RootExisted -and (Test-Path "Cert:\LocalMachine\Root\$expectedThumbprint")) { Remove-Item -LiteralPath "Cert:\LocalMachine\Root\$expectedThumbprint" }
        if (!$original.PublisherExisted -and (Test-Path "Cert:\LocalMachine\TrustedPublisher\$expectedThumbprint")) { Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPublisher\$expectedThumbprint" }
        $report.NextStep = 'Re-enable Secure Boot in firmware after the test driver has been removed.'
    }
    $report.Success = $true
} catch { $report.Success=$false; $report.Error=$_.Exception.Message }
$report | ConvertTo-Json -Depth 4 | Set-Content $reportPath -Encoding UTF8
if (!$report.Success) { throw $report.Error }
