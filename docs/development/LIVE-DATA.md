# 실제 한국 이벤트 수집과 갱신

2026-11-12 현재 [공식 이벤트 목록](https://www.ff14.co.kr/news/event) 첫 페이지에서 실제 10건을 가져왔다. 저장 HTML 후보만 만들던 상태에서 **실제 HTTP 수집 → 원문 보존·검토 → v2 일정 → 플러그인 번들**까지 연결했다. `data/live/kr-events.snapshot.json`은 가상 예시가 아니다. `kr-events.collection.json`의 확인 시각과 원문 SHA-256, `kr-events.review.json`의 검토 후보 hash·선택·시각으로 재현한다. 웹 도구의 공식 페이지 표시 내용과 수집 HTML의 제목·기간을 대조했다.

범위는 목록 첫 페이지의 제목·기간이다. 상세 본문·전체 paging·공지·패치·시즌 공지는 아직 이 수집기에 포함하지 않는다. `completeCoverage: false`를 유지하며 종료 미정/업데이트 전까지를 임의 날짜로 바꾸지 않는다. 원문 기간은 이벤트 note에 보존한다. 원문 HTML은 `.preview/` 또는 Actions artifact에 보관하고 소스 저장소에 넣지 않는다. 스냅샷만으로 공개 시간 알림의 freshness를 승인하지 않는다.

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
dotnet run --project tools/CalendarDataCollect -- check
```

## 별도 상시 서버 없이 운영

`.github/workflows/collect-events.yml`은 GitHub Actions runner에서 2시간 간격으로 한 페이지를 수집하고 `kr-events-collection` artifact를 14일 보존한다. 모든 결과는 미검수 후보이며 자동으로 공개 feed에 올라가지 않는다. main 게시 후 예약 실행되며 수집기 코드 변경 push에서도 실행한다. `workflow_dispatch`는 운영자가 수동 수집할 수 있다.

`.github/workflows/publish-events.yml`은 검토 commit의 `data/live/kr-events.review.json`과 snapshot, 지정한 collection run의 exact 원문 artifact를 결합한다. 원문을 다시 파싱해 승인 hash를 검증하고 생성 feed가 commit의 snapshot과 바이트까지 같은지 확인한 뒤 `CalendarDataPublish`로 불변 revision을 먼저, `current.json`을 마지막으로 만든다. 별도 `calendar-data` branch를 일반 push하여 경쟁 writer는 거부된다. force push하지 않는다. 게시 workflow는 쓰기 권한이 있는 운영자의 수동 실행과 검토 원문 artifact가 필요하다. `calendar-data` environment에 required reviewers를 추가하는 운영 절차도 권장한다. 첫 게시 전에는 branch가 없어도 생성하도록 준비했다.

게시 후 플러그인은 고정 주소 `https://raw.githubusercontent.com/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/current.json`만 읽는다. 서버·LLM 없이 GitHub Actions와 정적 JSON으로 동작하는 구조다. 현재 수집기는 부분 범위이므로 게시해도 source health는 not-run/coverage false다. freshness가 필요한 공개 시간 알림은 계속 차단되며 표시용 일정 갱신만 제공한다. 범위가 확대되고 전체 수집/정정 비교가 구현되기 전에는 성공 health를 위조하지 않는다.

현재 로컬 구현·오프라인 검사·실제 1회 수집은 완료했다. GitHub push, workflow 활성화, environment/reviewer 구성, 첫 원격 게시는 이 작업에서 수행하지 않았다. 따라서 원격 URL은 아직 운영 데이터 제공을 보장하지 않는다. 원문 artifact가 만료되면 해당 snapshot을 새로 수집·검토해야 하며 예전 review를 새 HTML에 재사용할 수 없다.
