# 실제 한국 이벤트 수집과 갱신

0.2.2 클라이언트 확인 주기는 설정에서30분/1시간/3시간/6시간/12시간을 선택한다. 기본은1시간이며 실행 중에만 확인한다. 성공한 수신 시각을 기준으로 다음 확인을 계산하고 검증된 캐시를 복원하면 재시작 후 남은 주기를 유지한다. 실패 시15분→1시간→6시간으로 재시도하며 수동 확인5분 제한과 서버429 Retry-After를 유지한다. 주기 변경은 실패 대기를 우회하지 않는다. 설정에는 마지막 확인·다음 확인 또는 재시도 시각을 표시한다. 이 설정은 이미 게시된 JSON의 확인 주기이며 원본을 수집하는 저장소 workflow의2시간 주기와 별개다.

> 현재 운영은 [전체 소스 수집](ALL-SOURCES.md)이다. 연결된 이벤트 페이지14건, 공지·패치·점검·시즌76건, 확장팩·얼리·혜택·PvP·대회6건을 합친96건/9출처를 실제 게시·수신했다. 아래는 유지하는 단일 이벤트 수집기·수동 검토 경로의 설명이며, 현재 workflow는 `collect-all`과 `mark-all-health`를 사용한다.

2026-11-12 현재 [공식 이벤트 목록](https://www.ff14.co.kr/news/event) 첫 페이지에서 실제 10건을 가져왔다. 저장 HTML 후보만 만들던 상태에서 **실제 HTTP 수집 → 원문 보존·검토 → v2 일정 → 플러그인 번들**까지 연결했다. `data/live/kr-events.snapshot.json`은 가상 예시가 아니다. `kr-events.collection.json`의 확인 시각과 원문 SHA-256, `kr-events.review.json`의 검토 후보 hash·선택·시각으로 재현한다. 웹 도구의 공식 페이지 표시 내용과 수집 HTML의 제목·기간을 대조했다.

범위는 목록 첫 페이지의 제목·기간이다. 상세 본문·전체 paging·공지·패치·시즌 공지는 아직 이 수집기에 포함하지 않는다. `completeCoverage: false`를 유지하며 종료 미정/업데이트 전까지를 임의 날짜로 바꾸지 않는다. 원문 기간은 이벤트 note에 보존한다. 원문 HTML은 `.preview/` 또는 Actions artifact에 보관하고 소스 저장소에 넣지 않는다. 스냅샷만으로 공개 시간 알림의 freshness를 승인하지 않는다.

최초 번들은 수동 원문 대조로 준비했으며, 이후 운영 갱신은 **공식 목록의 구조화된 `time[datetime]` 필드를 결정적 규칙으로 검증하는 자동 경로**를 추가했다. 자동 경로는 사람·LLM 검수라고 표현하지 않는다. 매번 수기 검토를 기다리는 흐름은 기본 운영 갱신의 필수조건이 아니며, 아래 수동 `prepare` 경로도 유지한다.

## 로컬에서 갱신

```powershell
# 공식 목록 한 번만 수집. 새 디렉터리, 원문/collection/candidates 및 빈 review template 출력.
./scripts/sync-live-events.ps1 -Mode Collect

# source.html과 candidates.json의 제목·기간·원문 링크를 확인한다.
# review.template.json을 복사해 reviewedAt(ISO8601), reviewedBy를 작성하고,
# 승인한 후보만 approved에 {id, normalizedRecordHash, category}로 넣는다.
# category: web-event / in-game-event / seasonal-event / sale / discount.

# 같은 원문 및 명시적 review를 검증해 번들 파일 교체 후 고정 Debug DLL 빌드.
./scripts/sync-live-events.ps1 -Mode Prepare `
  -CollectionDirectory .preview/event-collections/<수집시각> `
  -ReviewPath .preview/event-collections/<수집시각>/review.json `
  -BuildPlugin
```

원문은 고정 HTTPS 주소로만 조회한다. 쿠키·redirect·압축·스크립트/리소스 실행 없이 단일 요청을 하며 헤더와 실제 스트림 2MiB 상한, 요청부터 본문까지 20초 deadline을 적용한다. HTTP 오류·빈 결과·파싱 경고·synthetic 응답·잘못된 인코딩은 정상 자료로 승격하지 않는다. 검토 파일이 승인한 snapshot hash와 각 후보의 normalized hash를 원문 재파싱 결과에 대조한다. `candidates.json`을 편집해서 날짜를 위조해도 feed 생성에는 쓰이지 않는다. 기존 수집 디렉터리와 출력 파일을 덮어쓰지 않는다.

CLI 직접 사용:

```powershell
dotnet run --project tools/CalendarDataCollect -- collect .preview/new-collection
dotnet run --project tools/CalendarDataCollect -- prepare .preview/new-collection review.json .preview/new-feed.json
# 기존 운영 feed를 보존·병합하는 자동 경로. 최초 입력만 previous 위치에 '-' 사용.
dotnet run --project tools/CalendarDataCollect -- prepare-auto .preview/new-collection previous-feed.json .preview/automatic-feed.json
# current.json의 경로·SHA256·바이트 수·revision·v2를 확인한 exact payload 복사본.
dotnet run --project tools/CalendarDataCollect -- verify-current .preview/publication .preview/previous-feed.json
dotnet run --project tools/CalendarDataCollect -- check
```

자동 준비는 원문/manifest hash 일치, synthetic 아님, 파싱 경고0, 후보1~100건, 모든 phase의 알려진 시작, `time` 속성과 표시 날짜 일치, production Core 검증을 요구한다. `datetime`이 빠진 time 요소·임의 형식·오류 페이지·빈 결과·범위 모순이면 전체 갱신을 실패시킨다. 시작·종료 값은 구조화된 KST 분 단위 시각만 사용한다. 미정 종료는 계속 unknown이고 원문 문구를 보존한다. 새 항목은 web-event, 기존 ID는 이전 category를 유지한다. 첫 페이지에서 사라진 이전 사건은 삭제·취소·종료로 판단하지 않고 이전 경계와 증거를 보존하며 미확인 note를 붙인다. 이전 targets/withdrawals/전장 규칙도 보존한다. 전체500사건·근거4000개·payload2MiB 상한을 넘으면 임의로 자르지 않고 실패시켜 유지보수 대상으로 남긴다.

## 별도 상시 서버 없이 운영

`.github/workflows/collect-events.yml`은 GitHub Actions runner에서 2시간 간격으로 한 페이지를 수집하고 `kr-events-collection` 원문/후보 artifact를 14일 보존한다. main 게시 후 예약 실행되며 수집기 코드 변경 push 및 `workflow_dispatch`에서도 실행한다. 수집 후 기존 `calendar-data/kr/current.json`과 해당 payload를 바이트까지 검증하고 `prepare-auto`로 자동 병합한다. `CalendarDataPublish`로 불변 revision을 먼저, current를 마지막으로 만들고 별도 기계 검증 결과를 부분수집 health로 기록한다. 최종 pointer/payload를 다시 검증한 후 `calendar-data` branch에 일반 push한다. 단계 하나라도 실패하면 기존 원격 feed를 바꾸지 않는다. source checkout과 데이터 checkout을 분리하고 Actions checkout의 기본 인증만 사용한다. 사용자 로컬 credential을 조회하지 않는다.

별도 `.github/workflows/publish-events.yml`은 수동 검토를 원하는 경우 유지한 경로다. 검토 commit의 `data/live/kr-events.review.json`과 snapshot, 지정한 collection run의 exact 원문 artifact를 결합한다. 원문을 다시 파싱해 승인 hash를 검증하고 생성 feed가 commit의 snapshot과 바이트까지 같은지 확인한다. 자동 갱신과 수동 게시 모두 같은 concurrency group을 사용하고 force push 없이 일반 push하여 외부 경쟁 writer를 거부한다. 수동 경로는 쓰기 권한이 있는 운영자의 실행과 검토 원문 artifact가 필요하며 `calendar-data` environment에 required reviewers를 추가할 수도 있다. 자동 경로는 별도 사람 승인이나 LLM 없이 제한된 구조화 필드를 검증하는 정책이다.

게시 후 플러그인은 고정 주소 `https://raw.githubusercontent.com/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/current.json`만 읽는다. 서버·LLM 없이 GitHub Actions와 정적 JSON으로 동작하는 구조다. 자동 경로의 source health는 `lastResult: partial`, 실제 수집의 attempt/success 시각과 비교 revision, `completeCoverage: false`를 기록한다. 부분 성공을 전체 수집 성공이나 사람 검수로 표시하지 않는다. freshness가 필요한 공개 시간 알림은 계속 차단되며 표시용 일정 갱신만 제공한다. 수동 로컬 Publish 도구 단독 실행은 여전히 not-run이며 자동 경로가 증거를 재확인한 뒤 partial 상태를 덧붙인다.

현재 실제10건의 source/data branch 게시와 플러그인 Workspace의 원격 HTTPS smoke가 완료됐고 최초 Ubuntu 수집 run `43197397575`도 성공했다. 새 자동 게시 경로의 실제 runner 결과는 [현재 상태](../STATUS.md)에 기록한다. 원문 artifact가 만료되면 수동 review의 재현에 새 원문이 필요하며 예전 review를 새 HTML에 재사용할 수 없다. 자동 경로는 각 실행의 원문을 새로 검증한다. 현재는 관측마다 event revision과 불변 content revision을 추가하므로 장기 저장 용량·변경 없는 관측의 revision 생략 최적화는 후속 과제다. 자동 병합은 미확인 사건을 지우지 않으므로 500건 상한 도달 시 명시적인 역사 정리 정책이 필요하다.
