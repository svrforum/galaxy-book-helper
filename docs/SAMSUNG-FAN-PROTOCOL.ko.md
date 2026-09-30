# Samsung PC Diagnostics에서 발견한 팬 프로토콜

## 실기 검증 후속 결과 — 2026-09-25

관리자로 실행한 자체 읽기 프로브는 CreateFile에서 `1168`로 실패했습니다. SetupAPI로 현재 장치 인터페이스를 열거해 경로가 일치함을 확인했습니다. 이 오류만으로 서명 검증 실패 원인을 확정할 수는 없습니다.

반면 설치된 삼성 PC 진단을 정상 실행해 냉각 팬 항목을 선택하자 RPM 조회와 단계 설정이 실제로 동작했습니다. 팬 수 2개, 최대 단계 6개가 응답됐으며 단계 0에서 양쪽 0 RPM을 확인했습니다. 이후 단계 1~6 설정에 `AA` 성공 응답과 RPM 상승이 기록됐습니다. 이는 Book6의 전용 팬 제어 프로토콜 존재에 대한 실기 증거입니다. 독립 앱에서의 제어 성공 또는 임의 RPM 상한 검증과는 구분합니다.

관측 및 복원 결과는 `tools/Summarize-SamsungFanTest.ps1`이 `artifacts/samsung-official-fan-test.json`으로 정리합니다. 단계별 마지막 관측값은 상승 과도 구간을 포함하므로 고정 목표 RPM이나 보장 상한으로 사용하지 않습니다.

10:39:12에 공식 진단이 `result: True`로 완료됐습니다. 성능 모드 재조회에서 원래 `Silent` 복원을 확인했고, `43 58 2C 00 AA 80 ...` 응답으로 자동 팬 제어 복귀 명령 성공을 확인했습니다. 단계 6의 마지막 RPM은 4226/4309였습니다. 이 검증은 공식 앱 경로의 성공이며 자체 앱에서 동일 명령을 실행했다는 의미가 아닙니다.

### 자체 접근 실패 추가 분석

#### 인증서 경로 역추적 결과

공식 `PCDiagnostics.exe`의 PE 인증서 영역을 정적으로 파싱해 `CN=SCAI Client,C=KR`, 발급자 `CN=Samsung Electronics SCAI Verifier CA,C=KR` 인증서를 확인했습니다. EKU는 codeSigning입니다. 일반 Authenticode 조회에 표시되는 Samsung/DigiCert 체인과 별도로 파일 안에 존재합니다. 자체 프로브 EXE에는 PE 인증서 영역이 없습니다. 공개 인증서 정보만 분석했으며 파일을 수정하거나 인증서를 이식하지 않았습니다. 결과: `artifacts/scai-client-certificates.json`.

드라이버 함수 `0x140026A34`는 후보 데이터에서 `SCAI Client` 문자열을 검사합니다. 이 문자열 포인터는 `0x140024268`에 있습니다. `0x140028594`의 선별 검사 실패는 STATUS_NOT_FOUND로 변환되며, 호출자 `0x140029760`은 해당 결과에서 프로세스 항목 제거 함수를 호출합니다. 이후 장치 열기 검사 `0x140029BF8`이 항목을 찾지 못하면 같은 상태를 반환합니다. 관측한 1168에 대해 이전보다 구체적인 정적 설명을 확보했습니다. 실행 중 커널 추적을 통한 분기 확정은 하지 않았습니다.

장치 열기 검사에는 파일의 사용자 쓰기 매핑 검사(`MmDoesFileHaveUserWritableReferences`)와 추가 검증 `0x1400284A4`가 있고, 후자는 `0x140026DF8` → `0x140027024`로 인증서·이미지 검증을 진행합니다. 따라서 이름이나 문자열이 같다는 사실만으로 허용되는 구조라고 해석하면 안 됩니다. 공식 진단의 SabiHelper에는 별도 등록 요청 없이 SetupAPI 열거 → CreateFile → IOCTL 흐름이 있습니다. 현재 근거는 앱 초기화 누락보다는 OEM 전용 인증 경로를 가리킵니다.

