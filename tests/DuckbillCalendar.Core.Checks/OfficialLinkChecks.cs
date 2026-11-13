using DuckbillCalendar.Core;

internal static class OfficialLinkChecks
{
    public static void Run(Action<bool,string> check)
    {
        var phase = new EventPhase { Id = "participation",Start = CalendarBoundary.Instant(DateTimeOffset.Parse("2026-11-18T17:00:00+09:00")) with { EvidenceIds = ["list"] } };
        var item = new CalendarEvent { Id = "kr:event:772",Title = "염영의 여로",Category = "web-event",Phases = [phase] };
        var proof = new EvidenceRecord { Id = "list",SourceId = "kr-events",Url = "https://www.ff14.co.kr/news/event",Note = "개별 안내: https://www.ff14.co.kr/news/event/view/772?category=1&page=1" };
        var feed = new FeedDocument { Events = [item],Evidence = [proof] };
        check(OfficialEventLinks.Resolve(feed,item) == "https://www.ff14.co.kr/news/event/view/772?category=1&page=1","List-page evidence resolves to the separately recorded event detail href");
        check(NotificationPolicy.SourceUrl(feed,item) == OfficialEventLinks.Resolve(feed,item),"Inbox and calendar use the same detail resolver");
        check(OfficialEventLinks.Resolve(feed with { Evidence = [proof with { Note = "개별 안내: https://www.ff14.co.kr/news/event/view/772?category=1&amp;page=1. 확인 완료." }] },item)!.EndsWith("category=1&page=1",StringComparison.Ordinal),"Old review punctuation and HTML query escaping do not damage the detail URL");
        check(OfficialEventLinks.Resolve(feed with { Evidence = [proof with { Note = null }] },item) is null,"A listing page is never substituted when the detail link is absent");
        check(OfficialEventLinks.Resolve(feed with { Evidence = [proof with { Note = "개별 안내: https://www.ff14.co.kr/news/event/view/771" }] },item) is null,"A different event ID is not silently opened");
        foreach (var bad in new[] { "javascript:alert(1)","https://www.ff14.co.kr.evil.example/event/772","https://evil.example/x","https://www.ff14.co.kr:444/events/x","https://user@www.ff14.co.kr/events/x" })
            check(OfficialEventLinks.Resolve(feed with { Evidence = [proof with { Note = "개별 안내: " + bad }] },item) is null,"Unsafe detail destination is rejected: " + bad);
        var direct = proof with { Url = "https://www.ff14.co.kr/events/fun/special/ff14xff16",Note = null };
        check(OfficialEventLinks.Resolve(feed with { Evidence = [direct] },item) == direct.Url,"An already recorded campaign detail page is preserved");
        var second = phase with { Id = "reward",Start = phase.Start with { EvidenceIds = ["reward"] } };
        var multi = item with { Phases = [phase,second] };
        check(OfficialEventLinks.Resolve(feed with { Events = [multi],Evidence = [proof,direct with { Id = "reward",Url = "https://www.ff14.co.kr/events/reward" }] },multi,second) == "https://www.ff14.co.kr/events/reward","A selected phase prefers its own evidence destination");
        check(OfficialEventLinks.Resolve(feed,item with { IsPersonal = true }) is null,"Personal schedules cannot borrow public evidence links");
        check(SpecialCalendarDays.IsOguDay(new(2026,5,9)) && SpecialCalendarDays.IsOguDay(new(2027,5,9)) && SpecialCalendarDays.IsOguDay(new(2032,5,9)),"Ogu Day repeats on May ninth every year");
        check(!SpecialCalendarDays.IsOguDay(new(2026,5,8)) && !SpecialCalendarDays.IsOguDay(new(2026,9,5)),"Adjacent dates and reversed month/day do not become Ogu Day");
    }
}
