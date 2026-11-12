# 확장팩·앞서 해보기·PvP의 공식 날짜 매핑

2026-11-12 확인. 공식 이벤트 목록의 기간 필드만으로 알 수 없는 의미를 `data/curated/sources.json`의 검토 가능한 규칙에 등록했다. 수집기는 지정한 공식 페이지의 지정 영역·필수 구절이 일치하는지 확인한 경우에만 해당 날짜를 출력한다. 새로운 날짜를 추론하거나 LLM으로 승인하지 않는다.

| 일정 | 표시 | 근거와 주의점 |
| --- | --- | --- |
| V8.0 백은의 탐구자 정식 출시 | 2027-02-23 날짜 마커 | [공식 GM 라이브](https://www.ff14.co.kr/Eorzea/GMLive)의 고정 게시물18. 서버 오픈 시각은 발표되지 않았으므로 날짜만 저장한다. |
| V8.0 앞서 해보기 | 2027-02-19 18:00 KST 시작 마커 | [공식 콜렉터즈 에디션 안내](https://www.ff14.co.kr/events/2026/NovShop)의 동적 본문에 명시된 시각. 사전예약 종료와 같은 시각이어도 출시·시작 의미를 별도로 표시한다. |
| 콜렉터즈 에디션 20% 할인 | 2026-11-04~12-31 날짜 기간 | 공식 안내의 날짜 구간. 종료 시각이 없으므로 임의의23:59를 넣지 않는다. |
| 사전예약 특전 아이템2종 대상 구매 | 2026-11-04 날짜~2027-02-19 18:00 KST | 예약 특전 아이템 대상 구간이다. 앞서 해보기 권한의 구간별 제공 조건과 동일하다고 단정하지 않는다. 실제 구매 여부는 조회하지 않는다. |
| PvP 시리즈12 | 2026-09-08 날짜 시작 / 8.0 점검 시작 전 종료 | [7.56 패치노트2947](https://www.ff14.co.kr/news/notice/view/2947)과 [실제 점검 완료2949](https://www.ff14.co.kr/news/notice/view/2949)를 함께 확인한다. 시작일은 날짜 단위이며 종료는 미발표 점검에 연결한 condition이다. |
| CCRC 2027 Korea | 선발11/10~12/7, 신청12/8~12/28, 예선·본선2027-01-30, 결선03-14 | [공식 대회 공지20](https://www.ff14.co.kr/events/ccrc_Korea/news/detail/20). 모두 날짜 단위다. 크컨 등급전 시즌이나 PvP 시리즈와 다른 대회이므로 notice로 분류한다. |

크컨 시즌22와 V7.58의 실제 개시는 별도 notice 수집기가 완료 공지3162를 처리한다. 예정 점검 종료19:00를 실제 종료18:15보다 우선하지 않는다. 다음 PvP 시리즈13의 시작 일시를 단순히 V8.0 출시 날짜로 만들어 넣지 않는다. 시리즈12 종료에 연결한 `kr:target:v8-maintenance-start`는 미발표 점검 시작이므로 `unresolved`다. 얼리 액세스·정식 출시·업데이트 점검 시작은 서로 다른 경계다.

## 원문 구성요소와 검증

NovShop의 초기 HTML에는 앞서 해보기 시각이 들어 있지 않다. 공식 `image.ff14.co.kr/event/2026/novshop/js/ui.min.js`의 정적 HTML 템플릿에 시각이 명시되어 있다. 이 파일은 실행하지 않고 바이트와 필수 구절만 읽는다. IIS의 `application/x-javascript`를 제한적으로 지원한다. evidence URL은 독자가 읽을 수 있는 NovShop 공식 안내이고, evidence note에 직접 수집한 구성요소 URL과 SHA256의 대상이 구성요소 바이트임을 명시한다. HTML 페이지 hash로 위장하지 않는다.

수집은 설정된 www/image 공식 HTTPS 호스트, redirect·압축·쿠키 없는 HTTP 클라이언트, 소스별20초 deadline과 실제 스트림2MiB 상한을 사용한다. 일반 HTML의 script/style/template 내용은 텍스트 검증에서 제외한다. `.view_contents`, 고정 게시물18, 기간/가격 영역 등 구절 범위를 제한해 푸터나 다른 기사에 있는 날짜로 승인하지 않는다.

필수 구절 변경·페이지 삭제·HTTP 오류가 생기면 새로운 날짜를 받아들이지 않는다. 이전 이벤트가 있으면 기존 경계·증거와 함께 재검토 대기 문구를 유지하고 `PendingEventIds`를 반환한다. 처음 수집하는데 검증되지 않은 매핑은 출력하지 않는다. 주기적 수집을 계속하면서 변경된 공식 사실에 대한 규칙 갱신을 검토할 수 있다.

## 구현과 확인

`CuratedSources.CollectAsync(rulesPath, outputDirectory, previousFeed, http, ct)`는 `CuratedCollectionResult`의 `Feed`, `PendingEventIds`, `SourceResults`를 반환한다. 새 output 디렉터리에 원문 `.html/.js`, `curated-feed.json`, `curated-status.json`을 보존한다. 이전 feed는 정상 production 계약을 검증한 입력만 받는다.

```powershell
dotnet run --project tools/CalendarDataCollect -- collect-curated data/curated/sources.json .preview/new-curated-run -
```

`data/curated/snapshot.json`과 `verification.json`은 실제6소스 수집·6이벤트·대기0으로 생성했고 production validator를 통과했다. 원문은 `.preview/curated-live-final-20261112/`에만 보존한다. `CuratedSourcesChecks`15개는 구절/날짜 변경·범위 밖 구절·스크립트 제외·정적 템플릿 비실행·바이트 근거·날짜 정밀도·기존 이벤트 유지·신규 미검증 차단·반복 대기 문구·외부 호스트 차단을 확인한다. 공개 시간 알림의 전체 source coverage를 승인한 것은 아니다.

현재 페이지가 해당 정적 템플릿 URL(버전 query 포함)을 계속 참조하는지도 함께 검증한다. 지문이 바뀌면 이전 구성요소에 남은 날짜를 최신으로 재사용하지 않고 재검토 대상으로 남긴다. 이 결합 검증2개를 추가해 Curated 검사17개가 통과했다.
