# 데이터 계약과 운영 설계

상태: 구현 제안. 저장소의 JSON은 계약 예시이며 배포용 피드가 아니다. **현행 결정은 [3회 검토](architecture/DECISIONS.md)·[수집 설계](architecture/DATA-PIPELINE.md)·[Plugin 설계](architecture/PLUGIN.md)와 v2 계약을 따른다.** 아래 v1 모델은 현재 HTML 목업용이며 운영 계약으로 고정하지 않는다.

## 1. 데이터의 소유권

| 영역 | 소유자·저장소 | 갱신 |
| --- | --- | --- |
| 공개 일정 | 운영자가 검수한 `data/curated/` → 정적 JSON | 수집 후보 확인 또는 직접 편집 후 게시 |
| 전장/반복 규칙 | patch 범위와 근거를 붙인 versioned rule | 패치별 수동 검증 |
| 개인 이용권 | 로컬 계정 프로필 | 사용자 입력. 공개 이벤트 보상 안내로 자동 가산하지 않음 |
| 개인 일정·완료 체크 | 로컬 전체/계정/캐릭터 scope | 사용자 입력 |
| 관심·알림·숨김 | 로컬 안정 ID 참조 | 피드 업데이트 후 유지 |

공개 피드 ID는 `kr:event:766`처럼 원본 서비스·게시물 ID를 사용한다. 동일 이벤트의 참여와 수령은 `phaseId`로 구분한다. 제목이 바뀌어도 같은 ID를 유지한다. 개인 기록은 무작위 로컬 ID를 사용하며 외부로 전송하지 않는다.

## 2. v1 목업 일정 모델과 v2 변경

[feed.example.json](../data/feed.example.json)은 다음 계약을 보여준다.

실제 구현에서는 [v2 계약](../data/contracts/v2/README.md)처럼 시작·끝을 각각 instant/date/condition/unknown으로 분리한다. phase 전체 precision/certainty를 운영 모델로 쓰지 않는다. 추론한 시작 시각에는 두 사건의 출처와 inferred 근거를 연결한다.

- Feed: `schemaVersion`, 단조 증가 `revision`, `region`, `generatedAt`, `lastCollectedAt`, `lastReviewedAt`, `isExample`, `events`, `withdrawnIds`.
- Event: `id`, `revision`, `category`, `title`, `status`, `sources`, `phases`.
- Phase: `id`, `label`, `startsAt`, `endsAt`, `startCondition`, `endCondition`, `precision`, `endSemantics`, `certainty`.
- Source: `url`, `publishedAt`(불명이면 null), `checkedAt`, `method`(html/manual/game-observation), `note`.
- 상태는 `scheduled`, `active`, `completed`, `cancelled`, `postponed`; 확실성은 `confirmed`, `tentative`, `unknown`. `completed`는 기간 종료 사실이며 플레이어의 완료 여부가 아니다.
- `precision`은 `minute`, `date`, `conditional`; 날짜만 확인한 경우 offset 시각 대신 `startDate/endDate`를 사용한다. 예제는 분 단위/조건형만 포함한다.
- 시간은 RFC3339 offset 포함으로 저장하고 내부 계산은 UTC instant로 한다. 기본 표시는 Asia/Seoul. ET는 이 제품의 공식 마감 기준이 아니다. 글로벌은 별도 `region`·timezone·규칙으로 도입한다.
- `endSemantics`는 `exclusive` 또는 `inclusive-minute`. 공식 ‘23:59까지’는 원문값을 유지하면서 분 단위 포함으로 모델링하고 계산상 다음 분을 exclusive 경계로 쓴다. 초 단위 정확성을 주장하지 않는다. ‘점검 시작 전’은 exclusive다.
- 날짜만 있는 마감은 날짜 정보만 보여주고 시·분 단위 카운트다운과 1시간 전 알림을 제공하지 않는다. 미발표 시각은 `null`; 조건 설명과 해결할 대상 ID를 보존한다. 일정 삭제와 미정은 서로 다르다.
- 공개 일정의 상태와 현재 시각의 관계가 모순되면 검수 경고를 낸다. 취소·연기는 상태를 우선한다. 진행/종료 표시는 선택한 시각으로 계산하며 피드의 단순 `active` 문자열만 믿지 않는다.

