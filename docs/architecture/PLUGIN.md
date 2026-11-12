# DuckbillCalendar 플러그인 구조와 구현 설계

> 2026-11-12 구현 갱신: 아래는 11/11 설계 검토다. 실제 첫 개발판의 완료·대기는 [현재 상태](../STATUS.md)와 [10회 개발 기록](../development/DEVELOPMENT-LOOPS.md)을 따른다.

검토일: 2026-11-11. **아래의 C# 프로젝트·클래스·v2 계약은 구현 제안**이다. 현재 저장소에는 문서·HTML 목업·v1 목업 데이터·v2 계약 예시가 있고 플러그인 DLL·배포 피드·수집기는 없다. 목업의 상태·데이터 복사 기능은 실제 원격 수집을 수행하지 않는다. 실제 관찰, 권장 설계, 확인 대기를 구분한다.

## 1. 결론과 참고 범위

공개 피드 다운로드는 .NET HTTP 어댑터가 처리하고, Core는 검증된 값으로 달력·전장 순환·알림 계획을 계산한다. 게임 접근은 로그인된 본인의 로컬 캐릭터 식별과 전투·임무 상태 확인 정도로 제한한다. 공개 일정은 게임 로그인 없이도 로컬 캐시로 표시할 수 있다. 이용권 자동 판독과 게임 네이티브 접근은 첫 버전에서 제외한다.

참고한 DuckbillFisher 체크아웃 HEAD는 `a2b223b646aecf0897c6c1a3b0610a5921d45d41`이다. 관찰한 사실:

| 참고 파일 | 확인한 사실 | Calendar에 적용할 범위 |
| --- | --- | --- |
| `src/DuckbillFisher.Plugin/DuckbillFisher.Plugin.csproj` | SDK `Dalamud.NET.Sdk/15.0.0`, API 15, Plugin→Core 참조 | 동일한 분리 원칙. Calendar SDK/참조 조합은 착수 때 별도 고정 |
| `src/DuckbillFisher.Core/DuckbillFisher.Core.csproj`, `global.json` | `net10.0`, SDK floor `10.0.101`, `latestFeature` | 현재 개발 환경의 출발점이며 최신/호환성 보장이 아님 |
| `Plugin.cs` | WindowSystem·명령·Draw 등록과 Dispose에서 구독 해제 | 수명별 등록/해제 대칭 유지 |
| `docs/development/ARCHITECTURE.md` | Framework에서 관리 값 복사, 작업자는 snapshot 계산, Draw는 표시 | HTTP·파일·스캔을 Draw 밖에 두기 |
| `AGENTS.md` | Fisher에는 런타임 HTTP 금지 방침 | Calendar 별도 프로젝트의 선택적 공개 피드 갱신 정책으로 문서화. Fisher 자체는 수정하지 않음 |

이 정보는 Fisher의 소스 관찰이다. Calendar의 KR/글로벌 빌드·실기 검증은 수행되지 않았다. Fisher 코드를 옮길 때 AGPL-3.0과 각 의존성 고지를 보존한다. 낚시·관찰·자체 네이티브 후크는 가져오지 않는다.

## 2. 프로젝트 경계

```text
DuckbillCalendar.Plugin ──→ DuckbillCalendar.Core ←── CalendarDataImport
          │                         ↑                       (read-only collector)
          └──── infrastructure ─────┤
                                   └──── CalendarDataPublish (trusted writer)
                                   ↑
                                Core.Tests
```

Core, Plugin, CalendarDataImport, CalendarDataPublish의 네 프로젝트로 수집 실행과 게시 실행을 구분한다. 인프라를 위한 별도 서비스·DI 프레임워크·DB·IPC는 필요해질 때 추가한다. Plugin의 `Plugin.cs`에서 구성요소를 직접 조립한다. Core에서 네트워크·디스크·Dalamud·ImGui·Lumina 참조를 금지한다. wire DTO는 Core/Contracts에서 명시적으로 검증해 Domain으로 변환한다. 수집기의 HTML 파싱은 Import 도구에만 둔다. Publish는 신뢰된 검증 코드로만 정적 산출물을 쓰고 원문 수집을 수행하지 않는다.

