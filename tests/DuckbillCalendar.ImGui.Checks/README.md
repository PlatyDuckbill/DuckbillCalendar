# Native UI 검증

0.2는 실제 MainWindow·CalendarModel·ThemeScope·한국어 리소스와 설치된 API15 native ImGui를 사용한다. 게임 프로세스나 GPU를 실행하지 않는다.

```powershell
dotnet run --project tests/DuckbillCalendar.ImGui.Checks
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview 1060 720 midnight
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview 1320 1050 cloud
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview 620 480 midnight 2
```

571개 검사는3테마×3크기×3배율, 도구 줄바꿈·달력 면적·빈 상세 제거·새소식 너비·설정 내 이용권, 실제 날짜/표식 클릭, 날짜 팝업, 휠 방향/분수/영역/보조키/팝업/연도 경계,6주 월, native 미니 달력 날짜 선택/시각 입력, 이름 없는90일 등록과 잘못된 일수를 확인한다. 넓은 도구 한 줄 배치, 색상·아이콘 편집 폭, 분류 스타일 일치·기본값 복원, 오늘/내일 요약과 전장 날짜 칸 선택의 독립을 추가했다. 사용자 분류 생성·개인 일정 지정·분류/개별 알림 시점 저장·250개 일정의12건 페이지 조회와 작은 창 저장 버튼을 검사한다.

PNG는 `.preview/native-visual/`에 월·출시월·날짜 팝업·새소식·설정·등록 후 설정·충전·개인 일정 폼으로 저장한다. 실제 DrawData를 clip/UV/indexed triangle/alpha/font atlas/embedded minion texture 기준으로 CPU rasterize한다. HTML 목업이나 별도로 그린 디자인이 아니다. 에테르웨이의 실제 응답을 고정한 전장 fixture와 실제 공식96건 번들을 사용한다.

게임 GPU sampling, 실제 게임 글꼴/fallback·DPI, Dalamud 통합과 플레이어 사용성 과제는 별도 확인이 필요하다. 통과한 geometry 검사를 실기 검증으로 보고하지 않는다. [10회 재설계 기록](../../docs/design/REDESIGN-10-LOOPS.md).

0.2.4 추가: 실제 CCRC 메뉴에서 단계만/관련 전체 숨김을 각각 클릭하고 다른 단계와 분류 체크의 보존을 확인한다. 0.2.5에서는 자동 창 숨김을 제거하고 해당 검사를 삭제했다. 충전 후 만료 시각의 native 입력·저장·잘못된 시각 거부를 각 배율에서 확인한다.

0.2.6: 일·시간 합산과 시간만 충전, 시각 미등록 이용권의 기존 시각 native 입력을 검사한다. 남은 시간·오늘 이미 만료됨·날짜만 저장됨의 구분을 확인한다. 마감/만료 배지는 실제 DrawData로 시각 검토한다.

0.2.7: 분 수량 native 입력·분만 충전·일/시간/분 합산, 초기화 확인/취소의 실제 클릭과 대상 한정 제거를 확인한다.

0.2.8: 접지 않아도8분류가 표시되는지 확인하고, 실제 아이콘 선택기에서 항목을 클릭해 적용/색상 보존을 검사한다. 설정과 아이콘 목록의 실제 DrawData를 확인한다.

0.3.0: 사용자 분류·알림 설정·개별 예외 목록·내역의 실제 DrawData를 추가 렌더링한다. 분류 생성과 여러 시점/개별 예외 저장을 실제 클릭하고 1/1.5/2배율에서 확인한다. 작은 창의 본문은 스크롤하고 저장/취소는 하단에 유지한다.

0.3.2: 컴팩트한 카드 상단 수정/숨기기/삭제, 독립 창 유지, 반복 요일/시각 입력과 실제 등록, 원래 순서 기준 이후 삭제, 작은 창 탭과 고정 하단 버튼, 매년5월9일 표시 및 실제 이미지 리소스를 확인한다. --inspect-plugin DLL_PATH로 실제 배포 DLL의0.3.2.0버전·아이콘 SHA256·피드 리소스2개를 별도 검사한다.

최초 배포 내역: 설정의 실제 버튼 → 내역 페이지 → 설정 복귀를1/1.5/2배율에서 클릭한다. 작은620×480 창의 본문 스크롤/복귀 버튼과 읽는 동안 로컬 상태 불변을 검사한다. 내역은 최초 배포 한 건만 시작하며 실제 DLL/manifest/내역 버전 일치 및 manifest Changelog도 검사한다.
