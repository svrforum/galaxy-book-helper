# Windows 팬 RPM 제한 조사 현황

**후속 실기 검증 성공:** 설치된 Samsung PC Diagnostics에서 SABI 팬 RPM 조회, 0~6단계 설정, 자동 복귀가 실제 동작했습니다. 2026-09-25 10:39:12 진단 성공 및 원래 Silent 모드 복원을 확인했습니다. 자체 앱의 장치 열기는 오류 1168로 실패해 독립 제어 경로가 남은 과제입니다. 직접 RPM 설정 상수는 호출부·인자가 미확인입니다. 아래의 “설정 프로토콜 미확보” 판단은 이 발견 이전 상태이며, 최신 근거는 [SAMSUNG-FAN-PROTOCOL.ko.md](SAMSUNG-FAN-PROTOCOL.ko.md)를 참조합니다.

2026-09-25 현재 전력 제한은 실기 검증됐지만 임의 팬 RPM 상한은 구현하지 못했습니다. Windows에서 개발하며 Linux 부팅을 요구하지 않습니다.

## 확보한 증거

| 경로 | 실제 결과 | 해석 |
|---|---|---|
| Windows ACPI 레지스트리 | DSDT 1개 + SSDT 26개 추출, 길이·체크섬 검증 | 전체 SSDT 확보에 Linux가 필요하다는 이전 판단을 정정 |
| 추출한 27개 AML | `_FSL`, `_FPS`, `_FIF` 발견되지 않음 | 이 테이블 집합에서 표준 팬 설정 인터페이스 미확인 |
| DSDT `_FST` | MMIO `0xFE0B0000 + 0x87/0x88` 값을 100배 하는 코드 | RPM 조회 필드이며 쓰기 대상이라는 근거 없음 |
| Windows 팬 PDO `_FST` 호출 | 두 팬 모두 오류 50 | 현재 사용자 모드 호출 경로 미지원 |
| Intel IPF | 참여자 13개 열거 성공, 동작하는 GET_FAN_STATUS 미확보 | 팬 제어 경로 미확인 |
| Samsung Settings SDK | 클라이언트 서명 검증 거부 | 공개 호출로 접근하지 못함 |
| Samsung 성능 모드 코드 | CSXI/PRF3 → CMDD(0xEE), 모드 열거값 | 임의 RPM 값이 아닌 성능 모드 명령 |
| Windows ETW 20초 | 전원 이벤트 20개 해석 성공, 팬/냉각 후보 0개 | 짧은 실행 중 팬 이벤트 부재; 기능 부재를 증명하지 않음 |

ACPI 결과는 `artifacts/acpi-registry/manifest.json`, ETW 결과는 `artifacts/fan-etw-20260925-093008/fan-analysis.json`입니다. `tools/Summarize-FanEtw.ps1`은 이벤트 이름과 필드 이름을 검사하며 파일명에 포함된 fan 문자열은 후보로 세지 않습니다.

PTID의 CPU Fan Duty Cycle과 CPU Fan #1 Speed 문자열도 조사했습니다. DUMY 장치 존재 조건에 의존하지만 확보한 테이블에는 DUMY 정의가 없어 동작하는 제어 경로로 취급하지 않습니다.

## 공개 자료 검토

- Microsoft Fan Noise Signal은 팬 상태 전달 기능입니다. RPM 설정 API와 구분해야 합니다. 현재 ETW 프로브는 공식 전체 부팅 추적 프로필이 아닌 두 TraceLogging 제공자의 제한된 실행 중 수집입니다. https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/design-guide#testing-and-tracing
- Galaxy Book4 Edge 연구에는 RPM 명령이 있지만 ARM/ENE KB9058 I²C 경로입니다. Book6 EC와 호환성이 검증되지 않아 명령을 전송하지 않았습니다. https://github.com/Saddytech/Galaxy-Book4-Edge-linux/blob/main/docs/timeline/11-FAN-CONTROL-RESEARCH.md
- 동일 358H 기반 Book6 공개 자료는 성능 프로필과 BIOS 분석을 설명하지만 임의 팬 RPM 설정법은 제공하지 않습니다. 이 프로젝트의 NVRAM 변경 코드는 실행하지 않았습니다. https://github.com/dszczyt/awesome-galaxy_book6

## 남은 개발 의존성

1. Windows RPM 조회: ACPI 조회 브리지 또는 모델을 제한한 PawnIO 읽기 모듈이 후보입니다. 공식 PawnIO의 새 모듈은 upstream 병합·서명이 필요하므로 소스 작성만으로 현재 드라이버에서 실행할 수 없습니다. https://github.com/namazso/PawnIO.Modules/wiki/Contribution-guidelines
2. 설정 프로토콜: 해당 Book6 EC의 수동 모드, 목표 RPM/PWM, 자동 제어 복귀 명령을 확인해야 합니다. 현재 공개 자료와 추출 AML에서는 확보하지 못했습니다. 공식 펌웨어 이미지나 해당 모델의 제조사 프로토콜 자료가 추가 분석 대상입니다.
3. 실제 상한 구현: 설정 프로토콜과 RPM 읽기를 모두 검증한 뒤 온도 기반 자동 복귀, 앱 종료·통신 실패 복원, 삼성 서비스와의 충돌 처리를 구현합니다.

현재 앱에서 팬 RPM 제한이 동작한다고 표시하거나, 전력 제한을 팬 RPM 제한으로 대체해서 설명하지 않습니다. 기존 Linux 수집 스크립트는 이 개발 경로의 요구 사항이 아닙니다.
