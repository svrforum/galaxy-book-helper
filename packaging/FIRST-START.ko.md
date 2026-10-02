# 처음 실행하기 · v0.2.0-experimental

이 버전은 Galaxy Book6 Pro **PAMB / Core Ultra X7 358H / BIOS PAMB.1.5.74.371** 전용 시험판입니다. 다른 모델·BIOS는 지원하지 않습니다.

## 다운로드

GitHub Releases의 `GalaxyHelper-v0.2.0-experimental-Setup.exe` 하나를 받으세요.
수동 압축 해제나 개발 도구 설치는 필요 없습니다. 앱 EXE는 미서명이므로 Windows에서 게시자를 확인하지 못했다는 경고가 나올 수 있습니다. 조직 정책이 실행을 차단하면 관리자에게 문의하세요.

1. EXE를 실행하고 Windows 관리자 권한 요청을 확인합니다.
2. PawnIO가 없으면 공식 GitHub에서 2.2.0을 다운로드합니다. 고정 SHA256과 Authenticode 서명을 검사한 뒤 공식 설치 프로그램을 엽니다. 처음에는 인터넷 연결이 필요합니다.
3. 현재 보안 설정으로 시험용 팬 드라이버를 설치할 수 없으면 팬 제어 없이 앱을 실행합니다. 전력·화면 기능은 기기의 드라이버 및 센서가 준비된 범위에서 사용합니다. 이 경로의 새 PC/Secure Boot 실기 검증은 아직 완료하지 않았습니다.

## 팬 커브와 0 RPM 시험 참여

현재 포함된 GalaxyFanRead 0.5.0.0은 **테스트 서명** 드라이버이며 Microsoft 정식 서명이 아닙니다. 개발·시험용으로만 제공하며 일반 사용자용 정식 드라이버가 아닙니다.

- Secure Boot가 꺼져 있고, 현재 부팅에서 TESTSIGNING이 활성화된 시험 환경이 필요합니다. 설치 프로그램은 BIOS, TESTSIGNING, BitLocker 설정을 자동 변경하지 않습니다.
- Windows 관리자 터미널의 `bcdedit /set testsigning on`은 재부팅 후 적용됩니다. Secure Boot 정책이 막으면 BIOS 설정이 필요합니다. BitLocker 사용자는 복구 키를 준비하고 제조사·Microsoft 안내를 확인해야 합니다.
- 설치 전 공개 테스트 인증서의 시스템 신뢰 저장소 등록을 안내하고 확인을 받습니다. 개인 키는 포함하지 않습니다.
- 설치 후 재부팅이 필요하면 안내합니다. 재부팅 후 **같은 EXE를 다시 실행**하세요.
- 최초 사용 또는 만료된 RPM 보정은 앱에서 **정밀 보정**을 실행해야 합니다. 약 7~9분이며 팬과 전력을 실제 변경합니다.
- 0 RPM은 5/10W 전력 제한을 함께 적용합니다. 온도·센서·전력·갱신 보호 조건이 깨지면 자동 냉각으로 복귀합니다. 모든 환경의 무팬 열 안정성을 보장하지 않습니다.

Microsoft 안내: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/the-testsigning-boot-configuration-option

## 설정 유지와 업데이트

성공적으로 적용한 전력·팬 커브는 `%LOCALAPPDATA%\GalaxyHelper`에 저장합니다. 앱 종료 시 하드웨어를 복원하고, 다음 정상 실행 시 마지막 적용값을 다시 적용합니다. 자동 냉각으로 복귀한 상태를 저장하면 팬은 자동 제어로 시작합니다.

재부팅 뒤에도 자동으로 실행하려면 트레이 우클릭 → **Windows 시작 시 마지막 설정 적용**을 체크하세요. Windows 로그인 30초 후 실행합니다. 부팅 직후·로그인 전에는 OEM 제어입니다. 센서/드라이버 준비와 보정 유효성 검사에 실패하면 자동 적용을 중단합니다.

커브 제어 중 그래프·숫자·프리셋 변경은 편집이 끝난 뒤 약 0.5초 후 재적용합니다. 팬 자동 복귀와 재적용 응답, 실제 회전 안정화에는 추가 시간이 필요합니다. 같은 삼성 팬 단계로 매핑되는 요청값끼리는 RPM이 같을 수 있습니다. 자동 냉각 중에는 **커브 적용**을 눌러 시작하세요.

업데이트 전 트레이 → **종료 및 설정 복원**을 선택한 뒤 새 EXE를 실행하세요. 앱을 강제 종료하거나 설정 폴더를 삭제하지 마세요. EXE 위치를 바꿔도 사용자 설정은 유지됩니다.

## 제거

1. 트레이의 자동 시작 체크를 해제하고 **종료 및 설정 복원**을 선택합니다.
2. 팬 드라이버를 설치했다면 관리자 터미널에서 `pnputil /enum-drivers`로 **Galaxy Helper Research / GalaxyFanRead.inf** 패키지의 게시 이름(`oem숫자.inf`)을 확인합니다. 해당 패키지만 `pnputil /delete-driver oem숫자.inf /uninstall`로 제거하고 Windows를 재부팅합니다. **SamsungEventController 기본 드라이버는 제거하지 마세요.**
3. 필요하다면 Windows의 설치된 앱에서 PawnIO를 제거합니다. 다른 모니터링 앱이 사용할 수 있습니다.
4. 시험 인증서를 제거하려면 LocalMachine의 Root/TrustedPublisher에서 지문 `D6E40CB650F5A8F873DB76C7F1DBC258F2CC8145`인 인증서만 제거합니다. 시험 모드 해제는 `bcdedit /set testsigning off` 후 재부팅입니다. Secure Boot 복원은 제조사 BIOS 안내를 따르세요.
5. 내려받은 EXE와 `%PROGRAMFILES%\GalaxyHelper`를 삭제합니다. 설정까지 지우려면 `%LOCALAPPDATA%\GalaxyHelper`도 삭제합니다.

검증 범위: 기존 개발 장비의 전력·팬 제어, 오프라인 정책/UI 및 패키지 검사. 새 Windows에서 최초 설치부터 재부팅까지의 전체 경로는 미검증입니다.
