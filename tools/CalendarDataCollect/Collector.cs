using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal sealed record CollectionManifest
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public string Source { get; init; } = EventPageParser.Source;
    [JsonRequired] public DateTimeOffset FetchedAt { get; init; }
    [JsonRequired] public int Status { get; init; }
    [JsonRequired] public int Bytes { get; init; }
    [JsonRequired] public string SnapshotSha256 { get; init; } = "";
    [JsonRequired] public bool CompleteCoverage { get; init; }
    public int Page { get; init; } = 1;
}

internal sealed record ReviewedCandidate
{
    [JsonRequired] public string Id { get; init; } = "";
    [JsonRequired] public string NormalizedRecordHash { get; init; } = "";
    [JsonRequired] public string Category { get; init; } = "web-event";
}

internal sealed record ReviewDocument
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public string SnapshotSha256 { get; init; } = "";
    [JsonRequired] public DateTimeOffset ReviewedAt { get; init; }
    [JsonRequired] public string ReviewedBy { get; init; } = "";
    [JsonRequired] public string Scope { get; init; } = "event-list-first-page";
    [JsonRequired] public IReadOnlyList<ReviewedCandidate> Approved { get; init; } = [];
}

internal static class Collector
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = 32
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<byte[]> FetchAsync(HttpClient http, CancellationToken cancellationToken, int page = 1)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, EventPageParser.PageAddress(page));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.AcceptEncoding.ParseAdd("identity");
        request.Headers.UserAgent.ParseAdd("DuckbillCalendarData/0.1 (+https://github.com/PlatyDuckbill/DuckbillCalendar)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("UnexpectedHttpStatus:" + (int)response.StatusCode);
        if (response.RequestMessage?.RequestUri is { } final && final != request.RequestUri) throw new InvalidDataException("EventRedirectNotAllowed");
        if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml")) throw new InvalidDataException("UnexpectedContentType");
        if (response.Content.Headers.ContentEncoding.Count != 0) throw new InvalidDataException("UnexpectedContentEncoding");
        var charset = response.Content.Headers.ContentType.CharSet?.Trim('"');
        if (charset is not null && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UnsupportedCharset");
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("HtmlSizeLimit");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > MaximumBytes) throw new InvalidDataException("HtmlSizeLimit");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        _ = Utf8.GetString(bytes); // Reject incorrectly decoded pages rather than changing titles/dates.
        return bytes;
    }

    public static async Task<int> CollectAsync(string directory, HttpClient http, CancellationToken cancellationToken, int page = 1)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("CollectionDirectoryAlreadyExists");
        var bytes = await FetchAsync(http, cancellationToken, page);
        var fetchedAt = DateTimeOffset.UtcNow;
        var candidates = await EventPageParser.ParseAsync(Utf8.GetString(bytes), fetchedAt);
        if (candidates.IsSynthetic) throw new InvalidDataException("SyntheticLiveResponse");
        var manifest = new CollectionManifest { Source = EventPageParser.PageAddress(page), Page = page, FetchedAt = fetchedAt, Status = 200, Bytes = bytes.Length, SnapshotSha256 = Hash(bytes), CompleteCoverage = false };
        Directory.CreateDirectory(directory);
        await WriteNewAsync(Path.Combine(directory, "source.html"), bytes);
        await WriteNewAsync(Path.Combine(directory, "candidates.json"), JsonSerializer.SerializeToUtf8Bytes(candidates, CalendarJson.Options));
        // Manifest is written last so an incomplete directory is never treated as a collection.
        await WriteNewAsync(Path.Combine(directory, "collection.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, CalendarJson.Options));
        Console.WriteLine($"Collected: candidates={candidates.Candidates.Count}; warnings={candidates.Errors.Count}; completeCoverage=false; reviewRequired=true");
        return candidates.Errors.Count == 0 ? 0 : 1;
    }

    public static async Task<FeedDocument> PrepareAsync(string collectionDirectory, string reviewPath)
    {
        var manifest = CalendarJson.Parse<CollectionManifest>(await LocalInput.ReadAsync(Path.Combine(collectionDirectory, "collection.json"), 65536));
        var bytes = await LocalInput.ReadAsync(Path.Combine(collectionDirectory, "source.html"));
        var now = DateTimeOffset.UtcNow;
        if (manifest.SchemaVersion != 1 || manifest.Source != EventPageParser.Source || manifest.Status != 200 || manifest.CompleteCoverage ||
            manifest.FetchedAt == default || manifest.FetchedAt > now.AddMinutes(5) || manifest.Bytes != bytes.Length || manifest.SnapshotSha256 != Hash(bytes))
            throw new InvalidDataException("CollectionProvenanceMismatch");
        // Reparse original bytes; candidates.json is a review aid and cannot substitute forged dates.
        var candidates = await EventPageParser.ParseAsync(Utf8.GetString(bytes), manifest.FetchedAt);
        if (candidates.IsSynthetic || candidates.Errors.Count != 0) throw new InvalidDataException("CandidatesRequireRepair");
        var review = CalendarJson.Parse<ReviewDocument>(await LocalInput.ReadAsync(reviewPath));
        if (review.SchemaVersion != 1 || review.SnapshotSha256 != manifest.SnapshotSha256 || review.Scope != "event-list-first-page" ||
            string.IsNullOrWhiteSpace(review.ReviewedBy) || review.ReviewedBy.Length > 120 || review.ReviewedAt < manifest.FetchedAt || review.ReviewedAt > now.AddMinutes(5) ||
            review.Approved.Count is < 1 or > 1000 || review.Approved.Any(a => a is null) || review.Approved.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != review.Approved.Count)
            throw new InvalidDataException("InvalidReviewManifest");
        var events = new List<CalendarEvent>();
        var evidence = new List<EvidenceRecord>();
        foreach (var approved in review.Approved)
        {
            var candidate = candidates.Candidates.SingleOrDefault(c => c.Id == approved.Id);
            if (candidate is null || approved.NormalizedRecordHash != candidate.NormalizedRecordHash) throw new InvalidDataException("ReviewedCandidateChanged:" + approved.Id);
            if (approved.Category is not ("web-event" or "in-game-event" or "seasonal-event" or "sale" or "discount")) throw new InvalidDataException("InvalidReviewedCategory");
            var evidenceId = candidate.Id + ":list-evidence";
            evidence.Add(new()
            {
                Id = evidenceId, SourceId = "kr-events", Url = EventPageParser.Source, ObservedAt = manifest.FetchedAt, SourceSnapshotSha256 = manifest.SnapshotSha256,
                Note = $"공식 이벤트 목록 첫 페이지의 제목·기간을 검토한 번들 스냅샷. 전체 페이지·본문 수집 아님. 개별 안내: {candidate.SourceUrl}. 확인 {manifest.FetchedAt:O}."
            });
            events.Add(new()
            {
                Id = candidate.Id, Title = candidate.Title, Category = approved.Category,
                Note = "공식 목록의 표시 기간 기준. 상세 참여 조건·대상은 원문을 확인하세요. 기간 원문: " + string.Join(" / ", candidate.Phases.Select(p => p.RawText)),
                Phases = candidate.Phases.Select((p, index) => new EventPhase
                {
                    Id = p.Label.Contains("수령", StringComparison.Ordinal) || p.Label.Contains("보상", StringComparison.Ordinal) ? "reward-" + index : "participation-" + index,
                    Label = string.IsNullOrWhiteSpace(p.Label) ? "이벤트 기간" : p.Label,
                    Start = p.StartsAt is { } start ? CalendarBoundary.Instant(start) with { EvidenceIds = [evidenceId] } : new() { Reason = "not-announced" },
                    End = p.EndsAt is { } end ? CalendarBoundary.Instant(end) with { EvidenceIds = [evidenceId] } : new() { Reason = "not-announced" },
                    EndInclusion = "inclusive-unit"
                }).ToArray()
            });
        }
        var feed = new FeedDocument { ContentRevision = 1, GeneratedAt = review.ReviewedAt, Events = events, Evidence = evidence };
        var errors = FeedValidator.Validate(feed);
        if (errors.Count != 0) throw new InvalidDataException("InvalidPreparedFeed:" + string.Join(",", errors.Take(10)));
        return feed;
    }

    public static async Task WriteNewAsync(string path, byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("OutputSizeLimit");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        stream.Flush(true);
    }
}
