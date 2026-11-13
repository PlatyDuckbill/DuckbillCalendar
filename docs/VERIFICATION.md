# 검증 기록

## 2026-11-14 · 최초 배포 업데이트 내역

현재0.3.2.0의 최초 배포 한 건을 내장했다. 설정 진입/설정 복귀, 작은 창 스크롤과 버튼, 상태를 바꾸지 않는 오프라인 읽기를 native UI에서 검사했다. Core291 / Infrastructure203 / Model151 / Native571 / 수집117 / CLI30 = **1363개 검사**. 실제 DLL·manifest·내역 버전과 설치 화면의 최초 배포 요약을 대조한다. 게임 GPU·게임 글꼴/DPI의 최종 인수는 별도다.


## 2026-11-14 · 0.3.2 배포 후보 5회 검토

Core291 / Infrastructure203 / Model151 / Native561 / 수집117 / CLI30 = **1353개 검사**, 한국어 리소스222개, 게시/소비자/손상/재시작 smoke 및 실제 DLL 버전·아이콘 SHA256·번들 피드 검사. Debug/Release 빌드 경고/오류0. 반복 등록·범위 삭제·프로필 경합·삭제 중 편집·취소와 독립 창·세 테마/1~2배율·안전한 이벤트 상세 링크·매년5월9일을 검증했다. [5회 수정 내용·예외 행렬·인게임 인수·되돌리기](development/RELEASE-READINESS-2026-11-14.md). 실제 게임 GPU/글꼴/DPI/채팅은 별도 인수 대상이며 원격 배포하지 않았다.

## 2026-11-13 · 0.3.0 알림·사용자 분류

Core245 / Infrastructure201 / Model121 / Native462 / 수집117 / CLI30 =1176개 검사, 리소스222개와 파이프라인 smoke. 분류별 여러 시점·일정별 변경/끄기/상속 복원·기존 선택 보존, 새 글 기준/중복/비활성/대량 요약, 공개 근거 신선도, 저장 선행·채팅 대기·실패/재시작·이전 형식 백업을 검사했다. Native UI에서 분류 생성·개인 일정 지정·분류/개별 시점 저장과250개 일정의 페이지 조회를 실제 실행했다. 차콜 일반 창과 미드나이트620×480/2배율의 실제 DrawData를 시각 검토했다. 게임 GPU·실제 채팅은 별도 확인 대상이다. [범위·사용법·아키텍처](development/NOTIFICATIONS-AND-CATEGORIES.md).

## 2026-11-13 · 0.2.8 설정 정리·아이콘 미리보기

Core216 / Infrastructure185 / Model105 / Native441 / 수집117 / CLI30 =1094개 검사, 리소스222개와 파이프라인 smoke. 색상/아이콘은 기본 펼침과 반응형2열이며 아이콘 선택기/목록 모두 실제 모양을 표시한다. Native 클릭으로 선택 적용·색상 보존을 확인했다. 전장 수동 갱신 버튼 제거, 기존 개인/이용권 알림 선택을 설정으로 이동, 비활성 알림 재선택/기존 시점 보존을 검증했다. [반영 범위와 알림 추천안](development/SETTINGS-REFINEMENT.md).

## 2026-11-13 · 0.2.7 분 단위 충전·등록 초기화

Core216 / Infrastructure185 / Model103 / Native435 / 수집117 / CLI30 =1086개 검사, 리소스222개와 게시/소비자/손상/재시작 smoke. 분만 충전·3개 기간 합산·자정 경계·기준 시각 미등록, 초기화 확인/취소·다른 자료 보존·반복 초기화·재시작 보존을 확인했다. 초기화 팝업 폭과 실제 native 버튼 클릭도 확인했다. [동작과 범위](development/MINUTE-RENEWAL-AND-RESET.md).

## 2026-11-13 · 0.2.6 시간 단위 충전·마감 배지·요청 대조

‘시간 단위 충전’을 만료 시각 지정으로 잘못 구현했던 부분을 바로잡았다. 일·시간 합산/시간만 충전, 날짜만 있는 기존 만료일의 기준 시각 입력, 잔여 시간·오늘 이미 만료됨 표시를 추가했다. 제목의 `[마감]`은 별도 주황 배지, 만료는 붉은 배지, 좁은 칸은 경고 표식으로 바꿨다. Core208 / Infrastructure185 / Model95 / Native417 / 수집117 / CLI30 =1052개 검사와 리소스222개 및 파이프라인 smoke. [전체 요청 대조·실제 미완료 범위](development/REQUEST-AUDIT-2026-11-13.md).

