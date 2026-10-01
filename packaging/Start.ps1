#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
try {
 $root=$PSScriptRoot
 $manifest=Get-Content (Join-Path $root 'hashes.json') -Raw|ConvertFrom-Json
 foreach($file in $manifest.PSObject.Properties){if((Get-FileHash -LiteralPath (Join-Path $root $file.Name) -Algorithm SHA256).Hash -ne $file.Value){throw ('파일 검증 실패: '+$file.Name)}}
 if(Get-Process GalaxyHelper -ErrorAction SilentlyContinue){[Windows.Forms.MessageBox]::Show('이미 실행 중입니다. 트레이 아이콘을 눌러주세요. 새 버전으로 바꾸려면 기존 앱에서 종료 및 설정 복원을 선택한 뒤 다시 실행하세요.','Galaxy Helper')|Out-Null;exit 0}
 $bios=Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
 if($bios.SystemProductName -ne 'Galaxy Book6 Pro - PAMB' -or $bios.BIOSVersion -ne 'PAMB.1.5.74.371'){throw '이 패키지는 Galaxy Book6 Pro PAMB / BIOS PAMB.1.5.74.371 전용입니다.'}
 $key='HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
 $state=Get-ItemProperty $key -ErrorAction SilentlyContinue
 if((Test-Path "$env:LOCALAPPDATA/GalaxyHelper/rapl-recovery.json") -or ($state -and $state.FanControlNeedsRestore -ne 0)){throw '기존 앱에서 전력·팬 설정을 먼저 복원하세요.'}
 $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToFileTimeUtc()
 $fanReady=$state -and $state.FanZeroHoldReadyTime -eq $state.FanControlReadyTime -and $state.FanZeroHoldReadyTime -gt $boot
 $pawn=Get-Service PawnIO -ErrorAction SilentlyContinue
 if(!$pawn -or !$fanReady){
  if(!$fanReady){
   if(Confirm-SecureBootUEFI){throw '현재 팬 드라이버는 테스트 서명 버전입니다. Secure Boot가 켜져 있어 설치할 수 없습니다. 보안 설정은 자동 변경하지 않습니다.'}
   Add-Type 'using System;using System.Runtime.InteropServices;public class CodeIntegrityCheck{[DllImport("ntdll.dll")]static extern int NtQuerySystemInformation(int c,int[] p,int n,out int r);public static bool Ready(){int[] p={8,0};int r;return NtQuerySystemInformation(103,p,8,out r)==0&&(p[1]&2)!=0;}}'
   if(![CodeIntegrityCheck]::Ready()){throw '현재 부팅에서 테스트 서명 모드가 활성화되지 않았습니다. 설정 후 재부팅이 필요합니다. 이 설치 프로그램은 부팅 보안 설정을 변경하지 않습니다.'}
  }
  if([Windows.Forms.MessageBox]::Show('필요한 PawnIO 및 테스트 서명 팬 드라이버를 설치합니다. 팬 드라이버용 공개 테스트 인증서를 신뢰 저장소에 추가하며 재부팅이 필요할 수 있습니다. 진행할까요?','Galaxy Helper 설치','YesNo','Information') -ne 'Yes'){exit 0}
  if(!$pawn){$setup=Join-Path $root 'PawnIO_setup.exe';if((Get-AuthenticodeSignature $setup).Status -ne 'Valid'){throw 'PawnIO 서명을 검증하지 못했습니다.'};$p=Start-Process $setup -Wait -PassThru;if($p.ExitCode -notin @(0,3010)){throw 'PawnIO 설치가 완료되지 않았습니다.'};if(!(Get-Service PawnIO -ErrorAction SilentlyContinue)){throw 'PawnIO 설치 상태를 확인하지 못했습니다.'}}
  if(!$fanReady){
   $instance='ACPI\SAM0430\2&DABA3FF&0'
   if((Get-PnpDevice -InstanceId $instance).Status -ne 'OK' -or (Get-PnpDeviceProperty -InstanceId $instance -KeyName DEVPKEY_Device_Service).Data -ne 'SamsungEventController'){throw '삼성 기본 드라이버 상태를 확인하지 못했습니다.'}
   $cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $root 'test-certificate.cer')
   if($cert.Thumbprint -ne 'D6E40CB650F5A8F873DB76C7F1DBC258F2CC8145'){throw '테스트 인증서가 일치하지 않습니다.'}
   foreach($store in @('Root','TrustedPublisher')){Import-Certificate -FilePath (Join-Path $root 'test-certificate.cer') -CertStoreLocation "Cert:\LocalMachine\$store"|Out-Null}
   foreach($file in @('GalaxyFanRead.sys','GalaxyFanRead.cat')){$sig=Get-AuthenticodeSignature (Join-Path $root "package/$file");if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint){throw '팬 드라이버 서명 검증 실패'}}
   $quick=Get-Service 'Quick Search Service' -ErrorAction SilentlyContinue;$running=$quick -and $quick.Status -eq 'Running'
   try {if($running){Stop-Service 'Quick Search Service'};& "$env:WINDIR/System32/pnputil.exe" /add-driver (Join-Path $root 'package/GalaxyFanRead.inf') /install|Out-Null;if($LASTEXITCODE -notin @(0,3010)){throw '팬 드라이버 설치 실패'}}finally{if($running){Start-Service 'Quick Search Service'}}
   [Windows.Forms.MessageBox]::Show('드라이버 설치 명령이 완료됐습니다. Windows를 다시 시작한 뒤 이 EXE를 다시 실행하세요. 자동으로 재부팅하지 않습니다.','Galaxy Helper')|Out-Null;exit 0
  }
 }
 Start-Process (Join-Path $root 'bin/GalaxyHelper.exe') -WorkingDirectory (Join-Path $root 'bin')|Out-Null
} catch {[Windows.Forms.MessageBox]::Show($_.Exception.Message,'Galaxy Helper 설치 / 실행 오류')|Out-Null;exit 1}
