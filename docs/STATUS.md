# 현재 구현 상태

갱신: 2026-11-12. **0.1.0.0 개발판, 10회 구현 후 실기 제보 수정과 실제 이벤트 연결.**

- .NET10 Core / Infrastructure / Dalamud API15 Plugin / 오프라인 Import / 로컬 Publish를 구현했다.
- 4개 상단 탭·기간 막대/경계·목록·일정/이용권 CRUD·date-only·작은 화면·한영/테마를 구현했다.
- opt-in 로컬 채팅 알림은 ledger 저장 후 출력·게임 조건 억제·세션/선택/경계 재검사로 연결했다. 실제 전달은 실기 대기다.
- KR·글로벌 API15.0.3.9 Debug, KR Release: 경고0/오류0.
- 툴팁 세로 줄바꿈·편집 팝업의 지속적인 가로 확장을 수정하고 실제 native ImGui로 기존 버그 재현/수정 회귀를 확인했다.
- 공식 이벤트 첫 페이지 실제10건을 수집·대조한 저장본을 포함한다. 예시 모드에서는 ‘실제 일정 보기’로 전환한다.
- Core102 + Infrastructure97 + 실제 Model19 + native ImGui30 + Collector18 + CLI23 = **289검사**, 한영138쌍 통과.
- 수동 가져오기 데이터는 이전 원격 health를 재사용하지 않는다. UI에 적용된 원격 파일의 출처·해시·revision을 함께 확인한다.
- 최신 개발 DLL: `src/DuckbillCalendar.Plugin/bin/Debug/DuckbillCalendar.dll`. 이전10회차 ZIP보다 최신이다.
- 운영 피드·예약 수집·원격 게시·게임 자동 로드·인게임 검증은 수행하지 않았다.
- [실기 후 수정](development/POST-PLAYTEST.md), [실제 수집](development/LIVE-DATA.md), [개발 루프](development/DEVELOPMENT-LOOPS.md), [빌드·실행](development/BUILD-AND-RUN.md), [검증](VERIFICATION.md).

다음 확인은 **KR 실제 게임에서 고정 Debug DLL 다시 로드와 툴팁·편집창 확인**이다. 실제 수집기·수집/게시 workflow는 구현했으나 원격 활성화와 전체 수집 범위는 별도다. 미게시 URL의 다운로드 코드를 운영 서비스 완료로 해석하지 않는다.
