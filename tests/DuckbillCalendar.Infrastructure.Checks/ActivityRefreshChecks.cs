using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

internal static class ActivityRefreshChecks
{
    public static async Task Run(Action<bool, string> check, string root, byte[] example)
    {
        var now = DateTimeOffset.Parse("2026-11-13T03:00:00Z");
        var feed = new FeedDocument { ContentRevision = 1, GeneratedAt = now };
        var payload = JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options);
        var pointer = JsonSerializer.SerializeToUtf8Bytes(new CurrentPointer { PublicationSequence = 1, GeneratedAt = now,
            Content = new() { Revision = 1, Path = "revisions/000001.json", Sha256 = Convert.ToHexStringLower(SHA256.HashData(payload)), Bytes = payload.Length } }, CalendarJson.Options);
        HttpResponseMessage Reply(HttpRequestMessage request) => new(HttpStatusCode.OK) { RequestMessage = request,
            Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal) ? pointer : payload) { Headers = { ContentType = new("application/json") } } };
        async Task Drain(CalendarWorkspace workspace) { await workspace.FlushAsync(); workspace.Pump(); }
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wasCancelled = false; var calls = 0;
        using var http = new HttpClient(new AsyncHttp(async (request, token) =>
        {
            if (++calls == 1)
            {
                began.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { wasCancelled = true; throw; }
            }
            return Reply(request);
        }));
        using (var workspace = new CalendarWorkspace(Path.Combine(root, "activity-auto-cancel"), example, http, () => now))
        {
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress } });
            await Drain(workspace); workspace.Refresh(); await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
            workspace.SetAutomaticRefreshAllowed(false); await Drain(workspace);
            check(wasCancelled && calls == 1 && !workspace.RemoteRefreshFailed && workspace.StatusCode == "AutomaticDeferred", "Entering combat cancels an automatic request without recording a server failure");
            workspace.Refresh(); await Drain(workspace);
            check(calls == 1, "Paused workspace rejects automatic calls even outside the UI model");
            workspace.UpdateState(workspace.State with { Entries = [new() { Id = "while-busy", Title = "Local", When = CalendarBoundary.Date(new(2026, 11, 15)) }] });
            await Drain(workspace); check(!workspace.HasUnsavedChanges && workspace.State.Entries.Count == 1, "Activity deferral does not defer personal-state persistence");
            now = now.AddMinutes(6); workspace.Refresh(true); await Drain(workspace);
            check(calls == 3 && workspace.Feed.ContentRevision == 1 && !workspace.RemoteRefreshFailed, "Manual refresh bypasses the activity hold while retaining the normal cooldown");
        }
        var manualBegan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualCancelled = false;
        using var manualHttp = new HttpClient(new AsyncHttp(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal))
            {
                manualBegan.TrySetResult();
                try { await release.Task.WaitAsync(token); } catch (OperationCanceledException) { manualCancelled = true; throw; }
            }
            return Reply(request);
        }));
        using (var workspace = new CalendarWorkspace(Path.Combine(root, "activity-manual-continues"), example, manualHttp, () => now))
        {
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress } }); await Drain(workspace);
            workspace.Refresh(true); await manualBegan.Task.WaitAsync(TimeSpan.FromSeconds(3));
            workspace.SetAutomaticRefreshAllowed(false); release.TrySetResult(); await Drain(workspace);
            check(!manualCancelled && workspace.Feed.ContentRevision == 1, "A manually started request is not cancelled by a later combat hold");
        }
    }
    private sealed class AsyncHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
