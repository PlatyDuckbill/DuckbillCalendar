# 개발판 빌드와 실행

0.3.2.0. .NET10, Windows x64, 완전한 Dalamud API15 참조가 필요하다. 기본 참조는 `%APPDATA%/XIVLauncherKR/addon/Hooks/dev`이며 다른 배포판 DLL과 섞지 않는다.

```powershell
./scripts/build.ps1
./scripts/check.ps1 -NoBuild
./scripts/prepare-dev.ps1
```

`build.ps1`은 빌드, `check.ps1`은 Core/Infrastructure/Model/native UI/수집 도구/CLI/리소스 검사, `prepare-dev.ps1`은 Release 전체 검사 후 필수 DLL과 SHA-256을 확인한 ZIP을 생성한다. git push, 원격 Release, 게임 자동 로드를 수행하지 않는다.

`check.ps1`에는 `check-pipeline.ps1`의 실제 게시→소비자→손상 유지→재시작 통합 검사도 포함한다. 단독 실행은 `./scripts/check-pipeline.ps1 -NoBuild`다. 실제 수집/운영 게시/사용자 갱신은 [흐름과5회 점검](PIPELINE-AUDIT-2026-11-13.md)을 참고한다.

## 개발 중 같은 경로로 다시 로드

`C:/Users/hyogu/Downloads/DuckbillCalendar/src/DuckbillCalendar.Plugin/bin/Debug/DuckbillCalendar.dll`을 개발 플러그인 위치에 등록한다. 빌드 후 게임에서 다시 로드하면 같은 경로의 최신 버전을 사용한다. Core/Infrastructure DLL을 함께 보존한다. 한국어 리소스는 주 DLL에 포함되어 별도 ko 폴더가 필요 없다.

ZIP은 별도 검증용 복사본이다. 다른 경로의 이전 버전과 동시에 로드하지 않는다.

## 사용

- `/dbcal`, `/duckbillcalendar`: 열기/닫기. `/dbcal settings`: 설정.
- 큰 년월을 눌러 직접 이동한다. 이전/다음·오늘·달력 위 휠을 사용할 수 있다.
- 날짜나 표식을 누르면 그날 전체 제목·기간·공식 링크를 팝업으로 본다. 새소식 행은 공식 링크를 바로 연다.
- 표시 메뉴는 실제 일정 종류와 기간 막대만 다룬다. 공지 게시일·부가 서비스 점검은 달력에 표시하지 않는다.
- 이용권은 설정에서 종류·만료일 또는 충전 일수를 입력한다. 현재 만료일에 이어서 충전하며 만료된 값은 오늘부터 계산한다.
- 설정의 일정 색상 및 아이콘에서 사용자 분류를 추가하고 개인 일정에 지정한다. 분류 옆 알림에서 여러 기본 시점을 정하고 일정 메뉴에서 개별 변경·끄기·상속 복원을 선택한다. 날짜만 아는 항목의 시각을 추측하지 않는다. [알림·사용자 분류 안내](NOTIFICATIONS-AND-CATEGORIES.md).
- 전장은 에테르웨이의 한국 날짜별 맵을 수신하며 설정에서 끌 수 있다. 일정 최신 정보 받기와 전장 수신은 새 설치에서 기본 켜짐이며 끌 수 있다. 개인 데이터를 보내지 않는다.
- 전투·임무 중 자동 갱신은 기본 보류한다. 공개 일정의 수동 갱신은 기존 요청 제한 안에서 사용할 수 있다. 창을 자동으로 숨기지 않으며 직접 열고 닫는다.
- 숨김 메뉴의 ‘이 일정만’은 선택 단계의 시작·마감·진행 표시만 숨긴다. ‘관련 일정 모두’는 대상 개수·목록을 확인하고 선택한다. 분류 필터는 변경하지 않는다.
- 종 버튼은 새 공지·이벤트·시간 알림 내역을 연다. 게임 종료 중에는 동작하지 않고 전투·임무·PvP·로딩·영상 중에는 채팅만 기다린다. 채팅은 별도 선택이며 기록 후 출력 전 종료되면 채팅이 누락될 수 있다. 기록된 알림은 재전송하지 않는다.

[10회 화면 재설계](../design/REDESIGN-10-LOOPS.md), [전체 데이터 수집](ALL-SOURCES.md), [전장 수신](FRONTLINE-SOURCES.md).

## 추가 확인

```powershell
dotnet run --project tests/DuckbillCalendar.Infrastructure.Checks -- --frontline-live
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview 1060 720 midnight
dotnet run --project tests/DuckbillCalendar.ImGui.Checks -- --preview 1320 1050 cloud
```

전장 실수신과 오프라인 fixture 검사는 별개다. Preview는 실제 DrawData의 CPU 렌더이며 게임 GPU 화면을 대체하지 않는다. 실제 로드/unload·게임 DPI/폰트·임무 찾기의 전장과 대조·채팅 전달은 인게임 재확인이 필요하다.

0.3.2 배포 후보의 기능·검사·인게임 인수 항목과 되돌리기는 [최종 보고](RELEASE-READINESS-2026-11-14.md)를 따른다. 개발 ZIP은 자동으로 게임에 로드하거나 원격으로 게시하지 않는다.

정식 배포: [설치/공개 검증 기록](RELEASE-0.3.2.0.md), [다음 배포 절차](PUBLISHING.md).
