namespace DuckbillCalendar.Core;

/// <summary>Calendar dates describe playable events, not the publication time of announcements.</summary>
public static class CalendarContentPolicy
{
    public static bool IsPublication(EventPhase phase) => phase.Id == "published" || phase.Label.Contains("공지 게시", StringComparison.Ordinal);
    public static bool Includes(CalendarEvent item, EventPhase phase)
    {
        if (item.IsPersonal) return true;
        if (IsPublication(phase)) return false;
        if (item.Category == "maintenance")
        {
            var title = item.Title;
            // The collector creates a distinct game interval even when its source notice
            // also describes website/guide downtime. Do not discard that normalized interval.
            if (item.Id.StartsWith("kr:maintenance:game:", StringComparison.Ordinal)) return true;
            if (new[] { "PC방", "피시방", "웹젠", "크리스탈샵", "홈페이지", "공식 가이드", "결제", "고객센터" }.Any(title.Contains)) return false;
            return item.Id.StartsWith("kr:maintenance:game:", StringComparison.Ordinal) ||
                new[] { "게임", "서버", "런처", "접속 불가" }.Any(title.Contains);
        }
        return item.Category is "web-event" or "in-game-event" or "seasonal-event" or "patch" or "pvp-season" or "pvp-series" or "reward" or "sale" or "discount" ||
            (item.Category == "notice" && item.Id.StartsWith("kr:curated:competition", StringComparison.Ordinal)) ||
            (item.Category == "notice" && item.Id == "kr:curated:ccrc-2027-korea");
    }
}