## 2026-11-13 · 0.2.5 설정·충전·마감 표시

Core200 / Infrastructure185 / Model95 / Native396 / 수집117 / CLI30 =1023개 검사와 한국어 리소스222개, 게시/소비자/손상/재시작 통합 smoke. 자동 창 숨김 제거와 기존 JSON 호환, 기본 온라인 켜짐/저장된 끄기 유지/빈 기본 주소 실제 요청, 일수 추가 후 만료 시각 지정·native 문자 입력·잘못된 시각 거부를 확인했다. 만료 경고색·마감/종료 라벨·업데이트 끄기 안내는 실제 DrawData로 확인했다. [동작과 알림 협의 범위](development/SETTINGS-AND-RENEWAL-REVIEW.md).

## 2026-11-13 · 상단 높이·정렬 보정

오늘 버튼에 월 이동/표시/추가와 같은 높이와 충분한 좌우 여백을 적용했다. 년월은 같은 행 안에서 세로 가운데에 놓고, 아이콘·문자·전장 요약도 버튼 높이를 기준으로 가운데 정렬한다. 좁은 창에서는 표시/추가가 함께 다음 행으로 이동한다. 기존 배치 순서와 오늘 이동 기능은 유지했다. Debug 빌드 경고/오류0, 기존 native UI393검사 통과. 실제 DrawData의 차콜1000×720, 라이트1000×720, 미드나이트620×480/2배율 PNG를 확인했다. 게임 내 글꼴/DPI 확인은 별도다. 이 보정은 현재 Debug DLL에 반영했으며 앞서 생성한 ZIP은 보정 전 복사본이다.

## 2026-11-13 · 0.2.4 숨김/게임 중 동작 사용성 점검

Core193 / Infrastructure184 / Model94 / Native393 / 수집117 / CLI30 =1011개 검사와 한국어 리소스222개, 별도 게시/실제 소비자/손상/재시작 통합 smoke. 단계별/관련 전체 숨김·분류 독립·ID 재정렬/기한 변경·재시작/복원, 전투/임무/PvP 자동 요청 보류·취소·수동 예외·15초 안정 대기를 확인했다. Native UI에서는 실제 CCRC 메뉴 클릭과 자동 창 숨김 후 개인 일정 초안 복원을 확인한다. [발견한 문제·수정·남는 경계](development/ACTIVITY-HIDING-REVIEW.md). 실제 게임 내 조건/입력/채팅 전달 검증과 원격 게시를 수행한 결과는 아니다.

## 2026-11-13 · 0.2.3 수집/갱신 5회 점검

Core168 / Infrastructure172 / Model80 / Native381 / 수집111 / CLI30 =942개 검사와 리소스222개, 별도 게시→실제 Workspace→개인 자료 보존→다음 파일 손상 거부→재시작 통합 smoke를 통과했다. [점검 기록과 아키텍처/운영 적용 범위](development/PIPELINE-AUDIT-2026-11-13.md).

실제 GitHub 성공 run과 별개로 공지/패치 출처가 실패하던 문제를 재현했다. 시각-only 공지 목록을 본문 전체 날짜로 검증하도록 고친 후99건/9출처 오류0을 실제 재수집했다. 마지막 공지/패치 독립화 후에도 같은 실수집 결과를 확인하고 로컬 publication20을 실제 소비자에 적용했다. 원격 운영 publication18/96건과 전장 HTTPS도 별도로 재확인했다. 원격 source push/data 게시/Release/수정 Actions runner 실행과 게임 GPU/실기 재현은 수행하지 않았다.

## 2026-11-13 · 0.2.2 갱신 주기·분류 스타일·전장 요약

Core159 / Infrastructure149 / Model80 / Native381 / 수집96 / CLI23 =888개 검사, 리소스222개. 가짜 시계·HTTP로 5개 갱신 주기의 경계 직전/도달, 변경 시 재계산, 재시작 복원, 실패 15분/1시간/6시간 대기와 429 Retry-After, 복구를 검사한다. 실제 HTTPS smoke에서는 revision18/96건의 pointer·payload 검증과 캐시 재시작을 통과했다.

