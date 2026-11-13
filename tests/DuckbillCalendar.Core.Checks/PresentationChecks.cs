using DuckbillCalendar.Core;

internal static class PresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuckbillCalendar.slnx"))) directory = directory.Parent;
        var feed = CalendarJson.Parse<FeedDocument>(File.ReadAllBytes(Path.Combine(directory!.FullName, "data/live/kr-calendar.snapshot.json")));
        var events = CalendarEngine.Merge(feed, new()).Select(e => e with { Phases = e.Phases.Where(p => CalendarContentPolicy.Includes(e, p)).ToArray() }).Where(e => e.Phases.Count > 0).ToArray();
        IReadOnlyList<CalendarDisplayEntry> Day(int y, int m, int d) => CalendarPresentation.Build(CalendarEngine.DayEvents(events, new(y, m, d)), new(y, m, d));
        var nov10 = Day(2026, 11, 10);
        check(nov10.Count(e => !e.Ongoing) == 3 && nov10.Count(e => e.Ongoing) == 2, "November 10 projects three direct planning units and two optional ongoing groups instead of eight flat cards");
        var update = nov10.Single(e => e.Group == CalendarDisplayGroup.Update);
        check(update.Members.Count == 3 && update.Primary.Event.Category == "maintenance" && update.Primary.Phase.Start.Value!.Contains("12:00") && update.Primary.Phase.End.Value!.Contains("18:15"), "Update group keeps both exact downtime boundaries and the date-only patch source");
        check(nov10.Any(e => e.Category == "pvp-season" && e.Group == CalendarDisplayGroup.Single) && nov10.Any(e => e.Category == "competition"), "Same-day PvP season and tournament stay independent from patch downtime");
        var nov4 = Day(2026, 11, 4);
        check(nov4.Count(e => !e.Ongoing) == 2 && nov4.Single(e => e.Group == CalendarDisplayGroup.PurchaseCampaign).Members.Select(i => i.Event.Id).Distinct().Count() == 3, "Three reviewed purchase starts share one campaign tile while the desk calendar event stays separate");
        var feb19 = Day(2027, 2, 19);
        check(feb19.Count(e => !e.Ongoing) == 2 && feb19.Single(e => e.Group == CalendarDisplayGroup.PurchaseCampaign).Edge == "end" && feb19.Any(e => e.Primary.Phase.Id == "early-access" && e.Group == CalendarDisplayGroup.Single), "Two purchase deadlines group without merging the simultaneous early-access start");
        check(Day(2026, 12, 31).Single(e => !e.Ongoing).Primary.Event.Id == "kr:curated:v8-ce-discount", "December discount deadline stays visible independently from the February reservation deadline");
        check(Day(2027, 2, 23).Single(e => !e.Ongoing).Primary.Phase.Start.Kind == "date", "Formal release keeps date-only precision and is never replaced by early access");
        var nov15 = Day(2026, 11, 15);
        check(nov15.All(e => e.Ongoing) && nov15.Count == 3, "An ordinary date has no invented direct event and only three optional period groups");
        foreach (var date in new[] { new DateOnly(2026, 9, 8), new(2026, 10, 13), new(2026, 11, 4), new(2026, 11, 10), new(2027, 2, 19) })
        {
            var raw = CalendarEngine.DayEvents(events, date);
            var projected = CalendarPresentation.Build(raw, date);
            string Key(CalendarItem i) => i.Event.Id + "|" + i.Phase.Id + "|" + i.Edge;
            check(raw.Select(Key).Order().SequenceEqual(projected.SelectMany(e => e.Members).Select(Key).Order()), "Projection retains every original phase/edge/source on " + date);
        }
        var day = new DateOnly(2026, 11, 10);
        var maintenance = update.Primary.Event;
        var patch = update.Members.Single(i => i.Event.Category == "patch").Event;
        CalendarDisplayEntry[] Project(params CalendarEvent[] values) => CalendarPresentation.Build(CalendarEngine.DayEvents(values, day), day).ToArray();
        check(Project(maintenance).Length == 1 && Project(maintenance)[0].Edge == "period", "Same-day start and end form one tile even without a patch");
        var noSharedEvidence = patch with { Phases = [patch.Phases[0] with { Start = patch.Phases[0].Start with { EvidenceIds = ["different-evidence"] } }] };
        check(Project(maintenance, noSharedEvidence).Length == 2, "Matching version/date without common evidence never forces a merge");
        check(Project(maintenance, patch with { Id = "kr:patch:v7-59" }).Length == 2, "Different structured patch versions stay separate even with shared evidence");
        check(Project(maintenance, maintenance with { Id = "kr:maintenance:game:v7-58:other" }, patch).All(e => e.Group != CalendarDisplayGroup.Update), "Ambiguous multiple downtime intervals never select an arbitrary merge");
        var overnight = maintenance with { Phases = [maintenance.Phases[0] with { Start = CalendarBoundary.Instant(new(2026, 11, 9, 22, 0, 0, TimeSpan.FromHours(9))) }] };
        check(Project(overnight, patch).Length == 2 && Project(overnight, patch).Any(e => e.Edge == "end"), "Overnight maintenance preserves its separate end-date boundary");
        check(Project(maintenance, patch with { Note = "공식 원문 재검토 대기" }).All(e => e.Group != CalendarDisplayGroup.Update), "Review-pending data cannot silently merge with confirmed downtime");
        foreach (var certainty in new string?[] { "tentative", null })
        {
            var unconfirmed = patch with { Phases = [patch.Phases[0] with { Start = patch.Phases[0].Start with { Certainty = certainty } }] };
            check(Project(maintenance, unconfirmed).All(e => e.Group != CalendarDisplayGroup.Update), "An unconfirmed patch boundary cannot inherit confirmed maintenance timing: " + certainty);
        }
        var sameTitle = events.First(e => e.Id == "kr:event:772") with { Phases = [new() { Id = "point", Point = true, Start = CalendarBoundary.Date(day) }] };
        check(Project(sameTitle, sameTitle with { Id = "unrelated-event" }).Length == 2, "Identical event titles and dates do not imply a common campaign");
        var purchases = feb19.Single(e => e.Group == CalendarDisplayGroup.PurchaseCampaign).Members.Select(i => i.Event).DistinctBy(e => e.Id).ToArray();
        var later = purchases[1] with { Phases = [purchases[1].Phases[0] with { End = CalendarBoundary.Instant(new(2027, 2, 19, 18, 1, 0, TimeSpan.FromHours(9))) }] };
        check(CalendarPresentation.Build(CalendarEngine.DayEvents([purchases[0], later], new(2027, 2, 19)), new(2027, 2, 19)).Count(e => !e.Ongoing) == 2, "Even a one-minute difference prevents merging purchase deadlines");
        var local = new CalendarEvent { Id = "local-party", IsPersonal = true, Category = "personal", Title = "절바하 공대", Phases = [new() { Id = "local", Point = true, Start = CalendarBoundary.Instant(new(2026, 11, 10, 18, 0, 0, TimeSpan.FromHours(9))) }] };
        check(Project(maintenance, patch, local)[0].Primary.Event.Id == local.Id, "Explicit personal plans outrank public announcements without being merged");
        var hiddenPurchase = events.Where(e => e.Id != "kr:curated:v8-reservation-items").ToArray();
        check(CalendarPresentation.Build(CalendarEngine.DayEvents(hiddenPurchase, new(2027, 2, 19)), new(2027, 2, 19)).All(e => e.Members.All(i => i.Event.Id != "kr:curated:v8-reservation-items")), "Hiding one campaign member removes it before grouping and cannot leak it in a group");
        check(CalendarPresentation.Build(CalendarEngine.DayEvents(events, day), new(2026, 11, 11)).Count == 0, "Projection refuses items from another selected date");
        check(Day(2027, 2, 24).All(e => e.Members.All(i => i.Event.Category != "pvp-series")), "Unresolved conditional PvP dates never borrow an expansion release date");
    }
}
