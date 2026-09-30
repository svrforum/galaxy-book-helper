# Galaxy Book6 Pro 제어 인터페이스 조사

초기 조사일: 2026-09-24. 최신 구현·실측 결과는 [HARDWARE.ko.md](HARDWARE.ko.md)와 [README](../README.md)를 참고하세요. 아래 초기 관찰은 당시 상태입니다. 이 문서는 공개 1차 자료와 사용 중인 기기에서의 읽기 전용 관찰을 구분합니다.

## 결론

가벼운 Windows 제어 앱은 만들 수 있습니다. 그러나 G-Helper의 ASUS 전용 제어부를 삼성 노트북에 그대로 적용할 수는 없습니다. 2026-09-25 현재 **와트 단위 전력 제한 GUI와 실기기 10W 시험을 구현·완료했습니다. 임의 팬 속도 제한은 아직 미구현**입니다.

우선순위는 삼성의 기존 성능 모드 제어 경로를 검증하는 것입니다. 임의 팬 상한은 별도 기능이며, 성능 모드 전환이 가능해져도 커스텀 팬 곡선이 가능하다는 의미는 아닙니다.

## 실제 기기에서 확인한 내용

| 항목 | 관찰 |
|---|---|
| SMBIOS 모델 | Samsung / Galaxy Book6 Pro - PAMB |
| CPU | Intel Core Ultra X7 358H, 16코어·16스레드 |
| BIOS | PAMB.1.5.74.371 |
| Samsung ACPI ID | SAM0430 / Samsung System Event Controller |
| 팬 ACPI 장치 | PNP0C0B 2개 |
| Win32_Fan 센서 | 반환 항목 없음. RPM은 확인 불가 |
| 활성 전원 계획 | SAMSUNG MODE |
| CPU 최대 상태 | 클래스 0·1 모두 AC/DC 100% |
| 부스트 정책 | AC/DC 모두 2 |
| Samsung Settings | 8.0.13.0, Runtime 7.0.6.0 |
| 서비스 | SamsungSystemSupportService, SamsungPlatformEngine 실행 중 |
| ModulePerformance | Support=1, Value=1, ModeList=[2,1,0,3] |

기본 앱 `ModulePerformance` 레지스트리는 **보고된 상태**로만 사용합니다. 값 쓰기가 실제 펌웨어 제어 명령인지, 캐시인지, 서비스 감시 대상인지 검증하지 못했으므로 쓰지 않습니다. Value=1을 특정 팬 RPM이나 실제 펌웨어 상태라고 해석하지 않습니다.

삼성에 설치된 `SamsungSystemSupportEngine.exe` 문자열에서 `SCAIHandler::CallFunc_21`, `CallFunc_256`, `AbstractPerfMode::SetFanMode`, `ModulePerformanceModeV1/V2`, `DeviceIoControl`, named-pipe 사용 흔적이 확인됐습니다. 이는 삼성 SCAI/서비스 경로의 조사 단서일 뿐 **호출 형식·접근 권한·응답 검증이 완료된 API는 아닙니다**. 실행 파일 수정, 패치, 드라이버 교체는 하지 않았습니다.

설치된 AppxManifest에서 `samsungsettings15` URI 등록을 확인했고 앱의 Samsung Settings 실행 버튼에 사용했습니다. 특정 설정 페이지나 모드 변경용 미확인 인수는 보내지 않습니다.

## 공개 자료와 해석

### 1. G-Helper의 구조