색상·아이콘·전장 날짜 칸·주기 설정의 저장/재시작 유지와 개인 일정 보존, 구형 설정의 기본값 호환을 확인한다. 실제 MainWindow/native ImGui로 3테마·3크기·3배율의 스타일 설정 폭, 넓은 도구 한 줄 배치, 오늘/내일 요약과 선택적인 전장 날짜 칸의 독립을 검사한다. DrawData 렌더로 표시 메뉴·설정·달력과 작은 2배율 화면을 검토한다. 게임 GPU·실제 게임 글꼴/DPI의 최종 확인은 사용자의 실기 확인과 구분한다.

## 2026-11-13 · 0.2.1 노출 알고리즘·개인화

Core146 / Infrastructure116 / Model77 / Native327 / 수집96 / CLI23 =785개 검사, 한국어 리소스222개. [공식 원문·전체 케이스 검토](design/DISPLAY-POLICY-RESEARCH.md)에 따라 표시 투영을 추가했다. 원본 event/phase/edge를 모두 보존하며 같은 날 점검과 같은 근거 패치, 검토한 구매 캠페인만 묶는다. 날짜 팝업은 직접 일정 우선/진행 중 기본 접기를 적용한다.

Native 검사는 실제 날짜 선택기와 월 이동, 진행 중 영역 접기/펼치기, 카드별 숨김을 포함한다. Model 검사는 숨김 뒤 원본·뉴스 보존과 재시작 유지·개별/전체 복원을 확인한다. 차콜·미드나이트와 카드/미니 달력을 실제 DrawData PNG로 검토한다. 게임 GPU·실제 글꼴/DPI·새 버전 실기 검증은 별도다.
## 2026-11-13 · 0.2 전면 재설계

Core118 / Infrastructure116 / Model71 / Native300 / 수집96 / CLI23 =724개 검사와 한국어 리소스222개. 기존 화면에 종속된 검사를 새 구조의 실제 클릭/휠/문자 입력 및3테마·3크기·3배율 검사로 교체했다. 새 검사는 월·날짜 동작과 종료일 충전, 새소식과 달력의 상태 독립, 공지/부가 서비스 점검 배제, 언어/테마 호환 변환을 포함한다.

실제 HTTPS 전장 응답에서2026-11-13 워코 치테, 다음 날 봉인된 바위섬을 수신했다. 고정 응답을 사용하는 parser/transport/cache 실패 검사와 실수신 검사를 구분한다. 실제 DrawData PNG로 새 다크·라이트·압축 화면을 검토했다.

[10회 수정·후속 검토](design/REDESIGN-10-LOOPS.md). 아래의 과거 검증 기록은 당시 화면 기준이다. 게임을 자동 조작/로드하지 않았으며 게임 GPU·DPI·실제 채팅 전달·사용성 과제의 실기 확인은 남아 있다.
## 2026-11-13 제품 흐름·선택 맥락 재설계

전체497검사·한영221쌍. Core103/Infrastructure97/Model61/native ImGui117/소스96/CLI23이 통과했고 KR Debug 빌드는 경고0·오류0이었다. Model16개 추가 검사는 실제96건으로 월·날짜·단계 상태, 정확한 마감 선택과 뒤로 가기, 월 이동 시 오래된 선택 제거, 2/19 세 경계, 빈 날짜, 필터 유지, 조건형 PvP 종료와 개인 만료의 미정 오분류 방지를 확인한다. 기존 휠·팝업 폭·입력·배율 검사는 유지한다. Native6개 추가 검사는 날짜의 빈 영역 클릭과 겹치는 마커의 우선 선택을1/1.5/2배율의 실제 마우스 입력으로 확인한다.

실제 MainWindow DrawData를 CPU rasterize한 PNG를980×660·1280×800·780×660에서 사람이 검토했다. 마지막 주 clipping, 시각 앞자리와 종류 선 겹침, 좁은 창의 중복 월 목록을 수정했다. 기본 월 상태에서 오래된 선택이 남지 않으며 2/19 세 마커를 모두 읽을 수 있다. 별도 작은 창과 명령을 제거하고 좁은 창은 같은 본문에서 날짜/상세를 전환한다. [제품 검토](design/PRODUCT-UX-REVIEW.md), [상호작용 명세](design/INTERACTION-DESIGN.md), [렌더링 방법·한계](design/VISUAL-VERIFICATION.md). 게임 GPU·실제 폰트/DPI·사용성 과제를 완료한 것으로 해석하지 않는다.

## 2026-11-13 달력 휠 이동

