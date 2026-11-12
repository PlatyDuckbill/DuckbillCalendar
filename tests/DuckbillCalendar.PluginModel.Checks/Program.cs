using System.Net;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

var checks = 0;
void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; }
static async Task Settle(CalendarModel model, CalendarWorkspace workspace, DateTimeOffset time)
{
    for (var i = 0; i < 8; i++)
    {
        model.Tick(time);
        await workspace.FlushAsync();
        model.Tick(time);
        if (!workspace.IsBusy) return;
    }
    throw new Exception("ModelDidNotSettle");
}
var repository = new DirectoryInfo(AppContext.BaseDirectory);
while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "DuckbillCalendar.slnx"))) repository = repository.Parent;
if (repository is null) throw new Exception("RepositoryNotFound");
var root = Path.Combine(repository.FullName, ".preview", "model-checks", Guid.NewGuid().ToString("N"));
var example = File.ReadAllBytes(Path.Combine(repository.FullName, "data", "contracts", "v2", "revisions", "000001.json"));
var now = new DateTimeOffset(2026, 11, 11, 20, 0, 0, TimeSpan.FromHours(9));

var firstPath = Path.Combine(root, "shutdown");
var workspace = new CalendarWorkspace(firstPath, example);
var model = new CalendarModel(workspace);
model.Tick(now);
Check(model.Events.Count == 0, "Default is an empty real feed, no fictional records");
Check(model.TodayFrontline?.IsKnown == false && model.TomorrowFrontline?.IsKnown == false, "Unknown frontline never invents maps");
var entry = new PersonalEntry { Id = "local-model", Title = "Manual subscription", Kind = "subscription", When = CalendarBoundary.Date(new(2026, 11, 18)) };
model.SaveEntry(entry);
Check(workspace.State.Entries.Count == 0, "Draw command queues instead of doing IO immediately");
var drainErrors = 0;
model.DrainPendingActions(_ => drainErrors++);
Check(drainErrors == 0 && workspace.State.Entries.Count == 1, "Unload transfers last-frame edit to the durable queue");
workspace.Dispose();
await workspace.FlushAsync();
using (var restored = new CalendarWorkspace(firstPath, example))
{
    Check(restored.State.Entries.Single().Title == entry.Title, "Last-frame entry survives unload and restart");
    var next = new CalendarModel(restored);
    next.Tick(now);
    Check(next.NextSubscription?.Id == entry.Id, "Subscription projection selects active profile");
    next.SelectDate(new(2026, 11, 18)); next.Tick(now);
    Check(next.DayItems.Count == 1 && next.SelectedPhaseId == "local", "Date selection focuses local phase");
    var localEvent = next.Events.Single();
    next.ToggleAlert(localEvent, localEvent.Phases[0], "end"); next.Tick(now);
    Check(next.HasAlert(localEvent) && restored.State.Alerts.Single().ProfileId == "default", "Alarm choice has account scope");
    next.UpdateView(v => v with { ExampleMode = true }); next.Tick(now);
    Check(next.Events.Any(e => e.Id == "kr:pvp:series:12"), "Explicit example mode projects public sample");
    var series = next.Events.Single(e => e.Id == "kr:pvp:series:12");
    next.SetQuery("nonmatching", 4, true); next.Tick(now);
    next.JumpToStart(series); next.Tick(now);
    Check(next.Year == 2026 && next.MonthNumber == 9 && next.SelectedDate == new DateOnly(2026, 9, 8) && next.SelectedPhaseId == "series", "Milestone jump resets query and focuses correct date/phase");
    Check(next.Search == "" && next.CategoryFilter == 0 && !next.OnlyAlerts, "Milestone clears conflicting filters");
    next.GoToday(); next.Tick(now);
    Check(next.SelectedDate == new DateOnly(2026, 11, 11) && next.MonthNumber == 11, "Today restores current calendar month");
    next.Tick(now.AddHours(5));
    Check(next.Today == new DateOnly(2026, 11, 12), "KST midnight refreshes today independently of visible month");
    await restored.FlushAsync(); restored.Pump();
    Check(!restored.HasUnsavedChanges, "Durable save completion clears unsaved indicator");
}