```text
src/DuckbillCalendar.Core/
  Contracts/       CurrentPointerV2, FeedDtoV2, validation results
  Domain/          DateBoundary, EventPhase, normalized immutable values
  Calendar/        month/agenda projections
  Rules/           FrontlineRules, relative-boundary resolver
  Personal/        AccountProfile, CharacterBinding, PersonalEvent
  Alerts/          AlertPlan, DedupKey, suppression decisions
  State/           immutable snapshots, typed result codes
src/DuckbillCalendar.Plugin/
  Plugin.cs        service composition, command and lifecycle registration
  Infrastructure/ FeedTransport, CacheStore, PersonalStore, bounded IO
  Application/    FeedUpdateCoordinator, CalendarCoordinator, AlertCoordinator
  Game/            CharacterSessionAdapter, LocalNotificationSink
  UI/              MainWindow, CalendarPanel, AgendaPanel, EventDetailPanel
  Configuration/   LocalSettings, migrations, backup/export
  Localization/    stable TextKey, Korean/English .resx
tools/CalendarDataImport/
  collectors, normalization, candidate diff
tools/CalendarDataPublish/
  approved-input validation, immutable payload/current publishing
tests/             domain tests, transport/cache integration tests
```

`Contracts`는 임의 JSON을 그대로 Domain에 노출하지 않는다. 상태는 문자열 메시지 대신 `UpdateErrorCode`, `SourceFreshness`, `BoundaryKind`, `ProfileState`로 표현하고 번역은 UI에서 한다. 화면은 안정적인 `eventId/phaseId`를 보관하며 번역된 제목이나 배열 위치를 키로 사용하지 않는다.

## 3. v1 시간 모델에서 반드시 고칠 결함

현재 예시는 phase 전체에 `precision: conditional`, `certainty: confirmed`를 붙인다. 시작은 분 단위 확정이고 종료는 조건형인 경우, 이 한 쌍으로는 시작 알림의 가능 여부와 종료 미확인을 분리하기 어렵다. v2에서는 **시작과 종료 경계를 각각 모델링**한다.

```json
{
  "id": "series",
  "start": {
    "kind": "instant",
    "value": "2026-09-08T18:25:00+09:00",
    "precision": "minute",
    "certainty": "inferred",
    "evidenceIds": ["ev:series12", "ev:maintenance756"]
  },
  "end": {
    "kind": "condition",
    "condition": {"type": "patch-maintenance", "targetId": "kr:patch:8.0", "relation": "before"},
    "displayText": "패치 8.0 점검 시작 전까지",
    "certainty": "confirmed",
    "evidenceIds": ["ev:series12"]
  },
  "endInclusion": "exclusive"
}
```

위 구조는 현재 [v2 Boundary 계약](../../data/contracts/v2/boundary.schema.json)의 필드명에 맞춘 발췌다. 전체 evidence 항목과 payload는 예제 파일을 따른다. 경계 종류는 `instant`, `date`, `condition`, `unknown`의 tagged union으로 만든다. 각 경계는 출처·확정 정도를 개별 보관한다. `date`는 `DateOnly + timezone`이며 23:59로 승격하지 않는다. `condition`은 `targets` registry의 future patch/season을 참조하고, target이 `resolution: unresolved`이면 미해결 조건으로 유지한다. registry에 없는 ID·type 불일치·내부 참조의 누락·순환은 오류이며 합법적인 미해결 미래 조건과 구분한다. 점검 완료로 시작 시각을 연결한 것은 원문에 직접 적힌 시각과 다른 `certainty: inferred`다. source의 식별은 `evidenceIds → evidence.sourceId`로 연결한다.

달력은 날짜만 확정되어도 시작 항목을 표시한다. 시간 알림은 그 경계의 시각이 확인된 경우에만 제공한다. 미정 종료에서 D-day나 임의의 긴 막대를 만들지 않는다. 여러 날짜의 막대는 `[start, endExclusive)`로 정규화하되 표시에는 원문 정밀도를 유지한다. 공식 `23:59까지`는 해당 분 포함으로 정규화하고 임의의 초 단위 정밀성을 주장하지 않는다.

`cancelled/postponed`는 편집 상태, `upcoming/ongoing/ended`는 현재 시각으로 계산하는 표시 상태로 분리한다. 과거에 게시된 `active` 문자열을 미래까지 신뢰하지 않는다. 시작일이 지난 모든 이벤트를 사용자 완료로 처리하지 않는다. `isExample`인 피드는 운영 모드에서 거부하고 개발 목업에서만 로드한다.

## 4. 스레드와 수명