재현용 `tools/Inspect-PeFunction.py`는 PE 런타임 함수 경계와 import 이름을 포함해 정적 디스어셈블을 출력합니다. 분석 원본은 `artifacts/scai-functions.txt`, `scai-cert-functions.txt`, `scai-cert-policy.txt`, `scai-verify-details.txt`에 저장했습니다. 이 분석 과정에서 하드웨어 설정·보안 설정을 변경하지 않았습니다.

2026-09-25 11:11 추가 검증: PowerShell 호스팅을 제외한 독립 x64 EXE를 컴파일하고 관리자 권한으로 실행했습니다. 동일하게 지원 조회 전 장치 열기에서 `OpenError=1168`이 발생했습니다. 따라서 PowerShell/Add-Type에만 국한된 문제라는 가설은 배제합니다. 결과는 `artifacts/samsung-fan-standalone.json`, 재현 소스는 `tools/SamsungFanProbeProgram.cs`입니다. 팬 쓰기는 수행하지 않았습니다.

SamsungEventController.sys의 정적 코드에서 요청 프로세스 확인 함수(이미지 기준 VA `0x140029BF8`)와 이를 호출해 요청을 완료하는 경로(`0x140027CF4`)를 확인했습니다. 프로세스 항목 조회가 실패하면 `STATUS_NOT_FOUND (0xC0000225)`를 반환하며, 이는 관측한 Win32 1168과 일치합니다. 다른 검증 실패에는 접근 거부 분기도 있습니다. 동적 커널 추적은 하지 않았으므로 정확히 어느 분기에서 실패했는지 확정하지 않습니다. 관리자 권한이나 IOCTL 패킷 수정만으로 해결된다고 볼 수 없습니다.

공식 앱의 URI 활성화 코드도 확인했습니다. `viewmodel`로 화면 이동, `pipename`으로 진단 결과 전달을 지원하지만 임의 RPM이나 팬 단계 입력 API는 발견하지 못했습니다. 화면을 열면 진단이 시작될 수 있으므로 단순 센서 폴링 경로로 사용하지 않습니다.

현재 소스의 `SamsungFanProtocol`은 하드웨어 접근 없는 코덱입니다. 실기 응답 해석과 오류 검사를 준비했지만 팬 쓰기·장치 호출과 연결하지 않았습니다. 독립 앱을 완성하려면 OEM이 허용하는 클라이언트 경로나 별도의 서명된 제한적 드라이버 브리지가 필요합니다. 기존 공식 PawnIO 모듈에는 이 Samsung SABI 호출 경로가 없습니다. 바이너리 패치나 공식 앱 프로세스 삽입으로 접근 검사를 우회하는 구현은 포함하지 않았습니다.

2026-09-25, 설치된 Samsung PC Diagnostics 1.0.24.0의 정적 분석 결과입니다. 이 문서의 명령 존재와 현재 Book6에서의 실행 성공은 별개의 사실입니다. 아직 팬 쓰기 명령은 실행하지 않았습니다.

## 출처와 재현

- 삼성의 Book6 공식 안내에 Windows PC 진단의 냉각 팬 테스트가 명시되어 있습니다: https://www.samsungsvc.co.kr/solution/4603277
- 로컬 패키지: `SAMSUNGELECTRONICSCO.LTD.PCDiagnostics_1.0.24.0_x64__3c1yjt4zspk6g`.
- `PCDiagnostics.exe` Authenticode 검증: Valid, Samsung Electronics Co., Ltd.
- 분석 파일: `Diagnostics.Core.dll`, SHA256 `1EC42D8B5F47B3FBCA975DDB4DF51E4EAC8A4E24F6B47B111DCA6326A4C738F2`.
- ILSpy 정적 분석 결과는 `artifacts/reverse/PCDiagnostics/Core`에 보관합니다. 원본 앱을 수정하거나 원본 DLL의 하드웨어 코드를 실행하지 않았습니다.

