# 0.4.0.0 배포 기록

게시: 2026-11-15 03:32 KST (2026-11-14T18:32:00Z).

- [정식 Release](https://github.com/PlatyDuckbill/DuckbillCalendar/releases/tag/v0.4.0.0). 초안/사전 배포 아님.
- 빌드·태그 소스: `ca211ef6fd4059d065762aa71a5290b5beee77be`.
- ZIP: `DuckbillCalendar-0.4.0.0.zip`, 637,019바이트.
- ZIP SHA-256: `16938321b93efbc56e6d4d3b16bbc8f98f2f21b60b663dd963853cd5e4596f91`.
- 태그 아이콘 SHA-256: `195ccd75c41eee806b581ed7f1076adf3295f4516c480c56a2a524963061dd84`.

## 업데이트 노트

[사용자용 전체 내역](../../CHANGELOG.md)은 하우징 청약, 달력 검색, 가독성과 화면 전환 개선으로 나누고 각 기능의 사용법을 적었다. 하우징은 에테르웨이 한국 서버 주기 자료 기준이며 게임 내 표시와 공식 공지가 우선임을 안내한다.

GitHub Release 본문과 저장소 CHANGELOG, 게임 안의 업데이트 내역, 설치 정보의 최신 요약을 함께 갱신했다. 기존 0.3.2.0 최초 배포 기록을 보존한다. 개인 일정·이용권·표시 설정을 초기화하는 작업은 하지 않았다.

## 패키지와 공개 검증

깨끗한 소스 커밋에서 KR Dalamud API15 Release 빌드: 경고 0, 오류 0.

Core319 + Infrastructure203 + Model166 + Native UI863 + 수집117 + CLI30 = **1,698개 검사**. 한국어 리소스222개와 별도 게시→실제 소비자→손상 복구→재시작 통합 smoke를 통과했다.

ZIP과 체크섬을 먼저 초안에 올리고 서버 digest·바이트 크기를 확인한 뒤 정식 공개했다. 인증 없는 공개 URL에서 ZIP·SHA256·태그 아이콘을 다시 받아 대조했다. ZIP 안의 파일별 해시10개, 주 DLL과 Core/Infrastructure DLL 식별자, manifest/API15, BUILD-INFO의 소스 커밋과 태그 일치를 확인했다. 공개 ZIP에서 추출한 주 DLL의 버전·내장 리소스·업데이트 노트와 manifest 요약도 재검사했다.

검증한 공개 ZIP을 가리키는 단일 배포 객체를 `repo/release.json`에 게시했다. 배포 객체 커밋은 `b1a10e8423545c418d8b876084404468eb0fe35c`이다.

## 공용 설치 목록과 운영 확인

[공용 목록 동기화 Actions](https://github.com/PlatyDuckbill/DuckbillRepo/actions/runs/43627273207)가 성공했다. 목록 커밋은 DuckbillRepo `d6136f8b7afdcc60e43c2694cb9f528c28292a94`이다. 공개된 해당 커밋의 목록과 GitHub main 내용에서 Calendar0.4.0.0·새 ZIP·태그 아이콘·최신 업데이트 요약을 확인했다. 공용 저장소를 정상 fast-forward한 뒤 `scripts/sync.ps1 -Check`로 출처·공개 패키지·목록 일치를 재확인했다. Tweak0.2.2.2·Fisher0.9.0.0은 유지했고 Relic은 첫 배포 대기로 유지했다.

[캘린더 수집 Actions](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43627081116)도 새 소스 커밋에서 성공했다. 실제 플러그인 수신 경로로 공개 HTTPS 포인터·payload SHA256/크기·계약과 캐시 재시작을 확인했다: content revision35, 일정99건. 전장 HTTPS 응답은 한국 날짜2026-11-15의 외곽 유적지대와 내일 온살 하카이르를 확인했다. 이 확인은 게임 설정과 분리된 `.preview` 경로를 사용했다.

설치에 사용하는 인증 없는 `main/pluginmaster.json` URL도 2026-11-15 03:40 KST에 HTTP200·Calendar0.4.0.0·새 다운로드 링크·최신 업데이트 요약을 최종 확인했다. 초기에는 CDN 캐시에 기존0.3.2.0이 남았으나 갱신 후 실제 설치 주소의 반영까지 확인했다. 실제 게임 설치·로드·게임 글꼴/DPI·채팅 전달 확인은 공개 패키지/목록/Actions 검증과 별개다.