상대 조건은 원문 문자열과 구조화된 참조를 함께 둔다. 예: `beforePatchMaintenance:8.0` 또는 `beforeSeasonEnd:22`. 날짜가 발표되면 그 근거를 추가하고 기존 phase ID를 유지해 수정한다. 패치일 추정에서 상대 종료를 자동 확정하지 않는다.

**공지 자체는 게시일만으로 달력의 실행 일정이 되지 않는다.** 원문 목록에는 남기되 실행 시점이 확인된 단계만 달력에 올린다. 하나의 공지에 점검·패치·시즌이 있으면 공지 ID를 출처로 공유하고 의미별 event ID를 부여해 중복 요약을 제어한다.

## 3. 파이프라인

```text
collect (public HTML or manual input)
  → normalize (candidate only, source hashes)
  → compare (new / changed / missing / suspicious)
  → review (operator fixes dates, region, scope, status)
  → validate (schema + domain checks)
  → publish (immutable revision file, then manifest)
  → fetch/cache (client verifies before atomic replace)
```

권장 초기 운영은 GitHub 저장소 기반 수동 검수다. 계정 로그인이나 별도 관리자 서버를 만들지 않아도 운영할 수 있다. 목업의 검수 화면은 UI 방향을 설명하며 GitHub 권한·API를 사용하지 않는다.

### 수집과 검수

- 목록 수집은 2시간 간격 제안. 정각 집중을 피한 UTC cron `17 */2 * * *`가 후보. 예약 지연에 대비한 수동 실행을 둔다.
- 최근 7~30일 공지와 모든 진행 이벤트를 재확인한다. 월/연 단위 장기 이벤트는 원문/정정 공지를 함께 추적한다.
- `checkedAt`과 콘텐츠 해시는 분리한다. 변경 없는 재수집은 새 일정 revision을 만들지 않고 수집 상태만 갱신한다. 검수 시각은 수집 성공만으로 갱신하지 않는다.
- 수집 건수가 0, 직전 대비 50% 이상 감소, 날짜 파싱 실패, phase 순서 역전, 예상치 못한 redirect이면 자동 게시를 막고 마지막 정상본을 유지한다. 수치는 운영 중 조정한다.
- 이미지/OCR·모호한 날짜·미정 패치·정정 공지는 항상 수동 검수한다. 후보 추출 확률이 높아도 `confirmed`와 혼용하지 않는다.
- 초기 운영은 오전/저녁 검수 각 1회 제안. 급한 점검·정정은 수동 파일 수정 후 같은 검증 경로로 게시한다. 담당자 부재 시 피드가 오래됐다는 표시를 유지한다.

### 정정 우선순위

1. 해당 일정에 대한 최신 공식 정정/완료 공지.
2. 현재 공식 상세 페이지.
3. 공식 목록의 기간.
4. 공식 방송·예고의 운영자 확인 기록.
5. 제보·추정은 후보 상태.

출처 간 불일치는 자동으로 최신 게시일 하나를 선택하지 않는다. 담당자가 적용 범위를 확인한다. 단계 변경은 이전값·새값·출처·적용 시각을 변경 기록으로 남긴다. 이미 알림을 받은 마감이 연장되면 로컬에 ‘일정 변경’을 한 번 표시하고 새 마감의 알림을 다시 예약한다.

원문 목록에서 사라진 것은 종료/취소의 증거가 아니다. `missing` 후보로 남겨 검토한다. 철회가 확정되면 `withdrawnIds` 또는 `cancelled` 기록을 최소 90일 유지하고, 오래된 전체 snapshot에서 다시 살아나지 않게 revision을 확인한다.

### 게시

