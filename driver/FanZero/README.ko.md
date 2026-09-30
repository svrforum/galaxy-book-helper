# 0 RPM 검증 후보 0.4.0.0

사용자가0 RPM 기능을 요청해 만든 별도 GALAXY_FAN_ZERO 빌드입니다. 기존0.3의1~3단계 기능을 유지하며, 일반커브의0 RPM은 아직 활성화하지 않습니다.

- 삼성 공식진단 캡처의 CSFI 2C / payload00만 사용합니다. 자체경로정지·재가동 검증은 아직 별도 결과를 확인해야 합니다.
- 요청 Mode1/2,Step0을 허용하지만 시작온도40°C미만, 유지50°C미만을 요구합니다.
- 0 RPM 진입당 절대30초 제한은 Renew로 연장되지 않습니다. 5초갱신임대/Auto복원의무/전원전환보호도 유지합니다.
- 온도50°C 또는30초경과 시 Auto복귀 reason10. 센서실패/고온80°C는 기존reason3일수있습니다.
- 실행코드가 새로 기록한 FanZeroReadyTime과FanControlReadyTime이동일하고 현재부팅이후일때만 CLI가0 요청을 보냅니다. 설치경로만으로활성판단하지않습니다.

## 검증

관리자에서 bin/GalaxyHardware.exe fan-zero-verify. 원래전력값저장 후5/5W로냉각(최대90초),38°C이하시작. 단계2회전15초→0RPM20초(마지막3샘플양팬0필수)→재회전20초(양팬500RPM초과필수)→다시0RPM에서30초커널Auto한계검증. 모든경로finally팬Auto/전력복원. CPU/GPU부하를발생시키지않습니다.

artifacts/fan-zero-verification.json 전체전력복원결과, fan-zero-trial.json 단계별샘플을확인합니다. 성공하기전0RPM을 일반커브에연결하거나 Verified로표시하지않습니다. 이패키지의30초실험제한을장시간0RPM지원으로표현하면안됩니다.

빌드/서명 Prepare-Package.ps1, 설치 driver/Install-FanZero.ps1 (고정해시검증). 설치스크립트는재부팅이나0RPM시험을시작하지않습니다. tools/Apply-PresetsAndZero.ps1은앱업데이트/설치후실행코드적용여부를확인하고적용됐을때만시험합니다.

오프라인22개zero정책검사, 기존14개커널정책검사, 앱29개검사통과. 실제WDF드라이버정적분석/INF/서명/카탈로그검증통과. 설치된자체oem패키지번호는 artifacts/driver-fanzero-install-result.json에서확인하며 삼성기본oem145는유지합니다.