## 실제 호출하는 코드

`CoolingFanTest`는 `SabiHelper`를 통해 Samsung Event Controller 장치 인터페이스 `{53567919-4A93-414F-9772-7171DA240ECF}`로 21바이트 IOCTL `0x82774004`를 보냅니다. 이는 앞서 시도한 Samsung Settings named pipe와 다른 호출 경로입니다. 같은 커널 드라이버의 접근 검증은 여전히 적용될 수 있습니다.

기본 버퍼는 `43 58 [subcommand LE16] 00 [payload] [zero padding]`입니다. 응답은 정확히 21바이트여야 하며 offset 4 완료 플래그 `AA`를 검사합니다. payload의 숫자 상수는 **가변 길이 big-endian**으로 변환하므로 uint little-endian 직렬화와 혼동하면 안 됩니다.

| 기능 | subcommand | payload | 확인 수준 |
|---|---|---|---|
| 진단 지원 확인 | 0x7A | BB AA | 현재 진단 코드가 호출, 응답 payload[0:2] = DD CC 확인 |
| 팬 개수·RPM 읽기 | 0x7A | 82 B5 80 | 현재 진단 코드가 호출 |
| 최대 팬 단계 읽기 | 0x7A | 82 B5 81 | 현재 진단 코드가 호출; 최대 RPM이 아님 |
| 팬 단계 설정 | 0x2C | 단계 값 | 현재 진단 코드가 0부터 최대 단계까지 사용 |
| 팬 자동 제어 복귀 | 0x2C | 80 | SetFanNormalState가 호출 |
| 빠른 응답 모드 | 0x7A | 82 EF BB + 인자 | 호출 코드 존재; 이번 프로브에서 제외 |
| 새 팬 제어 지원 | 0x7A 추정 | 82 B5 AA BB | enum 정의만 발견, 실행 경로 미확인 |
| 새 팬 단계 설정 | 0x7A 추정 | 82 B5 82 + 미확인 인자 | enum 정의만 발견 |
| 진단 모드 종료 | 0x7A 추정 | 82 B5 83 | enum 정의만 발견 |
| 직접 RPM 설정 | 0x7A 추정 | 82 B5 84 + 미확인 인자 | SetFanRpm enum만 발견; 인자·지원 여부 미확인 |

RPM 응답 payload[2]는 팬 개수입니다. 첫 팬은 `(payload[3] << 8) | payload[4]`, 둘째 팬은 `(payload[5] << 8) | payload[6]`입니다. DSDT의 100배 환산과 달리 이 경로의 앱 코드는 이 값을 RPM으로 기록합니다. UI 그래프는 별도로 RPM × 100 / 6000으로 정규화하므로 그래프 값 자체를 RPM으로 읽으면 안 됩니다.

진단 앱은 성능 모드를 저장하고 고성능으로 전환한 다음 팬 정지·단계 상승 검사를 수행합니다. 이 전체 진단은 읽기 전용이 아닙니다. 이번 `Run-SamsungFanReadProbe.ps1`은 지원 확인, RPM, 최대 단계의 세 읽기만 허용하고 오류 시 중단합니다.

## 이전 결론의 수정

표준 ACPI `_FSL`이 없다는 사실은 Samsung 전용 SABI 설정 명령이 없다는 뜻이 아닙니다. 이제 **단계 설정과 자동 복귀 명령의 실제 사용 근거**를 확보했습니다. 직접 RPM 설정은 이름만 발견했으므로 동작을 단정하지 않습니다.

남은 검증은 (1) 현재 드라이버가 우리 프로세스의 읽기 호출을 허용하는지, (2) Book6 펌웨어가 해당 진단 명령을 지원하는지, (3) 제어·복원을 제한된 시험에서 확인할 수 있는지입니다. RPM 상한 구현은 그 다음이며, 읽기 실패를 0 RPM으로 표시해서는 안 됩니다.