var clock = now;
var handler = new FailureHandler();
using var client = new HttpClient(handler);
using var remote = new CalendarWorkspace(Path.Combine(root, "retry"), example, client, () => clock);
var remoteModel = new CalendarModel(remote);
remoteModel.Tick(clock);
Check(handler.Requests == 0, "Opt-out model starts no request");
remoteModel.UpdateView(v => v with { RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress });
await Settle(remoteModel, remote, clock);
Check(handler.Requests == 1 && remote.StatusCode == "Http503", "Opt-in failure is reported without fabricated data");
clock = clock.AddMinutes(14); remoteModel.Tick(clock);
await Settle(remoteModel, remote, clock);
Check(handler.Requests == 1, "Automatic retry honors workspace backoff");
clock = clock.AddMinutes(1); remoteModel.Tick(clock);
await Settle(remoteModel, remote, clock);
Check(handler.Requests == 2, "15-minute recovery is not blocked by a duplicate hourly UI timer");
remoteModel.UpdateView(v => v with { RemoteUpdatesEnabled = false }); remoteModel.Tick(clock);
await remote.FlushAsync(); remote.Pump();
clock = clock.AddHours(2); remoteModel.Tick(clock);
Check(handler.Requests == 2, "Permission revocation stops model-driven refresh");
CalendarBoundary KnownDate(int year, int month, int day) => CalendarBoundary.Date(new(year, month, day)) with { EvidenceIds = ["fixture:evidence"] };
EventPhase Point(string id, CalendarBoundary date) => new() { Id = id, Label = id, Point = true, Start = date, End = new() { Reason = "not-announced" } };
var futureFeed = new FeedDocument
{
    ContentRevision = 1, GeneratedAt = now,
    Evidence = [new() { Id = "fixture:evidence", SourceId = "kr-curated:fixture", Url = "https://www.ff14.co.kr/events/2026/NovShop", ObservedAt = now, SourceSnapshotSha256 = new string('a', 64) }],
    Events = [
        new() { Id = "kr:fixture:expansion", Title = "Expansion fixture", Category = "patch", Phases = [
            Point("release", KnownDate(2027, 2, 23)),
            Point("early-access", CalendarBoundary.Instant(new(2027, 2, 19, 18, 0, 0, TimeSpan.FromHours(9))) with { EvidenceIds = ["fixture:evidence"] })] },
        new() { Id = "kr:fixture:notice", Title = "Notice fixture", Category = "notice", Phases = [Point("published", KnownDate(2026, 11, 11))] },
        new() { Id = "kr:fixture:cc", Title = "CC fixture", Category = "pvp-season", Phases = [Point("season", KnownDate(2026, 12, 1))] },
        new() { Id = "kr:fixture:pending", Title = "Unknown series fixture", Category = "pvp-series", Phases = [new() { Id = "pending", Label = "TBA", Start = new() { Reason = "not-announced" }, End = new() { Reason = "not-announced" } }] },
        new() { Id = "kr:curated:competition-fixture", Title = "Competition fixture", Category = "notice", Phases = [
            Point("published", KnownDate(2026, 11, 5)),
            new() { Id = "registration", Label = "Registration", Start = KnownDate(2026, 12, 8), End = KnownDate(2026, 12, 28), EndInclusion = "inclusive-unit" },
            Point("preliminary", KnownDate(2027, 1, 30)), Point("final", KnownDate(2027, 3, 14))] },
        new() { Id = "kr:fixture:sale", Title = "Sale fixture", Category = "sale", Phases = [new() { Id = "sale", Label = "Sale", Start = KnownDate(2026, 11, 4), End = KnownDate(2026, 12, 31), EndInclusion = "inclusive-unit" }] }
    ]
};
using (var futureHttp = new HttpClient(new FailureHandler()))
using (var futureWorkspace = new CalendarWorkspace(Path.Combine(root, "future-presentation"), example, futureHttp, bundledFeed: JsonSerializer.SerializeToUtf8Bytes(futureFeed, CalendarJson.Options)))
{
    var futureModel = new CalendarModel(futureWorkspace); futureModel.Tick(now);
    Check(!futureWorkspace.State.View.PeriodBars && futureModel.Calendar!.Weeks.All(w => w.Bars.Count == 0), "First use defaults to start/end markers instead of continuous bars");
    Check(futureModel.FilteredEvents.Any(e => e.Id == "kr:fixture:notice") && futureModel.CalendarEvents.All(e => e.Id != "kr:fixture:notice"), "Notice remains in news while publication markers stay off calendar by default");
    Check(futureModel.UpcomingHighlights.Count == 8 && futureModel.UpcomingHighlights[0].Event.Category == "pvp-season", "Upcoming milestones sort real starts and sale deadlines across year boundary");
    Check(futureModel.CalendarEvents.Single(e => e.Id == "kr:curated:competition-fixture").Phases.Count == 3 && futureModel.UpcomingHighlights.Count(h => h.Event.Id == "kr:curated:competition-fixture") == 4,
        "Hiding publication dates preserves every actual competition phase even when its event category is notice");
    Check(futureModel.UpcomingHighlights.Single(h => h.Event.Category == "sale").Edge == "end", "Sale milestone uses deadline rather than purchase opening");
    Check(futureModel.PendingHighlights.Single().Id == "kr:fixture:pending" && futureModel.UpcomingHighlights.All(h => h.Event.Id != "kr:fixture:pending"), "Unannounced PvP start stays pending without fabricated calendar date");
    var release = futureModel.UpcomingHighlights.Single(h => h.Phase.Id == "release");
    var early = futureModel.UpcomingHighlights.Single(h => h.Phase.Id == "early-access");
    Check(release.Boundary.Kind == "date" && early.Boundary.Kind == "instant", "Release date-only precision remains distinct from exact early access time");
    futureModel.JumpToHighlight(release); futureModel.Tick(now);
    Check(futureModel.Year == 2027 && futureModel.MonthNumber == 2 && futureModel.SelectedDate == new DateOnly(2027, 2, 23) && futureModel.SelectedPhaseId == "release", "Release shortcut selects exact phase and February day instead of first phase");
    Check(futureModel.Calendar!.Weeks.SelectMany(w => w.Days).SelectMany(d => d.Items).Any(i => i.Phase.Id == "early-access" && i.Date == new DateOnly(2027, 2, 19)) &&
        futureModel.Calendar.Weeks.SelectMany(w => w.Days).SelectMany(d => d.Items).Any(i => i.Phase.Id == "release" && i.Date == new DateOnly(2027, 2, 23)), "February grid contains both actual milestone dates as separate point markers");
    futureModel.JumpToHighlight(early); futureModel.Tick(now);
    Check(futureModel.SelectedDate == new DateOnly(2027, 2, 19) && futureModel.SelectedPhaseId == "early-access", "Early-access shortcut selects exact instant phase");
    futureModel.GoToMonth(2027, 1); futureModel.Tick(now);
    Check(futureModel.Year == 2027 && futureModel.MonthNumber == 1, "Direct month navigation reaches next year without repeated arrow clicks");
    futureModel.GoToMonth(2101, 13); futureModel.Tick(now);
    Check(futureModel.Year == 2027 && futureModel.MonthNumber == 1, "Invalid direct year/month never changes visible calendar");
    futureModel.SetQuery("", 6, false); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.Any(e => e.Category == "patch") && futureModel.CalendarEvents.All(e => e.Phases.All(p => !CalendarModel.IsPublicationPhase(p))) && futureModel.NewsEvents.All(e => e.Category == "notice"), "News category filter does not override independent calendar checkboxes");
    futureModel.SetQuery("", 0, false); futureModel.UpdateView(v => v with { ShowNoticePublications = true }); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.Any(e => e.Id == "kr:fixture:notice"), "Persistent notice visibility option takes effect in calendar projection");
    futureModel.GoToMonth(2026, 12); futureModel.SetPeriodBars(true); futureModel.Tick(now);
    Check(futureModel.Calendar!.Weeks.Any(w => w.Bars.Count > 0) && futureWorkspace.State.View.PeriodBarsConfigured, "Continuous bars remain an explicit persisted display option");
    futureModel.SetPeriodBars(false); futureModel.Tick(now);
    var boundaryDays = futureModel.Calendar!.Weeks.SelectMany(w => w.Days).ToArray();
    Check(futureModel.Calendar.Weeks.All(w => w.Bars.Count == 0) &&
        boundaryDays.Single(d => d.Date == new DateOnly(2026, 12, 8)).Items.Any(i => i.Phase.Id == "registration" && i.Edge == "start") &&
        boundaryDays.Single(d => d.Date == new DateOnly(2026, 12, 28)).Items.Any(i => i.Phase.Id == "registration" && i.Edge == "end") &&
        boundaryDays.Single(d => d.Date == new DateOnly(2026, 12, 15)).Items.All(i => i.Phase.Id != "registration"), "Endpoint mode shows both edges and leaves intermediate days free of period markers");
    futureWorkspace.UpdateState(futureWorkspace.State with { Alerts = [new() { EventId = "kr:fixture:expansion", PhaseId = "release", Edge = "start" }] });
    futureModel.SetCalendarVisible(CalendarGroup.Patches, false); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.All(e => e.Category != "patch") && futureModel.UpcomingHighlights.All(h => h.Event.Category != "patch"), "Unchecked patch calendar hides release and early-access milestones together");
    Check(futureModel.NewsEvents.Any(e => e.Category == "patch") && futureWorkspace.State.Alerts.Count == 1 && futureModel.Events.Count == futureFeed.Events.Count, "Visibility never deletes source data, news or reminder selections");
    futureModel.SetCalendarVisible(CalendarGroup.Pvp, false); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.All(e => e.Category is not ("pvp-season" or "pvp-series")) && futureModel.PendingHighlights.Count == 0, "PvP checkbox also hides undated series and upcoming entries");
    futureModel.SetCalendarVisible(CalendarGroup.Notices, false); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.Any(e => e.Id == "kr:curated:competition-fixture") && futureModel.CalendarEvents.All(e => e.Phases.All(p => !CalendarModel.IsPublicationPhase(p))), "Notice checkbox hides only publication dates while competition periods remain visible");
    futureModel.SetCalendarVisible(CalendarGroup.Notices, true); futureModel.SetCalendarVisible(CalendarGroup.Competitions, false); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.Single(e => e.Id == "kr:curated:competition-fixture").Phases.All(CalendarModel.IsPublicationPhase), "Competition checkbox can hide actual phases while keeping its independently selected publication date");
    futureModel.SetAllCalendars(false); futureModel.Tick(now);
    Check(futureModel.CalendarEvents.Count == 0 && futureModel.UpcomingHighlights.Count == 0 && futureModel.PendingHighlights.Count == 0 && futureModel.NewsEvents.Count > 0, "Hide-all clears calendar and highlights while news remains accessible");
    futureModel.SetAllCalendars(true); futureModel.Tick(now);
    Check(Enum.GetValues<CalendarGroup>().All(futureModel.IsCalendarVisible) && futureModel.CalendarEvents.Count == futureFeed.Events.Count, "Select-all restores every calendar group");
    futureModel.SetCalendarVisible(CalendarGroup.Patches, false); futureModel.SetPeriodBars(true); futureModel.Tick(now);
    await futureWorkspace.FlushAsync(); futureWorkspace.Pump();
    using var persisted = new CalendarWorkspace(Path.Combine(root, "future-presentation"), example, futureHttp, bundledFeed: JsonSerializer.SerializeToUtf8Bytes(futureFeed, CalendarJson.Options));
    var persistedModel = new CalendarModel(persisted); persistedModel.Tick(now);
    Check(!persistedModel.IsCalendarVisible(CalendarGroup.Patches) && persisted.State.View.PeriodBars && persisted.State.View.PeriodBarsConfigured, "Category choices and explicit bar option survive restart");
}

using (var legacyHttp = new HttpClient(new FailureHandler()))
using (var legacy = new CalendarWorkspace(Path.Combine(root, "legacy-bars"), example, legacyHttp))
{
    legacy.UpdateState(legacy.State with { View = legacy.State.View with { PeriodBars = true, PeriodBarsConfigured = false } });
    await legacy.FlushAsync(); legacy.Pump();
    var legacyModel = new CalendarModel(legacy); legacyModel.Tick(now);
    Check(!legacy.State.View.PeriodBars && legacy.State.View.PeriodBarsConfigured, "Previous default-on settings switch once to requested endpoint default");
    legacyModel.SetPeriodBars(true); legacyModel.Tick(now); await legacy.FlushAsync(); legacy.Pump();
    var reloadedModel = new CalendarModel(legacy);
    Check(legacy.State.View.PeriodBars, "Explicitly re-enabling bars is not reset by another model load");
}

Console.WriteLine($"PASS: {checks} actual CalendarModel checks; localization is stubbed, no ImGui/game runtime is exercised.");

sealed class FailureHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request });
    }
}