전체475검사·한영182쌍. Native ImGui111검사는 실제 휠 입력27개를 포함한다. 이전/다음, 분수 누적, 영역 밖·Ctrl·팝업 차단, 부모 스크롤 분리,2020~2100 경계를1/1.5/2배율로 확인했다. [동작](development/MOUSE-WHEEL.md).

## 2026-11-13 캘린더 표시 선택

전체448검사·한영182쌍 통과. Model45 + native ImGui84를 포함하며 종류별 체크박스·양 끝 마커·legacy 기본값 전환·선택 저장·새소식/알림 보존·9개 크기/배율 경계를 확인했다. [표시 규칙](development/CALENDAR-VISIBILITY.md).

## 2026-11-12 전체 소스·화면 개편

426검사 + 한영177쌍. Core103/Infrastructure97/Model32/native ImGui75/소스96/CLI23. Native 검사에는1/1.5/2배율·760/1180/1600px 새소식/설정/빈 이용권, 실제96건 번들의2027.02 얼리/정식 두 마커와 월 이동을 포함한다. GPU·실제 게임 시각 확인과 구분한다.

[전체 수집·게시 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43202504781) 성공, 실제 Workspace 수신 revision4/events96, SHA256/bytes 및 캐시 재시작 성공. 출처9개 모두 재검토 대기0이었다. 공식 정적 템플릿은 코드를 실행하지 않고 현재 원문 페이지의 script 참조와 구절을 함께 검증한다. [범위](development/ALL-SOURCES.md), [화면](development/SCHEDULE-PRESENTATION.md).

## 2026-11-12 실기 제보 후 수정

[GitHub Actions 첫 실제 수집](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43197397575) 성공. calendar-data/kr/current.json과 immutable payload 게시 후 실제 Workspace 수신은 RemoteUpdated, revision1, events10이었다. SHA256/bytes 검증과 재시작 캐시 복원까지 통과했다. 네트워크 검사는 명시적 `dotnet run --project tests/DuckbillCalendar.Infrastructure.Checks -- --live-smoke`로만 수행하고 기본 검사는 오프라인으로 유지한다.

[수정 기록](development/POST-PLAYTEST.md), [실제 데이터 연결](development/LIVE-DATA.md)을 추가했다. 현재 **308검사 + 한영138쌍** 통과. 실제 native ImGui30검사는 기존 오류패턴의 negative control, 1/1.5/2배율의100프레임 팝업 너비, 한국어 툴팁, 날짜·시각 문자 입력, 실제10건 데이터로 전체 화면을 확인한다. 실제 게임 GPU 화면을 확인한 것으로 보고하지 않는다. 공식 이벤트 HTTP수집·검토원문 재생성 hash·번들 연결과 fakeHTTP Collector18검사도 통과했다. 최신 확인 파일은 고정 Debug 경로이며 아래 ZIP은 수정 전 이력이다.

[자동 게시 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43198381662)은 Ubuntu에서 수집·이전 payload 검증·자동 생성·partial health·일반 push까지 성공했다. 이어 실제 Workspace로 revision2/events10 수신과 캐시 재시작이 성공했다. 자동 규칙18검사를 추가해 현재308검사다.

## 2026-11-12 첫 개발판

**10회 구현 루프 완료.** [개발 기록](development/DEVELOPMENT-LOOPS.md), [실행 안내](development/BUILD-AND-RUN.md)가 현재 구현 기준이다. 아래 11/11 부분은 최초 기획·목업의 이력이다.

- KR·글로벌 각각 Dalamud15.0.3.9 Debug 참조 빌드, KR Release 전체 빌드: 경고0·오류0.
- Core102 + Infrastructure89(fake HTTP/디스크/알림) + 실제 CalendarModel19(localization만 대역) + CLI23 = **총233개 통과**.
- 한영136쌍·포맷 인자·직접 참조 키와 ResourceManager 영어/한국어 로딩 확인.
- 수동 저장한 공식 이벤트 목록 한 장에서 기간 카드10개·경고0을 추출. 원문 전체는 Git에 넣지 않았다. 전체 수집·정책·자동 승인을 증명하지 않는다.
- KR Release 개발 ZIP에 필수6파일(주/Core/Infrastructure DLL, manifest/deps, ko satellite)을 넣고 archive 내부 hash를 원본과 재대조했다.
- 운영 피드·스케줄·원격 게시·게임 자동 로드 없음. 실제 게임 UI/DPI·명령·채팅 전달/억제·캐릭터 전환은 확인 대기.

