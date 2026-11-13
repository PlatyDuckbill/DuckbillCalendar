using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal sealed record NoticeIndexItem(string Id, string Title, DateOnly? PublishedDate, TimeOnly? PublishedTime = null);
internal sealed record NoticePage(string Kind, string Url, string File, int Bytes, string Sha256, DateTimeOffset ObservedAt);
internal sealed record NoticeManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string SourceId { get; init; } = "kr-notices";
    public DateTimeOffset CollectedAt { get; init; }
    public int LookbackDays { get; init; } = 180;
    public int MaximumPages { get; init; } = NoticeCollector.MaximumPages;
    public bool CompleteCoverage { get; init; }
    public bool IncludesPatchCatalog { get; init; } = true;
    public IReadOnlyList<NoticePage> Pages { get; init; } = [];
}

internal static class NoticeCollector
{
    public const int MaximumPages = 6;
    public const int MaximumArticles = 60;
    private const int MaximumCollectionBytes = 32 * 1024 * 1024;
    public const string NoticeRoot = "https://www.ff14.co.kr/news/notice";
    public const string PatchRoot = "https://www.ff14.co.kr/news/patchnote";

    // Each URL is constructed here or validated against the exact official routes.
    public static async Task<byte[]> FetchAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        if (!IsAllowed(url)) throw new InvalidDataException("UnsupportedNoticeUrl");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.AcceptEncoding.ParseAdd("identity");
        request.Headers.UserAgent.ParseAdd("DuckbillCalendarData/0.1 (+https://github.com/PlatyDuckbill/DuckbillCalendar)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("NoticeHttpStatus:" + (int)response.StatusCode);
        if (response.RequestMessage?.RequestUri is { } final && final != request.RequestUri) throw new InvalidDataException("NoticeRedirectNotAllowed");
        if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml") || response.Content.Headers.ContentEncoding.Count != 0)
            throw new InvalidDataException("NoticeContentTypeOrEncoding");
        var charset = response.Content.Headers.ContentType.CharSet?.Trim('"');
        if (charset is not null && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("NoticeCharset");
        if (response.Content.Headers.ContentLength > Collector.MaximumBytes) throw new InvalidDataException("NoticeBodySizeLimit");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token)) != 0)
        {
            if (output.Length + count > Collector.MaximumBytes) throw new InvalidDataException("NoticeBodySizeLimit");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        _ = Collector.Utf8.GetString(bytes);
        return bytes;
    }

    public static async Task<int> CollectAsync(string directory, HttpClient http, CancellationToken cancellationToken, bool includePatchCatalog = true)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("NoticeCollectionAlreadyExists");
        var cutoff = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date).AddDays(-180);
        var pages = new List<NoticePage>();
        var notices = new Dictionary<string, NoticeIndexItem>(StringComparer.Ordinal);
        int totalBytes = 0;
        async Task<byte[]> Save(string kind, string url, string file)
        {
            var bytes = await FetchAsync(http, url, cancellationToken);
            totalBytes = checked(totalBytes + bytes.Length);
            if (totalBytes > MaximumCollectionBytes) throw new InvalidDataException("NoticeCollectionSizeLimit");
            await Collector.WriteNewAsync(Path.Combine(directory, file), bytes);
            pages.Add(new(kind, url, file, bytes.Length, Collector.Hash(bytes), DateTimeOffset.UtcNow));
            return bytes;
        }
        for (int page = 1; page <= MaximumPages; page++)
        {
            var bytes = await Save("index", NoticeRoot + "?page=" + page, $"notice-list-{page}.html");
            var parsed = await NoticeParser.ParseIndexAsync(Collector.Utf8.GetString(bytes));
            foreach (var item in parsed.Where(n => n.PublishedDate is null || n.PublishedDate >= cutoff))
            {
                if (notices.TryGetValue(item.Id, out var previous) && previous != item) throw new InvalidDataException("ConflictingNoticeIndex:" + item.Id);
                notices[item.Id] = item;
            }
            if (notices.Count > MaximumArticles) throw new InvalidDataException("NoticeArticleLimit");
            if (parsed.All(n => n.PublishedDate is not null && n.PublishedDate < cutoff)) break;
        }
        foreach (var item in notices.Values)
            await Save("article", NoticeRoot + "/view/" + item.Id, "notice-" + item.Id + ".html");
        if (includePatchCatalog) await Save("patch-catalog", PatchRoot, "patch-catalog.html");
        var manifest = new NoticeManifest { CollectedAt = DateTimeOffset.UtcNow, Pages = pages, IncludesPatchCatalog = includePatchCatalog };
        await Collector.WriteNewAsync(Path.Combine(directory, "notices.collection.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, CalendarJson.Options));
        Console.WriteLine($"Collected notices: {notices.Count} articles; bounded {MaximumPages} list pages / 180 days; completeCoverage=false");
        return notices.Count;
    }

    public static async Task<FeedDocument> PrepareAsync(string directory, FeedDocument? previous = null)
    {
        directory = Path.GetFullPath(directory);
        var manifest = CalendarJson.Parse<NoticeManifest>(await LocalInput.ReadAsync(Path.Combine(directory, "notices.collection.json"), 65536));
        if (manifest.SchemaVersion != 1 || manifest.SourceId != "kr-notices" || manifest.CompleteCoverage || manifest.LookbackDays != 180 || manifest.MaximumPages != MaximumPages ||
            manifest.CollectedAt == default || manifest.CollectedAt > DateTimeOffset.UtcNow.AddMinutes(5) || manifest.Pages is null || manifest.Pages.Count < (manifest.IncludesPatchCatalog ? 3 : 2) || manifest.Pages.Count > MaximumPages + MaximumArticles + 1 ||
            manifest.Pages.Any(p => p is null) || manifest.Pages.Select(p => p.File).Distinct(StringComparer.Ordinal).Count() != manifest.Pages.Count)
            throw new InvalidDataException("NoticeManifestInvalid");
        if (previous is not null && FeedValidator.Validate(previous).Count != 0) throw new InvalidDataException("InvalidNoticePreviousFeed");
        var events = (previous?.Events ?? []).ToDictionary(e => e.Id, StringComparer.Ordinal);
        var evidence = (previous?.Evidence ?? []).ToDictionary(e => e.Id, StringComparer.Ordinal);
        var documents = new List<(NoticePage Page, string Html)>();
        int bytesTotal = 0;
        foreach (var page in manifest.Pages)
        {
            if (!IsAllowed(page.Url) || page.ObservedAt == default || page.ObservedAt > manifest.CollectedAt || !Regex.IsMatch(page.File, @"^(notice-list-\d+|notice-\d+|patch-catalog)\.html$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("NoticePageMetadataInvalid");
            var bytes = await LocalInput.ReadAsync(Path.Combine(directory, page.File));
            bytesTotal = checked(bytesTotal + bytes.Length);
            if (bytesTotal > MaximumCollectionBytes || bytes.Length != page.Bytes || Collector.Hash(bytes) != page.Sha256) throw new InvalidDataException("NoticePageHashMismatch");
            documents.Add((page, Collector.Utf8.GetString(bytes)));
        }
        var listed = new Dictionary<string, NoticeIndexItem>(StringComparer.Ordinal);
        foreach (var source in documents.Where(d => d.Page.Kind == "index"))
            foreach (var item in await NoticeParser.ParseIndexAsync(source.Html))
            {
                if (listed.TryGetValue(item.Id, out var old) && old != item) throw new InvalidDataException("ConflictingNoticeIndex:" + item.Id);
                listed[item.Id] = item;
            }
        var patchCatalogs = documents.Where(d => d.Page.Kind == "patch-catalog").ToArray();
        if (patchCatalogs.Length != (manifest.IncludesPatchCatalog ? 1 : 0)) throw new InvalidDataException("MissingNoticePatchCatalog");
        foreach (var catalog in patchCatalogs)
        {
            foreach (var item in await NoticeParser.ParsePatchCatalogAsync(catalog.Html, catalog.Page))
            {
                if (!events.ContainsKey(item.Event.Id)) events[item.Event.Id] = item.Event;
                evidence[item.Evidence.Id] = item.Evidence;
            }
        }
        var parsedArticles = new List<NoticeArticle>();
        foreach (var source in documents.Where(d => d.Page.Kind == "article"))
        {
            var article = await NoticeParser.ParseArticleAsync(source.Html, source.Page);
            if (!listed.TryGetValue(article.Id, out var index) || index.Title != article.Title ||
                index.PublishedDate is { } date && date != DateOnly.FromDateTime(article.PublishedAt.Date) ||
                index.PublishedTime is { } time && time != TimeOnly.FromDateTime(article.PublishedAt.DateTime))
                throw new InvalidDataException("NoticeIndexArticleMismatch:" + article.Id);
            parsedArticles.Add(article);
        }
        if (parsedArticles.Count == 0 || parsedArticles.Select(a => a.Id).Distinct().Count() != parsedArticles.Count) throw new InvalidDataException("EmptyOrDuplicateNoticeArticles");
        // Changed/completed notices supersede an earlier scheduled end for the same game patch.
        foreach (var article in parsedArticles.OrderBy(a => a.PublishedAt).ThenBy(a => int.Parse(a.Id, CultureInfo.InvariantCulture)))
            foreach (var item in NoticeParser.EventsFor(article))
            {
                events[item.Event.Id] = item.Event;
                evidence[item.Evidence.Id] = item.Evidence;
            }
        if (events.Count > 1000) throw new InvalidDataException("MergedNoticeEventLimit");
        var referenced = events.Values.SelectMany(e => e.Phases).SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds))
            .Concat((previous?.Targets ?? []).Where(t => t.ResolvedBoundary is not null).SelectMany(t => t.ResolvedBoundary!.EvidenceIds)).ToHashSet(StringComparer.Ordinal);
        var feed = new FeedDocument
        {
            ContentRevision = previous?.ContentRevision ?? 1, GeneratedAt = manifest.CollectedAt,
            Events = events.Values.ToArray(), Evidence = evidence.Values.Where(e => referenced.Contains(e.Id)).ToArray(),
            Targets = previous?.Targets ?? [], FrontlineRules = previous?.FrontlineRules ?? [], Withdrawals = previous?.Withdrawals ?? []
        };
        var errors = FeedValidator.Validate(feed);
        if (errors.Count != 0) throw new InvalidDataException("NoticeFeedRejected:" + string.Join(",", errors.Take(8)));
        if (JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options).Length > Collector.MaximumBytes) throw new InvalidDataException("NoticeFeedSizeLimit");
        return feed;
    }

    public static async Task<FeedDocument> CollectPatchCatalogAsync(string directory, FeedDocument previous, HttpClient http, CancellationToken token)
    {
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("PatchCollectionAlreadyExists");
        Directory.CreateDirectory(directory);
        var bytes = await FetchAsync(http, PatchRoot, token);
        var source = new NoticePage("patch-catalog", PatchRoot, "patch-catalog.html", bytes.Length, Collector.Hash(bytes), DateTimeOffset.UtcNow);
        await Collector.WriteNewAsync(Path.Combine(directory, source.File), bytes);
        var parsed = await NoticeParser.ParsePatchCatalogAsync(Collector.Utf8.GetString(bytes), source);
        var previousProofs = previous.Evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var items = parsed.Where(item =>
        {
            var old = previous.Events.FirstOrDefault(e => e.Id == item.Event.Id);
            return old is null || old.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds))
                .All(id => previousProofs.TryGetValue(id, out var proof) && proof.SourceId == "kr-patch-catalog");
        }).ToArray();
        await Collector.WriteNewAsync(Path.Combine(directory, "source.json"), JsonSerializer.SerializeToUtf8Bytes(source, CalendarJson.Options));
        return new() { ContentRevision = 1, GeneratedAt = source.ObservedAt, Events = items.Select(i => i.Event).ToArray(), Evidence = items.Select(i => i.Evidence).ToArray() };
    }

    private static bool IsAllowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "www.ff14.co.kr" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        ((uri.AbsolutePath == "/news/notice" && (uri.Query.Length == 0 || Regex.IsMatch(uri.Query, @"^\?page=[1-6]$", RegexOptions.CultureInvariant))) ||
        (Regex.IsMatch(uri.AbsolutePath, @"^/news/notice/view/\d+$", RegexOptions.CultureInvariant) && uri.Query.Length == 0) || (uri.AbsolutePath == "/news/patchnote" && uri.Query.Length == 0));
}

