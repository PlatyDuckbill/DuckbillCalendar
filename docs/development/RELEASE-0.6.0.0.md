# 0.6.0.0 배포 기록

게시: 2026-11-16T08:52:41Z / 2026-11-16 17:52:41 KST. 사용자 지시로 현재 개발 변경을 푸시하고 공개 배포했다. 고정 ZIP의 인게임/외부 캘린더 확인은 대기다.

| 항목 | 값 |
|---|---|
| [공개 Release](https://github.com/PlatyDuckbill/DuckbillCalendar/releases/tag/v0.6.0.0) | ID446791931 · draft=false/prerelease=false |
| 빌드·태그 소스 | `bf3059e02fb5e0ad6dd3072e3e755b5ff4cd938d` |
| 고정 후보 | `artifacts/releases/0.6.0.0/20261116-085014` |
| ZIP | `DuckbillCalendar-0.6.0.0.zip` · 513,755바이트/11파일 |
| ZIP SHA256 | `62e43771841596fdcb23f2e3cc05064f82fd417ea81e7897799980f11c574092` |
| 태그·ZIP 아이콘 SHA256 | `31bd9f2c6613ed8f50e88480bb759ea094ab3a3583029085551a489fe88502b2` |
| 설치 원본 커밋 | `c9e6322` · repo/release.json |
| [공용 동기화](https://github.com/PlatyDuckbill/DuckbillRepo/actions/runs/43835152193) | success · 공용 `43e9069c5eabf3f0d2a8f90637716d5e4532d9e3` |
| 고정 확인 DLL | `.cache/release-validation/0.6.0.0/calendar-files-20261116/DuckbillCalendar.dll` |

개인 일정/이용권 시점의 .ics 교환, JSON 전체 개인 자료·설정 백업/복원과 형식별 버튼, 기본 접힘·같은 폭 정렬을 묶었다. .ics의 반복 규칙/예외 등 미지원 항목은 제외 건수를 표시한다. JSON 복원은 현재 자료를 교체하며 적용 전 보존본을 남긴다. [계약과 대표 확인 순서](CALENDAR-FILES.md).

- KR15.0.3.9 고정 참조 Release 잠금 복원·빌드 경고0/오류0. prepare-release.ps1을 테스트 옵션 없이 실행했다. 자동 테스트는 추가/실행하지 않았다.
- 고정 ZIP11파일·payload 전 파일 해시·BUILD-INFO 소스·DLL/manifest/API/버전, 컴파일된 한국어271문구의 원본 일치, 내장/설치/CHANGELOG4항목 일치를 확인했다. 추출본만 검사하고 새 빌드로 교체하지 않았다.
- 새 태그의 소스와 Release 자산 크기/digest를 확인한 뒤 공개했다. 인증 없는 ZIP·체크섬·태그 아이콘 HTTP200/원본 해시 일치를 확인한 후 설치 JSON을 게시했다. 기존 공개 태그/ZIP은 변경하지 않았다.
- 공용 고정 커밋과 실제 main/pluginmaster.json에서 Calendar0.6.0.0·Tweak0.3.1.0·Fisher0.10.2.0의 각22필드가 원본과 일치한다. DuckbillRepo sync.ps1 -Check도 통과했다. Relic은 원본 미게시로 기존 첫 배포 대기를 유지했다.
- [수집 Actions](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43834634887)는 성공했다. 실제 HTTPS current/payload HTTP200·schema2/KR·revision43/103건·174,200바이트와 SHA256 `4bee883c78825d0c8c2297b63ddf3fb06bf1de6cf32e5b10dd1bb10b48e86694`가 포인터와 일치한다. 플러그인의 실제 수신/게임 로드를 뜻하지 않는다.
- 실제 Dalamud 업데이트/설치, 수정 후 접기/버튼 정렬·파일 선택, 구글 등 외부 .ics 가져오기, JSON 복원·저장/재로드·게임 글꼴/배율 확인은 대기다. 기존 공개판의 미확인을 이번 게시로 완료 처리하지 않는다.

게시 영수증·익명 다운로드·공용 실제 URL·피드 검사 근거는 Fisher 작업 폴더의 `.cache/release-20261116/`에 보존한다.
