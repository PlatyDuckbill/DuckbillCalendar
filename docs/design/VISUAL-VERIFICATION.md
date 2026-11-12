# Native UI 시각 검증

실제 Plugin UI가 만드는 ImGui DrawData를 CPU로 rasterize해 PNG로 저장한다. HTML 목업이나 별도로 그린 디자인 이미지가 아니다. 게임 프로세스·GPU·네트워크·사용자 게임 설정을 사용하지 않는다.

`tests/DuckbillCalendar.ImGui.Checks/NativePreview.cs`의 `NativePreview.Run(width, height)`는 실제 96건 번들·MainWindow·CalendarModel·ThemeScope·UiText를 사용한다. 2027년 2월의 월 맥락, 2월 19일 날짜 맥락, 얼리 액세스 개별 일정 맥락, 진행·기한 미정 보기를 각각 6프레임 측정한 뒤 PNG를 만든다.

```powershell
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview 1280 800
```

결과는 `.preview/native-visual/<실행시각-식별자>/`에 `month-980x660.png`, `day-980x660.png`, `event-980x660.png`, `undated-980x660.png`와 draw count를 기록한 text 파일로 남는다. 다른 크기는 `NativePreview.Run(1280, 800)`처럼 호출할 수 있다. 출력·원문 폰트·native DLL은 Git에 넣지 않는다.

Renderer는 native clip rectangle, vertex position/UV/color, indexed triangle, alpha blending 및 여러 font texture를 적용한다. 현재 한국어 글꼴 범위는 2개의 atlas에 나뉘므로 각 atlas에 고유 texture ID를 등록하고 command별 atlas를 선택해야 한다. 두 atlas를 같은 ID로 처리하면 글자가 깨지므로 단일 atlas 가정을 하지 않는다.

맑은 고딕 17px와 software nearest-neighbor sampling을 사용한다. 게임의 실제 글꼴·font fallback·GPU sampling·OS/Dalamud scaling·게임 배경과 합성한 결과를 보증하지 않는다. 이 프리뷰는 정보 위계, 날짜 맥락, 줄바꿈·잘림, 여백과 상태 전환을 사람이 직접 검사하는 보조 근거다. 실제 게임 확인을 대체하지 않는다.

지원하지 않는 별도 이미지 texture나 native draw callback이 나타나면 임의로 생략하지 않고 실패시킨다. Preview가 공식 이벤트 소스를 다시 수집하거나 개인 프로필·게임 캐릭터 정보를 읽는 일은 없다.

2026-11-13 검토 산출물: `.preview/native-visual/20261112-155406-35cddc/`(980×660), `20261112-155408-273b5f/`(1280×800), `20261112-155410-745e39/`(780×660). 파일 이름의 시각은 UTC다. 월/날짜/상세의 정보 맥락과 미정 경계 표현을 검사했고, 좁은 월 화면의 중복 agenda 제거와 마지막 주/시각 잘림 수정이 반영됐다. 이후 날짜 전체 클릭 대상 개선은 화면 좌표를 바꾸지 않으며 별도 native 입력 검사6개로 검증했다.
