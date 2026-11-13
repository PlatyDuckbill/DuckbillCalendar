using System.Net;
using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Collect;

internal static class NoticeChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("FAIL: " + name); checks++; }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException) { checks++; return; }
            throw new InvalidOperationException("FAIL: " + name);
        }
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var area = Path.Combine(root, ".preview/notice-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(area);
        string Index(string id, string title, string date = "2026.11.10") => $"<ul class='list_type1'><li><div class='subj'><a href='/news/notice/view/{id}?page=1'><span class='txt'>{title}</span><i>NEW</i></a></div><time datetime='{date}'>{date}</time></li></ul>";
        string Article(string title, string body, string time = "2026-11-10T18:00") => $"<div class='view_box'><div class='view_summary'><h4>{title}</h4><time datetime='{time}'>{time}</time></div><div class='view_contents'>{body}</div></div>";
        const string catalog = "<ul class='list_patch'><li><div class='head'><span class='date'>2026. 04. 28</span><strong class='title'>Fixture major patch</strong></div><div class='patch_links'><a>7.5 패치노트</a><a>7.58 패치노트</a></div></li></ul>";
        var observed = DateTimeOffset.UtcNow;
        NoticePage Page(string kind, string path, string file, string html) => new(kind, "https://www.ff14.co.kr" + path, file, Encoding.UTF8.GetByteCount(html), Collector.Hash(Encoding.UTF8.GetBytes(html)), observed);
        var index = await NoticeParser.ParseIndexAsync(Index("3162", "V7.58 업데이트 점검 완료"));
        Check(index.Single().Title == "V7.58 업데이트 점검 완료", "Notice index title excludes NEW badge");
        await Reject(async () => { await NoticeParser.ParseIndexAsync("<h1>error</h1>"); }, "HTTP-200 error page is not an empty successful notice list");
        var clockIndex = (await NoticeParser.ParseIndexAsync(Index("1", "today notice", "18:00"))).Single();
        Check(clockIndex.PublishedDate is null && clockIndex.PublishedTime == new TimeOnly(18, 0), "List clock is retained without inventing a date; article will supply the date");
        await Reject(async () => { await NoticeParser.ParseIndexAsync(Index("1", "bad time", "24:00")); }, "Invalid clock cannot stand in for a date");
        const string body = "1. 게임 및 런처 점검 완료 - 일시: 2026년 11월 10일(화) 12:00 ~ 18:15 - 내용: V7.58 업데이트 점검 3. 크리스탈라인 컨플릭트 시즌 관련 안내 - V7.58 업데이트 점검이 완료되어, 시즌 21이 종료되고 시즌 22가 시작됩니다.";
        var html = Article("V7.58 업데이트 점검 완료", body);
        var article = await NoticeParser.ParseArticleAsync(html, Page("article", "/news/notice/view/3162", "notice-3162.html", html));
        var derived = NoticeParser.EventsFor(article);
        Check(derived.Single(e => e.Event.Category == "notice").Event.Phases[0].Label == "공지 게시", "Publication is labeled separately from gameplay boundary");
        Check(derived.Single(e => e.Event.Category == "maintenance").Event.Phases[0].End.Value!.Contains("18:15", StringComparison.Ordinal), "Explicit completed maintenance interval is extracted");
        Check(derived.Single(e => e.Event.Category == "patch").Event.Phases[0].Start.Kind == "date", "Patch day never invents a launch minute");
        Check(derived.Single(e => e.Event.Category == "pvp-season").Event.Id == "kr:pvp:cc-season-22", "Explicit notice season transition identifies new CC season");
        var planned = NoticeParser.EventsFor(article with { Title = "V7.58 업데이트 점검 안내", Body = body.Replace("점검 완료", "점검", StringComparison.Ordinal).Replace("점검이 완료되어", "점검 완료 후", StringComparison.Ordinal) });
        Check(planned.Single(e => e.Event.Category == "patch").Event.Phases[0].Start.Certainty == "tentative", "Planned patch day is visibly tentative");
        Check(NoticeParser.EventsFor(article with { Body = "시즌 22 시작, V7.58 업데이트", Title = "V7.58 안내" }).Count == 1, "Unscoped version/season prose does not reuse publication day");
        Check(NoticeParser.EventsFor(article with { Body = body.Replace("12:00 ~ 18:15", "23:00 ~ 02:00", StringComparison.Ordinal) }).Count == 1, "Cross-midnight end without explicit date is not guessed");
        var patches = await NoticeParser.ParsePatchCatalogAsync(catalog, Page("patch-catalog", "/news/patchnote", "patch-catalog.html", catalog));
        Check(patches.Count == 1 && patches[0].Event.Id == "kr:patch:v7-5", "Major patch card date is never assigned to minor patch links");
        var historicCatalog = catalog + "<ul class='list_patch'><li><div class='head'><span class='date'>2016. 04. 12</span></div><div class='patch_links'><a>Part 2 패치노트 (상)</a></div></li></ul>";
        Check((await NoticeParser.ParsePatchCatalogAsync(historicCatalog, Page("patch-catalog", "/news/patchnote", "patch-catalog.html", historicCatalog))).Count == 1,
            "Historical nonnumeric patch labels outside two-year scope do not corrupt current catalog");
        using (var http = new HttpClient(new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.Redirect))))
            await Reject(async () => { await NoticeCollector.FetchAsync(http, NoticeCollector.NoticeRoot, default); }, "Notice redirect fails closed");
        using (var http = new HttpClient(new ResponseHandler(() => HtmlResponse("ok"))))
            await Reject(async () => { await NoticeCollector.FetchAsync(http, "https://example.com/news/notice", default); }, "Notice fetch rejects off-host URL before HTTP");

        var oldTitle = "V7.58 업데이트 점검 안내";
        var oldHtml = Article(oldTitle, body.Replace("18:15", "19:00", StringComparison.Ordinal).Replace("점검 완료", "점검", StringComparison.Ordinal).Replace("점검이 완료되어", "점검 완료 후", StringComparison.Ordinal), "2026-11-06T18:20");
        var bothIndex = Index("3159", oldTitle, "2026.11.06") + Index("3162", article.Title);
        var sources = new[]
        {
            (Page("index", "/news/notice?page=1", "notice-list-1.html", bothIndex), bothIndex),
            (Page("article", "/news/notice/view/3159", "notice-3159.html", oldHtml), oldHtml),
            (article.Source, html), (Page("patch-catalog", "/news/patchnote", "patch-catalog.html", catalog), catalog)
        };
        foreach (var (source, content) in sources) await File.WriteAllTextAsync(Path.Combine(area, source.File), content, Collector.Utf8);
        await File.WriteAllBytesAsync(Path.Combine(area, "notices.collection.json"), JsonSerializer.SerializeToUtf8Bytes(new NoticeManifest { CollectedAt = observed.AddSeconds(1), Pages = sources.Select(s => s.Item1).ToArray() }, CalendarJson.Options));
        var feed = await NoticeCollector.PrepareAsync(area);
        Check(FeedValidator.Validate(feed).Count == 0 && feed.Events.Count == 6, "Notice collection produces validated production feed");
        Check(feed.Events.Single(e => e.Category == "maintenance").Phases[0].End.Value!.Contains("18:15", StringComparison.Ordinal), "Later completed notice supersedes earlier scheduled maintenance end");
        Check(feed.Evidence.All(e => e.Url!.StartsWith("https://www.ff14.co.kr/", StringComparison.Ordinal) && e.SourceSnapshotSha256!.Length == 64), "Notice feed carries exact official evidence hashes");
        await File.WriteAllBytesAsync(Path.Combine(area, "notices.collection.json"), JsonSerializer.SerializeToUtf8Bytes(new NoticeManifest
            { CollectedAt = observed.AddSeconds(1), IncludesPatchCatalog = false, Pages = sources.Where(s => s.Item1.Kind != "patch-catalog").Select(s => s.Item1).ToArray() }, CalendarJson.Options));
        var noticeOnly = await NoticeCollector.PrepareAsync(area);
        Check(noticeOnly.Events.Any(e => e.Category == "maintenance") && noticeOnly.Events.All(e => e.Id != "kr:patch:v7-5"), "Notice and game downtime processing do not depend on the separate patch catalog");
        using (var catalogHttp = new HttpClient(new ResponseHandler(() => HtmlResponse(catalog))))
        {
            var catalogOnly = await NoticeCollector.CollectPatchCatalogAsync(Path.Combine(area, "catalog-only"), noticeOnly, catalogHttp, default);
            Check(catalogOnly.Events.Single().Id == "kr:patch:v7-5" && FeedValidator.Validate(catalogOnly).Count == 0, "Patch catalog can independently provide a validated calendar snapshot");
        }
        var clockListing = Index("3159", oldTitle, "2026.11.06") + Index("3162", article.Title, "18:00");
        await File.WriteAllTextAsync(Path.Combine(area, "notice-list-1.html"), clockListing, Collector.Utf8);
        var clockSources = sources.Select(s => s.Item1.Kind == "index" ? Page("index", "/news/notice?page=1", "notice-list-1.html", clockListing) : s.Item1).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(area, "notices.collection.json"), JsonSerializer.SerializeToUtf8Bytes(new NoticeManifest { CollectedAt = observed.AddSeconds(1), Pages = clockSources }, CalendarJson.Options));
        var clockFeed = await NoticeCollector.PrepareAsync(area);
        Check(clockFeed.Events.Single(e => e.Id == "kr:notice:3162").Phases[0].Start.Value!.StartsWith("2026-11-10T18:00", StringComparison.Ordinal), "A clock-only index uses the article's full KST date, independent of the collector's current date");
        var mismatchedClock = clockListing.Replace("18:00", "17:00", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(area, "notice-list-1.html"), mismatchedClock, Collector.Utf8);
        clockSources[0] = Page("index", "/news/notice?page=1", "notice-list-1.html", mismatchedClock);
        await File.WriteAllBytesAsync(Path.Combine(area, "notices.collection.json"), JsonSerializer.SerializeToUtf8Bytes(new NoticeManifest { CollectedAt = observed.AddSeconds(1), Pages = clockSources }, CalendarJson.Options));
        await Reject(async () => { await NoticeCollector.PrepareAsync(area); }, "Clock-only list and full article timestamp must still agree");
        await File.AppendAllTextAsync(Path.Combine(area, article.Source.File), "changed");
        await Reject(async () => { await NoticeCollector.PrepareAsync(area); }, "Notice source tampering is rejected before publication");
        Console.WriteLine($"PASS: {checks} Notice source checks; deterministic fixtures and fake HTTP only; artifacts={area}");
        return checks;
    }

    private static HttpResponseMessage HtmlResponse(string text)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/html") };
        return response;
    }
    private sealed class ResponseHandler(Func<HttpResponseMessage> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(factory());
    }
}
