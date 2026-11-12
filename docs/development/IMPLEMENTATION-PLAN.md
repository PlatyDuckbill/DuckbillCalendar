# 구현 계획과 검증 관문

기준: [3회 검토 결정](../architecture/DECISIONS.md). 아래는 전체 계획이다. **현재 구현 상태는 [STATUS](../STATUS.md)와 [10회 개발 기록](DEVELOPMENT-LOOPS.md)이 우선한다.**

2026-11-12: T01~T09의 C# 계약·Core·저장·실제 UI·전송/캐시·로컬 알림을 구현하고 오프라인 검사했다. 정적 JSON 전체 schema와 인게임 인수는 남았다. T10은 저장한 이벤트 HTML 후보 추출만, T12는 로컬 준비만 구현했다. T00 외부 관문, T11 검수 workflow, T13 예약/게시, T14 실제 전장 기준점, T15~16 운영·실기·출시는 남았다. 공용 IO를 게임 없이 검사하기 위해 Infrastructure를 독립 프로젝트로 추가했다.

## 제안 저장소 구조

```text
src/
  DuckbillCalendar.Core/
    Contracts/ Domain/ Calendar/ Alerts/ Personal/ Rules/ State/
  DuckbillCalendar.Plugin/
    Infrastructure/ Application/ Game/ UI/ Configuration/ Localization/
tools/
  CalendarDataImport/       # collector/normalize/candidate CLI
  CalendarDataPublish/      # trusted validation/publish CLI
data/
  sources/ curated/ schemas/ fixtures/
tests/
  DuckbillCalendar.Core.Tests/
  DuckbillCalendar.Feed.Tests/
docs/
```

처음부터 범용 framework를 만들지 않는다. Core에 게임 API나 HTTP를 넣지 않고 Plugin의 네이티브 접근은 필요한 경우에만 어댑터로 추가한다. 인증·DB·웹 관리 서버·다중 데이터센터 자동 추론은 초기 제외한다. 라이선스는 코드 이전 시 결정하고 Fisher 코드를 복사한다면 AGPL-3.0 및 외부 고지를 지킨다.

## 순서·의존성·완료 기준

| ID / 단계 | 작업 | 선행 | 완료 판단 |
| --- | --- | --- | --- |
| T00 / 조사 관문 | 사이트 정책·source registry·hosted runner probe·검수 담당 확정 | 없음 | 비로그인 최소 요청 접근, 수집 가능/보류 기록. 실패하면 수기 공개 일정 운영으로 분기 |
| T01 / 계약 | Boundary v2·event/phase/region·health/current/payload schema와 domain validator | 없음 | 확정 시작/미정 종료, date-only, 조건형, 유효하지 않은 혼합을 구별. 예시 데이터 운영 거부 |
| T02 / Core | IClock·UTC/KST 날짜 투영·시작/끝 포함 규칙·기간 겹침·막대 분할 | T01 | 윤일·월/연·자정·exclusive 00:00·3행 초과를 검증. 출력 view model이 원본과 독립 |
| T03 / 로컬 저장 | 계정 별칭·이용권·개인 일정·알림 선택·schema migration·백업 | T01 | 저장 실패 때 이전 정상본/입력 보존. 명시적 프로필 연결. v1 설정 이전 후 재실행 확인 |
| T04 / Plugin 골격 | SDK lock·Dalamud 서비스·명령·WindowSystem·dispose·한/영 리소스 | T02,T03 | KR/글로벌 목표 참조 양쪽 compile. 최소 기능 실제 로드·unload. 자동 게임 행동 없음 |
| T05 / 오프라인 UI | 상단 탭·달력/목록·상세·기간 옵션·계정 이용권 | T04 | 980×660/760×500·DPI·keyboard·Paper/Graphite·선택 단계 유지. 목업은 참고이며 ImGui 재구현 |
| T06 / 전송 캐시 | long-lived HttpClient·전체 timeout·stream limit·allowlist·revision 검증 | T01,T03 | 200/304/404/429/느린 본문/redirect/초과/손상/지원 안 함에서 마지막 정상본 유지 |
| T07 / 수락 상태 | advertised vs active·health freshness·같은 pointer 재시도·원자 cache pointer | T06 | 새 payload 실패→동일 sequence 정상 재시도 성공. 낮은 seq·같은 seq 다른 digest 거부. 디스크 실패 복구 |
| T08 / 비동기 연결 | 전역 feed 수명·UI thread snapshot 적용·캐릭터 generation | T05,T07 | 다운로드 중 캐릭터 전환/unload/화면 닫기에서 예외·중복·이전 개인 상태 오염 없음 |
| T09 / 로컬 알림 | phase·start/end·lead·dedupe·suppression·경계 변경 재예약 | T02,T03,T08 | 제목 수정은 재발송 안 함. 마감 변경은 새 경계. 미정은 대기. 게임 종료 중 알림 없음 안내 |
| T10 / 후보 수집 | source별 목록/상세·정정 후보·304·record별fingerprint·coverage·오류형태 감지 | T00,T01 | 정상 fixture 결과와 오류/빈 HTML을 구분. 내부 record 비교→comparedContentRevision/completeCoverage/영향ID pending. artifact만 생성. 게시 권한 없음 |
| T11 / 검수 흐름 | 원문/이전/새 값/phase/출처hash를 검수 파일에 연결 | T10 | 수기로 동일 경로 등록 가능. 검수 후 원문 변경 시 자동 확정하지 않음. 후보 누락이 취소가 되지 않음 |
| T12 / 게시 | payload immutable·current health inline·seq할당·singlewriter/CAS·rollback | T07,T11 | 부분 실패·충돌·내용 변화 없는 health 갱신·이전 정상 내용 새 revision 게시 재현 |
| T13 / 운영 연결 | schedule/dispatch/job permissions·로그·마지막 수집/검수 상태 | T10,T12 | GITHUB_TOKEN 트리거 함정 회피. 작업 이력과 실패 원인을 볼 수 있고 idle schedule 중단을 발견 |
| T14 / PvP | 시즌·시리즈 시작, 조건형 보상, 검증된 전장 규칙 | T02,T05,T08 | 두 시작일은 독립. 오늘/내일 각각 유효 구간, 패치 경계·기준점 음수 modulo 검증. 미확인은 미확인 |
| T15 / 제한 운영 | 대표 실제 데이터 1주 운영과 KR 실기 | T09,T13,T14 | 누락·정정 시간·검수 시간 측정. 오래된 피드·실기 미지원 기능 노출. 정정/장애 runbook 실행 |
| T16 / 출시 판단 | 패키지·안내·운영 채널·공식 저장소 정책 재확인 | T15 | 검증 ZIP만 배포. API·PvP 정책·AI 사용 공개 등 실제 채널 요구 준수. 사용자가 승인하지 않은 push/게시 없음 |

