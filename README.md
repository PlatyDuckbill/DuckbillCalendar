# DuckbillCalendar

한국 서비스 파이널판타지14의 공개 일정과 개인 이용권을 같은 달력에 표시하는 **Dalamud API15 개발 플러그인**입니다. 현재 버전은 **0.1.0.0 개발판**입니다.

## 구현한 기능

- 상단 달력 / 새소식 / 이용권 / 설정 탭과 작은 화면. `/dbcal`, `/duckbillcalendar`, `/dbcal compact`, `/dbcal settings`.
- 월간 달력·목록·검색·종류 필터, 연속 기간 막대 / 시작·종료 항목 전환. 막대는 최대 3행이며 수령 막대는 선택적으로 표시합니다.
- 시작·마감 단계별 상세·알림 선택. 시즌·시리즈 시작일과 달력 이동, 오늘/내일 전장 요약. 실제 전장 기준점은 아직 미검증이므로 확인 불가입니다.
- 로컬 계정 별칭, 개인 일정·정액제/집사/가방 이용권 입력·수정·삭제, 날짜만 입력, 파일 저장·백업·복구.
- 기본 꺼짐인 로컬 채팅 알림. 발송 기록을 먼저 저장하고 전투·임무·PvP·로딩·영상 중에는 전달을 기다립니다. 공개 예시·미정·날짜 단위·추론 경계는 시간 알림을 만들지 않습니다.
- 공개 JSON 파일 가져오기, 명시적인 예시 모드, opt-in HTTPS 수신·검증·캐시·실패 복구. GitHub `calendar-data` branch에 실제 이벤트 JSON을 게시했습니다.
- 공식 이벤트·공지·패치·PvP·확장팩 등 실제96건의 저장본을 포함합니다. 예시 모드에서는 ‘실제 일정 보기’로 전환합니다. [실제 데이터 수집·갱신](docs/development/LIVE-DATA.md).
- 한국어/영어와 Graphite/Paper. 공개 HTML을 읽어 미검수 후보를 만드는 오프라인 도구와 로컬 feed 준비 도구.

2027-02-23 정식 출시와 02-19 18:00 KST 얼리 액세스를 별도 마커로 표시하며 주요 일정 상단에 고정합니다. 년월 직접 이동, 공지 게시일 기본 숨김, 새소식 표·스크롤 상세, 이용권 잔여일과 간결한 자동 저장 설정을 제공합니다. [전체 수집 범위](docs/development/ALL-SOURCES.md), [화면 개선](docs/development/SCHEDULE-PRESENTATION.md).

## 빌드·사용

[빌드와 실행 안내](docs/development/BUILD-AND-RUN.md)를 먼저 읽으세요. .NET10과 완전한 Dalamud API15 참조가 필요합니다.

```powershell
./scripts/build.ps1
./scripts/check.ps1 -NoBuild
./scripts/prepare-dev.ps1
```

`prepare-dev.ps1`은 Release 빌드·검사 후 `artifacts/development/`에 개발 DLL/의존성/한국어 리소스를 묶습니다. 출력된 폴더 전체를 보존하고 해당 DLL을 수동으로 개발 플러그인에 등록합니다. 원격 게시나 게임 자동 로드는 하지 않습니다.

## 현재 검증 범위

2026-11-12: **426개 검사와 한영 리소스177쌍**을 확인했습니다. 이 중75개는 실제 native ImGui에서 한국어 툴팁·팝업 폭·문자 입력·실제 일정 화면을 1/1.5/2배율로 확인합니다. [실기 제보 후 수정](docs/development/POST-PLAYTEST.md), [10회 개발 루프](docs/development/DEVELOPMENT-LOOPS.md), [현재 상태](docs/STATUS.md), [검증 기록](docs/VERIFICATION.md)에 근거와 한계를 남겼습니다.

실제 인게임 로드·화면·DPI·채팅 전달은 확인 대기입니다. 게임이 닫힌 동안 알림은 동작하지 않고, 발송 기록 저장 후 출력 전 종료되면 누락될 수 있습니다. 기록된 알림은 재전송하지 않습니다.

## 설계와 남은 운영 작업

- [최종 아키텍처·3회 검토](docs/architecture/DECISIONS.md)
- [수집·검수·게시 상세](docs/architecture/DATA-PIPELINE.md), [Plugin/Core/캐시 구조](docs/architecture/PLUGIN.md)
- [구현 계획](docs/development/IMPLEMENTATION-PLAN.md), [기능 우선순위](docs/product/FEATURE-PLAN.md)
- [공식 데이터 조사](docs/DATA-SOURCES.md), [v2 계약 예시](data/contracts/v2/README.md)
- [브리핑](docs/BRIEFING.md), [HTML 기획 목업](mockup/index.html), [화면 명세](docs/SCREENS.md)

실제 HTTP 수집기와 GitHub 예약 수집·검토 게시 workflow를 구현했습니다. GitHub runner의 실제 수집과 Plugin의 실제 원격 수신을 확인했습니다. 진행 이벤트 전체 연결 페이지와 최근 공지·점검·패치, 발표된 출시·얼리 액세스·PvP·대회·할인 일정을 수집합니다. 미발표 날짜·전장 기준점은 추측하지 않습니다. HTML 목업은 2026-11-11 고정 시각의 기획 자료이고 실제 Plugin은 현재 KST 날짜를 사용합니다.

개인 일정·알림 선택·이용권은 로컬에 저장합니다. 공개 HTTPS를 켜면 제공자는 IP와 일반 HTTP 메타데이터를 관찰할 수 있지만, 앱은 개인 일정·계정·캐릭터 정보를 업로드하지 않습니다. [의존성 고지](THIRD-PARTY-NOTICES.md)를 참고하세요.

저장소: <https://github.com/PlatyDuckbill/DuckbillCalendar>. `main`에 코드, `calendar-data`에 실제 이벤트 JSON을 게시했습니다. Plugin Release·설치 저장소 게시·게임 자동 로드는 수행하지 않았습니다.
