# 0.5.0.0 배포 기록

게시: 2026-11-15T10:43:06Z / 2026-11-15 19:43:06 KST. 사용자가 최종 아이콘으로 세 플러그인 배포를 명시 요청했다. 고정 ZIP의 인게임 확인은 하지 않았다.

| 항목 | 값 |
|---|---|
| [공개 Release](https://github.com/PlatyDuckbill/DuckbillCalendar/releases/tag/v0.5.0.0) | ID445982445 · draft=false/prerelease=false |
| 빌드·태그 소스 | `b5f6142068062edc5dc5eaf4c978076d60ade32a` |
| 고정 후보 | `artifacts/releases/0.5.0.0/20261115-103646` |
| ZIP | `DuckbillCalendar-0.5.0.0.zip` · 499,186바이트/11파일 |
| ZIP SHA256 | `7e79bd906a23fbb0e8dd96d75e0675e777879f0700eadf3357544df8e786fbdd` |
| 태그·ZIP 아이콘 SHA256 | `31bd9f2c6613ed8f50e88480bb759ea094ab3a3583029085551a489fe88502b2` |
| 설치 원본 커밋 | `01fa7c9` · repo/release.json |
| [공용 동기화](https://github.com/PlatyDuckbill/DuckbillRepo/actions/runs/43705600164) | success · 공용 `5cb4a754332c0b3ca7bba19edc4e27ba4a4cc679` |

스모크/펄·기본 접힌 실시간 화면 조절·별도 업데이트 내역, 날짜 상세의 한 번 클릭 전환과 제목줄 접기/닫기 겹침 수정, 새 대표 아이콘을 묶었다. 현재 월/선택·개인 일정·이용권과 표시 설정은 유지한다.

- KR API15 Release 빌드 경고0/오류0. 잠금 복원·244개 한국어 리소스/형식·실제 DLL의 내장 피드/아이콘3개·컴파일된 문자열 일치, DLL/manifest/API/ZIP·payload 전 파일 해시/소스 커밋과 내장/설치/GitHub 노트7항목을 확인했다. ZIP의 images/icon.png도 확정 PNG와 일치한다.
- 준비 스크립트는 테스트를 기본 실행하지 않고 `-RunTests` 명시 때만 실행하도록 분리했다. 이번 배포는 테스트 추가/실행 없이 빌드·리소스·산출물 검사만 수행했다.
- 새 태그/Release 자산 digest/크기를 확인한 뒤 공개했다. 인증 없는 ZIP·체크섬·태그 아이콘 HTTP200/원본 해시 일치를 확인한 뒤 설치 JSON을 게시했다. 기존 공개 태그와 ZIP은 변경하지 않았다.
- 고정 공용 커밋과 실제 공유 main/pluginmaster.json의22필드가 설치 후보와 일치한다. Fisher0.10.0.1/Tweak0.3.0.0을 포함한 기존 세 항목을 유지했다.
- 실제 Dalamud 업데이트/로드·관리 폰트, 날짜A→B 한 번 클릭과 팝업 경계, 설정 드래그/저장·읽음 재로드는 대기다. 빌드/다운로드 확인을 인게임 결과로 간주하지 않는다.

게시 영수증은 Fisher 작업 폴더의 `.cache/release-suite-2026-11-15/`, 추출본은 `.cache/release-validation/0.5.0.0-context-release-20261115/`에 보존한다.
