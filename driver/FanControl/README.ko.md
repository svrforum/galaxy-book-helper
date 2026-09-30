# FanControl 0.3 — 단계 유지와 RPM 목표 제어

## 2026-09-28 최종 상태

Auto 복귀 중간 상태(State2)를 기다리도록 클라이언트 수정 후, 최종3400 RPM 목표120초/Auto/전력 복원 모두 통과했습니다. 최종30개 3103~3106 / 3202~3206 RPM, Verified=true, 복원 의무0. `artifacts/fan-control-target-cooled-verification.json` 참조. 최종 UI 스모크와 앱 실행 확인 완료. 아래 최초 시험 기록도 보존합니다.

- 이전 0.2의 독립 ACPI 쓰기 시험 성공: 2단계 약 15초, 0 RPM에서 **2744 / 2844 RPM**까지 상승, 33~34°C, 자동 복귀 응답 성공, 후속 조회 0/0 RPM.
- 지속 제어용 **0.3.0.0 / oem305.inf** 설치 성공. 2026-09-28 재부팅 후 실제 실행 및 요청 처리 확인. 추가 드라이버 설치 없이 제어 시험을 진행했습니다.
- 지속 제어·갱신 만료 Auto 복귀·3,200 RPM 목표 120초 시험·GUI 적용/복원 흐름이 통과했습니다. 마지막 30개 목표 샘플은 3103~3106 / 3201~3206 RPM. 장시간 보정 최신 결과는 artifacts/fan-control-cooled-verification.json을 확인합니다.

## 제어 계약

관리자 전용 서비스 레지스트리 `FanControlRequest`의 고정 32바이트 구조만 받습니다. Version/Sequence/Owner/Mode/Step/IssuedUtc이며 임의 ACPI·주소·포트·RPM 명령은 없습니다. Mode는 Auto, 명시적 Start, Renew이고 Step은 1~3입니다. 단계0(팬 정지)은 없습니다. 요청은 3초 이내의 새 요청이어야 하며 부팅/복귀 때 저장된 요청을 재실행하지 않습니다.

갱신 신호의 임대 시간은 커널 고정 5초입니다. 만료 검사를 갱신 처리보다 먼저 하므로 만료된 제어를 Renew로 되살리지 않습니다. 다른 소유자의 Renew/Start는 거부합니다. 단계 설정 전에 자동 복귀 의무를 레지스트리에 기록합니다.

시작 온도 75°C 미만, 유지 온도 80°C 미만을 요구합니다. 온도/RPM 조회 실패, 최대 단계 변화, 갱신 만료, D0 이탈에서 Auto(2C/80)로 복귀합니다. 실패한 설정도 부분 적용 가능성을 고려해 복귀합니다. 복귀 실패 기록은 남기고 활성 상태에서 재시도합니다. 정상 종료/절전은 타이머를 중지하고 조회를 배출한 뒤 복귀합니다. 펌웨어 호출 지연/불응이나 OS 비정상 종료까지 정확한 5초 복귀를 보장하지 않습니다.

`FanControlResult`는 64바이트입니다. State 0 Auto / 1 manual / 2 restoring / 3 restore failure / 4 rejected. StopReason 1 user / 2 lease / 3 temperature or sensor / 4 RPM / 5 write / 6 power / 7 capability / 8 request / 9 journal recovery.

## 앱

`bin/GalaxyHelper.exe` 0.4와 `bin-next/GalaxyHelper.exe`는 동일 빌드입니다. 1~3단계 유지, 자동 제어 복귀, RPM 목표 적용 버튼이 있습니다. 수동 제어 중 UI 타이머가 1초마다 갱신하고 새 RPM을 표시합니다. UI 응답 정지/프로세스 종료로 갱신이 끊기면 커널의 임대 만료가 복귀를 담당합니다.

RPM 목표는 **단계 기반**입니다. 직접 SetFanRpm 명령의 미확인 인자를 추측하지 않습니다. 단계별 90초 측정의 마지막 20개 안정화 샘플을 사용하고 두 팬 중 높은 최대값에 100 RPM 여유를 더해 목표 이하인 가장 높은 단계를 선택합니다. 안정화 후 목표+100 RPM 초과가 3회 지속되면 낮은 단계로 내립니다. 최소 단계에서도 초과하면 자동 복귀하고 실패를 표시합니다. 열 보호 복귀가 RPM 목표보다 우선합니다. 임의 RPM의 절대 상한을 보장하는 기능은 아닙니다.

보정은 모델/BIOS/최대 단계에 종속되고 7일 유효합니다. 센서 누락, 불안정한 속도, 성능 모드 변화, 단조롭지 않은 단계값은 거부합니다. 목표 유지 시험과 Auto 복귀까지 성공해 Verified=true가 된 보정 파일만 앱에서 적용할 수 있습니다.

## 실기 검증 실행

사용자가 전체 팬 제어 개발·시험을 승인했습니다. 별도 재승인 없이 관리자 UAC를 거쳐 다음을 실행합니다.

```powershell
.\driver\FanControl\Verify-CooledControl.ps1
```

약 7분 동안 단계2 시험 15초 → 갱신 중단 복귀 → 단계1/2/3 각각90초 보정 → RPM 목표120초 시험 순서입니다. 시험 중 전력을 최대10W로 낮추며 finally에서 원래 전력값을 복원합니다. 열 보호는 유지합니다. 단계별 실패와 샘플, Auto/전력 복원 여부를 기록합니다.

- 최종 결과: artifacts/fan-control-cooled-verification.json
- 진행 상태: artifacts/fan-control-live.json 및 fan-calibration-progress.json
- 보정 파일: 사용자 LocalAppData/GalaxyHelper/fan-calibration.json
- GUI 실제 버튼 검증: tools/Verify-ControlUi.ps1

## 사전 검증

Release 정적 분석 경고/오류 0, API/INF/카탈로그/서명 검사 통과. 커널이 실제 포함하는 Policy.h를 사용하는 네이티브 테스트 14개 통과. 앱 오프라인 테스트 25개 통과. UI의 제어 대기 표시를 실제 실행/화면으로 확인했습니다. 이는 지속 제어 실기 검증이나 HLK/Driver Verifier를 대체하지 않습니다.

롤백 시 자체 oem305/304/303/302/297/291.inf의 공급자와 원본 파일명을 확인하여 제거합니다. 삼성 기본 oem145.inf는 유지합니다.