| 작업 | 실행 영역 | 경계 |
| --- | --- | --- |
| HTTP·본문 해시·JSON 파싱·캐시 IO | 관리 비동기 작업자 | 게임 API·ImGui·개인 캐릭터 객체 접근 없음 |
| 개인 프로필 연결·게임 조건 복사·로컬 알림 출력 | Framework 갱신 | 필요 값만 관리 snapshot으로 복사 |
| 검색·정렬·월 레이아웃 | 관리 계산 | feed/profile/filter/time revision 조합으로 무효화 |
| ImGui Draw | UI 콜백 | 이미 준비된 view snapshot 표시, 사용자 요청 enqueue |

[Dalamud IFramework 공식 인터페이스](https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IFramework/)와 [공식 소스](https://raw.githubusercontent.com/goatcorp/Dalamud/master/Dalamud/Plugin/Services/IFramework.cs)는 Framework 스케줄링 API와 동기 대기의 교착 위험을 설명한다. 버전별 호출 계약을 확인하고 `Task.Result`, `Wait()`, `async void`로 UI를 막지 않는다. HTTP를 Framework에서 await한 뒤 자동으로 안전한 작업자에 복귀한다고 가정하지 않는다.

`FeedUpdateCoordinator`는 플러그인 수명에 속하며 프로세스당 작업 1개만 허용한다. 캐릭터가 바뀌어도 공용 KR 피드 갱신을 폐기할 필요는 없다. 개인 projection은 별도 `sessionGeneration`과 `profileRevision`을 받으며 이전 캐릭터 결과는 적용하지 않는다. 공개 feed cache와 개인 저장 큐를 같은 취소 토큰으로 묶지 않는다.

로그아웃 시 개인 알림 계획과 해당 세대의 UI 계산만 무효화한다. 화면을 닫아도 설정에 따라 공개 갱신과 알림 계획은 살아 있고, 화면의 월 계산만 멈춘다. 네트워크 동의 철회는 진행 요청을 취소하고 이후 요청을 막는다. 패키지 unload는 모든 구독을 해제하고 lifetime token을 취소한다. 취소·실패가 관찰되는 작업을 추적하며 callback이 해제된 Dalamud 서비스에 접근하지 못하게 한다. Framework를 동기 대기해야만 끝나는 종료 구조를 만들지 않는다.

## 5. HTTP 수신 계약

일정 데이터는 게임 서버 통신이 아닌 공개 HTTPS 파일 요청이다. 사용자 키·계정 로그인·쿠키 없이 읽는 어댑터로 구현한다. 이 기술적 가능성이 공식 저장소 등록 승인을 뜻하지는 않는다.

권장 초기값은 `current.json` 64KiB, payload 2MiB, 이벤트 10,000개, JSON depth 32, 한 문자열 최대 길이, 10초 전체 작업 deadline, 자동 갱신 1시간+0~10분 jitter, 수동 갱신 5분 cooldown이다. 6시간 모드는 저빈도 선택값이다. 수치는 성능 측정 후 고정한다. 수동 실패도 cooldown을 우회해 무한 재시도하지 않는다.

처리 순서:

1. 동의·유효 캐시·TTL·진행 중 작업·backoff를 확인한다. 이미 실행 중이면 같은 작업 상태를 반환한다.
2. 최신 pointer인 `current.json`을 허용된 HTTPS host/path에서 받는다. 자동 redirect를 끄고 필요한 redirect만 제한 횟수로 매 hop 검증한다. 상대 payload path의 `..`, query, fragment, absolute URI를 거부한다.
3. 최상위 `schemaVersion`, `region`, `publicationSequence`와 `content{revision,schemaVersion,region,path,sha256,bytes}` 및 inline `health`를 검사한다. pointer region/content region/payload region, content.revision/payload.contentRevision, content.schemaVersion/payload.schemaVersion이 일치해야 한다. 미래 schema는 `UnsupportedSchema`로 표시하고 캐시를 보존한다. 같은 content revision의 다른 hash, 같은 publicationSequence의 다른 pointer digest는 `RevisionConflict`다.
4. 변경된 immutable payload만 bounded stream으로 읽는다. 해제된 본문 바이트 기준 상한을 두어 Content-Length 누락·거짓값·압축 확장에 대응한다. 처리하지 않는 content encoding은 거부한다.
5. raw UTF-8 바이트를 해시하고 DTO 파싱·domain validation을 한다. 타입만 맞고 기간·ID·참조·지역이 틀린 데이터도 거부한다.
6. 캐시 commit 성공 후 새 불변 snapshot을 게시한다. 상태·컬렉션을 부분적으로 덮어쓰지 않는다. 캐시 저장 실패면 기존 활성본을 유지하고 새 다운로드는 미적용 상태로 남긴다. pointer의 health가 유효하지만 새 payload만 실패하면 health와 실패 사실은 갱신할 수 있다. 이때 실제 활성 content는 기존본이며 새 content가 반영됐다고 표시하지 않는다.

`HttpClient`는 플러그인 수명 동안 재사용하고 handler의 연결 수명도 관리한다. 생성/Dispose를 매 요청 반복하지 않는다. [Microsoft HttpClient 지침](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)

중요한 구현 함정: `ResponseHeadersRead`에서 `HttpClient.Timeout`은 헤더까지이며 `MaxResponseContentBufferSize`도 스트리밍 본문 상한을 대신하지 않는다. 연결→헤더→본문→파싱 전체에 linked cancellation deadline을 적용하고 **실제 읽은 바이트**를 세어 제한한다. [Microsoft HttpCompletionOption 문서](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)

429는 `Retry-After`를 존중하고 지연 상한을 둔다. 연결·5xx는 15분→1시간→6시간의 제한된 backoff, 취소·schema/해시 오류는 같은 데이터에 즉시 자동 반복하지 않는다. 인증 실패나 원문 사이트 차단을 플러그인이 대신 우회하지 않는다. 로그는 오류 코드·공개 revision·호스트·HTTP 상태만 기록하고 개인 프로필 ID·전체 JSON·실제 로컬 계정 식별자는 남기지 않는다. 앱은 개인 일정·계정 ID·관심 목록을 외부 전송하지 않지만, HTTPS 요청 대상 사업자는 통신을 위해 IP와 통상적인 HTTP 접근 로그를 관찰할 수 있다. 이를 ‘네트워크를 켜도 정보 전송이 전혀 없음’으로 표현하지 않는다.

## 6. 캐시와 revision

content revision은 단조 증가하고 payload 바이트는 불변이다. `current.json`은 변경 때마다 증가하는 `publicationSequence`, content 참조, inline source별 health를 담는다. 콘텐츠가 같아도 수집 상태가 바뀌면 새 publicationSequence로 게시한다. 별도 status 파일을 추가하지 않아 초기 원격 파일 수를 줄인다. `health.sources[]`는 registry의 `id`와 `lastAttemptAt/lastSuccessAt/lastReviewedAt/normalizedSourceHash/approvedSourceHash/completeCoverage/comparedContentRevision/pendingChanges/lastResult`를 담는다. aggregate hash는 진단 필드이며 freshness의 동등성 필수 조건이 아니다. 수집 성공과 사람 검수, 클라이언트 다운로드 시각은 다르게 표시한다. HTTP 304는 어느 계층의 응답인지 구분한다. 클라이언트가 current의 304를 받으면 클라이언트 확인 시각만 바꾸고, 수집기가 공식 source의 304를 받으면 원문 수집 성공 시각만 갱신한다. 어느 쪽도 새 검수로 간주하지 않는다.

```text
cache/kr/live/
  payloads/000012-<sha>.json   immutable bytes
  active.json                one commit point: actual content + health + advertised metadata
  active.previous.json       last successfully replaced pointer
personal/
  profiles.v1.json            local only; public cache cleanup과 별도
  alerts.v1.json              local only
```

payload를 같은 디렉터리의 임시 파일에 완전히 쓴 뒤 Flush/close하고 고유한 immutable 경로로 이동한다. 새 payload를 재검증한 뒤 새 active pointer를 임시 파일로 작성해 **pointer 한 개**만 원자 교체한다. 기존 pointer가 없을 때의 최초 생성과 있을 때의 replace를 분리한다. `File.Replace`는 교체와 백업을 제공하지만 경로·권한·파일시스템 실패를 처리해야 한다. [Microsoft File.Replace 문서](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0)

캐시 본문·high-water mark·metadata 세 파일을 연달아 썼다는 이유로 원자 트랜잭션이라고 부르지 않는다. 활성 pointer가 가리키는 payload가 유효해야만 현재본으로 인정한다. pointer commit 전 crash이면 이전본, commit 후 crash이면 새 본문을 사용한다. 남은 temp·고아 payload는 나중에 정리한다. 활성/이전/rollback 보호 payload는 정리 대상에서 제외한다. 여러 게임 프로세스가 동일한 저장 경로를 사용할 수 있으므로 파일 writer lock과 충돌 시 재읽기·지연을 설계한다. 잠금 실패 시 다른 프로세스를 종료하지 않는다.

publicationSequence와 정상 수락한 content revision의 high-water를 각각 유지한다. 네트워크에서 더 낮은 publicationSequence나 content revision을 받으면 downgrade를 거부한다. health만 새로운 pointer가 낮은 content revision을 담아도 content를 되돌리지 않는다. 운영 rollback은 이전 내용을 **더 큰 새 content revision과 publicationSequence**로 재발행한다. 로컬 손상 복구로 이전 정상 payload를 보여줄 수 있지만 ‘복구본’을 표시하고 네트워크 high-water를 낮추지 않는다. 단순 재설치·캐시 초기화는 이전 수신 기억을 없애므로 강한 replay 방지를 보장하지 않는다. 이를 필요로 하는 배포는 별도 서명·키 수명 설계가 필요하다.

부분 실패 복구는 명시적으로 설계한다. 예를 들어 publication 32가 content 13을 가리키지만 다운로드가 실패하면, health는 publication 32를 보관하되 실제 활성본은 content 12다. `active.json`에 `advertisedContentRevision=13`, `activeContentRevision=12`, 마지막 pointer digest와 오류를 함께 기록한다. 이후 **같은 publicationSequence와 같은 digest의 재조회에서는 content 13 재시도를 허용**한다. 단순히 `sequence <= highWater`를 모두 버리면 이 복구가 불가능하다. 실패한 content의 번호는 정상 수락 high-water를 올리지 않는다.

마지막으로 검증한 advertised content revision/hash의 짝도 보관한다. 아직 payload를 성공 수락하지 못했더라도 다음 publication에서 같은 content revision을 다른 hash로 광고하면 불변 계약 위반이다. 같은 revision 파일을 고치는 대신 새 content revision을 발행해야 한다. HTTP 상태·다운로드 성공과 무관한 프로토콜 일관성 검사다.

freshness는 검수 나이나 source 전체 hash의 일치만으로 계산하지 않는다. collector는 내부에서 `sourceRecordId`별로 현재 원문과 승인 레코드를 비교한다. 공개 health의 `pendingChanges[]` 항목은 `kind`, `firstObservedAt`, `affectedEventIds`를 갖는다. 영향 ID가 `[]`이면 기존 일정에 영향이 없는 신규 후보, `null`이면 해당 source 전체에 영향을 배제할 수 없는 미확정 변경, `[id,…]`이면 열거된 기존 일정만을 뜻한다. 새 공지 하나로 aggregate hash가 달라졌다는 이유로 기존 모든 일정을 오래됨으로 만들지 않는다.

해당 일정의 fresh 판단에는 **`completeCoverage === true`, `comparedContentRevision === activeContentRevision`, 최근 유효한 `lastSuccessAt`, 해당 ID에 영향을 주는 pending 변경 없음, 적용 규칙 유효**가 모두 필요하다. `not-run`, `lastSuccessAt: null`, `comparedContentRevision: null`, 불완전 수집은 미확인이다. null hash 두 개의 동등성은 어떤 증거도 되지 않는다. 검수 시각만 72시간 전인 장기 이벤트도 이 조건을 충족하면 fresh를 유지한다. 미해결 변경의 경과와 영향 범위는 별도의 ‘변경 확인 대기’ 상태로 표시한다.

health가 새 content revision 13과의 비교 결과이고 실제 활성 캐시는 revision 12라면 그 관측을 revision 12의 fresh 근거로 사용하지 않는다. 새 health의 수집 성공 자체는 상태에 표시하되 활성 일정의 최신성 판단은 분리한다. 기존 활성 revision에 대한 마지막 적격 관측을 보관했다면 그 관측의 원래 시각·유효기간으로만 판단한다. 수집 또는 pending 항목이 상한을 초과한 경우 목록을 조용히 잘라 ‘변경 없음’으로 표시하지 않고 `completeCoverage: false`를 게시한다.

payload SHA-256은 pointer와 본문의 일치 검사다. 저장소가 침해되어 둘 다 바뀌면 hash만으로 차단하지 못한다. 초기 범위는 HTTPS·고정 origin/path·최소 권한 게시·불변 revision·크기/스키마 검증이다. 독립 오프라인 키로 서명할지는 실제 배포 범위와 키 회전/폐기 운영을 결정한 뒤 별도 단계로 진행한다. 피드로 코드·스크립트·DLL·원격 이미지·명령을 받지 않는다. 원문 링크는 고정 HTTPS allowlist에서 사용자 클릭으로만 연다.

## 7. 계정·캐릭터와 알림

실제 게임 계정 식별자는 요구하지 않는다. 사용자가 `내 계정`, `부계정` 같은 로컬 별칭과 무작위 profile ID를 만든다. 본인 캐릭터의 ContentId가 필요하면 로컬 연결에만 쓰고 공개 요청·내보내기·일반 로그에 포함하지 않는다. 연결을 모르면 기존 프로필을 추정 적용하지 않고 `ProfileUnbound`로 표시한다. 정액제·집사 이용권은 계정 프로필, 개인 일정은 전체/계정/캐릭터 scope를 명시한다.

수동 만료일은 원본 확인일과 함께 저장한다. 보상 공지의 무료 이용권을 개인 만료에 자동 가산하지 않는다. 공식 계정 조회 API를 확보하지 못한 상태에서 결제·런처 화면을 자동 탐색하지 않는다. 세션 전환에 실패해도 기존 수동 값은 남는다. 개인 저장 실패는 초안을 유지하고 ‘저장됨’ 표시를 내지 않는다.

개인 참여·보상 수령 체크는 `profileId + eventId + phaseId`의 로컬 기록으로 둔다. 공식 phase의 시간상 종료는 이 체크를 바꾸지 않는다. 체크를 완료했을 때 그 phase 알림을 보류하는 것은 사용자 선택 정책이며, 체크를 해제하면 남아 있는 미래 알림만 다시 계획한다. 다른 phase의 수령 알림이나 다른 계정의 체크에 영향을 주지 않는다.

알림은 UI 필터와 독립된 사용자 선택이다. 해당 phase/boundary별로 설정하고 ID는 `profile + eventId + phaseId + startOrEnd + effectiveBoundary + leadTime`로 만든다. 여기서 startOrEnd는 경계의 값 종류인 instant/date가 아니라 시작/종료 역할이다. 일정 제목·설명·단순 revision 변경은 발송 이력을 초기화하지 않는다. 마감 연장은 새 경계로 재예약하고 취소는 기존 예약을 제거한다. 단계별 알림을 구현하기 전에 event 하나의 관심 버튼을 모든 단계의 발송 승인으로 해석하지 않는다.

날짜만 있는 경계는 날짜 단위 요약, 시각이 확인된 경계는 선택한 사전 알림을 제공한다. 게임이 꺼졌거나 Dalamud가 패치 대응 중이면 알림을 보낼 수 없다. 재접속 때 지나간 알림을 전부 재생하지 않고 최근 마감/변경 요약을 1회 제공한다. 전투·임무·PvP 중에는 해당 알림을 보류하고 나올 때 유효한 내용만 묶는다. 계정 알림은 캐릭터를 바꿔도 같은 dedup 이력을 쓴다.

소리 출력과 디스크 기록을 완전한 exactly-once로 묶을 수 없다. 중복 방지를 우선해 발송 예약의 소비 기록을 먼저 저장하고 로컬 출력하는 at-most-once 정책을 권장한다. 그 사이 crash에서는 한 번 누락될 수 있다. 종료/네트워크/시간 변경 뒤 반복 발송을 막는 것이 이 제품의 우선 목표다. 저장 실패면 중요한 알림을 무한 반복하지 않고 상태줄 오류와 미확인 목록을 제공한다.

## 8. 화면·규칙의 계산 비용과 배율

CalendarProjection은 `feedRevision + profileRevision + filterRevision + displayMode + month + timezone + dayBucket`를 입력으로 받는다. 출력은 immutable day cells, week segments, list rows, selected detail이다. Draw마다 전체 JSON·문자열·장기 전장 예측을 재계산하지 않는다. 초 단위 카운트다운 텍스트는 보이는 항목만 바꾸고, 자정·마감 경계에서 상태 projection을 무효화한다. 긴 텍스트·겹치는 기간·수천 건 입력에 명시적 표시 상한을 둔다.

표시를 `기간 막대 / 시작·종료만`으로 바꿔도 source/model은 바뀌지 않는다. 막대는 phase별 주 구간으로 분할하고 한 주의 lane을 재사용한다. 선택 중인 phase/event는 ID로 유지한다. 필터에서 제외된 상세는 그 이유를 표시하거나 비우며 숨겨진 항목이 현재 검색 결과인 것처럼 보이지 않게 한다. 목록·달력·작은 화면은 같은 projection 정책을 사용한다.

전장 오늘/내일은 선택한 달과 독립적으로 실제 KST 오늘/내일을 사용한다. `anchorDate`, `anchorIndex`, 순서, 적용 범위가 모두 유효할 때만 계산한다. 날짜 override와 규칙 간 겹침도 검증한다. 기준점·적용 범위가 미확인이면 일반 사용자 화면은 ‘미확인’으로 보이고, 목업의 가상 필드 이름은 개발 예시 모드에서만 표시한다. 새 패치 규칙이 없다는 이유로 과거 순서를 무기한 연장하지 않는다.

## 9. KR·글로벌과 배포 경계

처음 제품 데이터 채널은 KR로 고정한다. 글로벌 클라이언트에서 한국 일정을 보고 싶을 수 있으므로 UI 언어·클라이언트 언어·데이터 지역을 독립 설정으로 두되, 초기에는 데이터 지역 `KR`을 명확히 표시한다. 글로벌 일정을 자동 섞거나 클라이언트 언어만으로 날짜 지역을 바꾸지 않는다.

새 프로젝트의 SDK/API 고정과 실제 KR·글로벌 참조 양쪽 compile 검사를 먼저 수행한다. Fisher 빌드 통과는 Calendar의 증명이 아니다. 공개 API 위주이므로 첫 구현은 unsafe 허용·native 후크 없이 시작한다. 필요한 로컬 알림·캐릭터 연결 API는 실제 사용 버전에서 확인한다. API major 변경은 별도 호환 작업이다.

Dalamud의 PvP 제한은 넓고 공식 등록은 미확정이다. 오늘/내일 공개 일정 및 시즌 시작일에 한정하고 경쟁 중 정보나 전술 추천을 추가하지 않는다. 인스턴스 중 숨김만으로 허용이 확정되는 것은 아니다. 해당 범위를 분리해 심사 방향을 확인하고, 거절되면 핵심 달력·이벤트·이용권 기능을 독립 배포할 수 있어야 한다. [공식 제한](https://dalamud.dev/plugin-publishing/restrictions/)

## 10. 구현 순서와 인수 기준

| 단계 | 구현 산출물 | 통과 조건 |
| --- | --- | --- |
| A / 계약 고정 | boundary별 정밀도·근거, v2 DTO/domain, validation report | 시작 확정+종료 미정, 날짜만 확정, 정정/취소, 상대 참조 순환을 명시 처리 |
| B / 오프라인 Core | 기간/월/목록 projection, 계정 scope, 알림 계획 | 윤년·월/연·KST 0시·inclusive/exclusive·빈 필터·동일 날짜 겹침 검사 |
| C / 피드 수신·캐시 | bounded transport, current pointer, pointer commit, typed status | A·B 이후 수행. 아래 네트워크/중단/rollback 시나리오를 fake transport/temp directory로 재현 |
| D / 플러그인 UI·개인 저장 | 명령·상단 탭·개인 일정·이용권 저장 | A·B 이후 오프라인 UI를 먼저 이식하고 C가 통과한 뒤 연결. 재실행/저장 실패/프로필 전환에 사용자 입력 보존. KR·글로벌 compile |
| E / 실제 로컬 알림 | phase별 선택·dedup·보류·재접속 요약 | B·D의 프로필/계획 동작 이후. 전환/재로드/동일 revision 정정/마감 연장/임무 보류에서 중복 방지 |
| F / 순환·운영 연결 | 검증된 전장 규칙, source freshness, 실제 게시 피드 연결 | C·D 및 수집/게시 파일럿이 통과한 뒤. KR 기준점과 인게임 정보 다일 확인. 정책·운영 담당자 확인 후 한정 활성화 |
| G / 출시 준비 | 파일 백업/이관, 한영 리소스, 최소 지원 SDK, 설치 패키지 | 실제 UI 배율·재로드·구독 정리·다운로드 실패·게임 패치 비가용 안내 실기 |

필수 수신·저장 통합 시나리오:

| 주입 조건 | 기대 결과 |
| --- | --- |
| 헤더 즉시 수신 후 본문 정지 / gzip 확장 / Content-Length 없음 | 전체 deadline·실측 바이트 제한, 기존 활성본 유지 |
| 200이지만 HTML 오류 / 잘못된 JSON / region 불일치 / duplicate ID | 검증 실패 코드. 부분 데이터 반영 없음 |
| 미래 schema / 같은 revision의 다른 hash / 낮은 revision | 거부 후 사용 가능 캐시 유지 |
| payload 쓰기 중 crash / pointer 교체 직전·직후 crash | 이전 또는 새 유효본 중 하나만 선택. 반쪽 파일 사용 안 함 |
| 캐시 읽기 전용 / 디스크 가득 참 / 다른 게임 프로세스 writer lock | 구체적 저장 오류, 개인 파일 손상/강제 종료 없음 |
| 업데이트 중 로그아웃/캐릭터 변경/동의 철회/unload | 공개 캐시와 개인 상태의 수명 분리, 해제된 서비스 호출 없음 |
| 같은 content revision + 새 publication health / 한 소스만 수집 실패 | 콘텐츠 재다운로드 없이 상태 반영. 실패 소스를 최신으로 위장하지 않음 |
| 새 publication health 정상 + payload 실패 후 같은 pointer 재시도 | 활성본은 기존 content로 유지. 동일 sequence/digest 재시도에서 복구 성공 |
| payload 미수락 revision을 후속 pointer가 다른 hash로 재광고 | 불변 계약 위반 거부. 운영자는 더 큰 새 content revision으로 정정 |
| 장기 이벤트의 검수일만 오래됐고 활성 content 전체 비교·최근 수집·해당 ID 무변경을 확인 | 오래됨/알림 보류 없음. 실제 미검수 변경의 영향 범위만 경고 |
| 새 공지 후보의 affectedEventIds=[] / 영향 불명 null / 명시 ID 목록 | 각각 기존 무영향 / 해당 source 전체 미확인 / 해당 일정에만 변경 대기 |
| health가 content 13 비교이고 active는 12 / 수집·pending 결과 상한 초과 | 옛 캐시를 새 관측으로 fresh 처리하지 않음 / 불완전 coverage를 명시 |
| Windows 시각 전후 이동 / 장시간 절전 복귀 | 미래 계획 재계산, 지나간 알림 폭주 없음 |

현재 단계에서는 문서·목업의 정적/관리 계산 검사만 수행한다. C# 코드가 존재하지 않으므로 이 표의 구현·빌드·게임 실기 통과를 주장하지 않는다. 테스트를 추가할 때 domain 경계·IO fault injection을 우선하고 구현 문자열을 그대로 대조하는 시험은 피한다.

### 세 차례 검토에서 바뀐 결정

| 루프 | 반박·발견 | 채택한 변경 | 남은 검증 |
| --- | --- | --- | --- |
| 1 / 기본 설계 | phase 전체 precision은 시작 확정/종료 미정을 혼합. HttpClient 헤더 timeout만으로 본문을 보호할 수 없음 | 경계별 tagged model, 전체 deadline·실측 본문 상한, 공용/개인 수명 분리 | C# DTO·stream 어댑터 구현 및 fault injection |
| 2 / 데이터 파이프라인 교차 검토 | content와 별도 status 파일은 파일 수·일관성 경계를 증가. 상태만 수락하면 같은 publication 재시도를 잘못 차단할 수 있음 | inline health current pointer, 두 high-water, actual/advertised content 분리, 동일 digest 재시도 | pointer 부분 실패·다중 프로세스 lock 테스트 |
| 3 / 제품·복구 시나리오 재검토 | 오래된 검수 시각이나 source aggregate hash 차이는 전체 일정의 장애 근거가 아님. 미수락 revision의 hash 변경도 불변 계약 위반 | record별 비교·coverage/활성 revision/영향 ID 기반 freshness, 광고 revision/hash 기억, 개인 수령 체크의 phase별 독립성 | source별 coverage·pending 의미 계약, 실제 운영 파일럿과 인게임 확인 |

이 표는 설계 검토의 수행 기록이며 런타임 시험 통과 기록이 아니다. 검토에서 남긴 실패 주입 항목을 구현 이슈의 인수 조건으로 이관한다.

## 11. 출시를 막는 미해결 항목

1. 피드 v2 계약과 v1 예시의 전환 방식, 공개 endpoint·지원 schema 기간.
2. 실제 KR 전장 기준일·규칙 적용 범위와 공식 저장소에서 허용할 PvP 일정 범위.
3. 캐시 단일 commit과 여러 게임 프로세스의 파일 충돌 동작.
4. 잘못된/오래된 공식 일정 알림의 보류 기준과 운영자의 실제 검수 cadence.
5. 구현에 사용할 정확한 Dalamud·KR·글로벌 참조 조합, unload 시 비동기 작업 종료 계약.
6. 관리자 도구와 일반 플러그인 설정의 분리. 일반 사용자에게 후보 검수·게시 권한을 제공할 이유가 없다.

현재 아키텍처는 별도 상시 서버 없이 시작할 수 있지만, 운영 소유자·실패 감시·정정 대응을 없애지는 않는다. 네트워크가 없어도 쓸 수 있는 개인 일정과 캐시 달력을 먼저 완성한 뒤 원격 배포를 연결하는 순서를 권장한다.
