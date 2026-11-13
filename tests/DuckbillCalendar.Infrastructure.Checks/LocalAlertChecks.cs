using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

internal static class LocalAlertChecks
{
    public static async Task<IReadOnlyList<string>> Run(string root, byte[] example)
    {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); checks.Add(name); }
        var now = DateTimeOffset.Parse("2026-11-12T06:00:00Z");
        LocalState State(int count = 1) => new()
        {
            View = new() { LocalChatAlerts = true },
            Entries = Enumerable.Range(0, count).Select(i => new PersonalEntry { Id = "local-alert-" + i, Title = "Personal " + i, Kind = "subscription", When = CalendarBoundary.Instant(now.AddHours(1)) }).ToArray(),
            Alerts = Enumerable.Range(0, count).Select(i => new AlertPreference { EventId = "local-alert-" + i, PhaseId = "local", Edge = "end", LeadMinutes = 60 }).ToArray()
        };
        async Task Drain(CalendarWorkspace workspace) { await workspace.FlushAsync(); workspace.Pump(); }
        async Task Scenario(string name, Func<CalendarWorkspace, LocalAlertCoordinator, string, Task> action)
        {
            var area = Path.Combine(root, "alerts-" + name);
            using var http = new HttpClient(new NoNetwork());
            using var workspace = new CalendarWorkspace(area, example, http, () => now);
            using var alerts = new LocalAlertCoordinator(workspace);
            await action(workspace, alerts, area);
            await workspace.FlushAsync();
        }
        await Scenario("disabled", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State() with { View = new() }); await Drain(workspace);
            alerts.Tick(now, "s1", true, _ => throw new Exception("Must not deliver"));
            Check(alerts.PendingCount == 0 && workspace.State.SentAlertKeys.Count == 0 && alerts.StatusCode == "Disabled", "Chat opt-out neither stages nor consumes alerts");
        });
        await Scenario("no-session", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace);
            alerts.Tick(now, null, true, _ => throw new Exception("Must not deliver"));
            Check(alerts.PendingCount == 0 && workspace.State.SentAlertKeys.Count == 0 && alerts.StatusCode == "NoSession", "Logged-out tick cannot consume or deliver alerts");
        });
        await Scenario("durable", async (workspace, alerts, area) =>
        {
            workspace.UpdateState(State()); await Drain(workspace);
            int deliveries = 0;
            alerts.Tick(now, "s1", true, _ => deliveries++);
            Check(deliveries == 0 && alerts.PendingCount == 1 && workspace.HasUnsavedChanges, "First due tick only stages consumption for persistence");
            await Drain(workspace);
            alerts.Tick(now, "s1", true, alert =>
            {
                var saved = CalendarJson.Parse<LocalState>(File.ReadAllBytes(Path.Combine(area, "state.json")));
                Check(saved.SentAlertKeys.Contains(alert.Key, StringComparer.Ordinal), "Delivery callback observes its consumption key already on disk");
                deliveries++;
            });
            alerts.Tick(now.AddSeconds(1), "s1", true, _ => deliveries++);
            Check(deliveries == 1 && alerts.PendingCount == 0, "Persisted key prevents second callback");
            using var replacement = new LocalAlertCoordinator(workspace);
            replacement.Tick(now.AddSeconds(1), "s1", true, _ => deliveries++);
            Check(deliveries == 1 && replacement.PendingCount == 0, "Coordinator restart does not replay consumed alert");
        });
        await Scenario("suppressed", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            alerts.Tick(now.AddSeconds(1), "s1", false, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 1 && alerts.StatusCode == "Suppressed", "Combat or duty suppression retains durable pending alert");
            alerts.Tick(now.AddMinutes(10), "s1", true, _ => delivered++);
            Check(delivered == 1 && alerts.PendingCount == 0, "Suppressed pending alert can deliver after due-window has elapsed");
        });
        await Scenario("session", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            alerts.Tick(now.AddSeconds(1), "s2", true, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 0 && workspace.State.SentAlertKeys.Count == 1, "Session change drops pending without undoing durable consumption");
        });
        await Scenario("profile", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State() with { Profiles = [new(), new() { Id = "other", Alias = "Other" }] }); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { ActiveProfileId = "other" }); await Drain(workspace);
            alerts.Tick(now.AddSeconds(1), "s1", true, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 0, "Active profile change drops prior profile pending");
        });
        await Scenario("selection", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { Alerts = [] }); await Drain(workspace);
            alerts.Tick(now.AddSeconds(1), "s1", true, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 0, "Disabled selection is revalidated immediately before delivery");
        });
        await Scenario("changed-boundary", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { Entries = [workspace.State.Entries[0] with { When = CalendarBoundary.Instant(now.AddHours(5)) }] }); await Drain(workspace);
            alerts.Tick(now.AddSeconds(1), "s1", true, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 0, "Changed effective boundary cancels old pending delivery");
        });
        await Scenario("revoked", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { LocalChatAlerts = false } });
            alerts.Tick(now.AddSeconds(1), "s1", true, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 0, "Revoked chat permission clears pending even before new settings finish saving");
        });
        await Scenario("callback-failure", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int attempts = 0;
            alerts.Tick(now, "s1", true, _ => attempts++); await Drain(workspace);
            alerts.Tick(now.AddSeconds(1), "s1", true, _ => { attempts++; throw new IOException("Simulated sink failure"); });
            Check(alerts.StatusCode == "DeliveryFailed" && attempts == 1, "Sink failure is surfaced after durable consumption");
            alerts.Tick(now.AddSeconds(2), "s1", true, _ => attempts++);
            Check(attempts == 1, "Partly-successful or failed sink is never retried");
        });
        await Scenario("ledger-full", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State() with { SentAlertKeys = Enumerable.Range(0, LocalAlertCoordinator.MaximumLedgerKeys).Select(i => "history-" + i).ToArray() }); await Drain(workspace);
            alerts.Tick(now, "s1", true, _ => throw new Exception("Must not deliver"));
            Check(alerts.PendingCount == 0 && alerts.StatusCode == "LedgerFull" && workspace.State.SentAlertKeys.Count == LocalAlertCoordinator.MaximumLedgerKeys, "Full ledger blocks new alerts without deleting old consumption history");
        });
        await Scenario("batch-limits", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State(25)); await Drain(workspace); int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            alerts.Tick(now.AddSeconds(10), "s1", false, _ => delivered++);
            Check(workspace.State.SentAlertKeys.Count == 5, "At most five keys staged within thirty seconds");
            for (int i = 1; i <= 3; i++) { alerts.Tick(now.AddSeconds(i * 30), "s1", false, _ => delivered++); await Drain(workspace); }
            alerts.Tick(now.AddSeconds(120), "s1", false, _ => delivered++);
            Check(alerts.PendingCount == 20 && workspace.State.SentAlertKeys.Count == 20 && alerts.StatusCode == "QueueFull", "Pending queue capped at twenty without consuming hidden overflow");
            alerts.Tick(now.AddSeconds(120), "s1", true, _ => delivered++); await Drain(workspace);
            alerts.Tick(now.AddSeconds(120), "s1", true, _ => delivered++);
            Check(delivered == 5 && alerts.PendingCount <= 20, "Pending burst output capped at five and remains bounded");
            alerts.Tick(now.AddSeconds(149), "s1", true, _ => delivered++);
            Check(delivered == 5, "Second output batch waits the full thirty seconds");
            alerts.Tick(now.AddSeconds(150), "s1", true, _ => delivered++);
            Check(delivered == 10, "Next output batch allowed after thirty seconds");
        });
        await Scenario("disposed", async (workspace, alerts, _) =>
        {
            workspace.UpdateState(State()); await Drain(workspace);
            alerts.Tick(now, "s1", false, _ => throw new Exception("Must not deliver")); await Drain(workspace);
            alerts.Dispose(); alerts.Tick(now.AddSeconds(1), "s1", true, _ => throw new Exception("Must not deliver"));
            Check(alerts.PendingCount == 0 && alerts.StatusCode == "Disposed", "Disposed coordinator never invokes game callback");
        });
        await Scenario("failed-write", async (workspace, alerts, area) =>
        {
            workspace.UpdateState(State()); await Drain(workspace); int delivered = 0;
            using (var blocked = new StateWriteBarrier(area))
            {
                alerts.Tick(now, "s1", true, _ => delivered++); await Drain(workspace);
                alerts.Tick(now.AddSeconds(1), "s1", true, _ => delivered++);
                Check(delivered == 0 && workspace.HasUnsavedChanges && alerts.StatusCode == "AwaitingPersistence", "Failed durable ledger write cannot trigger callback");
            }
            workspace.UpdateState(workspace.State with { Profiles = workspace.State.Profiles.ToArray() }); await Drain(workspace);
            alerts.Tick(now.AddSeconds(2), "s1", true, _ => delivered++);
            Check(delivered == 1 && !workspace.HasUnsavedChanges, "Successful retry acknowledges ledger before deferred delivery");
        });
        foreach (var mode in new[] { "health", "example", "file" })
        {
            var area = Path.Combine(root, "alerts-public-" + mode);
            var clock = now;
            var document = CalendarJson.Parse<FeedDocument>(example) with { IsExample = false };
            var item = document.Events[0];
            var phase = item.Phases[0];
            phase = phase with { End = phase.End with { Kind = "instant", Value = now.AddHours(1).ToString("O"), Certainty = "confirmed", Precision = "minute" }, EndInclusion = "exclusive" };
            document = document with { Events = [item with { Phases = [phase] }] };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(document, CalendarJson.Options);
            var pointer = new CurrentPointer
            {
                GeneratedAt = now, PublicationSequence = 1,
                Content = new() { Revision = document.ContentRevision, Path = "revisions/000001.json", Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), Bytes = bytes.Length },
                Health = new() { Sources = document.Evidence.Select(e => e.SourceId).Distinct().Select(id => new SourceHealth { Id = id, LastAttemptAt = now, LastSuccessAt = now, LastResult = "success", CompleteCoverage = true, ComparedContentRevision = document.ContentRevision }).ToArray() }
            };
            using var http = new HttpClient(new PublicFixtureHandler(() => JsonSerializer.SerializeToUtf8Bytes(pointer, CalendarJson.Options), bytes));
            using var workspace = new CalendarWorkspace(area, example, http, () => clock);
            using var alerts = new LocalAlertCoordinator(workspace);
            workspace.UpdateState(new() { View = new() { LocalChatAlerts = true, RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress }, Alerts = [new() { EventId = item.Id, PhaseId = phase.Id, Edge = "end", LeadMinutes = 60 }] });
            await Drain(workspace); workspace.Refresh(true); await Drain(workspace);
            Check(workspace.ActiveHealth is not null, "Remote health is bound to accepted bytes: " + mode);
            int delivered = 0;
            alerts.Tick(now, "s1", false, _ => delivered++); await Drain(workspace);
            Check(alerts.PendingCount == 1, "Fresh public exact alert stages before later revalidation: " + mode);
            if (mode == "example") { workspace.LoadExample(true); await Drain(workspace); clock = now.AddSeconds(1); }
            else if (mode == "file")
            {
                var path = Path.Combine(area, "import.json");
                await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(document with { Events = [document.Events[0] with { Title = "Different local bytes, same revision" }] }, CalendarJson.Options));
                workspace.ImportFile(path); await Drain(workspace); clock = now.AddSeconds(1);
                Check(workspace.ActiveHealth is null && workspace.RemotePointer is not null, "Imported same-revision bytes cannot borrow retained remote health");
            }
            else
            {
                pointer = pointer with { PublicationSequence = 2, Health = new() { Sources = pointer.Health.Sources.Select(s => s with { PendingChanges = [new() { Kind = "changed", FirstObservedAt = now, AffectedEventIds = [item.Id] }] }).ToArray() } };
                clock = now.AddMinutes(6); workspace.Refresh(true); await Drain(workspace);
            }
            alerts.Tick(clock, "s1", true, _ => delivered++);
            Check(delivered == 0 && alerts.PendingCount == 0, "Changed provenance or health cancels already-staged public alert: " + mode);
            if (mode == "file")
            {
                workspace.UpdateState(workspace.State with { SentAlertKeys = [] }); await Drain(workspace);
                alerts.Tick(clock, "s1", true, _ => delivered++);
                Check(alerts.PendingCount == 0 && workspace.State.SentAlertKeys.Count == 0, "Imported public feed cannot stage new alerts using old remote proof");
            }
        }
        return checks;
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Alerts must not start HTTP");
    }
    private sealed class PublicFixtureHandler(Func<byte[]> pointer, byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal) ? pointer() : payload) });
    }
}
