param([string]$VerifyReport)
$ErrorActionPreference='Stop'
try {
 $root=$PSScriptRoot
 $manifest=Get-Content (Join-Path $root 'hashes.json') -Raw -Encoding UTF8|ConvertFrom-Json
 foreach($file in $manifest.PSObject.Properties){if((Get-FileHash -LiteralPath (Join-Path $root $file.Name) -Algorithm SHA256).Hash -ne $file.Value){throw ('파일 검증 실패: '+$file.Name)}}
 if($VerifyReport){[IO.File]::WriteAllText($VerifyReport,'Verified by Windows PowerShell '+$PSVersionTable.PSVersion+'; no installation or hardware writes.');exit 0}
 Add-Type -AssemblyName System.Windows.Forms
 if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw '관리자 권한으로 실행하세요.'}
 if(Get-Process GalaxyHelper -ErrorAction SilentlyContinue){[Windows.Forms.MessageBox]::Show('이미 실행 중입니다. 트레이 아이콘을 눌러주세요. 새 버전으로 바꾸려면 기존 앱에서 종료 및 설정 복원을 선택한 뒤 다시 실행하세요.','Galaxy Helper')|Out-Null;exit 0}
 $bios=Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS'
 if($bios.SystemProductName -ne 'Galaxy Book6 Pro - PAMB' -or $bios.BIOSVersion -ne 'PAMB.1.5.74.371'){throw '이 패키지는 Galaxy Book6 Pro PAMB / BIOS PAMB.1.5.74.371 전용입니다.'}
 $key='HKLM:\SYSTEM\CurrentControlSet\Services\GalaxyFanRead\Parameters'
 $state=Get-ItemProperty $key -ErrorAction SilentlyContinue
 if((Test-Path "$env:LOCALAPPDATA/GalaxyHelper/rapl-recovery.json") -or ($state -and $state.FanControlNeedsRestore -ne 0)){throw '기존 앱에서 전력·팬 설정을 먼저 복원하세요.'}
 $boot=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToFileTimeUtc()
 $fanReady=$state -and $state.FanZeroHoldReadyTime -eq $state.FanControlReadyTime -and $state.FanZeroHoldReadyTime -gt $boot
 $pawn=Get-Service PawnIO -ErrorAction SilentlyContinue
 if(!$pawn){
  if([Windows.Forms.MessageBox]::Show('전력·온도 조회에 필요한 공식 서명 PawnIO 2.2.0을 GitHub에서 내려받아 설치합니다. 진행할까요?','Galaxy Helper','YesNo','Information') -ne 'Yes'){exit 0}
  $setup=Join-Path $root 'PawnIO_setup.exe'
  Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $setup -UseBasicParsing
  if((Get-FileHash $setup).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032' -or (Get-AuthenticodeSignature $setup).Status -ne 'Valid'){throw 'PawnIO 파일 또는 서명을 확인하지 못했습니다.'}
  $p=Start-Process $setup -Wait -PassThru
  if($p.ExitCode -notin @(0,3010) -or !(Get-Service PawnIO -ErrorAction SilentlyContinue)){throw 'PawnIO 설치가 완료되지 않았습니다.'}
 }
 if(!$fanReady){
   $secureBoot=$true
   try {$secureBoot=Confirm-SecureBootUEFI}catch {throw 'Secure Boot 상태를 확인하지 못했습니다. 팬 드라이버를 설치하지 않습니다.'}
   Add-Type 'using System;using System.Runtime.InteropServices;public class CodeIntegrityCheck{[DllImport("ntdll.dll")]static extern int NtQuerySystemInformation(int c,int[] p,int n,out int r);public static bool Ready(){int[] p={8,0};int r;return NtQuerySystemInformation(103,p,8,out r)==0&&(p[1]&2)!=0;}}'
   if($secureBoot -or ![CodeIntegrityCheck]::Ready()){
    [Windows.Forms.MessageBox]::Show('팬 커브·0 RPM에는 시험용 드라이버가 필요합니다. 현재 보안 설정에서는 설치할 수 없어 팬 제어 없이 실행합니다. Secure Boot와 부팅 설정을 자동 변경하지 않습니다. 자세한 준비 방법은 패키지의 시작 안내를 참조하세요.','Galaxy Helper','OK','Information')|Out-Null
   } elseif([Windows.Forms.MessageBox]::Show('팬 제어 시험에 참여하려면 테스트 서명 팬 드라이버를 설치할 수 있습니다. Microsoft 정식 서명 드라이버가 아닙니다. 공개 테스트 인증서를 신뢰 저장소에 등록하며 재부팅이 필요할 수 있습니다. 설치할까요? 아니오를 선택하면 팬 제어 없이 실행합니다.','Galaxy Helper 시험용 설치','YesNo','Information') -eq 'Yes'){
   $devices=@(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -like 'ACPI\SAM0430\*'})
   if($devices.Count -ne 1){throw '삼성 팬 장치를 하나로 식별하지 못했습니다.'}
   $instance=$devices[0].InstanceId
   if($devices[0].Status -ne 'OK' -or (Get-PnpDeviceProperty -InstanceId $instance -KeyName DEVPKEY_Device_Service).Data -ne 'SamsungEventController'){throw '삼성 기본 드라이버 상태를 확인하지 못했습니다.'}
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
} catch {if($VerifyReport){[IO.File]::WriteAllText($VerifyReport+'.error.txt',$_.Exception.ToString())}else{Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($_.Exception.Message,'Galaxy Helper 설치 / 실행 오류')|Out-Null};exit 1}
