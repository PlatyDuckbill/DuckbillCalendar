# 공지·패치·점검·크컨 시즌 수집

2026-11-12 실제 공식 목록과 본문을 확인하고 수집했다. [공지 목록](https://www.ff14.co.kr/news/notice), [공식 패치 목록](https://www.ff14.co.kr/news/patchnote)을 사용하며 사이트 스크립트·이미지·외부 리소스를 실행하지 않는다.

현재 범위는 최근180일 안의 목록을 최대6페이지·60개 본문까지 수집하는 제한된 창이다. 180일 전체를 항상 포함한다는 뜻이 아니며 전체 coverage는 false다. 공식 패치 카드의 직접 명시 날짜는 최근2년 범위를 포함한다. 과거 전체 수천 공지를 다시 긁는 방식은 아니다.

- 공지 게시: 목록 제목·게시 날짜와 본문 제목·게시 시각을 대조한다. category notice, point label ‘공지 게시’로 저장한다. 게시일을 점검·패치 적용일로 사용하지 않는다.
- 게임·런처 점검: 본문에서 게임·런처 점검 절의 명시된 연·월·일과 시각 범위만 읽는다. 날짜를 생략한 자정 넘김은 추측하지 않는다. 점검 공지의 예정 경계는 tentative, 완료 공지의 명시 경계는 confirmed다.
- 주요 패치: 공식 패치 카드의 날짜는 카드의 기본 대형 버전에만 연결한다. 예를 들어7.5카드의 날짜를7.58링크에 재사용하지 않는다. 점검 공지에 명시된 V버전의 적용일은 별도 date point로 추가한다. 완료 전에는 예정으로 표시하며 적용 분 단위 시각을 추측하지 않는다.
- 크컨 시즌: 게임 점검 날짜와 ‘기존 시즌 종료, 새 시즌 시작’ 문장이 함께 있는 경우만 시즌 시작 date point를 만든다. 글에 시즌 번호만 있거나 게시일만 있으면 시즌 시작일을 만들지 않는다.
- 후속 정정: 같은 버전·날짜의 게임 점검은 canonical ID를 공유하고 게시 시각 순으로 병합해 최신 완료/변경 공지가 앞선 예정 시간을 덮는다. 예전 증거는 참조 여부에 따라 정리한다.

실제 확인 사례: [3162 완료 변경 공지](https://www.ff14.co.kr/news/notice/view/3162)에 V7.58점검이2026-11-10 12:00~18:15로 명시돼 있으며 크컨 시즌22시작도 안내돼 있다. [3159 최초 공지](https://www.ff14.co.kr/news/notice/view/3159)의 예정 종료19:00을 최종18:15로 정정했다. 크컨 시작은 이 점검 완료일인2026-11-10 날짜로 표시하며 시각은 따로 만들어 내지 않는다.

실제 수집 결과는76건: 공지 게시60, 게임·런처 점검6, 패치 날짜9, 크컨 시즌 시작1이다. 원문·목록·manifest는 `.preview/notices-live-20261112/`, 검증된 feed는 같은 디렉터리의 `feed.json`이다. 원문 HTML은 Git에 넣지 않는다. 매 실행마다 본문을 다시 수집하고 hash를 기록하므로 게시일이 같아도 본문 변경을 놓치지 않는다. 목록 누락을 취소나 삭제로 해석하지 않고 기존 feed를 유지한다.

각 요청은 고정 공식 route만 허용하고 redirect·쿠키·압축을 사용하지 않는다. 요청별20초 deadline, 실제 스트림2MiB, 전체 수집32MiB 상한을 둔다. manifest는 마지막에 쓰며 각 파일의 SHA256/바이트·경로·목록/본문 대응을 준비 단계에서 다시 확인한다. 오류 페이지·빈 목록·파싱 불일치·제목/날짜 충돌·원문 변조·상한 초과는 실패시켜 기존 운영 feed를 보존한다.

도구 API는 `NoticeCollector.CollectAsync(directory, http, cancellationToken)`와 `PrepareAsync(directory, previousFeed)`다. CLI는 아래와 같다.

```powershell
dotnet run --project tools/CalendarDataCollect -- collect-notices .preview/new-notices
dotnet run --project tools/CalendarDataCollect -- prepare-notices .preview/new-notices previous-feed.json .preview/with-notices.json
```

처음 병합할 때만 previous 자리에 `-`를 쓴다. 출력파일은 덮어쓰지 않는다. `NoticeChecks.RunAsync()`는 가짜 HTTP와 합성 HTML로 기존 오류 패턴과 경계를 검사한다. 확장팩·얼리 액세스·PvP시리즈의 별도 공식 자료는 curated source 검증 경로와 병합한다.
