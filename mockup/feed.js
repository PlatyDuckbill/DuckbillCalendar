/* Generated local snapshot from data/feed.example.json. No network request. */
window.CALENDAR_FEED = {
  "schemaVersion": 1,
  "revision": 1,
  "region": "KR",
  "generatedAt": "2026-11-11T11:55:00Z",
  "lastCollectedAt": "2026-11-11T11:55:00Z",
  "lastReviewedAt": "2026-11-11T11:55:00Z",
  "isExample": true,
  "withdrawnIds": [],
  "events": [
    {
      "id": "kr:event:766",
      "revision": 1,
      "category": "web-event",
      "title": "모험가 맞춤 헬스케어 프로그램",
      "status": "active",
      "sources": [
        {
          "url": "https://www.ff14.co.kr/news/event",
          "publishedAt": null,
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "html",
          "note": "목록의 참여/수령 기간 확인"
        },
        {
          "url": "https://www.ff14.co.kr/news/notice/view/3153",
          "publishedAt": "2026-10-23T18:30:00+09:00",
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "manual",
          "note": "기간 연장 정정 근거"
        }
      ],
      "phases": [
        {
          "id": "participation",
          "label": "참여 기간",
          "startsAt": "2026-10-13T00:00:00+09:00",
          "endsAt": "2026-11-11T23:59:00+09:00",
          "startCondition": null,
          "endCondition": null,
          "precision": "minute",
          "endSemantics": "inclusive-minute",
          "certainty": "confirmed"
        },
        {
          "id": "reward-claim",
          "label": "보상 수령 기간",
          "startsAt": "2026-10-13T00:00:00+09:00",
          "endsAt": "2026-12-09T23:59:00+09:00",
          "startCondition": null,
          "endCondition": null,
          "precision": "minute",
          "endSemantics": "inclusive-minute",
          "certainty": "confirmed"
        }
      ]
    },
    {
      "id": "kr:event:768",
      "revision": 1,
      "category": "in-game-event",
      "title": "마법자판 난타전",
      "status": "active",
      "sources": [
        {
          "url": "https://www.ff14.co.kr/news/event",
          "publishedAt": null,
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "html",
          "note": "종료 날짜가 아닌 조건으로 표기됨"
        }
      ],
      "phases": [
        {
          "id": "participation",
          "label": "참여 기간",
          "startsAt": "2026-10-28T17:00:00+09:00",
          "endsAt": null,
          "startCondition": null,
          "endCondition": {
            "type": "beforePatchMaintenance",
            "target": "8.0",
            "text": "V8.0 업데이트 전까지"
          },
          "precision": "conditional",
          "endSemantics": "exclusive",
          "certainty": "confirmed"
        }
      ]
    },
    {
      "id": "kr:maintenance:3162",
      "revision": 1,
      "category": "maintenance",
      "title": "V7.58 업데이트 점검",
      "status": "completed",
      "sources": [
        {
          "url": "https://www.ff14.co.kr/news/notice/view/3162",
          "publishedAt": "2026-11-10T18:00:00+09:00",
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "manual",
          "note": "완료 공지의 실제 점검 종료 시각"
        }
      ],
      "phases": [
        {
          "id": "maintenance",
          "label": "게임·런처 점검",
          "startsAt": "2026-11-10T12:00:00+09:00",
          "endsAt": "2026-11-10T18:15:00+09:00",
          "startCondition": null,
          "endCondition": null,
          "precision": "minute",
          "endSemantics": "exclusive",
          "certainty": "confirmed"
        }
      ]
    },
    {
      "id": "kr:cc:season:22",
      "revision": 1,
      "category": "pvp-season",
      "title": "크리스탈라인 컨플릭트 시즌 22",
      "status": "active",
      "sources": [
        {
          "url": "https://www.ff14.co.kr/news/notice/view/3160",
          "publishedAt": null,
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "manual",
          "note": "시즌 전환과 이전 시즌 등급 보상 기한 확인. 정확한 게시 시각은 미수집"
        },
        {
          "url": "https://www.ff14.co.kr/news/notice/view/3162",
          "publishedAt": "2026-11-10T18:00:00+09:00",
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "manual",
          "note": "시즌 시작을 점검 완료 18:15에 연결한 해석"
        }
      ],
      "phases": [
        {
          "id": "season",
          "label": "시즌 기간",
          "startsAt": "2026-11-10T18:15:00+09:00",
          "endsAt": null,
          "startCondition": null,
          "endCondition": {
            "type": "unannounced",
            "target": null,
            "text": "종료 일시 미발표"
          },
          "precision": "conditional",
          "endSemantics": "exclusive",
          "certainty": "confirmed"
        }
      ]
    },
    {
      "id": "kr:cc:season:21:rewards",
      "revision": 1,
      "category": "reward",
      "title": "CC 시즌 21 등급 보상 수령",
      "status": "active",
      "sources": [
        {
          "url": "https://www.ff14.co.kr/news/notice/view/3160",
          "publishedAt": null,
          "checkedAt": "2026-11-11T11:55:00Z",
          "method": "manual",
          "note": "다음 시즌 종료에 종속된 수령 기한"
        }
      ],
      "phases": [
        {
          "id": "reward-claim",
          "label": "등급 보상 수령",
          "startsAt": "2026-11-10T18:15:00+09:00",
          "endsAt": null,
          "startCondition": null,
          "endCondition": {
            "type": "beforeSeasonEnd",
            "target": "22",
            "text": "시즌 22 종료 전까지"
          },
          "precision": "conditional",
          "endSemantics": "exclusive",
          "certainty": "confirmed"
        }
      ]
    },
    {
      "id": "kr:pvp:series:12",
      "revision": 1,
      "category": "pvp-series",
      "title": "PvP 시리즈 12",
      "status": "active",
      "sources": [
        {
          "url": "https://www.ff14.co.kr/news/notice/view/2947",
          "publishedAt": "2026-09-08T15:44:00+09:00",
          "checkedAt": "2026-11-11T12:30:00Z",
          "method": "manual",
          "note": "시리즈 12 시작과 패치 8.0 점검 전 종료 조건 확인"
        },
        {
          "url": "https://www.ff14.co.kr/news/notice/view/2949",
          "publishedAt": "2026-09-08T18:15:00+09:00",
          "checkedAt": "2026-11-11T12:30:00Z",
          "method": "manual",
          "note": "시작 시각은 7.56 점검 완료 시각에 연결한 해석"
        }
      ],
      "phases": [
        {
          "id": "series",
          "label": "시리즈 기간",
          "startsAt": "2026-09-08T18:25:00+09:00",
          "endsAt": null,
          "startCondition": null,
          "endCondition": {
            "type": "beforePatchMaintenance",
            "target": "8.0",
            "text": "패치 8.0 점검 시작 전까지"
          },
          "precision": "conditional",
          "endSemantics": "exclusive",
          "certainty": "confirmed"
        }
      ]
    }
  ],
  "frontlineRules": {
    "region": "KR",
    "timezone": "Asia/Seoul",
    "resetLocalTime": "00:00",
    "sequence": [
      "봉인된 바위섬(쟁탈전)",
      "영광의 평원(쇄빙전)",
      "온살 하카이르(계절끝 합전)",
      "워코 치테(연습전)",
      "봉인된 바위섬(쟁탈전)",
      "외곽 유적지대(제압전)",
      "온살 하카이르(계절끝 합전)",
      "워코 치테(연습전)"
    ],
    "anchorDate": null,
    "status": "anchor-unverified",
    "source": "https://guide.ff14.co.kr/pvp/sub04",
    "rotationSource": "https://www.ff14.co.kr/news/notice/view/2865"
  }
};
