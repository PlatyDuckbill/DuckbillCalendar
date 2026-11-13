# Third-party dependencies

The developer-only `CalendarDataImport` tool uses **AngleSharp 1.10.1**, pinned in its project and package lock.

- Copyright 2013–2026, AngleSharp
- License: MIT, as declared in the NuGet package metadata
- Source: https://github.com/AngleSharp/AngleSharp
- Package source revision: `d3cd1a023256b06497bc0cf803702fe94661f358`
- Package license: https://licenses.nuget.org/MIT
- Purpose: parse saved public-event HTML into unreviewed local candidates. No scripts or external resources are executed by the parser.

AngleSharp is not referenced by or bundled with the game plugin. Build-time Dalamud SDK/Packager and the installed Dalamud runtime retain their own licenses. Runtime reference DLLs are not copied into this repository or distributed by this project.

DuckbillFisher was consulted for architecture and API usage. Its implementation files and assets have not been imported into DuckbillCalendar. If code is transferred later, preserve its AGPL-3.0 and third-party conditions.

## 전장 데이터

[에테르웨이](https://aether-way.com/frontline)의 공개 날짜별 한국 서버 일일 전장 응답을 사용합니다. 커뮤니티 제공 정보이며 공식 검증 자료로 표시하지 않습니다. 제공 사이트의 코드를 포함하지 않습니다. 네트워크 없는 미리보기와 parser 검사에는2026-11-13에 받은2026-11 응답 fixture를 사용합니다.

## 오구데이 아이콘

`Assets/bitty-duckbill.png`는 [공식 에오르제아 데이터베이스의 Bitty Duckbill](https://na.finalfantasyxiv.com/lodestone/playguide/db/item/87347462fe9/)에 표시된 꼬마친구 아이콘 원본입니다. [이미지 출처](https://lds-img.finalfantasyxiv.com/itemicon/bd/bde3cd298cc6c320cf167bc5410e4b1da08d2852.png?n7.58). FINAL FANTASY XIV 관련 이미지의 저작권은 SQUARE ENIX에 있습니다. 이 아이콘은 매년5월9일의 작은 장식으로 사용하며 실행 중 외부 다운로드를 하지 않습니다. Native 검사용 RGBA fixture는 같은 PNG를 픽셀 형식만 변환한 값이며 두 파일의 SHA-256을 검사합니다.

배경 제거 요청 반영: 실행 시에는 원본을 참조해 built-in image_gen으로 배경을 제거하고40×40으로 정규화한 Assets/bitty-duckbill-cutout.png를 사용합니다. 원본 PNG는 출처 확인용으로 보존하며 DLL에는 편집본만 포함합니다. Native RGBA fixture도 편집본에서 변환합니다.

## 대표 아이콘

assets/duckbillcalendar-icon.png는 사용자의 DuckbillTweak·DuckbillFisher 대표 아이콘을 스타일 참조로 삼아 built-in image_gen으로 새로 제작했습니다. 오구데이용 공식 게임 아이콘과는 별개의 이미지입니다. 원본/최종 프롬프트·참조와 적용 경로는 assets/README.md에 기록합니다.
