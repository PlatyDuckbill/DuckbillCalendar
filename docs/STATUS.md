# 현재 구현 상태

갱신: 2026-11-12. 0.1.0.0 개발판.

- 실제96건/9개 출처: 진행 이벤트14, 공지·점검·패치·크컨76, 확장팩·얼리·혜택·시리즈·대회 매핑6.
- 정식 출시2027-02-23(시각 미발표), 얼리2027-02-19 18:00 KST를 별도 마커와 주요 일정 상단에 표시한다.
- 달력 년월 직접 이동·공지게시 기본숨김·분류색, 새소식3열/스크롤 상세, 이용권 잔여일, 일반/고급 설정 분리·자동 저장을 구현했다.
- Core103 + Infrastructure97 + Model32 + native ImGui75 + 소스96 + CLI23 = 426검사, 한영177쌍 통과. 소스96은 Collector18/Auto18/Notice18/Curated17/Paged15/Combined10이다.
- KR·글로벌 API15.0.3.9 참조 빌드 경고0/오류0. 게임 GPU 시각 검증은 재확인 대기.
- [전체 수집·게시 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43202504781) 성공. 실제 Plugin Workspace가 revision4/96건을 수신·해시/바이트 검증·캐시 재시작했다.
- GitHub Actions가2시간마다 갱신하며 실패한 소스는 이전 사건·근거를 보존한다. 명시된 날짜만 사용하고 출처 변경은 재검토로 표시한다.
- 최신 개발 DLL: `src/DuckbillCalendar.Plugin/bin/Debug/DuckbillCalendar.dll`. 예시 모드를 끄고 원격 갱신 토글을 켜면 자동 저장된다.
- 공개 일정은 부분 범위이므로 공개 시간 알림은 보류된다. 개인 일정 알림은 opt-in과 게임 조건을 따른다.
- 전장 오늘/내일의 KR 날짜 기준점은 확인되지 않았다. 임의 verified rule을 만들지 않아 확인 불가 표시를 유지한다. [공식 조사](development/FRONTLINE-SOURCES.md).
- Plugin Release·설치 저장소 게시·게임 자동 로드는 수행하지 않았다.

[전체 소스와 한계](development/ALL-SOURCES.md), [화면 표시](development/SCHEDULE-PRESENTATION.md), [빌드·실행](development/BUILD-AND-RUN.md), [검증](VERIFICATION.md), [10회 구현 기록](development/DEVELOPMENT-LOOPS.md).

최종 [원문·템플릿 연결까지 포함한 자동 수집 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43204681599)도 성공했다. 실제 Workspace가 revision5/96건을 수신해 SHA/bytes 검증과 캐시 재시작을 통과했다.
