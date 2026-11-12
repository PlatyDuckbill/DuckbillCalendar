using System.Text.Json;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Collect;

internal static class AllSourcesChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException("FAIL: " + name); checks++; }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { checks++; return; }
            throw new InvalidOperationException("FAIL: " + name);
        }
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var area = Path.Combine(root, ".preview/all-source-checks", Guid.NewGuid().ToString("N"));
        var collection = Path.Combine(area, "collection");
        var publication = Path.Combine(area, "publication");
        Directory.CreateDirectory(collection);
        Directory.CreateDirectory(Path.Combine(publication, "revisions"));
        var now = DateTimeOffset.UtcNow;
        EvidenceRecord Proof(string id, string source = "kr-events") => new()
        {
            Id = id, SourceId = source, Url = "https://www.ff14.co.kr/news/event", ObservedAt = now,
            SourceSnapshotSha256 = new string('a', 64)
        };
        CalendarEvent Event(string id, string title, string proof, string category = "web-event") => new()
        {
            Id = id, Title = title, Category = category, Phases = [new()
            {
                Id = "participation", Label = "Fixture period", Start = CalendarBoundary.Instant(now.AddHours(1)) with { EvidenceIds = [proof] },
                End = CalendarBoundary.Instant(now.AddHours(2)) with { EvidenceIds = [proof] }
            }]
        };
        var earlier = new FeedDocument
        {
            ContentRevision = 1, GeneratedAt = now,
            Events = [Event("kr:event:fixture", "Earlier", "proof-old"), Event("kr:notice:fixture", "Preserved notice", "proof-notice", "notice")],
            Evidence = [Proof("proof-old"), Proof("proof-notice", "kr-notices"), Proof("proof-target"), Proof("proof-unused")],
            Targets = [new() { Id = "kr:target:fixture", Kind = "patch-open", Resolution = "resolved", ResolvedBoundary = CalendarBoundary.Date(new DateOnly(2027, 2, 23)) with { EvidenceIds = ["proof-target"] } }]
        };
        var later = new FeedDocument
        {
            ContentRevision = 1, GeneratedAt = now,
            Events = [Event("kr:event:fixture", "Updated", "proof-new")], Evidence = [Proof("proof-new")]
        };
        var merged = AllSources.Merge(earlier, later);
        Check(merged.Events.Single(e => e.Id == "kr:event:fixture").Title == "Updated", "Later same-ID source observation replaces earlier event");
        Check(merged.Events.Any(e => e.Id == "kr:notice:fixture") && merged.Events.Count == 2, "Merging event source preserves another notice domain");
        Check(merged.Targets.Single().ResolvedBoundary!.EvidenceIds.All(id => merged.Evidence.Any(e => e.Id == id)), "Resolved target evidence survives event-source merge");
        Check(!merged.Evidence.Any(e => e.Id is "proof-old" or "proof-unused") && FeedValidator.Validate(merged).Count == 0, "Merge prunes obsolete and unreferenced evidence without invalidating feed");
        async Task WriteCollected(FeedDocument feed) => await File.WriteAllBytesAsync(Path.Combine(collection, "feed.json"), JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        async Task WriteReport(IReadOnlyList<SourceRunReport> sources) => await File.WriteAllBytesAsync(Path.Combine(collection, "sources.json"), JsonSerializer.SerializeToUtf8Bytes(new AllCollectionReport { Sources = sources }, CalendarJson.Options));
        var sources = new[]
        {
            new SourceRunReport { Id = "kr-events", AttemptedAt = now, SucceededAt = now, Result = "partial" },
            new SourceRunReport { Id = "kr-notices", AttemptedAt = now, SucceededAt = now, Result = "partial" }
        };
        await WriteCollected(merged);
        await WriteReport(sources);
        var published = merged with { ContentRevision = 7, GeneratedAt = now.AddSeconds(1) };
        var payload = JsonSerializer.SerializeToUtf8Bytes(published, CalendarJson.Options);
        await File.WriteAllBytesAsync(Path.Combine(publication, "revisions/000007.json"), payload);
        var pointer = new CurrentPointer
        {
            PublicationSequence = 7, GeneratedAt = now.AddSeconds(1),
            Content = new() { Revision = 7, Path = "revisions/000007.json", Sha256 = Collector.Hash(payload), Bytes = payload.Length },
            Health = new() { Sources = [new() { Id = "kr-events" }, new() { Id = "kr-notices" }] }
        };
        await File.WriteAllBytesAsync(Path.Combine(publication, "current.json"), JsonSerializer.SerializeToUtf8Bytes(pointer, CalendarJson.Options));
        await AllSources.MarkHealthAsync(collection, publication);
        var verified = await AutoPrepare.VerifyCurrentAsync(publication);
        Check(verified.Pointer.Health.Sources.All(s => s.ComparedContentRevision == 7 && s.LastSuccessAt == now), "Source report binds publication when only revision and generation time change");
        Check(verified.Pointer.Health.Sources.All(s => s.LastResult == "partial" && !s.CompleteCoverage) &&
            !HealthFreshness.ForEvent(verified.Feed, verified.Pointer.Health, verified.Feed.Events[0], now).IsFresh,
            "Actual partial source reports never unlock public timed alerts");
        await WriteCollected(merged with { Events = [merged.Events[0] with { Title = "Unpublished different content" }, merged.Events[1]] });
        await Reject(async () => { await AllSources.MarkHealthAsync(collection, publication); }, "Health report cannot attach to different collected event bytes");
        await WriteCollected(merged);
        await WriteReport([sources[0], sources[0]]);
        await Reject(async () => { await AllSources.MarkHealthAsync(collection, publication); }, "Duplicate source report IDs are rejected");
        await WriteReport(Enumerable.Range(0, 33).Select(i => sources[0] with { Id = "source-" + i }).ToArray());
        await Reject(async () => { await AllSources.MarkHealthAsync(collection, publication); }, "Oversized source report is rejected rather than truncated");
        await WriteReport([sources[0] with { SucceededAt = now.AddMinutes(1) }, sources[1]]);
        await Reject(async () => { await AllSources.MarkHealthAsync(collection, publication); }, "Source success after its attempt timestamp is rejected");
        Console.WriteLine($"PASS: {checks} Combined source checks; isolated files only; artifacts={area}");
        return checks;
    }
}
