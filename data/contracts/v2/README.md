# v2 계약 검토 예시

모든 파일은 `isExample: true`인 설계 자료다. production 피드·운영 URL이 아니다. 실제 수집을 수행한 것으로 해석하지 않는다.

- `boundary.schema.json`: 시작/종료 각각의 instant / date / condition / unknown 구분. KR 우선 schema다. JSON Schema 형식만으로 날짜 의미·증거·시간대·순서를 모두 검증할 수는 없다.
- `revisions/000001.json`: 참여·수령의 다른 마감, 확정 시작+조건형 종료, 날짜만 예정+종료 미정을 표현한다. `sourceSnapshotSha256: null`은 실제 provenance가 아직 준비되지 않았다는 뜻이다.
- `current.example.json`: payload 바이트 수와 SHA-256은 실제 예시 파일로 계산했다. publication sequence와 content revision을 분리하고 source health는 inline으로 둔다. `lastResult: not-run`이므로 수집 성공을 주장하지 않는다.

production validator는 `isExample`, 미검수 provenance, draft event, 무효 날짜/offset, 모순된 기간, source 참조 불일치, 경로 탈출을 거부해야 한다. 조건이 `targets` registry의 알려진 미래 사건을 참조하면 `resolution: unresolved`를 유지할 수 있다. 이는 오류인 dangling 참조와 다르다. registry에도 없는 target, type 불일치, 실제 내부 경계 참조의 누락·순환은 오류다. 미발표 target의 날짜를 추측해 채우지 않는다. payload 전체 schema는 후속 대상이며 현재 FeedValidator와 strict CalendarJson/Boundary converter로 v2 C# 검증과 IO 인수를 구현했다. 예시·미검수 provenance·draft의 운영 수락을 거부한다. HTML 목업은 v1을 계속 읽고 실제 Plugin은 v2를 사용한다. 예시는 여전히 운영 피드가 아니다.

상대 경로는 current가 위치한 KR feed 디렉터리 안에서만 해결한다. absolute URL·`..`·역슬래시·query·fragment·encoded traversal은 허용하지 않는 계약을 권장한다. 네트워크 allowlist·명시적 redirect 처리도 별도로 필요하다.

freshness는 최근 `lastSuccessAt`, `completeCoverage: true`, `comparedContentRevision == activeContentRevision`, 영향 ID의 pending 변경·나이와 규칙 유효성을 함께 판단한다. 수집기는 내부 sourceRecordId별 승인 hash를 비교한다. 공개 `normalizedSourceHash/approvedSourceHash`는 source aggregate 진단값이며 전체 hash 일치는 일정 freshness의 필수조건이 아니다. 신규 후보 하나로 같은 source의 정상 일정을 무효화하지 않는다.

pending의 `affectedEventIds`는 `[]`=신규 후보로 기존 일정에 영향 없음, ID 배열=그 일정에 영향, `null`=범위 미확정으로 source 전체 재확인이다. coverage와 pending 상한을 넘으면 일부를 잘라 정상이라고 보고하지 않고 coverage false로 표시한다. `lastResult: not-run`, coverage false, compared revision null, 마지막 성공 없음, hash null은 정상 근거가 아니다. 오래된 검수 시각 하나만으로 장기 일정을 무효화하지 않는다. 수집기 범위 보고에 대한 신뢰와 검증은 운영 측 인수 조건이다.

최종 의미 계약은 [결정 기록](../../../docs/architecture/DECISIONS.md)과 [플러그인 설계](../../../docs/architecture/PLUGIN.md)를 따른다.
