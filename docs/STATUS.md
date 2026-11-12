# 현재 구현 상태

갱신: 2026-11-13. 0.1.0.0 개발판.

- 실제96건/9개 출처: 진행 이벤트14, 공지·점검·패치·크컨76, 확장팩·얼리·혜택·시리즈·대회 매핑6.
- 정식 출시2027-02-23(시각 미발표), 얼리2027-02-19 18:00 KST를 별도 마커로 표시한다. 다른 달에서는 출시 바로가기로 이동한다.
- 달력 년월 직접 이동·공지게시 기본숨김·분류색, 새소식3열/스크롤 상세, 이용권 잔여일, 일반/고급 설정 분리·자동 저장을 구현했다.
- Core103 + Infrastructure97 + Model61 + native ImGui117 + 소스96 + CLI23 = 497검사, 한영221쌍 통과. 소스96은 Collector18/Auto18/Notice18/Curated17/Paged15/Combined10이다.
- KR·글로벌 API15.0.3.9 참조 빌드 경고0/오류0. 게임 GPU 시각 검증은 재확인 대기.
- [전체 수집·게시 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43202504781) 성공. 실제 Plugin Workspace가 revision4/96건을 수신·해시/바이트 검증·캐시 재시작했다.
- GitHub Actions가2시간마다 갱신하며 실패한 소스는 이전 사건·근거를 보존한다. 명시된 날짜만 사용하고 출처 변경은 재검토로 표시한다.
- 최신 개발 DLL: `src/DuckbillCalendar.Plugin/bin/Debug/DuckbillCalendar.dll`. 예시 모드를 끄고 원격 갱신 토글을 켜면 자동 저장된다.
- 공개 일정은 부분 범위이므로 공개 시간 알림은 보류된다. 개인 일정 알림은 opt-in과 게임 조건을 따른다.
- 전장 오늘/내일의 KR 날짜 기준점은 확인되지 않았다. 임의 verified rule을 만들지 않아 확인 불가 표시를 유지한다. [공식 조사](development/FRONTLINE-SOURCES.md).
- Plugin Release·설치 저장소 게시·게임 자동 로드는 수행하지 않았다.

[전체 소스와 한계](development/ALL-SOURCES.md), [화면 표시](development/SCHEDULE-PRESENTATION.md), [빌드·실행](development/BUILD-AND-RUN.md), [검증](VERIFICATION.md), [10회 구현 기록](development/DEVELOPMENT-LOOPS.md).

최종 [원문·템플릿 연결까지 포함한 자동 수집 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43204681599)도 성공했다. 실제 Workspace가 revision5/96건을 수신해 SHA/bytes 검증과 캐시 재시작을 통과했다.

2026-11-13: [종류별 체크박스](development/CALENDAR-VISIBILITY.md), 시작·종료 기본 표시와 명시적 기간 막대 옵션을 적용했다. 이전 기본-on 설정은 한 번 전환하며 새 선택은 유지한다.

2026-11-13: [마우스 휠 월 이동](development/MOUSE-WHEEL.md)을 추가했다. 날짜 격자 위 휠↑ 이전 달/↓ 다음 달이며 목록·상세·팝업·보조키 입력과 분리했다.

2026-11-13: [제품·UX 검토](design/PRODUCT-UX-REVIEW.md)와 [상호작용 설계](design/INTERACTION-DESIGN.md)에 따라 월·날짜·단계 맥락을 분리했다. 해당 월 경계 목록, 날짜별 전체 목록, 명시적 단계 상세와 뒤로 가기, 짧은 마커, 표시 체크박스 팝오버, 기한 미정 탐색을 구현했다. 별도 작은 창·명령·버튼은 제거했다. 좁은 창은 달력과 상세를 같은 본문에서 전환하며 월 목록은 상단 보기에서 연다. 실제 DrawData의 CPU 렌더링으로980×660·1280×800·780×660을 시각 검토했다. 게임 GPU 확인은 별도다.