[G-Helper 공식 저장소](https://github.com/seerge/g-helper)는 ASUS System Control Interface를 통해 제조사 모드와 설정을 다룹니다. 이 프로젝트에서는 가벼운 네이티브 UI와 기능별 백엔드 구조만 참고하며 코드를 복사하지 않았습니다. Samsung 기기에 ASUS 명령이나 팬 테이블을 사용하지 않습니다.

### 2. 삼성 성능 모드

[Linux 커널의 Samsung Galaxy Book 문서](https://docs.kernel.org/admin-guide/laptops/samsung-galaxybook.html)는 SCAI를 통해 성능 모드를 제어하고, 지원 기능을 실제 응답으로 확인한 뒤 활성화한다고 설명합니다. 프로파일에는 low-power, quiet, balanced, performance가 있으며 모델에 따라 지원 목록이 다릅니다.

[mainline 드라이버 소스](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/samsung-galaxybook.c)는 향후 프로토콜 검증의 기준 자료입니다. Linux ACPI 호출 경로가 있다는 사실만으로 Windows 사용자 모드에서 같은 호출을 할 수 있는 것은 아닙니다. 제품명이 같더라도 BIOS/ACPI ID별 검증이 필요합니다. 이 기기는 이름에서 추정하지 않고 SAM0430을 직접 관찰했습니다.

[원저자 저장소](https://github.com/joshuagrisham/samsung-galaxybook-extras)는 팬 속도 보고가 ACPI `_FST` 경로와 BIOS 구현의 영향을 받는다고 설명합니다. RPM **읽기**와 팬 곡선 **쓰기**는 별개의 문제입니다.

### 3. Panther Lake 전력 제한

[Intel 358H 공식 사양](https://www.intel.com/content/www/us/en/products/sku/245527/intel-core-ultra-x7-processor-358h-18m-cache-up-to-4-80-ghz/specifications.html)은 Panther Lake, 기본 전력 25W, 최대 터보 전력 80W를 명시합니다. 이는 이 갤럭시북의 현재 PL1/PL2 값이나 사용자가 설정할 수 있는 범위가 아닙니다.

[Intel Series 3 데이터시트](https://cdrdv2-public.intel.com/872188/872188-001.pdf)는 후속 레지스터·전력 제어 조사 자료입니다. 현재 구현에는 이 문서로부터 추정한 MSR/MMIO 쓰기를 넣지 않았습니다. 구세대 CPU의 RAPL 주소나 1/8W 변환식을 358H에 그대로 적용하지 않습니다. 지원되는 전력 백엔드, 잠금 상태, 단위, 펌웨어 재설정 동작을 확인해야 합니다.

### 4. 구현한 Windows 경로

[PowerWriteACValueIndex](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerwriteacvalueindex)와 대응하는 DC·읽기 API를 사용합니다. [Microsoft PPM 설명](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/configure-processor-power-management-options)은 Windows의 CPU 성능 정책을 설명합니다. [PERFBOOSTMODE 문서](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfboostmode)에 맞춰 부스트를 별도 제어합니다.

사용한 GUID는 실제 기기의 `powercfg /qh` 출력에서도 확인했습니다. 최대 상태는 클래스 0·1 두 항목을 함께 설정합니다. 최솟값은 읽어서 상한보다 큰 경우 적용을 거부하며, 최솟값 자체를 변경하지 않습니다.

이 경로는 OS의 성능 요청을 제한합니다. 패키지 전체(특히 iGPU/NPU)의 소비전력을 일정 W 이하로 고정하거나 팬 RPM을 제한하지 않습니다. 저장값 재조회 성공도 물리적 효과의 측정 결과와 구분해야 합니다.

## 검증 결과

- 빌드 성공: 시스템에 .NET SDK가 없어, 이미 설치된 .NET Framework C# 컴파일러로 x64 WinForms 실행 파일 생성.
- 10개 논리 테스트: AC/DC 격리, 두 클래스, 반복 적용 시 원본 보존, 재시작 후 복원, 잘못된 입력, 최소 상태 충돌, 부분 쓰기 실패, 재조회 불일치, 전원 계획 변경, 백업 실패·손상, 복원 재시도 등.
- 6개 실제 API 검사: 비활성 복제 계획의 최대 상태·클래스1 최대 상태·부스트 각각 AC/DC 쓰기/읽기/복원 성공.
- 임시 계획 삭제 완료. 활성 SAMSUNG MODE와 원래 설정값 유지 확인.
- 실제 기기 진단 JSON 생성, WinForms 실행·렌더링 확인.
- 미검증: 활성 전원 정책 적용 후 클럭·전력·온도·소음 변화, 장시간 부하, 절전 복귀, OEM 서비스와의 경합, DPI별 화면 및 모든 사용자 상호작용.

## 다음 개발 단계와 활성화 조건

1. **삼성 모드 백엔드:** 기존 삼성 앱이 서비스에 보내는 요청과 응답 형식·인증 경로 조사. 먼저 현재 모드 조회를 대조하고, 지원 모드 목록을 기기에서 얻은 뒤 모드 전환·복원 검증. 레지스트리 변경만으로 성공 판정하지 않음.
2. **센서:** 지원되는 센서 API/서명된 드라이버 검토. CPU 패키지 W, 온도, 두 팬 RPM의 실측 신뢰도 검증. ACPI thermal zone을 CPU 코어 온도로 표시하지 않음.
3. **와트 제한:** Panther Lake 지원을 명시한 경로와 실제 단위/lock 상태 확인. 설정 전후 readback 및 일정 부하에서 PL1/PL2 동작 검증. OEM 서비스 덮어쓰기 여부 확인. 미지원이면 비활성 유지.
4. **팬 직접 제어:** 현재 BIOS가 RPM/듀티 상한이나 팬 곡선 쓰기를 지원하는지 확인. 지원하지 않으면 삼성 quiet 프로파일만 제공. 지원할 경우에도 센서 장애·고온·앱 종료 시 펌웨어 자동 제어 복귀가 가능한지 먼저 검증.
5. 그 이후 AC/DC 자동 프로파일, 시작 프로그램, 핫키 등을 추가.

보안 기능 비활성화, 취약한 구형 커널 드라이버 설치, 임의 EC 레지스터 쓰기는 이 프리뷰의 동작 경로에 포함되지 않습니다.
