using System.Text;

namespace DuckbillCalendar.Core;

public sealed record CalendarSearchResult(CalendarEvent Event, EventPhase Phase, string Edge, CalendarBoundary Boundary, DateOnly? Date)
{
    public string Key => Event.Id + "." + Phase.Id + "." + Edge;
}

public static class CalendarSearch
{
    private static readonly HashSet<string> Filler = ["언제", "언제야", "언제지", "언제인가요", "열리지", "열려", "열려요", "열리나요", "나와", "나오지", "인가요", "좀", "일정", "날짜", "알려줘", "알려주세요", "찾아줘", "찾아주세요"];
    private static readonly HashSet<string> Opening = ["출시", "업데이트", "오픈", "시작"];
    private static readonly HashSet<string> Closing = ["마감", "종료", "끝", "끝나", "끝나지", "끝나요"];

    public static IReadOnlyList<CalendarSearchResult> Find(IEnumerable<CalendarEvent> events, string query, DateOnly today, IReadOnlyDictionary<string, string>? categoryNames = null)
    {
        var words = query.Split([' ', '\t', '\r', '\n', '?', '？', '!', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(w => w.Length > 0 && !Filler.Contains(w) && !w.StartsWith("언제", StringComparison.Ordinal)).ToArray();
        if (words.Length == 0) return [];
        var closing = words.Any(Closing.Contains);
        var terms = words.Where(w => !Opening.Contains(w) && !Closing.Contains(w)).ToArray();
        if (terms.Length == 0) terms = words;
        var joined = string.Concat(terms);
        var found = new List<CalendarSearchResult>();
        foreach (var item in events.Where(e => e.Lifecycle == "published"))
        foreach (var phase in item.Phases.Where(p => CalendarContentPolicy.Includes(item, p)))
        {
            if (closing && phase.Point) continue;
            var title = Normalize(item.Title);
            var category = categoryNames?.GetValueOrDefault(item.Category) ?? item.Category switch
            {
                "patch" => "업데이트 출시 확장팩 패치", "maintenance" => "게임 서버 점검",
                "sale" or "discount" => "할인 예약", "pvp-season" or "pvp-series" or "reward" => "PvP 시즌 보상",
                "subscription" or "retainer" or "bag" => "이용권 만료", "personal" => "개인 일정", _ => "이벤트"
            };
            var haystack = Normalize(item.Title + " " + phase.Label + " " + category + (phase.Id == "early-access" ? " 얼리 액세스 앞서 해보기" : ""));
            if (!title.Contains(joined, StringComparison.Ordinal) && !terms.All(t => haystack.Contains(t, StringComparison.Ordinal))) continue;
            var edge = closing || CalendarEngine.BoundaryDate(phase.Start) is null && CalendarEngine.BoundaryDate(phase.End) is not null ? "end" : "start";
            var boundary = edge == "end" ? phase.End : phase.Start;
            found.Add(new(item, phase, edge, boundary, CalendarEngine.BoundaryDate(boundary)));
        }
        return found.OrderBy(r => r.Date is null ? 2 : r.Date >= today || !r.Phase.Point && CalendarEngine.BoundaryDate(r.Phase.End) >= today ? 0 : 1)
            .ThenBy(r => r.Event.Category == "patch" ? r.Phase.Id == "release" ? 0 : r.Phase.Id == "early-access" ? 1 : 2 : r.Event.Category == "maintenance" ? 3 : r.Event.Category is "sale" or "discount" ? 6 : 4)
            .ThenBy(r => r.Date ?? DateOnly.MaxValue).ThenBy(r => r.Event.Title, StringComparer.Ordinal).ThenBy(r => r.Key, StringComparer.Ordinal).Take(200).ToArray();
    }

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