ZIP: `artifacts/development/DuckbillCalendar-0.1.0.0-dev-20261112-081746.zip`.
SHA256: `753F294194AEC65439C1A3A523856E2E646947DFAF79AE89C4FFB37A7E9E86FB`.

최종 provenance 점검에서 같은 revision의 수동 파일이 과거 원격 health를 빌려 알림을 발송할 수 있는 경로를 수정했다. UI 적용 snapshot의 remote 출처·payload SHA256·revision 일치가 있어야 ActiveHealth를 반환한다. 기존 pending 취소·신규 알림 차단 회귀 검사까지 포함한다.

로컬 알림은 기본 off이며 기록 저장 성공 후 전달한다. 기록 후 출력 전 crash/세션 전환이면 누락 가능성이 있고 출력 실패를 재시도하지 않는다. 게임 API 어댑터를 연결했지만 오프라인 callback 검사와 실제 전달 확인을 혼동하지 않는다.

확인일: 2026-11-11 KST

## 수행한 확인

- 비어 있는 사용자 지정 GitHub 저장소를 현재 디렉터리에 clone했다. 다른 프로젝트 파일은 변경하지 않았다.
- DuckbillFisher revision `a2b223b646aecf0897c6c1a3b0610a5921d45d41`의 지침·프로젝트·UI 테마와 구조 문서를 읽고 참고했다.
- KR 공식 이벤트·공지·패치노트 목록의 HTTP 200과 HTML 내 링크·기간을 확인했다. 기간 정정·실제 점검 종료·CC 시즌 전환의 개별 원문도 확인했다. 상세 출처는 [조사 문서](DATA-SOURCES.md)에 있다.
- Node VM 파서로 `mockup/app.js`, `mockup/feed.js`의 JavaScript 문법을 검사했다.
- 공개 JSON 예시가 파싱되고, 6개 일정 ID가 중복되지 않으며, 모든 알려진 종료가 시작보다 늦고 미정 종료에는 조건이 있음을 확인했다.
- 로컬 미리보기 서버가 `http://localhost:8765/mockup/`에서 시작됐음을 확인했다. HTTP 자산 응답과 파일 연결 검사는 최종 정리 때 별도로 수행했다.

후속 수정에서는 `calendar-layout.js`의 주/연 경계 분할, 윤일, 겹침 행 분리와 행 재사용을 Node VM에서 검사했다. 브라우저 대신 최소 DOM 대역으로 초기 렌더 함수·표시 옵션 전환/저장·기간 중간 날짜 선택·시리즈 시작일 이동·일반 4개/운영 도구 포함 5개 탭 전환에 실행 예외가 없고 예상 HTML이 생성되는지 확인했다. 이 검사는 실제 CSS 레이아웃·클릭 검증을 대신하지 않는다. 기존 브라우저 인프라 문제가 복구됐다는 근거가 없어 자동 제어를 재시도하지 않았다.

## 3회 심층 검토 후 추가 확인

수집·Plugin·제품 담당이 병렬로 설계/반박/수정 세 번을 수행하고 [결정 기록](architecture/DECISIONS.md)과 [구현 계획](development/IMPLEMENTATION-PLAN.md)을 교차 검토했다. 실제 구현·운영 검증으로 대체하지 않는다.

- v2 current가 참조하는 예시 payload의 **실제 바이트 수와 SHA-256**, region/schema/revision 일치를 확인했다. evidence/phase/event ID, 조건 target registry, 시작 추론의 두 출처 연결, 명시적인 isExample/not-run을 검사했다. 전체 production validator가 완성된 것은 아니다.
- DOM 대역 실행: 일반 운영 탭 숨김·방향키 건너뛰기·비활성화 시 설정 복귀, phase의 시작/마감별 독립 선택·legacy 종료 선택 이전, 표시 옵션 후 선택 보존을 확인했다.
- 수령 막대 off에서도 수령 endpoint·선택 날짜의 진행 목록·알림을 유지한다. 긴 기간이 월간 목록에서 누락되지 않고 주별 막대는 3행으로 제한되며 날짜별 숨김 수가 표시됨을 확인했다.
- 동적인 시즌/시리즈 제목, 시작일/오늘의 phase 초점, 필터 밖 선택 안내, 작은 화면의 실제 마감 시각, 오늘/내일의 서로 다른 규칙 유효 범위를 검사했다.
- 전체 로컬 문서 링크·코드 fence와 공백을 검사했다. 새 .NET 프로젝트·원격 workflow·게시 endpoint는 만들지 않았다.

