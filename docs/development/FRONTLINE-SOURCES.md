# 오늘·내일 전장: 확인된 근거와 남은 기준점

2026-11-12 조사. **공식 KR 문서에서 자정 변경과 개최 규칙은 확인했지만, 현재 날짜와 맵을 연결하는 기준일은 확인하지 못했다.** 따라서 `verified` 전장 규칙 JSON을 만들지 않았다. 현재 확인 불가 표시는 실제 데이터가 없는 상태를 나타낸다.

## 공식 한국 자료

- [공식 전장 가이드](https://guide.ff14.co.kr/pvp/sub04)는7.4/2026-02-03 갱신을 표시하며 매일0시 변경, 당일 도전 가능한 전장은 임무 찾기의 ‘정보’에서 확인하도록 안내한다. 자정을 임의 가정한 것은 아니지만 이 문서에는 특정 날짜의 회전 기준점이 없다.
- [한국7.4 패치노트2824](https://www.ff14.co.kr/news/notice/view/2824)는 규칙 순서를 봉인된 바위섬/워코 치테 → 영광의 평원/워코 치테 → 온살 하카이르/워코 치테 → 외곽 유적지대/워코 치테로 표시한다. 같은 날 신청 가능한 전장과 일일 도전 대상 임무를 별도 표로 설명한다. ‘오늘 전장’이 가용 임무 쌍인지 일일 도전의 배정 대상인지 구분해야 한다.
- 패치 출시일을 회전의 첫 날이라고 선언한 구절은 찾지 못했다. 2026-02-03을 봉인된 바위섬 기준일로 삼거나, 나열 순서를 특정 실제 날짜와 연결할 근거가 부족하다. 7.58 시점까지 같은 위상이 유지된다고 보장하는 날짜별 공식 표도 찾지 못했다.
- 글로벌 사이트의8일·한 맵 회전이나 글로벌15:00 UTC 변경을 한국 규칙으로 복사하지 않는다. 한국 공식 자료의 가용 임무 쌍·자정 표기와 구분되지 않으면 잘못된 맵을 표시할 수 있다.

조사 원문 중 한국7.4 패치노트는 `.preview/curated-research/frontline-patch74.html`에 보존했다. 공식 자료로 확인한 것은 규칙 설명이며 현재 날짜의 맵 관측값이 아니다.

## 게임 클라이언트에서 읽을 수 있는 후보

설치된 KR 참조에는 `FFXIVClientStructs.dll`, `FFXIVClientStructs.xml`, `Lumina.Excel.dll`이 존재한다. XML 메타데이터에서 `ContentInterface.GetContentCloseCycle()` 및 상속형 메서드, `ContentRouletteOpenRuleType`, `AgentContentsFinderInterface.Description`과 `SelectedDutyId` 관련 타입을 확인했다. 이것만으로 ‘오늘의 전장을 반환하는 검증된 메서드’가 있다고 결론 내릴 수는 없다.

개발자 원본 소스:

- [ContentRoulette](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/UI/ContentRoulette.cs)는 roulette row ID와 open-rule type을 가진다. 실제 날짜의 대상 맵을 직접 반환하는 메서드는 이 파일에 없다.
- [ContentInterface](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/UI/ContentInterface.cs)의 `GetContentCloseCycle()`는 cycle ID를 읽는 후보다. 날짜 평가·epoch·서버별 적용을 이 메서드 선언만으로 확인할 수 없다.
- [AgentContentsFinderInterface](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentContentsFinderInterface.cs)의 `Description`, `SelectedDutyId`, `LoadContentRoulette()`가 임무 찾기 정보와 연결된다. 사용자가 이미 임무 찾기를 연 상태에서 갱신된 설명을 읽는 접근을 검토할 수 있다. 실제 KR 클라이언트에서 배정 맵이 포함되는지 확인해야 한다.
- [AgentContentsFinder](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Agent/AgentContentsFinder.cs)의 ContentList/SelectedDuty는 현재 UI 선택과 목록이며, 목록 전체를 오늘의 일일 대상이라고 가정하면 안 된다.
- [ContentsFinder](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/UI/ContentsFinder.cs)의 queue 정보는 현재 신청/매칭 상태다. 전장을 자동 신청하거나 매칭 결과를 얻기 위해 사용하지 않는다. 인스턴스 Director의 현재 ContentFinderCondition은 들어간 임무 관측값이며 내일 일정을 제공하지 않는다.

위 GitHub 소스는 구조를 설명하는 개발자 원본이다. 최신 글로벌 offset/signature를 설치된 KR 게임에 그대로 적용할 근거는 아니다. Dalamud API 버전 일치도 클라이언트 메모리 구조의 호환성을 증명하지 않는다.

## 안전하게 마무리할 구현 순서

1. 설치된 KR 게임의 Lumina `ContentFinderCondition`·`ContentCloseCycle`·`ContentRouletteOpenRule` 관련 sheet/필드를 덤프하는 개발 전용 진단을 만든다. 개인 캐릭터 ID나 계정은 기록하지 않는다. 단순 맵 이름 목록은 기준점 증거가 아니다.
2. 사용자가 연 임무 찾기의 정보에서 현재 대상 맵을 읽거나 명시적 관측을 기록한다. 관측값에는 KST 날짜·시각, 게임 버전, 일일 도전/가용 임무 쌍의 구분을 함께 저장한다. 화면이 닫혀 있거나 설명이 이전 선택의 값이면 새 관측으로 인정하지 않는다.
3. 실제 변경일을 포함한 연속 관측과 공식 KR 개최 규칙을 대조하고, cycle의 epoch/phase를 확인한다. 오늘 한 번 관측한 것으로 글로벌 계산식을 전체 검증했다고 하지 않는다.
4. 확인된 패치 범위와 좁은 유효기간을 가진 rule을 만들고, 새 패치나 모순된 관측에서 무효화한다. 현재 Core의 `verified + anchorDate + sequence + validFrom/Until` 조건을 모두 만족할 때만 내일도 계산한다.
5. 그 전에는 오늘만 실제 관측되면 ‘게임에서 확인’으로 표시하고 내일은 ‘기준 미확인’으로 구분한다. 오래된 캐시나 미검증 추정값을 확정 이름으로 표시하지 않는다.

이번 조사에서는 게임 UI를 자동으로 열거나 메모리를 읽거나 매칭 신청을 하지 않았다. 확인되지 않은 anchor/validity가 필요한 `data/curated/frontline-rule.json`은 생성하지 않았다.
