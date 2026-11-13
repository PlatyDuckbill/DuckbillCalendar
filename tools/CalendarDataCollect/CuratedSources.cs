using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal sealed record CuratedSourceDefinition
{
    public string Id { get; init; } = "";
    public string Url { get; init; } = "";
    public string EvidenceUrl { get; init; } = "";
    public string Format { get; init; } = "html";
}
internal sealed record CuratedGuard
{
    public string SourceId { get; init; } = "";
    public string Selector { get; init; } = "body";
    public IReadOnlyList<string> RequiredText { get; init; } = [];
    public IReadOnlyList<string> RequiredScriptUrls { get; init; } = [];
}
internal sealed record CuratedEventRule
{
    public CalendarEvent Event { get; init; } = new();
    public IReadOnlyList<CuratedGuard> Guards { get; init; } = [];
}
internal sealed record CuratedRules
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<CuratedSourceDefinition> Sources { get; init; } = [];
    public IReadOnlyList<CuratedEventRule> Rules { get; init; } = [];
    public IReadOnlyList<TargetRecord> Targets { get; init; } = [];
}
internal sealed record CuratedSourceResult(string Id, string Url, string EvidenceUrl, DateTimeOffset FetchedAt,
    string Status, string? SnapshotSha256, int Bytes, string? Error);
internal sealed record CuratedCollectionResult(FeedDocument Feed, IReadOnlyList<string> PendingEventIds,
    IReadOnlyList<CuratedSourceResult> SourceResults);

