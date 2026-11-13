using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DuckbillCalendar.Infrastructure;

internal static class FrontlineChecks
{
    public static async Task Run(Action<bool, string> check, string root)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "DuckbillCalendar.slnx"))) repository = repository.Parent;
        var bytes = File.ReadAllBytes(Path.Combine(repository!.FullName, "tests/DuckbillCalendar.Infrastructure.Checks/Fixtures/frontline-2026-11.json"));
        var now = DateTimeOffset.Parse("2026-11-13T03:00:00Z");
        var parsed = FrontlineScheduleClient.Parse(bytes, "2026-11", now);
        check(parsed.Days.Count == 30 && parsed.Days.Single(d => d.Date == new DateOnly(2026, 11, 13)).Map == "워코 치테" && parsed.Days.Single(d => d.Date == new DateOnly(2026, 11, 14)).Map == "봉인된 바위섬", "Explicit KR provider dates retain today's and tomorrow's different maps");
        foreach (var invalid in new[] { "month", "reset", "map", "duplicate", "missing" })
        {
            var node = JsonNode.Parse(bytes)!; var days = node["data"]!["days"]!.AsArray();
            if (invalid == "month") days[0]!["date"] = "2026-12-01";
            if (invalid == "reset") days[0]!["startsAt"] = "2026-11-01T00:00:00Z";
            if (invalid == "map") days[0]!["subtitle"] = "invented map";
            if (invalid == "duplicate") days[1] = days[0]!.DeepClone();
            if (invalid == "missing") days.RemoveAt(0);
            var rejected = false;
            try { FrontlineScheduleClient.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()), "2026-11", now); }
            catch (Exception ex) when (ex is InvalidDataException or FormatException) { rejected = true; }
            check(rejected, "Frontline parser rejects " + invalid + " data instead of making up a rotation");
        }
        var status = HttpStatusCode.OK;
        using var handler = new FrontlineHttp(request => new(status) { RequestMessage = request, Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } });
        using var http = new HttpClient(handler);
        var cachePath = Path.Combine(root, "frontline-cache");
        using (var client = new FrontlineScheduleClient(http, cachePath))
        {
            client.Tick(now, false, 2026, 11);
            check(handler.Calls == 0, "Disabled frontline provider makes no request");
            await Pump(client, now);
            check(client.Get(new(2026, 11, 13), now)?.Map == "워코 치테" && handler.Calls == 1, "Bounded client fetches and applies the month on its caller thread");
            check(handler.LastUri == FrontlineScheduleClient.Endpoint + "?month=2026-11", "Frontline request only contains the public month, no account or character data");
            await Pump(client, now.AddHours(1));
            check(handler.Calls == 1, "Fresh frontline month is not fetched on every frame");
            status = HttpStatusCode.ServiceUnavailable;
            await Pump(client, now.AddHours(7));
            check(client.Get(new(2026, 11, 13), now.AddHours(7)) is not null && client.IsCached(new(2026, 11, 13), now.AddHours(7)), "A failed refresh retains a visibly cached explicit date for at most 24 hours");
            var failedCalls = handler.Calls;
            await Pump(client, now.AddHours(7).AddMinutes(1));
            check(handler.Calls == failedCalls, "Frontline failure backs off instead of retrying on every frame");
            check(client.Get(new(2026, 11, 13), now.AddHours(25)) is null, "Expired frontline cache cannot masquerade as a current map");
            check(client.Get(new(2026, 12, 1), now) is null, "A missing date is never extrapolated from a rotation");
        }
        using (var restarted = new FrontlineScheduleClient(http, cachePath))
        {
            await Pump(restarted, now.AddHours(1));
            check(restarted.Get(new(2026, 11, 13), now.AddHours(1))?.Map == "워코 치테", "Restart restores a validated unexpired month cache");
        }
        foreach (var failure in new[] { "redirect", "type", "oversized", "invalid-json", "compressed" })
        {
            using var badHttp = new HttpClient(new FrontlineHttp(request =>
            {
                var response = new HttpResponseMessage(failure == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(failure == "oversized" ? new byte[70000] : failure == "invalid-json" ? [123] : bytes)
                    { Headers = { ContentType = new(failure == "type" ? "text/html" : "application/json") } }
                };
                if (failure == "compressed") response.Content.Headers.ContentEncoding.Add("gzip");
                return response;
            }));
            using var bad = new FrontlineScheduleClient(badHttp);
            await Pump(bad, now);
            check(bad.Get(new(2026, 11, 13), now) is null && bad.Error is not null, "Frontline transport fails closed on " + failure);
        }
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false; var slowCalls = 0;
        using var slowHttp = new HttpClient(new AsyncFrontlineHttp(async (request, token) =>
        {
            if (++slowCalls == 1)
            {
                began.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
            }
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } };
        }));
        using (var cancellable = new FrontlineScheduleClient(slowHttp))
        {
            cancellable.Tick(now, true, 2026, 11); await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellable.Tick(now, false, 2026, 11); await cancellable.BackgroundTask.WaitAsync(TimeSpan.FromSeconds(3)); cancellable.Tick(now, false, 2026, 11);
            check(cancelled && !cancellable.IsBusy && cancellable.Error is null && cancellable.Get(new(2026, 11, 13), now) is null, "Disabling the provider cancels active IO without applying data or inventing a failure");
            await Pump(cancellable, now);
            check(slowCalls == 2 && cancellable.Get(new(2026, 11, 13), now) is not null, "Re-enabling a cancelled provider retries immediately instead of inheriting failure backoff");
        }
        var rateLimited = true; var limitedCalls = 0;
        using var limitedHttp = new HttpClient(new FrontlineHttp(request =>
        {
            limitedCalls++;
            var response = new HttpResponseMessage(rateLimited ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
            { RequestMessage = request, Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } };
            if (rateLimited) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(2));
            return response;
        }));
        using (var limited = new FrontlineScheduleClient(limitedHttp))
        {
            await Pump(limited, now); await Pump(limited, now.AddMinutes(20));
            check(limitedCalls == 1 && limited.Error is not null, "Frontline provider honors Retry-After beyond its normal 15-minute retry");
            rateLimited = false; await Pump(limited, now.AddHours(2));
            check(limitedCalls == 2 && limited.Error is null && limited.Get(new(2026, 11, 13), now.AddHours(2)) is not null, "Frontline recovers at the allowed retry time");
        }
        using var heldHandler = new FrontlineHttp(request => new(HttpStatusCode.OK) { RequestMessage = request,
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } });
        using var heldHttp = new HttpClient(heldHandler);
        using (var held = new FrontlineScheduleClient(heldHttp))
        {
            held.Tick(now, true, 2026, 11, allowAutomatic: false);
            check(heldHandler.Calls == 0 && !held.IsBusy, "Duty hold starts no automatic frontline or cache job");
            check(held.RequestManualRefresh(now), "Duty hold accepts an explicit manual frontline request");
            await Pump(held, now, allowAutomatic: false);
            check(heldHandler.Calls == 1 && held.Get(new(2026, 11, 13), now) is not null, "Manual frontline fetch runs and applies while automatic work is held");
            check(!held.RequestManualRefresh(now.AddMinutes(1)), "Manual frontline requests retain a five-minute cooldown");
            for (var i = 0; i < 20; i++) held.Tick(now.AddHours(7), true, 2026, 11, allowAutomatic: false);
            check(heldHandler.Calls == 1, "Stale frontline data does not auto-refresh during a long duty");
            check(held.RequestManualRefresh(now.AddHours(7)), "User can explicitly refresh stale frontline data during a long duty");
            held.Tick(now.AddHours(7), false, 2026, 11, allowAutomatic: false);
            check(heldHandler.Calls == 1, "Revoking provider consent clears a queued manual request before HTTP");
        }
    }
    private static async Task Pump(FrontlineScheduleClient client, DateTimeOffset now, bool allowAutomatic = true)
    {
        for (var i = 0; i < 1000; i++)
        {
            client.Tick(now, true, 2026, 11, allowAutomatic);
            if (!client.IsBusy) return;
            await Task.Delay(1);
        }
        throw new InvalidOperationException("Frontline fixture failed to settle");
    }
    public static async Task Live()
    {
        using var client = new FrontlineScheduleClient();
        var now = DateTimeOffset.UtcNow; var today = DuckbillCalendar.Core.CalendarEngine.Today(now);
        for (var i = 0; i < 200; i++)
        {
            client.Tick(now, true, today.Year, today.Month);
            if (client.Get(today, now) is { } day)
            {
                Console.WriteLine($"PASS: live HTTPS frontline response; KST {day.Date:yyyy-MM-dd}: {day.Map}; tomorrow={client.Get(today.AddDays(1), now)?.Map ?? "not supplied"}; source={FrontlineScheduleClient.SourceUrl}"); return;
            }
            if (client.Error is not null) throw new InvalidOperationException(client.Error);
            await Task.Delay(100);
        }
        throw new TimeoutException("Frontline live smoke timed out");
    }
    private sealed class FrontlineHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; LastUri = request.RequestUri!.AbsoluteUri; return Task.FromResult(respond(request)); }
    }
    private sealed class AsyncFrontlineHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
