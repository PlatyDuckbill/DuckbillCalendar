using DuckbillCalendar.Core;

internal static class WeeklyScheduleChecks
{
    public static void Run(Action<bool,string> check)
    {
        var category = new UserCalendarCategory { Id = "user:raid",Name = "공격대",Icon = "Raid" };
        var state = new LocalState { View = new() { UserCategories = [category] } };
        var request = new WeeklyScheduleRequest { Title = "절바하",CategoryId = category.Id,StartDate = new(2026,11,13),Weeks = 4,
            Days = [new(DayOfWeek.Monday,"20:00"),new(DayOfWeek.Sunday,"18:00")] };
        var plan = WeeklySchedulePlanner.Plan(state,request);
        check(plan.IsValid && plan.NewCount == 8,"Four weekly patterns expand into eight individual raid occurrences");
        check(plan.Occurrences.First().When.DayOfWeek == DayOfWeek.Sunday && plan.Occurrences.First().When.Day == 15,"The first eligible weekday is on or after the chosen start");
        check(plan.Occurrences.Where(o => o.When.DayOfWeek == DayOfWeek.Monday).All(o => o.When.Hour == 20) && plan.Occurrences.Where(o => o.When.DayOfWeek == DayOfWeek.Sunday).All(o => o.When.Hour == 18),"Each weekday retains its own clock");
        check(plan.Occurrences.All(o => o.When.Offset == TimeSpan.FromHours(9)) && plan.Occurrences.SequenceEqual(plan.Occurrences.OrderBy(o => o.When)),"Preview occurrences use Korean time and chronological ordering");
        check(WeeklySchedulePlanner.Plan(state,request with { IntervalWeeks = 2 }).NewCount == 4,"A two-week interval skips alternate weeks within the selected range");
        var leap = WeeklySchedulePlanner.Plan(state,request with { StartDate = new(2024,2,26),Weeks = 1,Days = [new(DayOfWeek.Thursday,"23:59")] });
        check(leap.IsValid && leap.Occurrences.Single().When.Day == 29 && leap.Occurrences.Single().When.Minute == 59,"Leap-day and minute precision are preserved");
        var entries = plan.Occurrences.Select((o,i) => new PersonalEntry { Id = "existing-" + i,Title = request.Title,CategoryId = category.Id,When = CalendarBoundary.Instant(o.When) }).ToArray();
        var duplicate = WeeklySchedulePlanner.Plan(state with { Entries = entries },request);
        check(duplicate.IsValid && duplicate.NewCount == 0 && duplicate.ExistingCount == 8,"Repeated registration skips exact existing occurrences");
        check(WeeklySchedulePlanner.Plan(state with { Entries = [entries[0] with { ProfileId = "other" }] },request).NewCount == 8,"Another profile's schedule does not suppress registration");
        check(WeeklySchedulePlanner.Plan(state with { Entries = [entries[0] with { Title = "다른 공대" }] },request).NewCount == 8,"A different raid title is a distinct schedule");
        check(WeeklySchedulePlanner.Plan(state with { Entries = [entries[0] with { CategoryId = "" }] },request).NewCount == 8,"Different categories do not collide");
        check(WeeklySchedulePlanner.Plan(state,request with { Days = [] }).Errors.Contains("NoWeekdays"),"An empty weekday pattern cannot register");
        check(WeeklySchedulePlanner.Plan(state,request with { Days = [new(DayOfWeek.Monday,"20:00"),new(DayOfWeek.Monday,"21:00")] }).Errors.Contains("DuplicateWeekday"),"Duplicate weekday definitions are rejected");
        foreach (var time in new[] { "24:00","18:60","unknown" })
            check(!WeeklySchedulePlanner.Plan(state,request with { Days = [new(DayOfWeek.Monday,time)] }).IsValid,"Invalid weekly clock is rejected: " + time);
        check(WeeklySchedulePlanner.Plan(state,request with { Days = [new(DayOfWeek.Monday,"8:05")] }).Occurrences.All(o => o.When.Hour == 8 && o.When.Minute == 5),"Convenient single-digit hour entry is normalized to exact time");
        check(!WeeklySchedulePlanner.Plan(state,request with { Weeks = 0 }).IsValid && !WeeklySchedulePlanner.Plan(state,request with { Weeks = 53 }).IsValid,"Registration range is bounded to one through fifty-two weeks");
        check(!WeeklySchedulePlanner.Plan(state,request with { IntervalWeeks = 0 }).IsValid,"A zero recurrence interval is rejected");
        check(!WeeklySchedulePlanner.Plan(state,request with { StartDate = new(2100,12,27) }).IsValid,"A range outside the supported calendar years cannot partially register");
        check(!WeeklySchedulePlanner.Plan(state,request with { CategoryId = "user:deleted" }).IsValid,"A deleted category is not silently replaced during registration");
        var full = state with { Entries = Enumerable.Range(0,2000).Select(i => new PersonalEntry { Id = "full-" + i,Title = "별도 일정" }).ToArray() };
        check(WeeklySchedulePlanner.Plan(full,request).Errors.Contains("CapacityExceeded"),"Bulk registration checks total storage capacity before making changes");
        check(WeeklySchedulePlanner.Plan(state,request with { Weeks = 52,Days = Enum.GetValues<DayOfWeek>().Select(d => new WeeklyScheduleDay(d,"20:00")).ToArray() }).NewCount == 364,"The largest weekly pattern remains bounded at 364 occurrences");
        check(!state.Notifications.Enabled && !state.Notifications.NewNotices && !state.Notifications.NewEvents && !state.View.LocalChatAlerts,"Every notification output and discovery option starts disabled");
        var series = entries.Select((e,i) => e with { SeriesId = "batch-A",SeriesIndex = i }).ToArray();
        var deletion = state with { Profiles = [new(),new() { Id = "other",Alias = "다른 계정" }],Entries = series.Concat(new[]
        { series[0] with { Id = "other-batch",SeriesId = "batch-B" },series[0] with { Id = "other-profile",ProfileId = "other" } }).ToArray() };
        check(FeedValidator.ValidateLocal(deletion).Count == 0,"Finite series identity and stable occurrence positions validate");
        check(PersonalDeletionPolicy.Targets(deletion,series[3].Id,"one").Count == 1,"One-occurrence deletion selects exactly one record");
        check(PersonalDeletionPolicy.Targets(deletion,series[3].Id,"following").Count == 5,"Following deletion uses the selected series position");
        check(PersonalDeletionPolicy.Targets(deletion,series[3].Id,"series").Count == 8,"Whole-series deletion excludes other batches and other profiles sharing the category");
        check(PersonalDeletionPolicy.Targets(deletion,"other-profile","series").Count == 0,"The active profile cannot delete another profile's series");
        check(PersonalDeletionPolicy.Targets(state with { Entries = entries },entries[0].Id,"series").Count == 0,"Unrelated manual schedules do not acquire an artificial series");
        check(PersonalDeletionPolicy.Targets(deletion with { Entries = deletion.Entries.Select(e => e.Id == series[0].Id ? e with { When = CalendarBoundary.Instant(plan.Occurrences[^1].When.AddDays(1)) } : e).ToArray() },series[3].Id,"following").Count == 5,"Moving one occurrence does not change its original recurrence position");
        check(FeedValidator.ValidateLocal(deletion with { Entries = deletion.Entries.Append(series[0] with { Id = "duplicate-position" }).ToArray() }).Contains("InvalidEntrySeries"),"Duplicate positions within one profile and series are rejected");
    }
}
