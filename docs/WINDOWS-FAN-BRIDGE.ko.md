# 독립 Windows 팬 제어 브리지 설계와 미해결 조건

상태: 읽기 전용 KMDF 프로브의 Debug/Release 빌드·정적 분석·API 검사·INF 검사·카탈로그 생성을 완료했습니다. 서명·설치·실기 검증은 미완료입니다. 저장된 조회 결과를 읽는 스크립트는 있으며 실시간 사용자 앱 연결 및 팬 쓰기 구현은 아직 없습니다. 자세한 범위는 [드라이버 설명](../driver/README.ko.md)을 참고하세요.

## 확인한 펌웨어 호출 경로

현재 추출 DSDT에서 `\_SB.SCAI.CSFI`는 `SAWS`를 호출합니다. SAWS는 MTX1을 획득하고 21바이트 SABF를 초기화·기록한 다음 SANO를 I/O 0xB2의 SAST에 써서 SMI를 발생시키고 응답을 반환합니다.

- 추출본 SHA256: 741E36FF9BA2525363B3F08892E39DAC25AD0E857C520C73D542E6CAE6A58E37
- 이 부팅에서 SAWB: SystemMemory 0x48B14AA9, 크기 0x20A
- SABF: 처음 21바이트
- SANO: 261바이트 오프셋 (168비트 + 1920비트)
- 동기화: ACPI MTX1, Field Lock

SAWB는 관측한 부팅의 주소일 뿐 영구 주소가 아닙니다. 사용자 공간 잠금은 ACPI MTX1을 대체하지 못합니다. 직접 공유 메모리를 덮어쓰고 SMI 포트를 두드리는 코드는 구현하지 않습니다. 기존 삼성 서비스와 충돌하면 다른 요청과 응답이 섞일 수 있습니다.

## 선택한 후보 구조

현재 장치의 ACPI 스택에 붙는 제한된 KMDF 필터가 하위 ACPI 경로에 CSFI 평가 요청을 보내는 구조를 검토합니다. Microsoft 문서상 ACPI 제어 메서드 평가는 해당 장치 스택의 드라이버가 수행하는 경로입니다. 실제 스택 구성과 하위 요청 전달 가능성은 별도 드라이버 시험이 필요합니다.

- https://learn.microsoft.com/en-us/windows-hardware/drivers/acpi/evaluating-acpi-control-methods
- https://github.com/microsoft/DMF/blob/master/Dmf/Modules.Library/Dmf_AcpiTarget.c

외부 앱에 임의 ACPI 메서드, 물리 주소, 포트, 원시 SABI 패킷을 받는 API를 제공하지 않습니다. 읽기는 지원 확인·RPM·최대 단계로 제한합니다. 쓰기는 모델/BIOS 재검증과 복원 검증을 마친 뒤 단계 설정·자동 복귀만 허용하는 별도 개발 단계로 둡니다. 미확인 `SetFanRpm` 인자는 추측해서 구현하지 않습니다.

제어 구현 전 조건:

1. 읽기 브리지의 서명된 드라이버가 현재 Windows 설정에서 로드되고 실기 RPM을 반환해야 합니다.
2. 팬 단계 명령 적용과 자동 복귀가 브리지에서 재검증돼야 합니다. 공식 앱 성공을 대체 증거로 쓰지 않습니다.
3. 세션 종료·프로세스 종료·통신 중단·절전 시 자동 복귀와 온도 기반 보호를 구현해야 합니다. 정상 종료만 의존하지 않습니다.
4. 기존 삼성 모드 제어와의 정책 충돌을 해결하고, 검증한 모드 외에는 수동 제어를 시작하지 않습니다.
5. 단계별 RPM을 부하·온도 변화에서도 측정해야 합니다. 단계 설정 성공은 절대 RPM 상한 보장을 뜻하지 않습니다.

## 지금 실행 가능한 드라이버가 없는 이유

설치된 공식 PawnIO 모듈은 Samsung CSFI 평가를 노출하지 않습니다. 새 모듈은 upstream 병합·서명이 필요합니다. 또한 프로젝트 지침은 적절한 제조사 드라이버 대체를 지양하므로 이 용도의 채택을 전제로 삼을 수 없습니다.

https://github.com/namazso/PawnIO.Modules/wiki/Contribution-guidelines

별도 Windows 커널 드라이버도 배포용 서명이 필요합니다. Build Tools와 WDK/SDK 준비 및 드라이버 빌드는 완료했지만 이 프로젝트의 드라이버 서명 자격 증명은 확보하지 못했습니다. 현재 PC의 Secure Boot와 메모리 무결성은 켜져 있습니다. 로컬 개발용 자체 서명만으로 현재 Windows에 배포 가능한 드라이버가 되는 것은 아닙니다.

https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-

따라서 현재의 실제 외부 의존성은 OEM 접근 허가 또는 별도 드라이버의 개발·검증·서명 경로입니다. 이 조건이 확보되기 전에는 앱에 동작하는 RPM 제한이라고 표시하지 않습니다. 보안 설정, 부팅 설정, 드라이버 스택은 이번 조사에서 변경하지 않았습니다.
