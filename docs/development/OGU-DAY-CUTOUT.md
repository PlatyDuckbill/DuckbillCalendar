# 오구데이 배경 제거

2026-11-14. Built-in image_gen 편집 사용. 원본: src/DuckbillCalendar.Plugin/Assets/bitty-duckbill.png. 적용 파일: src/DuckbillCalendar.Plugin/Assets/bitty-duckbill-cutout.png. 생성 결과를 alpha를 보존한40×40 PNG로 축소하고 주 DLL과 native preview에 적용했다. 원본을 보존한다.

최종 편집 프롬프트:

> Use case: background-extraction. Edit target: the provided 40x40 FFXIV Bitty Duckbill minion inventory icon. Remove ONLY the blue/gray rounded-square inventory tile, its frame/border, and its pale background glow. Keep the original little brown platypus character, its wide gray-brown bill, cream cheeks/chest, dark eye, feet and curled brown tail, EXACT SAME pose, direction (looking left), colors and painted style. Clean tight cutout of just the minion on genuinely transparent alpha background. Do not redesign or add an outline, shadow, tile, glow, text, or scenery. Intended for a tiny 18-24px calendar decoration. Preserve original silhouette and original artwork as faithfully as possible.

40×40의1600픽셀 중776픽셀이 완전 투명하다. 실물 배치 확인: [달력](../assets/release-v032/ogu-day.png). 원본과 편집본의 출처 표기는 THIRD-PARTY-NOTICES.md에 유지한다.