T00의 외부 수집 관문이 막혀도 T01~T09 오프라인 작업은 진행할 수 있다. 원격 피드는 T07 수락 상태와 T12 게시 양쪽을 통과해야 연결한다. ‘먼저 HTTP를 연결하고 나중에 예외 처리’ 순서로 출시하지 않는다.

## 운영자 역할

- 유지보수자: source registry·파서·workflow 수정, 장애 확인, 새 revision 재게시.
- 검수자: 신규/정정/취소의 실제 적용 범위·시간·출처 확인. 초기에는 동일인이 두 역할을 맡을 수 있지만 검수 기록은 구분한다.
- 최종 사용자: 원격 갱신 선택, 수동 계정 프로필 연결·이용권 확인·개인 알림 선택.

검수 담당자가 정해지지 않으면 ‘운영 가능한 자동 서비스’로 출시하지 않는다. 후보 artifact의 보관 기간과 최대 건수를 정하고 만료 전에 검수 파일로 확정해야 한다. README만 작성해 무인 예약 실행을 방치하지 않는다.

## 검증의 층

1. 계약·계산: 순수 Core 검사. 실제 경계값을 사용하고 구현을 그대로 복제하는 검사는 피한다.
2. importer: 최소 HTML fixture/정정/오류 200/목록 이동. 실제 사이트 전체 mirror를 저장하지 않는다.
3. 전송·저장: 가짜 HTTP handler와 임시 디렉터리로 timeout·지연·초과·원자 교체 실패를 주입한다.
4. Plugin integration: KR/글로벌 참조 compile, 실제 게임에서 열기/전환/unload/DPI/알림 억제 확인.
5. 운영: hosted runner probe·게시 URL 익명 요청·hash·304·cache 전파·되돌림 drill을 실제 수행한다.

현재 완료된 것은 HTML/JS와 예시 데이터의 오프라인 검사다. 실제 브라우저 렌더링·다운로드, 게임 실행, GitHub 운영 drill은 별도다.

## 일정 추정

초기 기존 추정 3~5주는 happy-path 수준이었다. 수집/전송/운영을 포함한 새 추정은 1인 기준 오프라인 기능 1~2주, 전송·검수·게시 2~3주, KR 실기·1주 운영 파일럿 1~2주로 **약 4~7주**다. 병렬화가 가능한 부분은 있으나 정책·외부 차단·배포판 변화는 기간을 늘릴 수 있다. 모델/자동화가 보장하는 납기가 아니다. 첫 관문과 오프라인 milestone을 마친 후 실제 작업량으로 다시 산정한다.

## 확장 순서

P1은 검증된 반복 일정, 종료 알림 세부 설정, JSON/ICS 개인 내보내기, 집사·가방 이용권 다계정 관리다. 각 기능에 source·scope·날짜 경계가 확보돼야 한다. 새 서버를 필요로 하는 계정 동기화·공유 일정·팀 협업, 정액제 자동 판독, LLM 추출은 별도 제품 결정 후 검토한다. 첫 버전의 안정화를 이 기능들에 종속시키지 않는다.