internal sealed record NoticeArticle(string Id, string Title, DateTimeOffset PublishedAt, string Body, NoticePage Source);
internal sealed record NoticeEvent(CalendarEvent Event, EvidenceRecord Evidence);

internal static class NoticeParser
{
    public static string Text(string value) => Regex.Replace(value, @"\s+", " ").Trim();
    private static bool Synthetic(IDocument document) => document.DocumentElement.HasAttribute("data-synthetic");
    public static async Task<IReadOnlyList<NoticeIndexItem>> ParseIndexAsync(string html)
    {
        var document = await new HtmlParser().ParseDocumentAsync(html);
        if (Synthetic(document)) throw new InvalidDataException("SyntheticNoticePage");
        var result = new List<NoticeIndexItem>();
        foreach (var row in document.QuerySelectorAll("ul.list_type1 > li"))
        {
            var anchor = row.QuerySelector(".subj a[href*='/news/notice/view/']");
            if (anchor is null) continue;
            var match = Regex.Match(anchor.GetAttribute("href") ?? "", @"^/news/notice/view/(\d+)(?:\?[^#]*)?$", RegexOptions.CultureInvariant);
            var title = Text(anchor.QuerySelector(".txt")?.TextContent ?? anchor.TextContent);
            var rawDate = row.QuerySelector("time[datetime]")?.GetAttribute("datetime");
            var hasDate = DateOnly.TryParseExact(rawDate, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
            var hasTime = TimeOnly.TryParseExact(rawDate, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
            if (!match.Success || title.Length is < 1 or > 200 || !hasDate && !hasTime)
                throw new InvalidDataException("UnexpectedNoticeIndexShape");
            // Today's list rows carry only a clock. The article is the date authority;
            // neither the runner's timezone nor its current date may fill this field.
            result.Add(new(match.Groups[1].Value, title, hasDate ? date : null, hasTime ? time : null));
        }
        if (result.Count is < 1 or > 20) throw new InvalidDataException("EmptyOrOversizedNoticeIndex");
        return result;
    }

    public static async Task<NoticeArticle> ParseArticleAsync(string html, NoticePage page)
    {
        var document = await new HtmlParser().ParseDocumentAsync(html);
        if (Synthetic(document)) throw new InvalidDataException("SyntheticNoticePage");
        var box = document.QuerySelector(".view_box");
        var title = Text(box?.QuerySelector(".view_summary h4")?.TextContent ?? "");
        var rawTime = box?.QuerySelector(".view_summary time[datetime]")?.GetAttribute("datetime");
        var body = Text(box?.QuerySelector(".view_contents")?.TextContent ?? "");
        var id = Regex.Match(new Uri(page.Url).AbsolutePath, @"/view/(\d+)$", RegexOptions.CultureInvariant).Groups[1].Value;
        if (title.Length is < 1 or > 200 || body.Length is < 1 or > 300000 || id.Length == 0 ||
            !DateTime.TryParseExact(rawTime, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var published)) throw new InvalidDataException("UnexpectedNoticeArticleShape");
        return new(id, title, new(DateTime.SpecifyKind(published, DateTimeKind.Unspecified), TimeSpan.FromHours(9)), body, page);
    }

    public static IReadOnlyList<NoticeEvent> EventsFor(NoticeArticle article)
    {
        var evidenceId = "kr:notice:" + article.Id + ":" + article.Source.Sha256[..12];
        var proof = new EvidenceRecord { Id = evidenceId, SourceId = "kr-notices", Url = article.Source.Url, ObservedAt = article.Source.ObservedAt, SourceSnapshotSha256 = article.Source.Sha256,
            Note = "공식 공지 본문. 게시일과 본문 일정을 분리해 표시합니다. 기계 규칙 검증이며 사람 검수나 전체 수집을 뜻하지 않습니다." };
        var result = new List<NoticeEvent>();
        CalendarBoundary Boundary(DateTimeOffset value) => CalendarBoundary.Instant(value) with { EvidenceIds = [evidenceId] };
        CalendarBoundary Day(DateOnly value) => CalendarBoundary.Date(value) with { EvidenceIds = [evidenceId] };
        NoticeEvent Point(string id, string title, string category, CalendarBoundary when, string label, string? note = null) => new(new()
        { Id = id, Title = title, Category = category, Phases = [new() { Id = "point", Label = label, Point = true, Start = when, End = new() { Reason = "not-announced" } }], Note = note }, proof);
        result.Add(Point("kr:notice:" + article.Id, article.Title, "notice", Boundary(article.PublishedAt), "공지 게시", "공지 게시 시각입니다. 점검·패치·시즌 시작 시각으로 간주하지 않습니다."));
        // Only the game's explicitly dated maintenance section can establish patch/season day.
        var game = Regex.Match(article.Body, @"(?:게임 및 런처|게임/런처|게임 서버) 점검(?: 완료)?\s*-?\s*일시\s*:\s*(?<y>20\d{2})년\s*(?<m>\d{1,2})월\s*(?<d>\d{1,2})일\s*(?:\([^)]{1,8}\))?\s*(?<a>\d{1,2}:\d{2})\s*~\s*(?<b>\d{1,2}:\d{2})", RegexOptions.CultureInvariant);
        if (!game.Success) return result;
        var dayText = FormattableString.Invariant($"{int.Parse(game.Groups["y"].Value, CultureInfo.InvariantCulture):D4}-{int.Parse(game.Groups["m"].Value, CultureInfo.InvariantCulture):D2}-{int.Parse(game.Groups["d"].Value, CultureInfo.InvariantCulture):D2}");
        if (!DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            !TimeOnly.TryParseExact(game.Groups["a"].Value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
            !TimeOnly.TryParseExact(game.Groups["b"].Value, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime) || endTime <= startTime) return result;
        var start = new DateTimeOffset(date.ToDateTime(startTime), TimeSpan.FromHours(9));
        var end = new DateTimeOffset(date.ToDateTime(endTime), TimeSpan.FromHours(9));
        var completed = article.Title.Contains("완료", StringComparison.Ordinal) || article.Body.Contains("점검이 완료되어", StringComparison.Ordinal);
        var version = Regex.Match(article.Title, @"V(?<v>\d+\.\d{1,2})\s*업데이트", RegexOptions.CultureInvariant);
        var suffix = version.Success ? "v" + version.Groups["v"].Value.Replace('.', '-') : date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        result.Add(new(new() { Id = "kr:maintenance:game:" + suffix + ":" + dayText, Title = version.Success ? "V" + version.Groups["v"].Value + " 게임·런처 점검" : "게임·런처 점검", Category = "maintenance",
            Phases = [new() { Id = "maintenance", Label = "게임·런처 점검", Start = Boundary(start) with { Certainty = completed ? "confirmed" : "tentative" }, End = Boundary(end) with { Certainty = completed ? "confirmed" : "tentative" } }], Note = "같은 업데이트·날짜의 후속 공지에 명시된 시간이 우선합니다." }, proof));
        var patchDay = Day(date) with { Certainty = completed ? "confirmed" : "tentative" };
        if (version.Success) result.Add(Point("kr:patch:" + suffix, "V" + version.Groups["v"].Value + " 업데이트", "patch", patchDay, completed ? "업데이트 적용일" : "업데이트 예정일", "공식 점검 공지에 연결한 날짜입니다. 시작 분 단위 시각을 별도로 추정하지 않으며 예정일은 점검 완료 후 확인합니다."));
        var cc = Regex.Match(article.Body, @"크리스탈라인 컨플릭트 시즌 관련 안내.{0,200}?시즌\s*\d+\s*(?:이|가)\s*종료되고\s*시즌\s*(?<season>\d+)\s*(?:이|가)\s*시작", RegexOptions.CultureInvariant);
        if (cc.Success) result.Add(Point("kr:pvp:cc-season-" + cc.Groups["season"].Value, "크리스탈라인 컨플릭트 시즌 " + cc.Groups["season"].Value + " 시작", "pvp-season", patchDay, "크컨 시즌 시작", "공식 점검 일정에 연결한 시작일. 완료 전에는 예정으로 표시하며 종료일은 이 공지에 발표되지 않았습니다."));
        return result;
    }

    public static async Task<IReadOnlyList<NoticeEvent>> ParsePatchCatalogAsync(string html, NoticePage page)
    {
        var document = await new HtmlParser().ParseDocumentAsync(html);
        if (Synthetic(document)) throw new InvalidDataException("SyntheticPatchCatalog");
        var rows = document.QuerySelectorAll("ul.list_patch > li");
        if (rows.Length == 0) throw new InvalidDataException("MissingPatchCatalogRows");
        var result = new List<NoticeEvent>();
        var cutoff = DateOnly.FromDateTime(page.ObservedAt.Date).AddYears(-2);
        foreach (var row in rows)
        {
            var rawDate = Text(row.QuerySelector(".head .date")?.TextContent ?? "");
            if (!DateOnly.TryParseExact(rawDate, "yyyy. MM. dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new InvalidDataException("UnexpectedPatchCatalogDate");
            if (date < cutoff) continue;
            var version = Regex.Match(Text(row.QuerySelector(".patch_links a")?.TextContent ?? ""), @"^(?<v>\d+\.\d)\s+패치노트", RegexOptions.CultureInvariant);
            if (!version.Success) throw new InvalidDataException("UnexpectedPatchCatalogShape");
            var evidenceId = "kr:patch-catalog:v" + version.Groups["v"].Value.Replace('.', '-') + ":" + page.Sha256[..12];
            var id = "kr:patch:v" + version.Groups["v"].Value.Replace('.', '-');
            result.Add(new(new() { Id = id, Title = "V" + version.Groups["v"].Value + " " + Text(row.QuerySelector(".head .title")?.TextContent ?? "업데이트"), Category = "patch",
                Phases = [new() { Id = "release", Label = "주요 패치 적용일", Point = true, Start = CalendarBoundary.Date(date) with { EvidenceIds = [evidenceId] }, End = new() { Reason = "not-announced" } }] },
                new() { Id = evidenceId, SourceId = "kr-patch-catalog", Url = page.Url, ObservedAt = page.ObservedAt, SourceSnapshotSha256 = page.Sha256, Note = "공식 패치 목록의 대형 패치 카드에 직접 명시된 날짜. 부패치 날짜로 재사용하지 않습니다." }));
        }
        return result;
    }
}