/// <summary>
/// Reviewed semantic mappings for facts not represented by event-card time attributes.
/// Each mapped date remains valid only while its scoped official statements still match.
/// This verifies configured facts; it never guesses new dates or executes page scripts.
/// </summary>
internal static class CuratedSources
{
    private const string PendingMarker = "[공식 원문 재검토 대기: 마지막 확인 내용을 보존합니다.]";
    public static async Task<CuratedCollectionResult> CollectAsync(string rulesPath, string outputDirectory,
        FeedDocument? previousFeed, HttpClient http, CancellationToken cancellationToken)
    {
        var rules = CalendarJson.Parse<CuratedRules>(await LocalInput.ReadAsync(rulesPath));
        ValidateRules(rules);
        if (previousFeed is not null && FeedValidator.Validate(previousFeed).Count != 0)
            throw new InvalidDataException("InvalidPreviousCuratedFeed");
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("CuratedOutputAlreadyExists");
        Directory.CreateDirectory(outputDirectory);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        var statuses = new List<CuratedSourceResult>();
        foreach (var source in rules.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var bytes = await FetchAsync(source, http, timeout.Token);
                var observed = DateTimeOffset.UtcNow;
                await Collector.WriteNewAsync(Path.Combine(outputDirectory, source.Id + (source.Format == "script-template" ? ".js" : ".html")), bytes);
                contents[source.Id] = Collector.Utf8.GetString(bytes);
                statuses.Add(new(source.Id, source.Url, source.EvidenceUrl, observed, "fetched", Collector.Hash(bytes), bytes.Length, null));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or DecoderFallbackException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                statuses.Add(new(source.Id, source.Url, source.EvidenceUrl, DateTimeOffset.UtcNow, "unavailable", null, 0, ex is InvalidDataException ? ex.Message : ex.GetType().Name));
            }
        }
        var events = new List<CalendarEvent>();
        var evidence = new Dictionary<string, EvidenceRecord>(StringComparer.Ordinal);
        var pending = new List<string>();
        foreach (var rule in rules.Rules)
        {
            bool matches = true;
            foreach (var guard in rule.Guards)
            {
                var source = rules.Sources.Single(s => s.Id == guard.SourceId);
                if (!contents.TryGetValue(source.Id, out var html) || !await GuardMatchesAsync(source, guard, html)) { matches = false; break; }
            }
            if (!matches)
            {
                pending.Add(rule.Event.Id);
                var old = previousFeed?.Events.FirstOrDefault(e => e.Id == rule.Event.Id);
                if (old is not null)
                {
                    events.Add(old with { Note = (old.Note ?? "").Contains(PendingMarker, StringComparison.Ordinal) ? old.Note : ((old.Note ?? "") + "\n" + PendingMarker).Trim() });
                    var ids = old.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds)).ToHashSet(StringComparer.Ordinal);
                    foreach (var item in previousFeed!.Evidence.Where(e => ids.Contains(e.Id))) evidence[item.Id] = item;
                }
                continue;
            }
            var boundEvidence = new List<string>();
            foreach (var sourceId in rule.Guards.Select(g => g.SourceId).Distinct(StringComparer.Ordinal))
            {
                var source = rules.Sources.Single(s => s.Id == sourceId);
                var status = statuses.Single(s => s.Id == sourceId);
                var id = "curated:" + sourceId + ":" + status.SnapshotSha256![..12];
                boundEvidence.Add(id);
                evidence[id] = new()
                {
                    Id = id, SourceId = "kr-curated:" + sourceId, Url = source.EvidenceUrl, ObservedAt = status.FetchedAt,
                    SourceSnapshotSha256 = status.SnapshotSha256,
                    Note = "공식 원문과 대조한 날짜 매핑을 필수 구절로 재확인. 원문/구성요소 직접 수집 URL: " + source.Url +
                        (source.Format == "script-template" ? " (공식 동적 페이지의 정적 HTML 템플릿, 실행하지 않음; SHA256은 이 구성요소 바이트 대상)." : " (SHA256은 이 응답 바이트 대상).")
                };
            }
            CalendarBoundary Bind(CalendarBoundary boundary) => boundary.Kind == "unknown" ? boundary : boundary with { EvidenceIds = boundEvidence.ToArray() };
            events.Add(rule.Event with { Phases = rule.Event.Phases.Select(p => p with { Start = Bind(p.Start), End = Bind(p.End) }).ToArray() });
        }
        var targets = (previousFeed?.Targets ?? []).Concat(rules.Targets).GroupBy(t => t.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();
        var targetEvidence = targets.Where(t => t.ResolvedBoundary is not null).SelectMany(t => t.ResolvedBoundary!.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        foreach (var proof in (previousFeed?.Evidence ?? []).Where(e => targetEvidence.Contains(e.Id))) evidence.TryAdd(proof.Id, proof);
        var feed = new FeedDocument { ContentRevision = 1, GeneratedAt = DateTimeOffset.UtcNow, Events = events, Evidence = evidence.Values.ToArray(), Targets = targets };
        var errors = FeedValidator.Validate(feed);
        if (errors.Count != 0) throw new InvalidDataException("CuratedFeedInvalid:" + string.Join(",", errors.Take(8)));
        var result = new CuratedCollectionResult(feed, pending, statuses);
        await Collector.WriteNewAsync(Path.Combine(outputDirectory, "curated-feed.json"), JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        await Collector.WriteNewAsync(Path.Combine(outputDirectory, "curated-status.json"), JsonSerializer.SerializeToUtf8Bytes(new { pendingEventIds = pending, sourceResults = statuses }, CalendarJson.Options));
        return result;
    }

    internal static async Task<bool> GuardMatchesAsync(CuratedSourceDefinition source, CuratedGuard guard, string content)
    {
        string text;
        if (source.Format == "script-template")
            text = WebUtility.HtmlDecode(Regex.Replace(content, "<[^>]+>", " ")).Replace("\\n", " ", StringComparison.Ordinal).Replace("\\t", " ", StringComparison.Ordinal);
        else
        {
            var document = await new HtmlParser().ParseDocumentAsync(content);
            var scripts = document.QuerySelectorAll("script[src]")
                .Select(n => Uri.TryCreate(new Uri(source.Url), n.GetAttribute("src"), out var url) ? url.AbsoluteUri : "")
                .ToHashSet(StringComparer.Ordinal);
            if (guard.RequiredScriptUrls.Any(url => !scripts.Contains(url))) return false;
            foreach (var node in document.QuerySelectorAll("script, style, template")) node.Remove();
            var nodes = document.QuerySelectorAll(guard.Selector);
            if (nodes.Length == 0) return false;
            text = string.Join(" ", nodes.Select(n => n.TextContent));
        }
        var normalized = Normalize(text);
        return guard.RequiredText.All(phrase => normalized.Contains(Normalize(phrase), StringComparison.Ordinal));
    }
    private static string Normalize(string value) => Regex.Replace(value, @"\s+", "");
    private static void ValidateRules(CuratedRules rules)
    {
        if (rules.SchemaVersion != 1 || rules.Sources.Count is < 1 or > 32 || rules.Rules.Count is < 1 or > 100 || rules.Targets.Count > 100 ||
            rules.Sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != rules.Sources.Count ||
            rules.Rules.Select(s => s.Event.Id).Distinct(StringComparer.Ordinal).Count() != rules.Rules.Count)
            throw new InvalidDataException("InvalidCuratedRules");
        foreach (var source in rules.Sources)
            if (!Regex.IsMatch(source.Id, "^[a-z0-9-]{1,48}$", RegexOptions.CultureInvariant) || source.Format is not ("html" or "script-template") ||
                !AllowedUri(source.Url, true) || !AllowedUri(source.EvidenceUrl, false)) throw new InvalidDataException("InvalidCuratedSource");
        foreach (var rule in rules.Rules)
            if (!rule.Event.Id.StartsWith("kr:", StringComparison.Ordinal) || rule.Guards.Count is < 1 or > 8 ||
                rule.Guards.Any(g => !rules.Sources.Any(s => s.Id == g.SourceId) || g.Selector.Length is < 1 or > 200 || g.RequiredText.Count is < 1 or > 12 || g.RequiredText.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 500) || g.RequiredScriptUrls.Count > 8 || g.RequiredScriptUrls.Any(url => !AllowedUri(url, true))))
                throw new InvalidDataException("InvalidCuratedGuard");
    }
    private static bool AllowedUri(string value, bool component) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && (uri.Host == "www.ff14.co.kr" || (component && uri.Host == "image.ff14.co.kr"));
    private static async Task<byte[]> FetchAsync(CuratedSourceDefinition source, HttpClient http, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        request.Headers.UserAgent.ParseAdd("DuckbillCalendarData/0.1 (+https://github.com/PlatyDuckbill/DuckbillCalendar)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
            response.Content.Headers.ContentLength > Collector.MaximumBytes ||
            (response.RequestMessage?.RequestUri is { } actual && actual != request.RequestUri)) throw new InvalidDataException("UnexpectedCuratedResponse");
        var media = response.Content.Headers.ContentType?.MediaType;
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (charset is not null && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UnsupportedCuratedCharset");
        if (source.Format == "html" ? media is not ("text/html" or "application/xhtml+xml") : media is not ("text/javascript" or "application/javascript" or "application/x-javascript" or "text/plain"))
            throw new InvalidDataException("UnexpectedCuratedContentType");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + read > Collector.MaximumBytes) throw new InvalidDataException("CuratedSourceSizeLimit");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray(); _ = Collector.Utf8.GetString(bytes); return bytes;
    }
}
