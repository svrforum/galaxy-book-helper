# G-Helper와 삼성 팬 접근 경로 비교

조사일: 2026-10-01. 목표는 커스텀 팬 커브를 유지하면서 GalaxyFanRead 의존성과 테스트 서명 필요성을 제거할 수 있는지 확인하는 것입니다.

## 결론과 검증 범위

G-Helper가 ASUS 공식 드라이버를 재사용한다는 설명은 맞습니다. 삼성에서도 공식 드라이버의 팬 명령 존재가 기록되어 있으므로 자체 커널 드라이버가 원리적으로 유일한 방법은 아닙니다. 다만 현재 우리 앱이 이용할 수 있는 삼성 공식 드라이버 접근 경로는 검증되지 않았습니다.

이번 조사는 공개 소스·개발자 설명과 저장소의 기존 실기 조사 기록을 비교한 것입니다. 현재 환경에는 사용자 PC의 삼성 드라이버 바이너리와 원본 실기 JSON이 없으며 Windows 하드웨어에 연결되어 있지 않습니다. 아래 삼성 관측값은 저장소 문서의 역사 기록이고, 이번에 재측정한 결과가 아닙니다. 앱이나 설치된 드라이버를 변경한 조사도 아닙니다.

## G-Helper에서 확인한 두 제어 방식

1. 기본판: `AsusACPI`가 `ATKACPI` 장치를 열어 `DeviceIoControl`로 ASUS 인터페이스를 사용합니다. `SetFanCurve`는 16바이트 커브를 CPU/GPU/중간 팬 장치에 전달합니다. 기본 커브는 펌웨어가 실행하므로 앱에서 팬 속도를 매초 직접 강제하는 방식과 구분됩니다. 모델에 따라 커스텀 커브 자체가 지원되지 않습니다.
2. 실험판: 개발자는 MyASUS의 팬 진단과 같은 방식으로 팬 속도를 주기적으로 설정한다고 설명합니다. #3735에서는 ASUS 드라이버 3.1.41.0 이후 관리자 실행도 실패했고 SYSTEM 실행으로 동작한 사례가 있습니다. 개발자의 마지막 댓글은 최신 실험판의 자동 SYSTEM 실행으로 해결됐다고 설명합니다. 별도의 PawnIO 변형도 존재하므로 모든 G-Helper 빌드가 ASUS 드라이버만 사용하는 것은 아닙니다.