권장 endpoint 초안(현재 미게시):

```text
https://raw.githubusercontent.com/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/current.json
https://raw.githubusercontent.com/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/revisions/000001.json
```

current는 `publicationSequence`, `content{revision,schemaVersion,region,path,sha256,bytes}`, `health.sources[]`, `generatedAt`을 갖는다. 콘텐츠 변경과 상태 게시 sequence를 분리한다. 같은 content revision 파일을 덮어쓰지 않고 payload를 먼저 올린 뒤 current를 갱신한다. 상태만 바뀌어도 publicationSequence를 올린다. SHA-256은 내용 일치 확인이며 같은 저장소 자체 침해에 대한 서명 검증을 대신하지 않는다. 서명 도입은 별도 위협 모델과 키 운영을 설계한 뒤 결정한다.

등록·검증·게시 CI는 구현하지 않은 단계다. 산출물을 검증하지 않고 바로 publish하는 workflow를 이번 저장소에 추가하지 않는다.

## 4. 클라이언트 갱신과 장애

- 최초 실행은 번들 snapshot으로 시작한다. 원격 갱신은 명시적 설정으로 켜고 접속 도메인과 마지막 갱신을 표시한다. 선택 전에는 자동 요청하지 않는다.
- 켠 경우 시작 시 TTL을 확인하고 이후 1시간 + 0~10분 jitter 제안. 6시간은 선택 저빈도 설정이다. 새로고침 버튼도 최소 5분 간격으로 제한한다. 로그인된 공식 페이지는 요청하지 않는다.
- `HttpClient` 재사용, 10초 timeout, 로딩 작업 1개, 취소 토큰, 2MiB/10,000 event 상한, redirect 최종 호스트 allowlist 검사를 둔다. TLS를 약화하지 않는다.
- current의 content revision이 같고 유효 payload가 이미 있으면 다시 읽지 않되 health 갱신은 수락한다. 아직 payload를 받지 못한 같은 publicationSequence는 같은 pointer digest에 한해 재시도할 수 있다. ETag/Last-Modified는 제공되는 경우 사용한다. 304는 해당 계층의 확인 성공이며 내용 검수 시각을 바꾸지 않는다.
- SHA·schema major·KR region·중복 ID·잘못된 기간·초과 크기·허용되지 않은 링크를 검사한 뒤 임시 파일 → 원자 교체. 전체 피드를 검증하기 전 기존 캐시를 지우지 않는다.
- 실패 시 15분→1시간→6시간의 제한된 backoff. 오프라인에서는 캐시 사용. 캐시가 없으면 수동 일정만 표시하고 ‘공개 일정 없음’을 알린다.
- 수집 확인 경과·completeCoverage·comparedContentRevision과 active revision 일치·영향 ID의 미검수 변경·규칙 유효 범위로 오래됨을 판단한다. 승인 hash는 내부 record별로 비교하고 공개 source aggregate hash는 진단용이다. 검수 72시간 경과만으로 변하지 않은 장기 이벤트를 무효화하지 않는다. 출처의 실패와 현재 기기의 다운로드 실패도 구분한다.
- 데이터가 오래돼도 이용권/개인 알림은 유지한다. 오래된 공개 일정 알림은 기본 중지하고 사용자가 선택할 수 있게 한다. 게임 패치 범위가 끝난 전장 예측은 즉시 미확인으로 전환한다.
- 원문은 allowlist의 HTTPS URL만 사용자 클릭으로 연다. 피드의 HTML·스크립트·명령은 실행하지 않는다. 텍스트 길이를 제한해 표시한다.
- 개인 account/character ID, 알림 선택, 만료시각, 사용 기록은 요청 파라미터·telemetry로 전송하지 않는다. 통상적인 HTTPS 통신으로 제공자에게 IP·HTTP 메타데이터가 전달되는 사실은 안내한다.

## 5. 전장과 기간 규칙

