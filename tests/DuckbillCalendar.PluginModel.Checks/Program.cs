using System.Net;
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
