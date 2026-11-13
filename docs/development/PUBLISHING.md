# 정식 배포와 공용 설치 목록

사용자 승인 후 검증한 현재 소스를 커밋하고 `scripts/prepare-release.ps1`을 실행한다. 이 스크립트는 깨끗한 소스 커밋에서 Release 빌드와 전체 검사, 실제 DLL/manifest/업데이트 내역 버전, 내장 리소스를 확인한다. 게임을 자동으로 재로드하거나 원격에 게시하지 않는다.

1. `artifacts/releases/CURRENT.txt`가 가리키는 폴더에서 `DuckbillCalendar-<버전>.zip`, `.zip.sha256`, 단일 객체 `release.json`을 확인한다. ZIP 내부 `BUILD-INFO.json`의 커밋이 태그의 소스 커밋과 같아야 한다.
2. 해당 커밋을 `main`에 정상 push하고 `v<버전>` 태그를 만든다. 기존 태그나 공개 자산을 강제로 덮어쓰지 않는다.
3. 같은 태그의 GitHub Release 초안에 ZIP·SHA256을 올린다. 업로드 크기·digest를 확인한 후 정식 공개한다.
4. 공개 URL로 ZIP·SHA256을 다시 받아 바이트 해시, ZIP 내부 JSON/DLL 버전·필수 DLL·아이콘을 검증한다. 태그의 대표 아이콘도 인증 없이 받아 로컬 해시와 대조한다.
5. 검증 후에만 준비한 단일 객체를 `main`의 `repo/release.json`에 반영한다. 개별 `repo/pluginmaster.json`을 만들지 않는다.
6. DuckbillRepo의 `sources.json`에 등록하고 그 저장소의 `scripts/sync.ps1`, `-Check`로 공개 자료를 검증한다. 성공한 목록·출처 잠금 파일과 함께 게시한다.
7. DuckbillRepo의 Actions 성공과 실제 공개 `pluginmaster.json`의 버전·다운로드·아이콘을 확인한다. 캘린더 수집 Actions와 `calendar-data`의 최신 수신 계약도 확인한다.

공개 ZIP/목록 검증, Actions 성공, 실제 게임 로드·글꼴/DPI·채팅 전달은 별도 검증으로 기록한다. 배포 시각·소스 커밋·자산 해시·Actions 링크는 배포 기록에 남긴다. 인증 정보·ZIP·DLL·개인 로컬 상태는 커밋하지 않는다.