전장 회전 규칙에는 `region`, `rulesetId`, `validFrom`, `validUntil`, `resetLocalTime`, `timezone`, `sequence`, `anchorDate`, `anchorIndex`, `sources`, `verifiedGameBuild`가 필요하다. 순서를 확보해도 기준점이 미확인이면 `anchorDate: null`이고 실제 전장 예측을 만들지 않는다.

계산은 선택 날짜를 해당 시간대의 일 경계로 바꾼 뒤 `positiveModulo(localDayIndex - anchorDayIndex + anchorIndex, sequence.length)`로 한다. UTC 날짜를 그대로 넣지 않는다. 중간 점검·새 필드·한시적 중단은 기간별 override로 처리한다. 서로 겹치는 규칙은 검증 오류다.

추가 조사로 7.5의 8일 순서(봉인→영광→온살→워코→봉인→외곽→온살→워코)를 확보해 예시 피드에 기록했다. 실제 적용 기준일은 아직 미확인이다. 목업은 오늘/내일 이름을 배치 예시로 표시하고 이를 명시한다. 검증된 기준점을 받으면 두 날짜를 계산하는 경로를 제공한다. 독립 전장·PvP 탭은 두지 않는다.

시리즈는 경험치·단계 보상 기간, CC 시즌은 등급전 기간으로 분리한다. 두 기간이 함께 바뀐다고 가정하지 않는다. 랭킹·등급 보상 수령 기간도 별도 phase다. 전투·PvP 인스턴스 안에서는 관련 알림과 보조 화면을 숨기는 보수적인 제품안이며 공식 심사 허용을 보장하지 않는다.

시즌·시리즈의 알려진 `startsAt`은 종료 미정과 무관하게 달력 날짜 항목을 만든다. 여러 날짜의 확정 기간은 사용자가 기간 막대 또는 시작·종료 항목을 선택한다. 막대는 phase 단위로 주 경계에 잘라 행을 배정하고, exclusive 종료는 경계 직전 날짜까지만 포함한다. 표시 옵션은 로컬 설정이며 원본 기간을 변경하지 않는다.

## 6. 로컬 알림과 저장

- 기본 알림: 사용자가 관심으로 지정한 분 단위 확정 마감 24시간/1시간 전. 이용권은 7일/3일/1일/만료 시점 선택. 날짜만 확정된 경우 날짜 단위 알림만 가능.
- 중복 키: `profile + eventId + phaseId + effectiveBoundary + leadTime`. 텍스트/단순 revision 변경만으로 같은 알림을 재발송하지 않는다. 마감 자체가 바뀌면 새 경계로 재예약한다.
- 게임이 닫힌 동안 알림을 보낼 수 없다. 접속 시 이미 지난 모든 알림을 재생하지 않고 최근 마감 요약 1회만 표시한다. 전투·임무 중에는 지연하고 나올 때 묶어 보여준다.
- 표시/차트 계산은 UTC, 짧은 대기 타이머는 단조 시계. PC 시간 큰 변경을 감지하면 재계산한다.
- 캐릭터 전환 시 generation 토큰으로 오래된 비동기 결과를 무시한다. unload에서 구독·취소 토큰·작업을 정리한다. 입력 초안/기존 정상 데이터는 저장 실패 시 유지한다.
- 로컬 JSON에 `schemaVersion`을 두고 마이그레이션 전에 백업한다. 민감한 로그인 토큰은 저장 대상이 아니다. scope 삭제·프로필 삭제는 UI에서 대상과 영향을 명확히 보여준다.

## 7. 필요한 검증

기간 모델의 연/월/윤년·0시·포함 마감, 미정→확정·연장·취소·중복 게시물·빈 수집·오류 HTML 200·잘못된 offset·회전 기준점 전후·부분 다운로드·재로드 알림 중복이 핵심이다. 실제 구현 시 domain 검증과 파서 fixture 검증을 추가한다. 이번 단계는 피드 예시 정합성과 목업 조작을 확인하며 런타임 수집기를 검증한 것으로 보고하지 않는다.
