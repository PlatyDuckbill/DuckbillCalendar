# 실기 제보 후 수정과 실제 데이터 연결

2026-11-12 사용자 스크린샷을 기준으로 두 UI 결함을 수정했다.

- 툴팁: 자동 크기 창에서 wrap position 0을 사용해 한국어가 한 글자씩 세로로 표시됐다. 폰트 기준 28자 폭의 양수 wrap position으로 측정한다.
- 일정 편집: AlwaysAutoResize 창에서 전체 폭 입력칸과 오른쪽 라벨이 서로 폭을 늘렸다. 창 폭을 폰트·viewport에 맞춰 매 프레임 제한하고 라벨을 위로 옮겼다. 높이는 자동 조절하되 화면 높이를 넘지 않도록 제한한다.
- 날짜·시각 입력 버퍼는 종료 문자를 포함해 11/6칸으로 잡아 2026-11-12와 23:59 전체를 입력한다.

설치된 실제 cimgui.dll과 맑은 고딕으로 1/1.5/2배율마다 기존 버그 negative control, 100프레임 너비 안정성, 한국어 툴팁 폭·높이, 실제 문자 입력, 실제 이벤트를 넣은 전체 UI 10프레임을 확인했다. native ImGui30검사 통과. GPU와 실제 게임 시각 검증은 별도다.

실제 공식 이벤트 첫 페이지10건을 HTTP로 수집하고 제목·기간을 대조해 플러그인에 포함했다. 최초 실행/유효 캐시 없음에 저장본을 표시하고 기존 수동 파일·원격 캐시를 덮어쓰지 않는다. 예시 모드는 ‘실제 일정 보기’로 해제한다. 저장본의 확인 시각·부분 범위를 표시하고 원격 freshness를 위조하지 않는다. 종료 미정7건은 임의 마감일을 생성하지 않는다.

Core103 + Infrastructure97 + 실제 Model19 + native ImGui30 + Collector18 + Auto18 + CLI23 = **308검사**, 한영138쌍을 확인했다. Infrastructure에는 번들 시작/예시 전환/수동 파일 우선/네트워크 없음/예시 번들 거부8검사를 추가했다.

개발 확인 경로는 `src/DuckbillCalendar.Plugin/bin/Debug/DuckbillCalendar.dll`이다. [실행 안내](BUILD-AND-RUN.md), [실제 수집·갱신](LIVE-DATA.md)을 따른다. 운영 원격 게시 상태는 [현재 상태](../STATUS.md)에 기록한다.

원격 자동 운영도 실제 확인했다. GitHub Actions가 공식 목록을 2시간 간격으로 수집하고 엄격한 결정적 규칙으로 기간을 검증한 뒤 calendar-data branch에 게시한다. 실패 시 이전 feed를 유지하고 첫 페이지에서 빠진 사건의 날짜·근거를 보존한다. 자동 경로는 사람/LLM 검수와 구별하며 health는 partial/coverage false다. 자동 게시 revision2를 실제 Workspace가 수신하고 캐시를 복원했다. Windows/Linux JSON 재생성 차이를 막기 위해 NewLine을 LF로 고정하고 Core 회귀 검사를 추가했다.
