# DuckbillCalendar 대표 아이콘

2026-11-15 개발 후보: 기존 오리너구리의 얼굴/몸 비율을 유지하며 달력 위에 몸을 기울여 큰 페이지를 넘기는 자세로 바꿨다. 큰 달력·아홉 칸/체크 표시로 기능을 먼저 구분한다. 글자·원형 배지·파란 배경은 제거했고 Fisher/Tweak과 윤곽선·색감을 맞췄다.

- duckbillcalendar-icon.png: 적용/배포용 512×512 RGBA 투명 PNG
- source/duckbillcalendar-icon-master.png: built-in image_gen의 1254×1254 원본
- previews/duckbillcalendar-128.png / duckbillcalendar-64.png: 실제 축소본. 스모크/펄 배경에서 기능 구분과 윤곽 확인
- [icon-prompt.txt](icon-prompt.txt): 생성 도구/전체 프롬프트와 참조 이력

기존 Calendar `71506ee`와 Fisher `aced4cc`의 원본 그림을 직접 참조했고 생성 후에는 alpha/비율을 유지해 크기만 정규화했다. README·설치 manifest IconUrl·빌드 출력 images/icon.png의 같은 경로를 유지한다. 현재 파일은 개발 후보이며 기존 공개 태그/Release/설치 JSON은 바꾸지 않았다.
이전 자연스러운 자세(v7-natural)의 원형 아이콘/원본/프롬프트는 Git 이력에 남는다. 더 오래된 시안·중복 비교본은 기존 artifacts/design-archive/의 보존 기록을 따른다.
