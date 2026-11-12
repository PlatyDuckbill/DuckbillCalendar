using System.Globalization;
using System.Text.RegularExpressions;

namespace DuckbillCalendar.Core;

public sealed record CalendarItem(CalendarEvent Event, EventPhase Phase, string Edge, DateOnly Date, bool Ongoing = false);
public sealed record CalendarDay(DateOnly Date, bool InMonth, IReadOnlyList<CalendarItem> Items, int HiddenCount);
public sealed record CalendarBar(CalendarEvent Event, EventPhase Phase, int StartColumn, int EndColumn, int Lane, bool ContinuesBefore, bool ContinuesAfter);
public sealed record CalendarWeek(DateOnly StartsOn, IReadOnlyList<CalendarDay> Days, IReadOnlyList<CalendarBar> Bars, int LaneCount);
public sealed record CalendarMonth(int Year, int Month, IReadOnlyList<CalendarWeek> Weeks);
public sealed record FrontlineResult(DateOnly Date, bool IsKnown, string? Name, string? Reason);

public static partial class CalendarEngine
{
    private static readonly TimeSpan KoreanOffset = TimeSpan.FromHours(9);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(KoreanOffset).DateTime);

    public static DateOnly? BoundaryDate(CalendarBoundary? boundary)
    {
        if (boundary?.Kind == "date") return ParseDate(boundary.Value);
        var instant = StartInstant(boundary);
        if (instant is null) return null;
        try { return Today(instant.Value); } catch (ArgumentException) { return null; }
    }

    public static DateTimeOffset? StartInstant(CalendarBoundary? boundary) =>
        boundary?.Kind == "instant" ? ParseInstant(boundary.Value) : null;

    public static DateTimeOffset? StartInstant(EventPhase? phase) => StartInstant(phase?.Start);

    public static DateTimeOffset? EndExclusive(EventPhase? phase)
    {
        var end = StartInstant(phase?.End);
        if (end is null || phase is null) return null;
        if (phase.EndInclusion != "inclusive-unit") return end;
        try { return end.Value.Add(phase.End.Precision == "second" ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static IReadOnlyList<CalendarEvent> Merge(FeedDocument feed, LocalState local)
    {
        var withdrawn = (feed.Withdrawals ?? []).ToHashSet(StringComparer.Ordinal);
        var result = (feed.Events ?? []).Where(e => e is not null && Visible(e) && !withdrawn.Contains(e.Id)).ToList();
        foreach (var entry in local.Entries ?? [])
        {
            if (entry is null || entry.ProfileId != local.ActiveProfileId) continue;
            bool expiry = entry.Kind is "subscription" or "retainer" or "bag";
            result.Add(new CalendarEvent
            {
                Id = entry.Id, Category = entry.Kind, Title = entry.Title, IsPersonal = true,
                ProfileId = entry.ProfileId, Note = entry.Note,
                Phases = [new EventPhase
                {
                    Id = "local", Label = expiry ? "이용 종료" : "개인 일정", Point = !expiry,
                    Start = expiry ? new() : entry.When, End = expiry ? entry.When : new()
                }]
            });
        }
        return result.ToArray();
    }

    public static IReadOnlyList<CalendarItem> DayEvents(IReadOnlyList<CalendarEvent> events, DateOnly date, bool includeOngoing = true)
    {
        var items = new List<CalendarItem>();
        foreach (var item in events.Where(Visible))
        foreach (var phase in item.Phases ?? [])
        {
            if (phase is null) continue;
            var start = BoundaryDate(phase.Start);
            var end = BoundaryDate(phase.End);
            bool hasEdge = false;
            if (start == date) { items.Add(new(item, phase, "start", date)); hasEdge = true; }
            if (!phase.Point && end == date) { items.Add(new(item, phase, "end", date)); hasEdge = true; }
            if (!hasEdge && includeOngoing && Range(phase) is { } range && range.Start <= date && range.End >= date)
                items.Add(new(item, phase, "ongoing", date, true));
        }
        return items.OrderByDescending(i => i.Edge == "start" && i.Event.Category is "pvp-season" or "pvp-series")
            .ThenBy(i => i.Ongoing).ThenBy(i => i.Event.Title, StringComparer.Ordinal).ThenBy(i => i.Phase.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<CalendarEvent> MonthEvents(IReadOnlyList<CalendarEvent> events, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return events.Where(Visible).Where(e => (e.Phases ?? []).Any(p => p is not null &&
            (In(BoundaryDate(p.Start), first, last) || In(BoundaryDate(p.End), first, last) ||
             Range(p) is { } r && r.Start <= last && r.End >= first ||
             !p.Point && p.End?.Kind is "unknown" or "condition" && BoundaryDate(p.Start) is { } openStart && openStart <= last)))
            .OrderBy(e => (e.Phases ?? []).Select(p => BoundaryDate(p.Start) ?? BoundaryDate(p.End) ?? DateOnly.MaxValue).DefaultIfEmpty(DateOnly.MaxValue).Min())
            .ThenBy(e => e.Title, StringComparer.Ordinal).ToArray();
    }

    public static CalendarMonth BuildMonth(IReadOnlyList<CalendarEvent> events, int year, int month, bool bars = true, bool showRewardBars = false, int maxLanes = 3)
    {
        if (year < 2 || year > 9998) throw new ArgumentOutOfRangeException(nameof(year));
        if (maxLanes is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(maxLanes));
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var gridStart = first.AddDays(-(int)first.DayOfWeek);
        int weeks = ((last.DayNumber - gridStart.DayNumber + 1) + 6) / 7;
        var ranges = bars ? events.Where(Visible).SelectMany(e => (e.Phases ?? []).Where(p => p is not null &&
            (showRewardBars || !IsReward(p)) && Range(p) is { } r && r.Start < r.End)
            .Select(p => (Event: e, Phase: p, Dates: Range(p)!.Value))).ToArray() : [];
        var output = new List<CalendarWeek>();
        for (int w = 0; w < weeks; w++)
        {
            var start = gridStart.AddDays(w * 7);
            var end = start.AddDays(6);
            var occupied = new List<int>();
            var weekBars = new List<CalendarBar>();
            var visible = ranges.Where(r => r.Dates.Start <= end && r.Dates.End >= start)
                .OrderBy(r => r.Dates.Start).ThenByDescending(r => r.Dates.End).ThenBy(r => r.Event.Id, StringComparer.Ordinal).ThenBy(r => r.Phase.Id, StringComparer.Ordinal);
            foreach (var range in visible)
            {
                int from = Math.Max(0, range.Dates.Start.DayNumber - start.DayNumber);
                int through = Math.Min(6, range.Dates.End.DayNumber - start.DayNumber);
                int lane = occupied.FindIndex(previousEnd => previousEnd < from);
                if (lane < 0) lane = occupied.Count;
                if (lane >= maxLanes) continue;
                if (lane == occupied.Count) occupied.Add(through); else occupied[lane] = through;
                weekBars.Add(new(range.Event, range.Phase, from, through, lane, range.Dates.Start < start, range.Dates.End > end));
            }
            var days = Enumerable.Range(0, 7).Select(column =>
            {
                var date = start.AddDays(column);
                var displayedBarKeys = weekBars.Where(b => column >= b.StartColumn && column <= b.EndColumn)
                    .Select(b => (b.Event.Id, b.Phase.Id)).ToHashSet();
                var points = DayEvents(events, date, false).Where(i => !displayedBarKeys.Contains((i.Event.Id, i.Phase.Id))).ToArray();
                var overflowRanges = ranges.Where(r => r.Dates.Start <= date && r.Dates.End >= date &&
                    !displayedBarKeys.Contains((r.Event.Id, r.Phase.Id))).Select(r => (r.Event.Id, r.Phase.Id)).ToHashSet();
                // Overflow ranges stay in the full day list; count each phase once even on a boundary date.
                var hiddenKeys = overflowRanges.Except(points.Select(i => (i.Event.Id, i.Phase.Id))).Count();
                return new CalendarDay(date, date.Month == month && date.Year == year, points, hiddenKeys);
            }).ToArray();
            output.Add(new(start, days, weekBars.ToArray(), occupied.Count));
        }
        return new(year, month, output.ToArray());
    }

    public static FrontlineResult FrontlineFor(FeedDocument feed, DateOnly date)
    {
        var valid = (feed.FrontlineRules ?? []).Where(r => r is not null && r.Status == "verified" && r.Region == "KR" &&
            r.Timezone == "Asia/Seoul" && r.ResetLocalTime == "00:00" && ParseDate(r.ValidFrom) is { } from &&
            ParseDate(r.ValidUntil) is { } until && from <= date && date < until).ToArray();
        if (valid.Length != 1) return new(date, false, null, valid.Length > 1 ? "OverlappingRules" : "UnverifiedRule");
        var rule = valid[0];
        if (ParseDate(rule.AnchorDate) is not { } anchor || rule.Sequence is null || rule.Sequence.Count == 0 ||
            rule.AnchorIndex < 0 || rule.AnchorIndex >= rule.Sequence.Count) return new(date, false, null, "InvalidAnchor");
        int offset = date.DayNumber - anchor.DayNumber + rule.AnchorIndex;
        int index = ((offset % rule.Sequence.Count) + rule.Sequence.Count) % rule.Sequence.Count;
        return new(date, true, rule.Sequence[index], null);
    }

    internal static DateOnly? ParseDate(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
    internal static DateTimeOffset? ParseInstant(string? value) => value is not null && InstantPattern().IsMatch(value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) ? instant : null;
    internal static bool Visible(CalendarEvent e) => e is not null && e.Lifecycle == "published";
    internal static bool IsReward(EventPhase p) => p.Id.Contains("reward", StringComparison.OrdinalIgnoreCase) || p.Id.Contains("claim", StringComparison.OrdinalIgnoreCase);
    internal static (DateOnly Start, DateOnly End)? Range(EventPhase phase)
    {
        if (phase.Point || BoundaryDate(phase.Start) is not { } start || BoundaryDate(phase.End) is not { } end) return null;
        if (phase.End.Kind == "instant")
        {
            if (EndExclusive(phase) is not { } exclusive) return null;
            try { end = Today(exclusive.AddTicks(-1)); } catch (ArgumentOutOfRangeException) { return null; }
        }
        else if (phase.EndInclusion == "exclusive")
        {
            if (end == DateOnly.MinValue) return null;
            end = end.AddDays(-1);
        }
        return start <= end ? (start, end) : null;
    }
    private static bool In(DateOnly? day, DateOnly first, DateOnly last) => day is { } value && value >= first && value <= last;
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();
}
