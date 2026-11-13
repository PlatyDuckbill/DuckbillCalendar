using System.Net;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

internal static class BehaviorChecks
{
    public static async Task<int> Run(string root, byte[] example, FeedDocument snapshot)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        var now = DateTimeOffset.Parse("2026-11-13T03:00:00Z");
        async Task Settle(CalendarModel model, CalendarWorkspace workspace)
        {
            for (var i = 0; i < 8; i++) { model.Tick(now); await workspace.FlushAsync(); model.Tick(now); if (!workspace.IsBusy) return; }
            throw new InvalidOperationException("Behavior model failed to settle");
        }
        var activityPath = Path.Combine(root, "activity-deferral");
        using (var handler = new FailingHttp())
        using (var http = new HttpClient(handler))
        using (var workspace = new CalendarWorkspace(activityPath, example, http, () => now))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false, "전투 중");
            model.UpdateView(v => v with { RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress }); await Settle(model, workspace);
            Check(handler.Calls == 0 && !workspace.RemoteRefreshFailed, "Combat deferral neither requests data nor creates a network failure");
            workspace.Refresh(true); await Settle(model, workspace);
            Check(handler.Calls == 1 && workspace.StatusCode == "Http503", "Manual refresh still runs when automatic updates are deferred");
            model.SetAutomaticUpdateState(true, null); await Settle(model, workspace);
            Check(handler.Calls == 1, "Leaving combat cannot bypass a genuine server-failure backoff");
            now = now.AddMinutes(15); await Settle(model, workspace);
            for (var i = 0; i < 100; i++) model.Tick(now);
            await workspace.FlushAsync(); model.Tick(now);
            Check(handler.Calls == 2, "Resuming issues one due request, not one per blocked tick");
        }
        var phasePath = Path.Combine(root, "phase-visibility");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, CalendarJson.Options);
        const string tournamentId = "kr:curated:ccrc-2027-korea";
        using (var workspace = new CalendarWorkspace(phasePath, example, null, bundledFeed: bytes))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false, null); model.Tick(now);
            var tournament = model.Events.Single(e => e.Id == tournamentId);
            model.SetPhaseHidden(tournamentId, "selection", true); model.SetPhaseHidden(tournamentId, "selection", true); model.Tick(now);
            var visible = model.CalendarEvents.Single(e => e.Id == tournamentId);
            Check(visible.Phases.Count == 3 && visible.Phases.All(p => p.Id != "selection") && model.State.View.HiddenEventIds.Count == 0, "Hiding a selection period leaves the tournament's other three schedules visible");
            Check(model.State.View.HiddenPhases.Count == 1 && model.IsCalendarVisible(CalendarGroup.Competitions), "Repeated individual hiding does not change category visibility or duplicate keys");
            model.SelectDate(new(2026, 12, 7)); model.Tick(now);
            Check(model.DayItems.All(i => i.Event.Id != tournamentId || i.Phase.Id != "selection"), "The hidden interval also disappears at its end and in ongoing results");
            model.SelectDate(new(2026, 12, 8)); model.Tick(now);
            Check(model.DayItems.Any(i => i.Event.Id == tournamentId && i.Phase.Id == "registration"), "A later registration remains visible after hiding selection");
            Check(model.Events.Single(e => e.Id == tournamentId).Phases.Count == 4 && model.NewsEvents.Any(e => e.Id == tournamentId), "Individual hiding preserves original phases and news");
            model.SetPhaseHidden(tournamentId, "selection", false); model.Tick(now);
            Check(model.CalendarEvents.Single(e => e.Id == tournamentId).Phases.Count == 4, "Individual restoration returns its schedule without refetching");
            foreach (var id in model.RelatedEventIds([tournamentId])) model.SetEventHidden(id, true); model.Tick(now);
            Check(model.CalendarEvents.All(e => e.Id != tournamentId) && model.IsCalendarVisible(CalendarGroup.Competitions) && model.CalendarEvents.Any(e => e.Category == "patch"), "Whole-event hiding leaves category visibility and unrelated categories untouched");
            model.SetEventHidden(tournamentId, false); model.SetPhaseHidden(tournamentId, "selection", true); model.Tick(now);
            var changed = snapshot with { Events = snapshot.Events.Select(e => e.Id == tournamentId ? tournament with { Title = "제목 수정", Phases = tournament.Phases.Reverse().ToArray() } : e).ToArray() };
            var imported = Path.Combine(root, "reordered-phases.json"); await File.WriteAllBytesAsync(imported, JsonSerializer.SerializeToUtf8Bytes(changed, CalendarJson.Options));
            workspace.ImportFile(imported); await Settle(model, workspace);
            Check(model.CalendarEvents.Single(e => e.Id == tournamentId).Phases.All(p => p.Id != "selection") && model.CalendarEvents.Single(e => e.Id == tournamentId).Phases.Count == 3,
                "Title and phase-order changes do not change what the stored hide key identifies");
            await workspace.FlushAsync();
        }
        using (var workspace = new CalendarWorkspace(phasePath, example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false, null); model.Tick(now);
            Check(model.State.View.HiddenPhases.Single().PhaseId == "selection" && model.CalendarEvents.Single(e => e.Id == tournamentId).Phases.Count == 3, "Individual hide survives restart");
            model.RestoreHiddenEvents(); await Settle(model, workspace);
            Check(model.State.View.HiddenPhases.Count == 0 && model.State.View.HiddenEventIds.Count == 0 && model.CalendarEvents.Single(e => e.Id == tournamentId).Phases.Count == 4, "Restore-all covers both old whole-event and new individual preferences");
        }
        var resetPath = Path.Combine(root, "reset-subscription");
        var resetEntry = new PersonalEntry { Id = "reset-service", Kind = "subscription", Title = "정액제", When = CalendarBoundary.Instant(now.AddDays(30)), Note = "충전 메모" };
        var keepEntry = resetEntry with { Id = "keep-retainer", Kind = "retainer", Title = "집사 이용권" };
        var personalEntry = resetEntry with { Id = "keep-personal", Kind = "personal", Title = "개인 일정" };
        var otherProfile = resetEntry with { Id = "other-profile-service", ProfileId = "other" };
        using (var workspace = new CalendarWorkspace(resetPath, example))
        {
            workspace.UpdateState(workspace.State with
            {
                Profiles = [new(), new() { Id = "other", Alias = "다른 프로필" }], Entries = [resetEntry, keepEntry, personalEntry, otherProfile],
                Alerts = new[] { resetEntry, keepEntry, personalEntry, otherProfile }.Select(e => new AlertPreference { EventId = e.Id, ProfileId = e.ProfileId, PhaseId = "local" }).ToArray(),
                View = workspace.State.View with { RemoteUpdatesEnabled = false, HiddenEventIds = [resetEntry.Id, keepEntry.Id], HiddenPhases = [new() { EventId = resetEntry.Id, PhaseId = "local" }, new() { EventId = keepEntry.Id, PhaseId = "local" }] }
            });
            await workspace.FlushAsync(); workspace.Pump();
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false, null); model.Tick(now);
            model.ResetSubscription(personalEntry.Id); model.ResetSubscription(otherProfile.Id); model.Tick(now);
            var migratedPersonal = model.State.Entries.Single(e => e.Id == personalEntry.Id);
            Check(model.State.Entries.Count == 4, "Subscription reset cannot delete personal entries or another profile's service");
            model.ResetSubscription(resetEntry.Id);
            Check(model.State.Entries.Count == 4, "Reset is queued until the UI model tick");
            await Settle(model, workspace);
            Check(model.State.Entries.Count == 3 && model.State.Entries.All(e => e.Id != resetEntry.Id) && model.State.Entries.Contains(keepEntry) && model.State.Entries.Contains(migratedPersonal) && model.State.Entries.Contains(otherProfile), "Reset clears only the selected subscription and its memo");
            Check(model.State.Alerts.Count == 3 && model.State.Alerts.All(a => a.EventId != resetEntry.Id), "Reset removes only the obsolete subscription reminder");
            Check(model.State.View.HiddenEventIds.SequenceEqual(new[] { keepEntry.Id }) && model.State.View.HiddenPhases.Single().EventId == keepEntry.Id, "Reset cleans the removed item's hidden preferences while retaining others");
            Check(!model.State.View.RemoteUpdatesEnabled && model.State.ActiveProfileId == "default", "Reset leaves update settings and the active profile unchanged");
            var afterReset = model.State; model.ResetSubscription(resetEntry.Id); model.Tick(now);
            Check(ReferenceEquals(afterReset, model.State), "Repeating reset on a removed subscription is a no-op");
            workspace.UpdateState(workspace.State with { Alerts = workspace.State.Alerts.Select(a => a.EventId == personalEntry.Id ? a with { Enabled = false, LeadMinutes = 30 } : a).ToArray() });
            model.SetEntryReminder(personalEntry.Id, true); await Settle(model, workspace);
            Check(model.State.Alerts.Single(a => a.EventId == personalEntry.Id) is { Enabled: true, LeadMinutes: 30 }, "Central notification settings can re-enable an old disabled choice without losing its lead time");
            model.SaveEntry(personalEntry with { Title = "변경된 개인 일정" }); await Settle(model, workspace);
            Check(model.State.Alerts.Single(a => a.EventId == personalEntry.Id) is { Enabled: true, LeadMinutes: 30 }, "Editing an entry preserves its separately managed notification preference");
            await workspace.FlushAsync();
        }
        using (var workspace = new CalendarWorkspace(resetPath, example))
            Check(workspace.State.Entries.Count == 3 && workspace.State.Entries.All(e => e.Id != resetEntry.Id) && workspace.State.Alerts.Count == 3, "Subscription reset survives restart without removing other data");
        return checks;
    }
    private sealed class FailingHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request }); }
    }
}
