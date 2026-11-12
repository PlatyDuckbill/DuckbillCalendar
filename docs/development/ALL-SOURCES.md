# 전체 일정 소스와 표시

2026-11-12 실제 집계96건, 출처9개를 확인했다. 이벤트 목록2페이지14건 + 공지·점검·패치·시즌76건 + 의미별 공식 날짜 매핑6건이다. 한 사건의 여러 phase는 사건 수에 중복 계산하지 않는다. 원격은 [실제 전체 수집 run](https://github.com/PlatyDuckbill/DuckbillCalendar/actions/runs/43202504781)에서 게시했고 실제 Plugin Workspace가 revision4/96건을 수신·SHA/bytes 검증·캐시 복원했다.

| 범위 | 실제 자료와 표현 | 수집 한계 |
| --- | --- | --- |
| 진행 중 이벤트 | 공식 목록의 연결된 페이지 모두(현재2페이지14건), 참여/수령 phase, 기간 막대/시작·종료 전환 | 최대8페이지, 원문 명시 시각만 자동 변환. 조건형 종료는 미정 유지 |
| 공지 | 최근180일 범위의 목록 최대6페이지·본문60건, 공지 게시 시각 마커 | 기본 달력에서는 게시 phase 숨김, 새소식·공지 필터로 확인 |
| 점검·패치 | 게임/런처 점검 기간과 명시된 패치 일자, 최근2년 패치 목록 | 게시일을 업데이트일로 사용하지 않음. 최신 완료 변경 공지가 예정 종료를 정정 |
| 크컨·PvP 시리즈 | 크컨22 시작11/10, 시리즈12 시작9/8, 시리즈 종료는8.0 점검 시작 전 조건 | 발표되지 않은 다음 시즌/시리즈 날짜는 추측하지 않음 |
| 확장팩 | 백은의 탐구자 정식2027-02-23 날짜 마커, 얼리2027-02-19 18:00 KST 시작 마커 | 정식 개장 시각 미발표, 얼리 종료를 출시일로 임의 지정하지 않음 |
| 상품·혜택 | CE 할인~12/31, 예약 특전 구매~2/19 18시, 기간과 마감 분리 | 날짜만 있는 할인에23:59 같은 시각을 만들지 않음 |
| 대회 | CCRC 선발/신청 기간,2027-01-30 예선/본선·03-14 결선 | 공지 게시와 실제 경기 phase를 구분해 달력에 표시 |

별도 상시 서버·LLM 없이 GitHub Actions가2시간마다 실행한다. `collect-all`은 기존 pointer/payload를 검증한 뒤 이벤트·공지·공식 날짜 규칙을 수집하고 합친다. 수집 실패 소스는 이전 사건·근거를 유지하며 source별 실패/재검토 상태를 기록한다. 원문에서 사라졌다고 삭제나 취소로 판단하지 않는다. 게시 직전 수집 feed와 payload가 ContentRevision/GeneratedAt 외에는 동일한지도 검증한다. 모든 단계는 로컬 준비 후 일반 Git push로 원자적으로 공개한다. 경쟁 writer는 force 없이 거부한다.

```powershell
dotnet run --project tools/CalendarDataCollect -- collect-all data/curated/sources.json - .preview/new-all-collection
# 이전 운영 자료를 쓸 때는 '-' 대신 verify-current로 검증한 payload 파일 지정.
dotnet run --project tools/CalendarDataPublish -- prepare .preview/new-all-collection/feed.json .preview/new-publication
dotnet run --project tools/CalendarDataCollect -- mark-all-health .preview/new-all-collection .preview/new-publication
```

`kr-calendar-collection` artifact는 모든 원문과 manifest를14일 보존한다. 기존 수동 검토용 `kr-events-collection`은 첫 페이지 원문만 보존해 이전 검토 경로를 유지한다. 수동 게시도 운영 feed와 병합하여 공지·출시·시즌을 지우지 않는다.

새 번들 `data/live/kr-calendar.snapshot.json`은96건의 전체 저장본이다. 기존 `kr-events.snapshot.json`은 최초10건의 수동 검토 재현 자료로 남겼다. 최초10건의 검토된 분류를 새 번들에도 적용했고 운영 갱신은 기존 ID의 분류를 유지한다.

모든 소스는 현재 부분 범위이므로 health는partial/coverage false다. 공개 시간 알림의 전체 확인 요건을 자동으로 풀지 않는다. 진행 이벤트 이력500건 및 payload2MiB 등의 보호 상한에 도달하면 이전 자료를 유지하며 역사 정리가 필요하다. 진행 이벤트 상한에 별도 공지 건수를 포함하지 않는다. 공지 수집은 최근 범위이며 사이트 전체 역사를 복제하지 않는다.

전장 오늘/내일은 별도다. [공식 조사](FRONTLINE-SOURCES.md)에서 KR 자정 변경과 가용 임무 쌍의 회전 설명은 확인했으나 현재 날짜와 연결할 기준점은 확인하지 못했다. verified rule을 만들어 값을 채우지 않는다. 검증된 게임 내 관측이나 날짜 기준점 확인 전까지 확인 불가 표시를 유지한다.

화면은 [표시 설계](SCHEDULE-PRESENTATION.md), 확장팩 근거는 [공식 날짜 확인](EXPANSION-SOURCES.md), 공지는 [수집 규칙](NOTICE-COLLECTION.md)을 따른다.
