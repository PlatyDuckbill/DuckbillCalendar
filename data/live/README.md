# 실제 목록에서 검토한 번들 일정

`kr-events.snapshot.json`: 공식 KR 이벤트 목록 첫 페이지에서 확인한 실제 일정10건. `isExample=false`이며 전체 사이트 수집을 뜻하지 않는다. 원본: [공식 이벤트](https://www.ff14.co.kr/news/event).

`kr-events.collection.json`: 확인시각·원문 SHA256·바이트 수·부분수집 범위. `kr-events.review.json`: 수집 원문과 승인한 후보hash·분류·검토시각. 원문은 `.preview/live-events-20261112-0853/source.html`에 보존했다(소스 저장소에서는 제외).

기간 미정은 날짜를 만들지 않았으며 원문 문구를 note에 보존했다. 번들만으로 실시간 수집 성공·변경 여부·공개 시간 알림을 보증하지 않는다. 갱신 절차와 GitHub Actions 운영 조건은 [실제 데이터 안내](../../docs/development/LIVE-DATA.md)를 참고한다.
