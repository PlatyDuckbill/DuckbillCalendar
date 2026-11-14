using System.Text.Json;
using DuckbillCalendar.Core;

internal static class HousingLotteryChecks
{
    public static void Run(Action<bool,string> check)
    {
        var anchor=HousingLotterySchedule.Anchor;
        var entry=HousingLotterySchedule.PeriodAt(anchor)!;
        check(entry.Phase==HousingLotteryPhase.Entry && entry.StartsAt==anchor && entry.EndsAt==anchor.AddDays(5),"Published KR anchor starts a five-day application period");
        check(HousingLotterySchedule.PeriodAt(anchor.AddDays(5).AddTicks(-1))?.Phase==HousingLotteryPhase.Entry,"Applications remain open immediately before the boundary");
        var results=HousingLotterySchedule.PeriodAt(anchor.AddDays(5))!;
        check(results.Phase==HousingLotteryPhase.Results && results.StartsAt==entry.EndsAt && results.EndsAt==anchor.AddDays(9),"Results start at the exact deadline and run four days");
        check(HousingLotterySchedule.PeriodAt(anchor.AddDays(9)) is {Phase:HousingLotteryPhase.Entry} next && next.StartsAt==results.EndsAt,"The next application cycle follows the results period");
        check(HousingLotterySchedule.PeriodAt(anchor.AddTicks(-1)) is null,"Dates before the published source anchor are not invented");
        var november=HousingLotterySchedule.ForDate(new(2026,11,14))!;
        check(november.Phase==HousingLotteryPhase.Results && november.StartsAt==DateTimeOffset.Parse("2026-11-11T00:00:00+09:00") && november.EndsAt==DateTimeOffset.Parse("2026-11-15T00:00:00+09:00"),"November period matches the externally checked calendar pattern");
        check(HousingLotterySchedule.ForDate(new(2026,11,15))?.Phase==HousingLotteryPhase.Entry,"The next day changes the calendar cell to the application phase");
        check(HousingLotterySchedule.DescribeDay(november,new(2026,11,14),new(2026,11,14)) is {Detail:"오늘 마감",DeadlineDay:true,Urgent:true},"Today's midnight deadline is attached to the final actionable date");
        check(HousingLotterySchedule.DescribeDay(november,new(2026,11,13),new(2026,11,13)).Detail=="내일 마감","Today's cell announces tomorrow's deadline");
        check(!HousingLotterySchedule.DescribeDay(november,new(2026,11,14),new(2026,11,16)).Urgent,"Historical deadline cells do not retain urgency");
        check(HousingLotterySchedule.PeriodAt(DateTimeOffset.Parse("2026-11-14T15:00:00Z"))?.Phase==HousingLotteryPhase.Entry,"UTC input observes the Korean midnight transition");
        check(Enumerable.Range(1,30).All(day=>HousingLotterySchedule.ForDate(new(2026,11,day)) is not null),"An entire viewed month is available without opening a game tab");
        check(HousingLotterySchedule.ForDate(new(2028,2,29)) is not null && HousingLotterySchedule.ForDate(new(2027,1,1)) is not null,"Leap day and year transitions stay within a continuous cycle");
        var state=new LocalState { View=new() { Calendars=new() { Housing=false },CalendarAppearances=Enum.GetValues<CalendarGroup>().Where(g=>g!=CalendarGroup.Notices).Select(g=>new CalendarAppearance { Group=g,Color="#E2C080" }).ToArray() } };
        check(FeedValidator.ValidateLocal(state).Count==0 && !CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(state,CalendarJson.Options)).View.Calendars.Housing,"Housing visibility and all nine styles persist");
        var old=JsonSerializer.Serialize(new LocalState(),CalendarJson.Options).Replace("\"housing\": true,", "");
        check(CalendarJson.Parse<LocalState>(System.Text.Encoding.UTF8.GetBytes(old)).View.Calendars.Housing,"Older state defaults to showing housing");
        check(HousingLotterySchedule.PeriodAt(DateTimeOffset.MaxValue) is null,"The upper date limit does not overflow while constructing the next boundary");
    }
}
