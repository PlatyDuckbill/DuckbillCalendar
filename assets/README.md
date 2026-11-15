# DuckbillCalendar 대표 아이콘

2026-11-15 공개0.5.0.0: 원본 옆모습을 직접 편집해 달력 오른쪽에서 짧은 앞발로 페이지를 넘기는 자세로 정리했다. 윗부분에 올라탄 이전 후보 대신 원본 얼굴/몸통 형태를 우선했다. 큰 달력·아홉 칸/체크 표시로 기능을 먼저 구분하며, 아래쪽에 얇은 책상 면과 빈 종이 귀퉁이를 더했다. 글자·원형 배지·파란 배경은 제거했고 Fisher/Tweak과 윤곽선·색감을 맞췄다.

- duckbillcalendar-icon.png: 적용/배포용 512×512 RGBA 투명 PNG
- source/duckbillcalendar-icon-master.png: built-in image_gen의 1254×1254 원본
- previews/duckbillcalendar-128.png / duckbillcalendar-64.png: 실제 축소본. 스모크/펄 배경에서 기능 구분과 윤곽 확인
- [icon-prompt.txt](icon-prompt.txt): 생성 도구/전체 프롬프트와 참조 이력

기존 Calendar `71506ee`에서 이어진 `f90eb61`의 원본에 작은 주변 면만 더했다. 배포용은 alpha>16의 그림 경계(28,117 / 1199×1004)로 아주 옅은 외곽 잔상/여백을 정리하고 비율을 유지해 최소7% 여백으로 축소했다. 생성 원본은 수정하지 않았다. README·설치 manifest IconUrl·빌드 출력 images/icon.png의 같은 경로를 유지한다. 현재 PNG는 v0.5.0.0 태그와 ZIP에 게시했고 익명 다운로드/아이콘 해시와 설치 목록 반영을 확인했다. 과거 공개 태그/ZIP은 보존한다.
이전 자연스러운 자세(v7-natural)의 원형 아이콘/원본/프롬프트는 Git 이력에 남는다. 더 오래된 시안·중복 비교본은 기존 artifacts/design-archive/의 보존 기록을 따른다.
