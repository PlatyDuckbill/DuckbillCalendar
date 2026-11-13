using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Collect;

internal static class CuratedSourcesChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); checks++; }
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("No workspace");
        var area = Path.Combine(root, ".preview", "curated-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(area);
        var definition = new CuratedSourceDefinition { Id = "official", Url = "https://www.ff14.co.kr/Eorzea/GMLive", EvidenceUrl = "https://www.ff14.co.kr/Eorzea/GMLive" };
        var guard = new CuratedGuard { SourceId = "official", Selector = "article", RequiredText = ["2027년 2월 23일 출시"] };
        Check(await CuratedSources.GuardMatchesAsync(definition, guard, "<article>2027년 <b>2월 23일</b> 출시</article>"), "Scoped official phrase ignores markup and whitespace");
        Check(!await CuratedSources.GuardMatchesAsync(definition, guard, "<article>2027년 2월 24일 출시</article>"), "Changed official date requires review");
        Check(!await CuratedSources.GuardMatchesAsync(definition, guard, "<article>미정</article><footer>2027년 2월 23일 출시</footer>"), "Footer text cannot satisfy article guard");
        Check(!await CuratedSources.GuardMatchesAsync(definition, guard, "<article><script>2027년 2월 23일 출시</script>미정</article>"), "Executable page script is excluded from HTML text proof");
        var linkedGuard = guard with { RequiredScriptUrls = ["https://image.ff14.co.kr/event/2026/novshop/js/ui.min.js?version=1"] };
        Check(await CuratedSources.GuardMatchesAsync(definition, linkedGuard, "<article>2027년 2월 23일 출시</article><script src='//image.ff14.co.kr/event/2026/novshop/js/ui.min.js?version=1'></script>"), "Template source must still be referenced by the current official page");
        Check(!await CuratedSources.GuardMatchesAsync(definition, linkedGuard, "<article>2027년 2월 23일 출시</article><script src='//image.ff14.co.kr/event/2026/novshop/js/ui.min.js?version=2'></script>"), "Changed official asset fingerprint requires review instead of reusing obsolete template");
        var templateDefinition = definition with { Format = "script-template" };
        var templateGuard = guard with { RequiredText = ["앞서 해보기 일시: 2027년 2월 19일(금) 18시부터 플레이 가능"] };
        Check(await CuratedSources.GuardMatchesAsync(templateDefinition, templateGuard, "const x='<p>앞서 해보기 일시: <b>2027년 2월 19일(금) 18시</b>부터 플레이 가능</p>';"), "Static official HTML template is inspected without execution");
        var calendarEvent = new CalendarEvent { Id = "kr:curated:test-release", Title = "Release", Category = "patch", Phases = [new()
        {
            Id = "release", Label = "출시", Start = CalendarBoundary.Date(new(2027, 2, 23)), End = new() { Reason = "not-announced" }, Point = true
        }] };
        var rules = new CuratedRules { Sources = [definition], Rules = [new() { Event = calendarEvent, Guards = [guard] }] };
        var rulePath = Path.Combine(area, "rules.json");
        await File.WriteAllBytesAsync(rulePath, JsonSerializer.SerializeToUtf8Bytes(rules, CalendarJson.Options));
        string html = "<article>2027년 2월 23일 출시</article>";
        HttpStatusCode status = HttpStatusCode.OK;
        using var http = new HttpClient(new FixtureHandler(() => new(status) { Content = Html(html) }));
        var first = await CuratedSources.CollectAsync(rulePath, Path.Combine(area, "first"), null, http, CancellationToken.None);
        Check(first.Feed.Events.Count == 1 && first.PendingEventIds.Count == 0 && FeedValidator.Validate(first.Feed).Count == 0, "Matching official statement emits valid production event");
        Check(first.Feed.Events[0].Phases[0].Start.Kind == "date", "Release date never becomes invented midnight instant");
        Check(first.Feed.Evidence[0].SourceSnapshotSha256 == Collector.Hash(Encoding.UTF8.GetBytes(html)), "Curated boundary is bound to exact source bytes");
        var targetProof = first.Feed.Evidence[0] with { Id = "proof-target-only" };
        var withTarget = first.Feed with { Evidence = first.Feed.Evidence.Append(targetProof).ToArray(),
            Targets = [new() { Id = "kr:target:resolved", Kind = "patch-open", Resolution = "resolved", ResolvedBoundary = CalendarBoundary.Date(new(2027, 2, 23)) with { EvidenceIds = [targetProof.Id] } }] };
        var retainedTarget = await CuratedSources.CollectAsync(rulePath, Path.Combine(area, "resolved-target"), withTarget, http, CancellationToken.None);
        Check(retainedTarget.Feed.Evidence.Any(e => e.Id == targetProof.Id) && FeedValidator.Validate(retainedTarget.Feed).Count == 0, "Resolved target proof survives even when no mapped event references it");
        html = "<article>2027년 2월 24일 출시</article>";
        var changed = await CuratedSources.CollectAsync(rulePath, Path.Combine(area, "changed"), first.Feed, http, CancellationToken.None);
        Check(changed.PendingEventIds.SequenceEqual(new[] { calendarEvent.Id }) && changed.Feed.Events[0].Phases[0].Start.Value == "2027-02-23", "Source date change preserves prior date and flags review");
        Check(changed.Feed.Evidence[0].SourceSnapshotSha256 == first.Feed.Evidence[0].SourceSnapshotSha256, "Pending fact keeps its original evidence hash");
        var freshChanged = await CuratedSources.CollectAsync(rulePath, Path.Combine(area, "fresh-changed"), null, http, CancellationToken.None);
        Check(freshChanged.Feed.Events.Count == 0 && freshChanged.PendingEventIds.Count == 1, "Unverified configured date is never emitted on fresh collection");
        status = HttpStatusCode.ServiceUnavailable;
        var unavailable = await CuratedSources.CollectAsync(rulePath, Path.Combine(area, "unavailable"), first.Feed, http, CancellationToken.None);
        Check(unavailable.Feed.Events.Count == 1 && unavailable.SourceResults[0].Status == "unavailable", "HTTP failure retains prior curated event");
        var repeated = await CuratedSources.CollectAsync(rulePath, Path.Combine(area, "repeated"), changed.Feed, http, CancellationToken.None);
        Check(repeated.Feed.Events[0].Note == changed.Feed.Events[0].Note, "Repeated review pending does not grow notes indefinitely");
        var foreign = rules with { Sources = [definition with { Url = "https://example.com/data" }] };
        var foreignPath = Path.Combine(area, "foreign.json"); await File.WriteAllBytesAsync(foreignPath, JsonSerializer.SerializeToUtf8Bytes(foreign, CalendarJson.Options));
        try { await CuratedSources.CollectAsync(foreignPath, Path.Combine(area, "foreign"), null, http, CancellationToken.None); throw new Exception("Foreign URL accepted"); }
        catch (InvalidDataException) { Check(true, "Foreign URL is rejected before HTTP"); }
        var templateRules = rules with { Sources = [definition with { Format = "script-template", Url = "https://image.ff14.co.kr/event/2026/novshop/js/ui.min.js" }], Rules = [new() { Event = calendarEvent, Guards = [templateGuard] }] };
        var templatePath = Path.Combine(area, "template.json"); await File.WriteAllBytesAsync(templatePath, JsonSerializer.SerializeToUtf8Bytes(templateRules, CalendarJson.Options));
        using var templateHttp = new HttpClient(new FixtureHandler(() =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes("throw new Error('must never execute'); const x='<p>앞서 해보기 일시: <b>2027년 2월 19일(금) 18시</b>부터 플레이 가능</p>';"));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-javascript");
            return new(HttpStatusCode.OK) { Content = content };
        }));
        Check((await CuratedSources.CollectAsync(templatePath, Path.Combine(area, "template"), null, templateHttp, CancellationToken.None)).Feed.Events.Count == 1,
            "Official IIS x-javascript template content type is accepted as data without execution");
        Console.WriteLine($"PASS: {checks} Curated source checks; fake HTTP only; artifacts={area}");
        return 0;
    }
    private static HttpContent Html(string html)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(html)); content.Headers.ContentType = new MediaTypeHeaderValue("text/html"); return content;
    }
    private sealed class FixtureHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response());
    }
}
