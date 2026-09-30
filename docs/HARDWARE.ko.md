# 전력(W)·팬 RPM 제어 개발 현황

실제 와트 제어 GUI는 **GalaxyHelper.exe**, 진단·시험 CLI는 **GalaxyHardware.exe**입니다. 2026-09-25에 PL1/PL2 10W 시험에서 실제 전력 감소와 원상 복원을 확인했습니다. 팬 RPM 제한은 미지원입니다.

## 확인한 근거

실기기 DSDT를 Windows `GetSystemFirmwareTable`로 읽고 ACPICA iASL 20260408로 디스어셈블했습니다. 펌웨어를 수정하거나 AML 메서드를 실행하지 않았습니다. Windows 테이블 API는 동일 시그니처의 여러 SSDT 중 첫 항목만 반환하므로 전체 ACPI 이름 공간을 모두 확보한 것은 아닙니다.

- DSDT OEM: `SECCSD / LH43STAR`, BIOS `PAMB.1.5.74.371`.
- CPU 식별: Family 6 / Model 204 (`0xCC`) / Stepping 2.
- `FAN0`와 `FAN1`의 `_FST`는 각각 RPM0, RPM1을 읽어 **100을 곱해 RPM**을 반환합니다.
- 이 RPM 필드는 `ECR` SystemMemory 영역 `0xFE0B0000`의 `0x87`, `0x88`에 있습니다. 이는 읽기 필드입니다. 같은 주소에 쓰면 속도가 설정된다고 가정할 수 없습니다.
- 확보한 DSDT에 `_FSL`, `_FPS`, `_FIF`가 없습니다. 따라서 표준 ACPI 팬 설정 경로를 확인하지 못했습니다. `_DSM`은 팬 트립 포인트 **읽기**를 제공합니다. 이것이 모든 OEM 커스텀 제어 기능의 부재를 증명하지는 않습니다.
- 전력 관련 `CPWR`는 `GMHB()+0x5000`; `PWRU`는 그 안의 `0x938`, `PPL1`은 `0x9A0`. 따라서 MCHBAR+`0x5938` 및 +`0x59A0`를 읽기 진단 대상으로 좁혔습니다. `SPL1`/`RPL1` 메서드는 PL1 저장·설정·복원 동작을 포함합니다. **메서드의 존재만으로 사용자 모드 쓰기가 가능한 것은 아닙니다.**

삼성 SettingsSDK에서 확인한 IPC 형식으로 `ModulePerformance / Get / Value`라는 읽기 요청을 보냈지만 서비스는 다음 응답을 반환했습니다:

```text
No Verify Client: Digital Sign check Fail
```

삼성 클라이언트 서명 검증 때문에 자체 앱에서 기존 서비스를 바로 호출하는 경로가 막혔습니다. 삼성 바이너리 패치·서명 검증 우회는 구현하지 않았습니다.

## 구현한 와트 제어 백엔드

