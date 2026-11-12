# 개발 구현 루프 기록

시작 2026-11-11, 이어서 2026-11-12. 사용자가 허용한 **10회 구현 루프를 완료**했다. 설계 검토 3회와 별도이며 실제 발견·수정·검증 단위다. 인게임 확인과 공개 배포는 포함하지 않는다.

| 회차 | 발견 | 구현·개선 | 검증 결과 |
| --- | --- | --- | --- |
| 01 | 문서만 있는 프로젝트. 게임·계산·IO 경계 필요 | .NET10 Core/Infrastructure/API15 Plugin, v2 DTO·오프라인 골격·Import/Publish CLI 생성 | 완료: 개별 빌드. 공용 IO를 게임 없이 검사 가능 |
| 02 | 중복 JSON·누락 metadata·경계 혼합·예시 공개 알림·프로필 범위 누락 | duplicate/required 검사, Boundary converter, 계정별 알림, 공개 예시 차단 | 완료: Core 검사. fixture 기대 오류와 제품 결함 구분 |
| 03 | 공통 배너가 같은 ID의 실제 이벤트 카드를 선점 | 기간 카드만 추출, 같은 중복 무시·내용 충돌 경고, phase/본문·출력 상한 | 완료: 수동 저장 공식 페이지 1장에서 10카드·경고0. synthetic 참여/수령·조건 종료 확인 |
| 04 | 손상 캐시·partial download·같은 publication 재시도·unload 저장·동의 철회 | active/advertised·replay watermark, cache 복구, 본문 deadline/상한, 저장 drain·원격 취소 분리 | 완료: fake HTTP·격리 디스크 검사. fallback에서도 최신 watermark 보존 |
| 05 | entrypoint·리소스 누락, UI stack·초기화 rollback·선택 초점 | 주/작은 화면, 4탭·기간 막대·CRUD·date-only·stable IDs, 안전한 텍스트·한영 리소스 | 완료: KR 빌드·ResourceManager·리소스 검사. 렌더링 실기 대기 |
| 06 | 솔루션·참조별 재현 명령·의존성 인계 없음 | 8프로젝트 솔루션, 명시적 참조 검사·빌드/검사 스크립트, KR/글로벌 compile | 완료: 두 API15.0.3.9 참조에서 경고0·오류0 |
| 07 | 이중 hourly timer가 15분 복구를 늦춤. 마지막 프레임 편집이 model 큐에 남음 | 주기는 Workspace 단일 소유·UI 1분 점검. unload 전 pending action을 durable 큐로 이전 | 완료: 실제 CalendarModel19검사. localization만 대역, ImGui 미실행 |
| 08 | 로컬 알림 중복·저장 실패·전투 지연·세션/선택/경계 변경 | 기본 off opt-in·durable-before-delivery·게임 조건 억제·freshness 재검사·상한 | 완료: Infrastructure82검사 중 알림28개. 실제 게임 전달 대기 |
| 09 | 비 Gregorian 날짜·명시적 null·수기 파일 BOM·CLI 덮어쓰기 | invariant 시각, nullable 검사·BOM parsing·경계 왕복, bounded 입력·예시 기본 거부·불변 local prepare | 완료: Core102 + CLI23. health는 not-run 유지 |
| 10 | 의존 DLL·ko satellite 누락 가능, 문서가 기획 상태로 남음. 수동 파일이 같은 revision의 원격 health를 재사용 | UI 적용 snapshot의 출처·해시·revision으로 health 격리. KR Release 전체 검사, 필수6파일 ZIP·archive hash 재검사·실행 인계 | 완료: 233검사 + 한영136쌍. 원격 게시·게임 자동 로드 없음 |

233개는 Core102 + Infrastructure89 + 실제 CalendarModel19 + CLI23이다. 한영136쌍은 별도 검사다. 10회차 최종 점검에서 원격 health 결합·수동 파일 격리 회귀7개를 추가했다. 실패 주입은 가짜 HTTP와 자기 소유 임시 파일에만 수행했다. 초기 실패한 fixture와 비동기 settle 기대를 고친 것을 실제 제품 결함과 구분했다.

## 첫 개발판과 한계

실제 UI, 이용권·개인 일정, 기간·목록, 알림 선택과 opt-in 로컬 전달, 파일 가져오기·명시적 예시, HTTPS·캐시와 로컬 데이터 도구를 구현했다. 기록 후 출력 전 종료되면 알림이 누락될 수 있고 기록된 알림은 재전송하지 않는다. unload 저장 큐는 drain하지만 프로세스 강제 종료나 디스크 고장까지 보장하지 않는다.

## 남은 작업

KR 실제 로드/unload·폰트/DPI·UI 클릭·채팅 출력과 억제, 전장 기준점, 수집 정책·runner·검수 운영, 공지/패치/정정·전체 paging coverage, 승인 provenance·분산 CAS·예약 게시, 조건 참조 자동 날짜 해석은 남았다. 자동 수집·공식 배포를 완료했다고 보고하지 않는다. 다음 확인은 [실행 안내](BUILD-AND-RUN.md)의 대표 실기다.
