# 현재 구현 상태

갱신: 2026-11-12. **0.1.0.0 개발판, 10회 구현 후 실기 제보 수정과 실제 이벤트 연결.**

- .NET10 Core / Infrastructure / Dalamud API15 Plugin / 오프라인 Import / 로컬 Publish를 구현했다.
- 4개 상단 탭·기간 막대/경계·목록·일정/이용권 CRUD·date-only·작은 화면·한영/테마를 구현했다.
- opt-in 로컬 채팅 알림은 ledger 저장 후 출력·게임 조건 억제·세션/선택/경계 재검사로 연결했다. 실제 전달은 실기 대기다.
- KR·글로벌 API15.0.3.9 Debug, KR Release: 경고0/오류0.
- 툴팁 세로 줄바꿈·편집 팝업의 지속적인 가로 확장을 수정하고 실제 native ImGui로 기존 버그 재현/수정 회귀를 확인했다.
- 공식 이벤트 첫 페이지 실제10건을 수집·대조한 저장본을 포함한다. 예시 모드에서는 ‘실제 일정 보기’로 전환한다.
- Core103 + Infrastructure97 + 실제 Model19 + native ImGui30 + Collector18 + Auto18 + CLI23 = **308검사**, 한영138쌍 통과.
- 수동 가져오기 데이터는 이전 원격 health를 재사용하지 않는다. UI에 적용된 원격 파일의 출처·해시·revision을 함께 확인한다.
- 최신 개발 DLL: `src/DuckbillCalendar.Plugin/bin/Debug/DuckbillCalendar.dll`. 이전10회차 ZIP보다 최신이다.
- main 코드와 calendar-data의 실제 일정 JSON을 GitHub에 게시했다. [첫 runner 수집](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43197397575)이 성공했고 실제 Plugin Workspace로 HTTPS 수신·payload SHA/bytes·캐시 재시작을 확인했다.
- Plugin Release·설치 저장소 게시·게임 자동 로드·인게임 검증은 수행하지 않았다.
- [실기 후 수정](development/POST-PLAYTEST.md), [실제 수집](development/LIVE-DATA.md), [개발 루프](development/DEVELOPMENT-LOOPS.md), [빌드·실행](development/BUILD-AND-RUN.md), [검증](VERIFICATION.md).

다음 확인은 **KR 실제 게임에서 고정 Debug DLL 다시 로드와 툴팁·편집창 확인**이다. 원격 갱신은 설정에서 켤 수 있다. 실제 수집 범위는 공식 이벤트 목록 첫 페이지이며 공지·패치·시즌 전체를 포함하지 않는다.

[자동 수집·검증·게시 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43198381662) 전체 성공. 2시간 간격으로 공식 목록의 명시적 time 필드·표시 날짜를 검증해 갱신한다. 삭제·취소 추정, 미정 날짜 추정, 사람 검수 주장은 하지 않는다. 자동 게시된 revision2/실제10건을 Plugin Workspace로 다시 수신하고 SHA/bytes·캐시 재시작을 확인했다.

구형 Actions runtime 경고를 확인해 공식 최신 release의 commit SHA와 Ubuntu24.04로 고정했다. [고정 환경에서도 자동 게시 성공](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43198723387)을 확인했다.
