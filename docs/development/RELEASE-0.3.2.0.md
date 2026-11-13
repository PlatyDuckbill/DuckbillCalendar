# 0.3.2.0 최초 배포 기록

게시: 2026-11-14 06:43 KST (2026-11-13T21:43:40Z).

- [정식 Release](https://github.com/PlatyDuckbill/DuckbillCalendar/releases/tag/v0.3.2.0), 태그 v0.3.2.0. 초안/사전 배포 아님.
- 빌드/태그 소스: 6f8a98ac804489e0650c4f51eab0822631cbcea9
- 배포 객체 커밋: 8c22215. repo/release.json은 ZIP 공개 검증 후 게시했다.
- 공용 등록 커밋: DuckbillRepo c06ef34.
- ZIP: DuckbillCalendar-0.3.2.0.zip, 616445바이트.
- ZIP SHA256: 3d5b2bfe0e4b388063d9a85f46dfad2bbeaeeb0dbb498f0fb1b20638c6ef2d6b
- 아이콘 SHA256: 195ccd75c41eee806b581ed7f1076adf3295f4516c480c56a2a524963061dd84

## 공개 검증

인증 없는 공개 URL로 ZIP·SHA256·태그 아이콘을 다시 받아 대조했다. ZIP의 필수 DLL3개·manifest·deps·아이콘·고지·업데이트 내역과 파일별 해시10개를 확인했다. DLL 이름/버전은 PE 메타데이터로 읽었으며, BUILD-INFO.json의 커밋과 태그를 대조했다. 자동 검사1363개, 한국어 리소스222개와 게시/실제 소비자/손상 복구/재시작 smoke를 통과했다.

[공용 설치 목록](https://raw.githubusercontent.com/PlatyDuckbill/DuckbillRepo/main/pluginmaster.json)에 Tweak0.2.2.2·Fisher0.9.0.0·Calendar0.3.2.0 세 항목을 확인했다. Relic은 아직 첫 배포 대기로 기존 정책대로 제외했다. sync.ps1 및 -Check와 [공용 목록 Actions](https://github.com/PlatyDuckbill/DuckbillRepo/actions/runs/43513740703)가 성공했다. 다른 프로젝트의 소스·Release·아이콘은 변경하지 않았다.

## 수집 Actions 운영 보정

첫 소스 push의 [수집 실행](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43513235882)은 Linux에서 파일 공유 잠금이 rename/replace를 막는다는 Windows 전용 검사 가정 때문에 실패했다. 런타임 로직을 바꾸지 않고 StateWriteBarrier로 실제 파일 경로 충돌을 만들도록 두 저장 실패 검사를 수정했다. 실패 중 채팅 차단/복구 후1회 전달의 검증 조건은 유지한다. 수정 커밋47fd185는 테스트 전용이며 기존 공개 태그와 ZIP을 덮어쓰지 않았다.

수집 재실행: [43513731543](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43513731543). 성공했다. Linux의 Infrastructure203검사와 실제 수집9출처/99건/오류0, publication/content25 게시를 확인했다. 데이터 커밋은46305da다.

실제 플러그인 CalendarWorkspace로 공개 HTTPS 포인터·payload SHA256/크기·계약 검증을 통과해 revision25/99건을 수신했고, 캐시 재시작 복원을 확인했다. 전장 API도 한국 날짜2026-11-14의 봉인된 바위섬과 내일 외곽 유적지대를 실수신했다. 이 실수신은 게임 설정 대신 격리된 .preview 경로만 사용했다.

## 구분

Release 공개·목록 검증·Actions와 실제 게임 설치/로드·게임 글꼴/DPI·채팅 전달은 별도다. 후자의 인게임 확인은 이번 자동화에서 수행하지 않았다. 향후 배포는 [게시 절차](PUBLISHING.md)를 따른다.