3차 말미에는 source 전체 hash 변화가 기존 일정을 전부 무효화하는 모순을 추가로 수정했다. 최종 계약은 내부 sourceRecordId 비교 후 `completeCoverage`·`comparedContentRevision`·영향 ID별 `pendingChanges`를 보고한다. 예시는 미실행이므로 coverage false / 비교 revision null이다. 이는 계약 정합 확인이며 실제 수집 범위의 정확성을 증명한 검사는 아니다.

## 브라우저 검증 제한

브라우저 접근 호스트가 `The browser access host is unavailable`을 반환했다. Windows Edge 앱 경로도 현재 URL을 충분히 확인할 수 없다는 이유로 Computer Use가 중단됐다. 이후 앱 제어를 진행하지 않았다.

따라서 **실제 화면 렌더링, 반응형 레이아웃, 클릭 동작, 파일 다운로드, 브라우저 재실행 후 저장 복원은 미확인**이다. 문법 검사나 HTTP 200을 이 동작들의 통과로 취급하지 않는다. 목업에 구현한 동작과 실기 검증 완료는 구분한다. 새 자동화 경로로 차단을 우회하지 않았다.

## 사용자가 바로 확인할 동선

`mockup/index.html`을 브라우저로 연다. 별도 패키지나 빌드는 없다. 기본 진입 화면은 2026년 11월, 헬스케어 프로그램의 참여/수령 기간 상세다.

1. 다음 달 → 오늘. 11월 11일 복귀와 선택 상세를 확인한다.
2. 이벤트/내 일정 필터를 선택하고 검색어를 넣었다 지운다. ‘마법자판’은 날짜 미정 목록에서도 검색된다.
3. 11월 18일의 이용권을 선택해 상세를 확인한다. 이용권 메뉴에서 종료시각을 수정하고 달력에 반영되는지 확인한다.
4. 내 일정 추가 → 제목과 날짜 입력 → 저장. 상세 수정·삭제와 페이지 새로고침 후 보존을 확인한다.
5. 관심 알림을 켜고 작은 화면에 반영되는지 확인한다. 실제 알림음은 구현하지 않았다.
6. 달력 아래 오늘/내일 전장의 기본 확인 불가 상태와 두 시작일 바로가기를 확인한다. 설정에서 전장 배치 예시를 켜면 예시 이름이 표시된다. 시리즈 시작일은 9월 8일로 이동한다. 시즌 시작은 11월 10일 칸에 표시된다.
7. 설정에서 Paper 테마, 오프라인, 오래된 데이터 상태를 확인한다. 모두 화면 시뮬레이션이다.
8. 설정에서 운영 도구 목업을 켜고 데이터 관리에서 검수 표시 후 JSON을 내보낸다. `isExample: true`, 미게시 상태, 개인 정보 제외를 확인한다.
9. 기간 막대 / 시작·종료만 옵션을 전환하고 새로고침 후 선택 보존을 확인한다. 막대의 주·월 경계와 참여/수령 구분을 확인한다.
10. 1180px/980px/760px/390px 폭과 키보드 Tab·방향키·Home·End·Enter·Escape를 확인한다. 상단 탭은 좁은 폭에서도 가로로 유지하고 상세를 아래로 쌓는다.

로컬 파일에서 저장이 제한되는 브라우저는 세션 내에서만 동작한다. HTTP 확인이 필요하면 저장소 루트에서 PowerShell `./tools/preview.ps1`을 실행한 뒤 출력 주소를 연다. 종료는 Ctrl+C. 이 도구는 localhost에서만 파일을 제공한다.

## 구현 후 별도로 필요한 검증

Dalamud DLL 빌드, KR/글로벌 실제 호환성, 게임 내 폰트·크기·입력, 실제 알림 중복과 캐릭터 전환, 계정 기간 자동 관찰, 실시간 수집 파서·CI·게시·캐시 갱신은 이번 산출물의 검증 대상이 아니다. 해당 기능은 아직 구현하지 않았다. GitHub 푸시와 외부 배포도 수행하지 않았다.

최종 [원문·템플릿 연결까지 포함한 자동 수집 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43204681599)도 성공했다. 실제 Workspace가 revision5/96건을 수신해 SHA/bytes 검증과 캐시 재시작을 통과했다.
