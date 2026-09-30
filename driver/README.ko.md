# GalaxyFanRead 연구용 드라이버

현재 구현은 Galaxy Book6 Pro PAMB 전용 **팬 읽기 진단 필터**입니다. 팬 RPM 제한 기능은 아직 구현·실기 검증되지 않았습니다. 전력 제한 기능은 별도 MSR/PawnIO 경로입니다.

2026-09-27: 사용자 승인에 따라 Secure Boot Off, TESTSIGNING On, 로컬 테스트 인증서 신뢰 등록을 완료했습니다. 메모리 무결성은 유지합니다. 0.1.2는 부팅 후 모델/BIOS 검사를 통과했지만 최종 조회가 `0xC00000BB`였습니다. 이 기록만으로 ACPI 전송 실패와 지원 응답 불일치를 구분할 수 없습니다. 설치/재부팅 상태의 최신 기록은 [이어가기 기록](TEST-MODE-NEXT.ko.md)을 확인하세요.

## 0.1.3 읽기 진단

- 모델 `Galaxy Book6 Pro - PAMB`, BIOS `PAMB.1.5.74.371`, 하드웨어 ID `ACPI\SAM0430`를 매 조회 전에 확인합니다. 이 비교는 실수 방지용이며 보안 인증이 아닙니다.
- 첫 D0 진입 30초 뒤 ACPI PDO의 `CSFI`에 고정된 지원 조회, 최대 단계 조회, RPM 조회를 각각 보냅니다. 지원 응답 불일치가 RPM 조회를 막지 않습니다. RPM 성공은 팬 설정 명령이 검증됐다는 뜻이 아닙니다.
- 각 조회의 전송 NTSTATUS, 응답 해석 NTSTATUS, 반환 길이, 입력 21바이트, 출력 최대 128바이트, 시각, 요청 번호를 자체 서비스 Parameters에 기록합니다.
- 서비스 레지스트리 `ProbeRequestSequence` 변경을 2초마다 확인하고 새 번호일 때만 세 읽기를 실행합니다. 유휴 타이머는 펌웨어를 호출하지 않습니다. 요청 번호 외에 패킷, 주소, 포트, 명령을 입력받지 않습니다.
- D0Exit는 잠금 안에서 재예약을 차단한 뒤 잠금을 풀고 타이머를 동기 중지합니다. 조회 중인 콜백이 끝나기 전에 전원 전환을 완료하지 않습니다.
- 세 개의 동기 요청에는 각각 3초 제한이 있습니다. ACPI/펌웨어가 취소를 처리하지 못하면 강제 중단은 보장되지 않습니다.
- 기존 OEM 요청에는 KMDF 필터 기본 전달 동작을 사용합니다. 팬 쓰기, 임의 ACPI 실행, 물리 메모리 접근, 사용자 IOCTL은 없습니다.

```powershell
# 일반 권한: 저장된 결과만 읽기
.\driver\Read-Probe.ps1 -Detailed
# 관리자 권한: 새 읽기 요청 및 완료 확인 (새 드라이버가 적용된 뒤)
.\driver\Request-Probe.ps1
```

`Read-Probe.ps1`은 결과를 캐시로 표시합니다. `Request-Probe.ps1`은 요청/완료 번호와 새 시각을 확인하고 `artifacts/driver-request-result.json`에 결과를 저장합니다. 실패 시 RPM을 0으로 표시하지 않습니다.

## 빌드 및 검증

```powershell
.\driver\Build-Driver.ps1 -Configuration Release -Analyze
.\driver\Test-ProbeDecoder.ps1
.\driver\Package-Driver.ps1
.\driver\Prepare-TestSignedDriver.ps1
```

VS Build Tools 2026, WDK 10.0.28000.2526, SDK 10.0.28000.1721을 사용합니다. 0.1.3 Release 정적 분석 경고/오류 0, API Validator/INF/카탈로그 검사 통과. 오프라인 응답 해석 3개 검증 통과. 별도 package-TestSigned의 SYS/CAT 서명 및 카탈로그 멤버 검증을 수행했습니다. 이는 Driver Verifier/HLK/절전 경합 실기 검증을 대체하지 않습니다.

`Install-ReadProbe.ps1`은 모델/BIOS, 부팅 상태, 파일 해시, 인증서, 카탈로그를 확인한 후 설치합니다. 자동 재부팅은 없습니다. INF는 SamsungEventController 기본 드라이버를 유지하는 Extension이고 AddFilter로 추가됩니다. 배포용 CHID 제한과 정식 서명은 아직 없습니다.

참고: [Microsoft ACPI 평가](https://learn.microsoft.com/en-us/windows-hardware/drivers/acpi/evaluating-a-control-method-that-takes-input-arguments), [WdfTimerStop](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdftimer/nf-wdftimer-wdftimerstop), [Microsoft DMF ACPI 구현](https://github.com/microsoft/DMF/blob/master/Dmf/Modules.Library/Dmf_AcpiTarget.c).