근거:
- [기본판 FAQ](https://github.com/seerge/g-helper/wiki/FAQ)
- [확인한 AsusACPI.cs 커밋](https://github.com/seerge/g-helper/blob/54c5bd00da82e20a0361228e5758f692b3b9560b/app/AsusACPI.cs)
- [실험판 제어 설명](https://github.com/seerge/g-helper/discussions/2272)
- [SYSTEM 제한 분석](https://github.com/seerge/g-helper/issues/3735#issuecomment-2671616772)
- [자동 SYSTEM 실행으로 해결했다는 개발자 설명](https://github.com/seerge/g-helper/issues/3735#issuecomment-4147844106)
- [PawnIO 변형에 대한 개발자 설명](https://github.com/seerge/g-helper/discussions/2272) — 페이지의 2026-08-02 개발자 답변.

## 삼성에서 현재 확인된 차이

[SAMSUNG-FAN-PROTOCOL.ko.md](SAMSUNG-FAN-PROTOCOL.ko.md)의 기존 실기 기록에 따르면 공식 PC Diagnostics에서는 RPM 조회, 팬 단계 설정, 자동 복귀가 동작했습니다. 우리 독립 EXE는 관리자 실행에서도 장치 열기 `1168`로 실패했습니다. 관리자 권한과 SYSTEM 권한은 같지 않으며, 동일 프로브를 SYSTEM으로 비교한 결과는 저장소에서 찾지 못했습니다.

하지만 삼성은 단순 계정 권한 이외의 검사 근거도 있습니다. 기존 정적 조사에서 공식 앱의 별도 SCAI Client 인증서와 드라이버의 인증서·이미지 검증 경로를 확인했습니다. [HARDWARE.ko.md](HARDWARE.ko.md)의 삼성 서비스 요청은 `No Verify Client: Digital Sign check Fail`을 반환했습니다. ASUS 사례만으로 SYSTEM 실행이 삼성 인증 검사를 통과한다고 결론 내릴 수 없습니다. 정확히 어느 검사 분기에서 독립 EXE가 거부되는지는 동적 검증이 남아 있습니다.

또한 현재 GalaxyHelper 커브는 보정된 팬 단계 1~3과 별도 0단계 기능으로 목표를 매핑합니다. ASUS 기본판의 16바이트 펌웨어 커브 업로드, 팬별 독립 백분율 설정과 같은 기능이라고 볼 수 없습니다. 직접 RPM 설정 명령은 기존 조사에서 이름만 발견했으며 지원 여부와 인자가 검증되지 않았습니다.

## PawnIO만으로 대체할 수 있는지

현재 사용하는 공식 IntelMSR/IntelMCHBAR 모듈은 삼성 CSFI/CSXI 호출을 제공하지 않습니다. upstream에는 `LpcACPIEC.p`가 있으나 이 소스가 허용하는 포트는 0x62/0x66입니다. [기존 Book6 펌웨어 조사](WINDOWS-FAN-BRIDGE.ko.md)의 CSFI 경로는 공유 버퍼, ACPI MTX1 잠금, 0xB2 SMI 요청을 사용합니다. 기존 EC 모듈을 추가하면 이 호출이 그대로 가능하다고 볼 수 없습니다. 모듈 소스 존재와 호환되는 서명 바이너리 배포·실기 검증도 별개입니다.

- [upstream EC 모듈 소스](https://github.com/namazso/PawnIO.Modules/blob/main/LpcACPIEC.p)
- [upstream 모듈 목록](https://github.com/namazso/PawnIO.Modules)

## 다음 실기 검증의 우선순위

1. 공식 SamsungEventController의 버전과 장치 인터페이스를 기록하고, 동일한 독립 읽기 프로브를 관리자와 SYSTEM에서 비교합니다. 지원 여부/RPM/최대 단계의 기존 세 조회만 사용합니다. 팬 설정, 진단 모드 변경, 드라이버 설치, Secure Boot 또는 TESTSIGNING 변경은 이 비교에 필요하지 않습니다.
2. SYSTEM에서 조회가 성공하면 반복 조회와 응답 검증을 먼저 확인하고, 공식 드라이버를 이용하는 작은 사용자 모드 백엔드가 후보가 됩니다. 조회 성공만으로 수동 팬 명령·커브·절전 복원이 검증된 것은 아닙니다.
3. SYSTEM에서도 실패하면 거부 지점과 삼성의 허용된 서비스/SDK 경로를 확인합니다. 인증서 검사가 원인이라면 SYSTEM만으로 해결할 수 없습니다. 공식 진단의 화면 자동 실행은 냉각 테스트를 수행하므로 지속적인 팬 커브 백엔드로 취급하지 않습니다.
4. 접근 경로가 확보된 뒤에만 단계 설정, 자동 복귀, 고온/센서 실패/프로세스 종료/절전 시 보호를 실기 검증합니다. 기존 커브 편집기는 유지하고 하드웨어 접근부만 교체할 수 있는지 평가합니다.

현재 판단: 별도 FanRead 없이 구현할 가능성을 닫을 단계는 아니지만, 동작한다고 배포할 단계도 아닙니다. 우선 확인할 새 후보는 SYSTEM 읽기 비교입니다. 확인되지 않은 삼성 바이너리 패치, 인증서 이식, 임의 EC 쓰기를 동작 경로로 가정하지 않습니다.
