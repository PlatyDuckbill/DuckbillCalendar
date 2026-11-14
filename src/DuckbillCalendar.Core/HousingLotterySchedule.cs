namespace DuckbillCalendar.Core;

public enum HousingLotteryPhase { Entry, Results }
public sealed record HousingDayLabel(string Title, string Detail, bool DeadlineDay, bool Urgent);
public sealed record HousingLotteryPeriod(HousingLotteryPhase Phase, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

// Kept readable for state files saved by the earlier game-screen implementation.
public sealed record HousingLotteryObservation(HousingLotteryPhase Phase, DateTimeOffset ObservedAt, DateTimeOffset? EndsAt, uint WorldId);

/// <summary>KR cycle published by Aetherway, checked against its public calendar implementation.</summary>
public static class HousingLotterySchedule
{
    public const string Category = "housing";
    public const string GuideUrl = "https://www.ff14.co.kr/news/notice/view/2198";
    public const string SourceUrl = "https://aether-way.com/calendar";
    public const string SourceName = "에테르웨이 한국 서버 캘린더";
    public const string SourceCheckedOn = "2026-11-14";
    public const string Rules = "참가 신청 5일 → 결과 발표·구매 확정 4일 → 다음 청약";
    // Aetherway's published housingPieces implementation explicitly uses this KR anchor.
    public static readonly DateTimeOffset Anchor = new(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(9));
    private const long CycleTicks = 9 * TimeSpan.TicksPerDay;

    public static HousingLotteryPeriod? PeriodAt(DateTimeOffset now)
    {
        if (now < Anchor) return null;
        var elapsed = now.UtcTicks - Anchor.UtcTicks;
        try
        {
            var cycle = Anchor.AddTicks(elapsed / CycleTicks * CycleTicks);
            var results = cycle.AddDays(5);
            return now < results ? new(HousingLotteryPhase.Entry, cycle, results)
                : new(HousingLotteryPhase.Results, results, cycle.AddDays(9));
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static HousingLotteryPeriod? ForDate(DateOnly date) => PeriodAt(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9)));

    public static HousingDayLabel DescribeDay(HousingLotteryPeriod period, DateOnly date, DateOnly today)
    {
        var lastDay = CalendarEngine.Today(period.EndsAt.AddTicks(-1));
        var entry = period.Phase == HousingLotteryPhase.Entry;
        var deadlineDay = date == lastDay;
        var detail = deadlineDay ? entry ? "신청 마감일" : "구매 확정 마감일" : entry ? "신청 기간" : "구매 확정 기간";
        if (date == today)
            detail = lastDay == today ? "오늘 마감" : lastDay.DayNumber == today.DayNumber + 1 ? "내일 마감" : entry ? "오늘 신청 가능" : "오늘 결과 확인";
        else if (date.DayNumber == today.DayNumber + 1)
            detail = deadlineDay ? "내일 마감" : entry ? "내일도 신청 기간" : "내일도 결과 기간";
        return new(entry ? "청약 신청" : "결과 확인", detail, deadlineDay,
            date >= today && deadlineDay || date == today && lastDay.DayNumber - today.DayNumber is 0 or 1);
    }
}