[Linux RAPL 드라이버](https://github.com/torvalds/linux/blob/master/drivers/powercap/intel_rapl_msr.c)는 Panther Lake를 지원 대상에 명시합니다. [공식 PawnIO IntelMSR 모듈](https://github.com/namazso/PawnIO.Modules/blob/0.2.11/IntelMSR.p)은 전력 단위 `0x606`, 전력 제한 `0x610`, 에너지 `0x611` 등의 읽기 및 `0x610` 쓰기를 허용합니다. [IntelMCHBAR 모듈](https://github.com/namazso/PawnIO.Modules/blob/0.2.11/IntelMCHBAR.p)은 Panther Lake 모델 식별과 MCHBAR 읽기를 지원하지만 **쓰기 함수는 제공하지 않습니다**.

`GalaxyHardware.exe`는 다음을 구현했습니다.

- `probe`: MSR PL1/PL2·단위·lock, 약 1초 패키지 전력 샘플, 패키지 온도, MCHBAR의 전력 제한 비교. 읽기 전용.
- `trial-power <PL1 W> <PL2 W> <초>`: 현재 켜져 있는 MSR 제한을 **낮추는 방향만**, 5–80W, PL1≤PL2, 5–300초 동안 시험. 전력 필드 외 시간창·클램프·enable·예약 비트 보존. 매초 재조회 및 패키지 전력 샘플. 정상 종료·Ctrl+C 시 원래 전력 필드 복원.
- `restore`: 같은 부팅 세션의 복구 기록으로 원래 MSR 전력 필드 복원. OEM이 그 사이 다른 값을 적용했다면 덮어쓰지 않고 오류 및 원본을 보존.
- `self-test`: 단위 변환, 마스크 보존, lock/범위/상향 거부, 에너지 카운터 wrap 테스트. 하드웨어에 접근하지 않음.

드라이버·BIOS·MSR 잠금·OEM 서비스·MMIO 제한에 따라 MSR 쓰기가 거절되거나 실효가 없을 수 있습니다. 레지스터 재조회와 실제 일정 부하의 소비전력 제한을 둘 다 확인해야 지원으로 판정합니다. MMIO 쓰기와 팬 RPM 쓰기는 구현하지 않았습니다.

강제 프로세스 종료·전원 중단 때 `finally` 복원을 보장할 수는 없습니다. MSR 원본은 `%LOCALAPPDATA%\GalaxyHelper\rapl-recovery.json`에 먼저 기록합니다. 다른 부팅 세션에는 기록을 적용하지 않습니다. GUI도 같은 드라이버·코덱·복원 함수를 사용합니다. 정상 종료와 절전 이벤트에서 복원을 시도하지만 해당 GUI 쓰기 흐름과 절전 동작은 아직 별도 실기기 검증 전입니다.

## 완료한 실측

공식 서명 PawnIO 2.2.0 설치 후 관리자 권한으로 진단·시험했습니다. MSR 0x610은 PL1 65W / PL2 80W, MMIO MCHBAR+0x59A0은 25W / 80W로 읽혔으며 둘 다 잠금이 해제되어 있었습니다. 전력 단위는 0.125W였습니다.

15W / 20W의 짧은 부하 시험은 약 20W가 관찰되어 PL1 실효가 불명확했습니다. 별도 승인 후 10W / 10W를 15초 적용한 동일 시험의 적용 전 5초 평균은 약 20.3W, 적용 후 15초 평균은 약 10.1W였습니다. 첫 전환 샘플은 11.43W이므로 엄격한 순간 상한이라고 해석하지 않습니다. 온도는 적용 전 56–58°C, 적용 후 45–48°C였습니다. 종료 후 MSR 원본 65W / 80W로 복원했고 MMIO·팬은 쓰지 않았습니다.

원본 증거: `artifacts/load-trial-10-10.txt`, `artifacts/load-trial-10-10.after.json`. GUI 읽기 스모크 결과: `artifacts/watt-ui.png`, 종료 코드 0. 복원 기록은 성공 후 삭제되었음을 확인했습니다.

## 팬 RPM에 남은 장애물

팬 RPM 상한을 완성하려면 검증된 OEM 설정 명령과 이를 호출할 접근 경로가 모두 필요합니다. 확보한 DSDT의 RPM 읽기 필드만으로 설정 명령을 알 수는 없습니다. 공식 PawnIO 모듈에 Samsung EC MMIO 전용 인터페이스가 없어, RPM 표시에도 범위가 제한된 모듈의 검토·서명 또는 정상 ACPI 조회 브리지가 필요합니다. **읽기 모듈 확보만으로 RPM 제한 기능이 완성되지는 않습니다.**

### 추가 조사: Intel IPF 연결 성공, 팬 조회 미확보

앞선 `ESIF_E_UNSUPPORTED_CLIENT_SDK` 결과는 진단 코드가 `IpfClientSdk_Version`을 비워 둔 영향이었습니다. 공개 SDK의 실제 버전 `1.0.11402`를 전달하자 `GetIpfInterface`, Init, SessionConnect가 모두 성공했습니다. 설치된 런타임은 Core `2.3.20306.4`, SDK `1.0.23203.06`을 반환했습니다. 따라서 앞선 “SDK 비호환” 결론은 철회합니다.

IPF 내부 명령 테이블을 정적으로 확인해 `execute-primitive <participant> <primitive> D0` 읽기 요청을 사용했습니다. 공개 소스의 팬 이름 TFN1/TFN2 및 DSDT 이름 FAN0/FAN1에 대한 GET_FAN_INFORMATION·GET_FAN_STATUS는 3000(항목 없음)을 반환했습니다. TCPU는 이름 조회를 통과했지만 팬 primitive에 2404를 반환했습니다. `send-command participants`는 3703(세션 권한 거부)입니다. 일반 연결 및 관리자 프로세스의 elevated 주소 연결에서 목록 조회가 거부됐습니다. 연결 성공만으로 팬 제어가 가능하다고 판단할 수 없습니다.

로그: `artifacts/ipf-public-fans.txt`, `artifacts/ipf-probe.txt`. 재현 소스: `tools/IpfProbe.cs`, 관리자 읽기 전용 실행: `tools/Run-IpfProbe.ps1`. 하드웨어 쓰기 명령은 포함하지 않았습니다.

### 추가 조사: 삼성 SetFanMode의 실제 의미

설치된 SamsungSystemSupportEngine.exe의 `AbstractPerfMode::SetFanMode`를 정적으로 분석했습니다. 현재 바이너리 VA `0x1400a5e10` 함수는 0~6 열거값을 분기해 0x0A, 0x0B, 0x02, 0x15, 0x16, 0x17 등의 모드 값으로 변환합니다. `CallFunc_256`으로 보낼 256바이트 버퍼의 한 바이트에 모드를 넣습니다. `CallFunc_256`의 IOCTL은 `0x82774008`이며 256바이트 입출력을 사용합니다. 주소는 이 바이너리에만 해당하고 재사용 가능한 API로 제공하지 않습니다.

이는 성능/팬 모드 설정 경로의 근거입니다. RPM 수치 또는 임의 PWM 값을 전달하는 명령의 근거는 확보하지 못했습니다. 해당 IOCTL을 직접 실행하거나 이 모드 값을 RPM 상한으로 표시하지 않았습니다. 펌웨어 RPM0/RPM1 필드도 확보한 DSDT에서는 읽기 참조만 확인했습니다.

아직 남은 필수 조건은 (1) 실제 RPM 센서 조회 경로, (2) 임의 팬 속도/상한 설정 프로토콜, (3) 설정 해제와 펌웨어 자동 제어 복귀 검증입니다. IPF 인터페이스 연결 문제는 해결했지만, 이 세 조건은 충족하지 못했습니다. 추가 조사만으로 RPM 제한 기능이 구현됐다고 볼 수 없습니다.

### 후속 검증: IPF 실제 장치 목록 확보

`IpfCore_SetAppIface`와 공개 SDK의 AppParticipantCreate/DomainCreate 콜백으로 서버가 전달하는 장치 목록을 받았습니다. 권한이 거부된 shell 명령 대신 정식 알림 인터페이스를 사용했습니다. 콜백 등록과 연결 모두 성공했습니다.

실제 전달된 13개 장치는 TCPU, SNS1~SNS5, IAUD, WIFI, IPU, PCIE, DPLY, TMEM, STG0입니다. 팬 타입의 도메인은 없었습니다. 각 장치 D0에 대한 GET_FAN_STATUS는 모두 2404를 반환했습니다. 이는 현재 연결에서 팬 상태 조회 경로를 확보하지 못했다는 근거이며 펌웨어 전체의 제어 불가능성을 증명하지는 않습니다.

로그는 `artifacts/ipf-participants.txt`, 코드 및 ABI 선언은 `tools/IpfProbe.cs`에 있습니다. CPU·팬 설정 쓰기는 없습니다.

Windows의 실제 팬 PDO는 이번 세션에서 각각 `\Device\00000058`, `\Device\00000059`이며 둘 다 machine.inf가 사용되고 별도 장치 서비스는 비어 있습니다. `tools/Run-FanAcpiProbe.ps1`은 매 실행마다 PnP에서 PDO를 다시 조회하고, 두 팬에 대해서만 표준 `_FST` 읽기를 시도합니다. 일반 앱에 이 드라이버용 API가 허용되는지는 실측 결과로 판단합니다. 관리자 프로세스 실행을 위한 UAC가 필요하며 결과 파일은 `artifacts/fan-acpi-probe.json`입니다.

삼성 SamsungEventController.sys 1.1.0.6의 IOCTL 디스패치도 정적으로 확인했습니다. CSFI/CSXI 전달 전에 요청 프로세스의 내부 권한 비트를 검사하는 분기가 있습니다. 장치 ACL의 관리자 접근 권한만으로 펌웨어 호출 권한까지 확보된다고 볼 수 없습니다. 내부 권한 테이블 수정이나 서명 검사 우회는 하지 않았습니다.

### ACPI 직접 조회 실측 결과 (2026-09-25 09:07 KST)

사용자가 UAC를 승인한 뒤 `Run-FanAcpiProbe.ps1`이 정상 실행됐습니다. 두 팬 PDO의 CreateFile은 성공했지만, `_FST`를 요청한 IOCTL_ACPI_EVAL_METHOD는 모두 Win32 오류 50(ERROR_NOT_SUPPORTED)을 반환했습니다. 관리자 승인 실패가 아니라 현재 일반 사용자 모드 호출 경로에서 요청이 지원되지 않는 결과입니다. 이 결과로 `_FST` 자체가 없거나 팬이 제어 불가능하다고 결론 내리지는 않습니다.

원본 결과: `artifacts/fan-acpi-probe.json`. 팬 속도·전력 설정은 변경하지 않았습니다. 이 경로를 반복 호출하는 대신 ACPI 메서드를 호출할 수 있는 적절한 드라이버 브리지 또는 모델 전용 읽기 모듈이 필요합니다. 그 경로를 확보하더라도 임의 RPM 설정 명령의 확인은 별도 과제로 남습니다.
