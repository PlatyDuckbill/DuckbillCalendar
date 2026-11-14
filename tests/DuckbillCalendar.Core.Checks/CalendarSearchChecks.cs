using DuckbillCalendar.Core;

internal static class CalendarSearchChecks
{
    public static void Run(Action<bool,string> check)
    {
        CalendarEvent Point(string id,string title,string category,string label,string date) => new()
        {
            Id=id,Title=title,Category=category,Phases=[new() {Id=id,Label=label,Point=true,Start=CalendarBoundary.Date(DateOnly.Parse(date))}]
        };
        var release=Point("release","V8.0 백은의 탐구자 정식 출시","patch","정식 출시","2027-02-23");
        var early=Point("early-access","V8.0 백은의 탐구자 앞서 해보기","patch","앞서 해보기 시작","2027-02-19");
        var reservation=new CalendarEvent {Id="reservation",Title="백은의 탐구자 사전예약",Category="sale",Phases=[new() {Id="buy",Label="예약 기간",Start=CalendarBoundary.Date(new(2026,11,4)),End=CalendarBoundary.Date(new(2027,2,19))}]};
        var publication=Point("published","백은 공지","notice","공지 게시","2026-11-4");
        var unknown=new CalendarEvent {Id="unknown",Title="백은 추가 일정",Category="patch",Phases=[new(){Id="later",Label="추가 업데이트"}]};
        var all=new[] {reservation,early,release,publication,unknown};
        var today=new DateOnly(2026,11,14);
        var found=CalendarSearch.Find(all,"백은",today);
        check(found[0].Event.Id=="release" && found[1].Event.Id=="early-access","Expansion search prioritizes formal release and early access before purchase periods");
        check(found[0].Date==new DateOnly(2027,2,23) && found[0].Boundary.Kind=="date","Search preserves a future date-only release without inventing a time");
        check(CalendarSearch.Find(all,"백은 언제 열리지?",today).Select(r=>r.Key).SequenceEqual(found.Select(r=>r.Key)),"A short Korean date question resolves its meaningful title keyword");
        check(CalendarSearch.Find(all,"V8.0",today).Any(r=>r.Event.Id=="release"),"Version punctuation is normalized");
        check(CalendarSearch.Find(all,"백은의탐구자",today).Count==3,"Titles also match without spaces");
        check(CalendarSearch.Find(all,"얼리",today).Single().Event.Id=="early-access","Early access can be found by its familiar alias");
        check(found.All(r=>r.Event.Id!="published"),"Announcement publication dates never masquerade as playable calendar schedules");
        check(found.Last().Event.Id=="unknown" && found.Last().Date is null,"Undated matching phases remain explicit and are not given an arbitrary date");
        var end=CalendarSearch.Find(all,"백은 마감",today).Single(r=>r.Event.Id=="reservation");
        check(end.Edge=="end" && end.Date==new DateOnly(2027,2,19),"Deadline search chooses the actual end boundary");
        check(CalendarSearch.Find(all,"   ",today).Count==0 && CalendarSearch.Find(all,"언제 알려줘",today).Count==0,"An empty or filler-only query never enumerates every schedule");
        check(CalendarSearch.Find(all,"없는 검색어",today).Count==0,"Unmatched queries have a real empty state");
        var personal=Point("local","토요일 모임","user:raid","개인 일정","2026-11-21") with {IsPersonal=true};
        check(CalendarSearch.Find([personal],"공격대",today,new Dictionary<string,string>{{"user:raid","공격대"}}).Count==1,"User category names are searchable without exposing internal IDs");
    }
}
