# 개발판 빌드와 확인

현재는 실제 Dalamud API15 플러그인과 실제 이벤트 수집·정적 피드를 연결한 개발판이다. 코드와 일정 JSON을 GitHub에 게시하고 runner 수집·실제 플러그인 수신을 확인했다. Plugin Release·설치 저장소 게시·게임 자동 로드는 수행하지 않았다.

## 환경

- .NET SDK 10.0.101 이상(현재 확인 10.0.402), Windows x64.
- 완전한 Dalamud API15 참조 한 벌. 현재 KR·글로벌은 모두 15.0.3.9를 확인했다. 서로 다른 배포판 DLL을 섞지 않는다.
- 현재 코드의 외부 runtime 참조는 Dalamud, 개발 도구의 HTML 파서는 AngleSharp 1.10.1이다. 패키지 lock을 보존한다.

```powershell
# 저장소 루트. 기본은 DALAMUD_HOME 또는 KR 참조 경로를 명시적으로 출력한다.
./scripts/build.ps1
./scripts/check.ps1 -NoBuild

# 글로벌 참조로 컴파일 확인
./scripts/build.ps1 -DalamudPath "$env:APPDATA/XIVLauncher/addon/Hooks/dev"

# KR 참조 Release 빌드·전체 검사·개발 확인용 묶음 생성
./scripts/prepare-dev.ps1 -DalamudPath "$env:APPDATA/XIVLauncherKR/addon/Hooks/dev"
```

`build.ps1`은 빌드만 한다. `check.ps1`은 Core·Infrastructure·실제 CalendarModel·Collector·CLI·리소스 검사와 실제 native ImGui 회귀 검사를 수행한다. PluginModel 검사에서 localization만 대역으로 바꾸고 별도 ImGui 프로젝트에서는 실제 UI 소스·한국어 폰트·native cimgui를 사용한다. GPU·게임은 실행하지 않는다. `prepare-dev.ps1`은 검증된 파일만 묶고 ZIP 안의 필수 파일과 SHA-256을 재확인한다. 이 스크립트들은 git push·게임 설정 변경을 하지 않는다.

## 수동으로 게임에서 확인

빠르게 반복 확인하려면 `./scripts/build.ps1`로 Debug 빌드하고 아래 고정 경로를 개발 플러그인 위치에 등록한다. 이후 같은 명령으로 빌드하면 같은 경로의 DLL과 의존 파일이 갱신된다. 게임에서 플러그인을 다시 로드해 변경 사항을 확인한다.

```text
C:\Users\hyogu\Downloads\DuckbillCalendar\src\DuckbillCalendar.Plugin\bin\Debug\DuckbillCalendar.dll
```

Debug 출력 폴더의 Core·Infrastructure DLL과 `ko` 리소스 폴더도 함께 보존한다. 아래 개발 ZIP은 별도 확인용 복사본이므로 개발 중에는 위 경로를 사용하면 된다.

스크립트가 출력한 개발 묶음의 **전체 폴더를 보존**한다. 주 DLL만 복사하면 Core·Infrastructure 또는 한국어 리소스가 누락될 수 있다. Dalamud 설정의 개발 플러그인 위치에 `DuckbillCalendar.dll`의 절대 경로를 등록하고 개발 플러그인 목록에서 수동 활성화한다. 같은 InternalName의 다른 개발본과 함께 로드하지 않는다.

- `/dbcal`, `/duckbillcalendar`: 주 화면 열기/닫기.
- `/dbcal compact`: 작은 화면.
- `/dbcal settings`: 설정.
- 첫 실행/유효 캐시 없음에는 검토한 실제 공식 이벤트10건의 저장본을 표시한다. 확인 시각과 부분 범위를 달력 위에 표시한다. 예시 모드를 켜 두었다면 ‘실제 일정 보기’로 전환한다. 저장본·기존 파일만으로 최신 원격 health를 승인하지 않는다.
- 이용권/개인 일정을 입력하고 날짜만 아는 경우 ‘날짜만’을 선택한다. 임의의 만료 시각을 만들지 않는다.
- 시작/마감 알림 선택과 설정의 로컬 채팅 알림을 모두 켜야 발송 대상이 된다. 미정·추론·날짜 단위 경계는 시간 알림을 만들지 않는다. 공개 예시는 발송하지 않고 개인 일정은 예시 모드에서도 실제 로컬 입력으로 취급한다.
- 전투·임무·PvP·로딩·영상 중에는 전달을 기다린다. 게임이 닫혀 있으면 발송하지 못한다. PvP 중에는 화면도 숨긴다.
- 발송 기록을 먼저 저장한다. 기록 후 출력 전 게임 종료나 세션 변경이 생기면 누락될 수 있고 이미 기록한 알림은 재시도하지 않는다. 전달은 5건/30초, 대기 20건, 기록 10,000건 상한이다.

## 데이터 도구

```powershell
# 수동으로 저장한 공식 목록 HTML 또는 synthetic fixture → 미검수 후보
dotnet run --project tools/CalendarDataImport -- events data/fixtures/events.synthetic.html .preview/candidates.json

# 검토용 예시는 기본 거부. 아래 옵션은 로컬 예시 확인에만 사용한다.
dotnet run --project tools/CalendarDataPublish -- validate data/contracts/v2/revisions/000001.json --allow-example
dotnet run --project tools/CalendarDataPublish -- prepare data/contracts/v2/revisions/000001.json .preview/prepared --allow-example
```

Importer는 네트워크를 실행하지 않고 파일을 읽는다. 한 페이지의 후보 추출은 전체 소스 수집 성공이나 검수 완료가 아니다. 정상·오류 페이지와 상대 종료를 구분하며 모든 후보는 `canPublish: false`, `completeCoverage: false`다. 공통 배너의 같은 ID 때문에 실제 카드를 놓치지 않는다.

Publish CLI 자체는 **로컬 준비**만 한다. 생산 입력에는 출처 hash와 검증 가능한 v2 내용이 필요하고 예시·draft는 거부한다. payload 먼저, current 마지막으로 쓰며 예전 revision을 덮어쓰지 않는다. CLI 단독 실행은 health를 not-run으로 남긴다. 별도 GitHub workflow가 실제 수집·원문 검증·부분수집 health를 기록하고 calendar-data branch에 일반 push한다. 2시간 자동 갱신과 수동 검토 경로는 [실제 데이터 안내](LIVE-DATA.md)를 따른다.

## 확인해야 할 실기

KR 실제 로드/unload, DPI·폰트·한영 전환·좁은 화면·막대 클릭, 저장 직후 unload와 재실행, 채팅 알림의 전투/임무/로그아웃 억제, 다른 캐릭터 전환, 실제 전장 기준점과 patch 유효 범위가 남았다. 참조 두 벌에서 컴파일된 것을 실제 양쪽 게임 동작 확인으로 보고하지 않는다.
