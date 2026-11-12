using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal sealed record SourceRunReport
{
    public string Id { get; init; } = "";
    public DateTimeOffset AttemptedAt { get; init; }
    public DateTimeOffset? SucceededAt { get; init; }
    public string Result { get; init; } = "failed";
    public string? Error { get; init; }
    public IReadOnlyList<string> PendingEventIds { get; init; } = [];
}

internal sealed record AllCollectionReport
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<SourceRunReport> Sources { get; init; } = [];
}

internal static class AllSources
{
    public static FeedDocument Merge(FeedDocument earlier, FeedDocument later)
    {
        var events = earlier.Events.Concat(later.Events).GroupBy(e => e.Id, StringComparer.Ordinal).Select(g => g.Last()).ToArray();
        var targets = earlier.Targets.Concat(later.Targets).GroupBy(t => t.Id, StringComparer.Ordinal).Select(g => g.Last()).ToArray();
        var ids = events.SelectMany(e => e.Phases).SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds))
            .Concat(targets.Where(t => t.ResolvedBoundary is not null).SelectMany(t => t.ResolvedBoundary!.EvidenceIds)).ToHashSet(StringComparer.Ordinal);
        var evidence = earlier.Evidence.Concat(later.Evidence).Where(e => ids.Contains(e.Id)).GroupBy(e => e.Id, StringComparer.Ordinal).Select(g => g.Last()).ToArray();
        return earlier with
        {
            GeneratedAt = later.GeneratedAt, Events = events, Evidence = evidence, Targets = targets,
            Withdrawals = earlier.Withdrawals.Concat(later.Withdrawals).Distinct(StringComparer.Ordinal).ToArray(),
            FrontlineRules = earlier.FrontlineRules.Concat(later.FrontlineRules).GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.Last()).ToArray()
        };
    }

    public static async Task<FeedDocument> CollectAsync(string rulesPath, string directory, FeedDocument? previous, HttpClient http, CancellationToken ct)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("CombinedCollectionAlreadyExists");
        Directory.CreateDirectory(directory);
        var feed = previous ?? new FeedDocument { ContentRevision = 1, GeneratedAt = DateTimeOffset.UtcNow };
        if (previous is not null && FeedValidator.Validate(previous).Count != 0) throw new InvalidDataException("InvalidPreviousCombinedFeed");
        var reports = new List<SourceRunReport>();
        int successes = 0;
        async Task Stage(string[] sourceIds, Func<CancellationToken, Task<FeedDocument>> action, int seconds)
        {
            var attempted = DateTimeOffset.UtcNow;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                var next = await action(timeout.Token);
                Validate(next);
                feed = next;
                successes++;
                foreach (var id in sourceIds) reports.Add(new() { Id = id, AttemptedAt = DateTimeOffset.UtcNow, SucceededAt = attempted, Result = "partial" });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or HttpRequestException or OperationCanceledException or ArgumentException)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var id in sourceIds) reports.Add(new() { Id = id, AttemptedAt = DateTimeOffset.UtcNow, Result = "failed", Error = ex.GetType().Name + ":" + ex.Message[..Math.Min(200, ex.Message.Length)], PendingEventIds = EventsForSource(feed, id) });
            }
        }
        await Stage(["kr-events"], async token =>
        {
            var area = Path.Combine(directory, "events");
            await PagedEvents.CollectAsync(area, http, token);
            return await PagedEvents.PrepareAsync(area, feed);
        }, 120);
        await Stage(["kr-notices", "kr-patch-catalog"], async token =>
        {
            var area = Path.Combine(directory, "notices");
            await NoticeCollector.CollectAsync(area, http, token);
            return await NoticeCollector.PrepareAsync(area, feed);
        }, 360);
        // The mapped facts are separately guarded so a changed component preserves its
        // previous date and records review-needed IDs while other sources still update.
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(160));
            var curated = await CuratedSources.CollectAsync(rulesPath, Path.Combine(directory, "curated"), feed, http, timeout.Token);
            var merged = Merge(feed, curated.Feed);
            Validate(merged);
            feed = merged;
            successes++;
            foreach (var source in curated.SourceResults)
                reports.Add(new()
                {
                    Id = "kr-curated:" + source.Id, AttemptedAt = source.FetchedAt,
                    SucceededAt = source.Status == "fetched" ? source.FetchedAt : null,
                    Result = source.Status == "fetched" && curated.PendingEventIds.Count == 0 ? "partial" : "review-needed",
                    Error = source.Error, PendingEventIds = curated.PendingEventIds
                });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or HttpRequestException or OperationCanceledException or ArgumentException)
        {
            ct.ThrowIfCancellationRequested();
            var rules = CalendarJson.Parse<CuratedRules>(await LocalInput.ReadAsync(rulesPath));
            foreach (var source in rules.Sources) reports.Add(new() { Id = "kr-curated:" + source.Id, AttemptedAt = DateTimeOffset.UtcNow, Result = "failed", Error = ex.GetType().Name, PendingEventIds = rules.Rules.Select(r => r.Event.Id).ToArray() });
        }
        if (successes == 0 || feed.Events.Count == 0) throw new InvalidDataException("AllSourcesUnavailable");
        Validate(feed);
        await Collector.WriteNewAsync(Path.Combine(directory, "feed.json"), JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        await Collector.WriteNewAsync(Path.Combine(directory, "sources.json"), JsonSerializer.SerializeToUtf8Bytes(new AllCollectionReport { Sources = reports }, CalendarJson.Options));
        Console.WriteLine($"Combined official calendar: events={feed.Events.Count}; sources={reports.Count}; pending/failed={reports.Count(r => r.Result != "partial")}");
        return feed;
    }

    public static async Task MarkHealthAsync(string directory, string publication)
    {
        var report = CalendarJson.Parse<AllCollectionReport>(await LocalInput.ReadAsync(Path.Combine(directory, "sources.json"), 65536));
        if (report.SchemaVersion != 1 || report.Sources.Count is < 1 or > 32 || report.Sources.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.AttemptedAt == default) ||
            report.Sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != report.Sources.Count) throw new InvalidDataException("InvalidCombinedSourceReport");
        var (pointer, feed, payload) = await AutoPrepare.VerifyCurrentAsync(publication);
        var collected = CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(Path.Combine(directory, "feed.json")));
        Validate(collected);
        var expected = JsonSerializer.SerializeToUtf8Bytes(collected with { ContentRevision = feed.ContentRevision, GeneratedAt = feed.GeneratedAt }, CalendarJson.Options);
        if (!expected.AsSpan().SequenceEqual(payload)) throw new InvalidDataException("SourceReportPayloadMismatch");
        var reports = report.Sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var sourceIds = feed.Evidence.Select(e => e.SourceId).Concat(reports.Keys).Distinct(StringComparer.Ordinal);
        var health = new FeedHealth { Sources = sourceIds.Select(id => reports.TryGetValue(id, out var source)
            ? new SourceHealth
            {
                Id = id, LastAttemptAt = source.AttemptedAt, LastSuccessAt = source.SucceededAt, LastResult = source.Result,
                CompleteCoverage = false, ComparedContentRevision = source.Result == "partial" ? pointer.Content.Revision : null,
                PendingChanges = source.Result == "partial" ? [] : [new() { Kind = "source-review", FirstObservedAt = source.AttemptedAt, AffectedEventIds = source.PendingEventIds.Count > 0 ? source.PendingEventIds : null }]
            } : new SourceHealth { Id = id, LastResult = "not-run" }).ToArray() };
        var updated = pointer with { Health = health };
        if (FeedValidator.Validate(updated).Count != 0) throw new InvalidDataException("InvalidCombinedHealth");
        // Like the publisher, this only prepares local files. The subsequent normal Git
        // push publishes all immutable bytes and the final pointer as a single commit.
        var path = Path.Combine(publication, "current.json");
        var temp = path + ".sources-" + Guid.NewGuid().ToString("N");
        try { await Collector.WriteNewAsync(temp, JsonSerializer.SerializeToUtf8Bytes(updated, CalendarJson.Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static IReadOnlyList<string> EventsForSource(FeedDocument feed, string source)
    {
        var ids = feed.Evidence.Where(e => e.SourceId == source).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        return feed.Events.Where(e => e.Phases.Any(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds).Any(ids.Contains))).Select(e => e.Id).ToArray();
    }
    private static void Validate(FeedDocument feed)
    {
        var errors = FeedValidator.Validate(feed);
        if (errors.Count != 0) throw new InvalidDataException("InvalidCombinedFeed:" + string.Join(",", errors.Take(5)));
        if (JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options).Length > Collector.MaximumBytes) throw new InvalidDataException("CombinedFeedSizeLimit");
    }
}
