using System.Globalization;

namespace DuckbillCalendar.Core;

public sealed record WeeklyScheduleDay(DayOfWeek Day, string Time);
public sealed record WeeklyScheduleRequest
{
    public string Title { get; init; } = "공격대";
    public string CategoryId { get; init; } = "";
    public DateOnly StartDate { get; init; }
    public int Weeks { get; init; } = 4;
    public int IntervalWeeks { get; init; } = 1;
    public IReadOnlyList<WeeklyScheduleDay> Days { get; init; } = [];
    public string Note { get; init; } = "";
}
public sealed record WeeklyOccurrence(DateTimeOffset When, bool AlreadyExists);
public sealed record WeeklySchedulePlan(IReadOnlyList<WeeklyOccurrence> Occurrences, IReadOnlyList<string> Errors)
{
    public int NewCount => Occurrences.Count(o => !o.AlreadyExists);
    public int ExistingCount => Occurrences.Count(o => o.AlreadyExists);
    public bool IsValid => Errors.Count == 0;
}
public sealed record WeeklyRegistrationResult(int Registered, int Skipped, DateOnly? FirstDate, IReadOnlyList<string> Errors);

/// <summary>Expands a finite Korean weekly pattern. It never creates a background recurrence or guesses times.</summary>
public static class WeeklySchedulePlanner
{
    public static WeeklySchedulePlan Plan(LocalState state, WeeklyScheduleRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 80) errors.Add("InvalidTitle");
        if (request.Note is null || request.Note.Length > 1000) errors.Add("InvalidNote");
        if (request.CategoryId is null || request.CategoryId.Length > 0 && !state.View.UserCategories.Any(c => c.Id == request.CategoryId)) errors.Add("InvalidCategory");
        if (request.StartDate.Year is < 2020 or > 2100) errors.Add("InvalidStartDate");
        if (request.Weeks is < 1 or > 52) errors.Add("InvalidWeeks");
        if (request.IntervalWeeks is < 1 or > 52) errors.Add("InvalidInterval");
        if (request.Days is null || request.Days.Count is < 1 or > 7) errors.Add("NoWeekdays");
        var clocks = new Dictionary<DayOfWeek,TimeOnly>();
        foreach (var day in request.Days ?? [])
        {
            if (day is null || !Enum.IsDefined(day.Day)) { errors.Add("InvalidWeekday"); continue; }
            if (clocks.ContainsKey(day.Day)) { errors.Add("DuplicateWeekday"); continue; }
            if (!TimeOnly.TryParseExact(day.Time,["HH:mm","H:mm"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var clock))
                errors.Add("InvalidTime:" + (int)day.Day);
            else clocks.Add(day.Day,clock);
        }
        if (errors.Count > 0) return new([],errors.Distinct().ToArray());
        var existing = state.Entries.Where(e => e.Kind == "personal" && e.ProfileId == state.ActiveProfileId && e.CategoryId == request.CategoryId && e.Title.Trim().Equals(request.Title.Trim(),StringComparison.Ordinal))
            .Select(e => CalendarEngine.StartInstant(e.When)).Where(at => at.HasValue).Select(at => at!.Value.UtcTicks).ToHashSet();
        var occurrences = new List<WeeklyOccurrence>();
        try
        {
            var end = request.StartDate.AddDays(request.Weeks * 7);
            if (end.AddDays(-1).Year > 2100) return new([],["InvalidRange"]);
            foreach (var (day,clock) in clocks)
            {
                var first = request.StartDate.AddDays(((int)day - (int)request.StartDate.DayOfWeek + 7) % 7);
                for (var date = first; date < end; date = date.AddDays(request.IntervalWeeks * 7))
                {
                    var at = new DateTimeOffset(date.Year,date.Month,date.Day,clock.Hour,clock.Minute,0,TimeSpan.FromHours(9));
                    occurrences.Add(new(at,existing.Contains(at.UtcTicks)));
                }
            }
        }
        catch (ArgumentOutOfRangeException) { return new([],["InvalidRange"]); }
        if (state.Entries.Count + occurrences.Count(o => !o.AlreadyExists) > 2000) errors.Add("CapacityExceeded");
        return new(occurrences.OrderBy(o => o.When).ToArray(),errors);
    }
}
