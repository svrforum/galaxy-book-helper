# 삼성 공식 드라이버 관리자 / SYSTEM 실기 비교

2026-10-01, Galaxy Book6 Pro PAMB에서 PR #3의 다음 후보를 실제 검증했습니다.

- SamsungEventController.sys: 1.1.0.6, Running
- 드라이버 SHA256: `3929193D86C691FD15407181E72B7AAAC98B62E8D7D95EC8A42CC9B6DA16AB11`
- 독립 x64 읽기 프로브 SHA256: `2B7918A388E50506C11ECAA104E0BD112EAD04363ADA4C2F6254FCF2E5E53434`
- 동일 바이너리를 보호된 Program Files 경로에서 관리자 및 SYSTEM으로 실행했습니다.
- SYSTEM 작업은 일회 실행 후 제거했습니다. 배터리에서도 실행하도록 지정했습니다.
- 실제 프로세스 토큰의 IsSystem을 기록해 SYSTEM 실행을 확인했습니다.

| 계정 | 장치 열기 권한 | 결과 |
|---|---|---|
| 관리자 | GENERIC_READ | OpenError=1168 |
| 관리자 | GENERIC_READ + GENERIC_WRITE | OpenError=1168 |
| SYSTEM | GENERIC_READ | OpenError=1168 |
| SYSTEM | GENERIC_READ + GENERIC_WRITE | OpenError=1168 |

현재 SetupAPI 인터페이스를 열거했으며, 실패는 IOCTL 전달 전 CreateFile 단계입니다. 조회 패킷은 기존 공식 진단에서 확인한 Support/Rpm/MaxStep으로 제한했습니다. 장치 열기에 실패해 지원 조회 패킷조차 전달되지 않았습니다. 팬 설정·진단 모드·드라이버·Secure Boot·부팅 설정은 변경하지 않았습니다.

**이 장비에서는 SYSTEM 권한만으로 삼성 공식 드라이버의 독립 EXE 접근이 열리지 않습니다.** G-Helper의 ASUS 사례를 그대로 적용할 수 없다는 실기 근거입니다. 기존 SCAI Client 인증서/프로세스 검증과 STATUS_NOT_FOUND 정적 분석과 부합하지만, 실행 중 해당 분기를 추적한 것은 아니므로 원인을 인증서 하나로 확정하지 않습니다.

현재 제어를 삼성 기본 드라이버로 교체할 검증된 경로는 여전히 없습니다. 다음 단계는 공식 서비스/SDK의 허용된 클라이언트 경로와 거부 분기를 확인하는 일입니다. 알려지지 않은 팬 setter를 시도하거나 인증서를 이식한 결과는 없습니다. 모든 가능한 공식 경로가 불가능하다는 결론도 아닙니다.

재현 소스: `tools/SamsungFanReadProbe.cs`, `tools/SamsungFanProbeProgram.cs`, `tools/Compare-SamsungReadContexts.ps1`. 원본 결과는 로컬 `artifacts/samsung-context-comparison.json`이며 개인 계정명이 포함돼 공개하지 않습니다.
