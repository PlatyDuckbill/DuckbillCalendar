using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal static class AutoPrepareChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); checks++; }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { checks++; return; }
            throw new InvalidOperationException("FAIL: " + name);
        }
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var fixture = (await File.ReadAllTextAsync(Path.Combine(root, "data/fixtures/events.synthetic.html"))).Replace(" data-synthetic=\"true\"", "", StringComparison.Ordinal);
        var area = Path.Combine(root, ".preview/auto-checks", Guid.NewGuid().ToString("N"));
        async Task<string> Collection(string name, string html)
        {
            var directory = Path.Combine(area, name); Directory.CreateDirectory(directory);
            var bytes = Encoding.UTF8.GetBytes(html);
            await File.WriteAllBytesAsync(Path.Combine(directory, "source.html"), bytes);
            await File.WriteAllBytesAsync(Path.Combine(directory, "collection.json"), JsonSerializer.SerializeToUtf8Bytes(new CollectionManifest { FetchedAt = DateTimeOffset.UtcNow, Bytes = bytes.Length, SnapshotSha256 = Collector.Hash(bytes), Status = 200 }, CalendarJson.Options));
            return directory;
        }
        var first = await Collection("first", fixture);
        var feed = await AutoPrepare.PrepareAsync(first, null);
        Check(feed.Events.Count == 2 && !feed.IsExample && FeedValidator.Validate(feed).Count == 0, "Structured source produces valid automatic feed");
        Check(feed.Events[1].Phases[0].End.Kind == "unknown" && feed.Events[1].Note!.Contains("V8.0", StringComparison.Ordinal), "Auto path does not guess conditional end date");
        Check(feed.Evidence.All(e => e.Note!.Contains("기계 검증", StringComparison.Ordinal) && e.Url == EventPageParser.Source), "Machine validation never claims human/LLM approval");
        var old = feed with { Events = [feed.Events[0] with { Category = "seasonal-event" }, feed.Events[1], feed.Events[0] with { Id = "kr:event:missing", Title = "Retain prior event" }] };
        var next = await Collection("next", fixture + "\n");
        var updated = await AutoPrepare.PrepareAsync(next, old);
        Check(updated.Events.Single(e => e.Id == feed.Events[0].Id).Category == "seasonal-event", "Existing category mapping is retained");
        var missing = updated.Events.Single(e => e.Id == "kr:event:missing");
        Check(missing.Lifecycle == "published" && missing.Note!.Contains("미확인", StringComparison.Ordinal), "Missing first-page event is retained without cancellation");
        Check(missing.Phases[0].Start.EvidenceIds.All(id => updated.Evidence.Any(e => e.Id == id && e.SourceSnapshotSha256 == feed.Evidence[0].SourceSnapshotSha256)), "Retained event keeps original provenance");
        Check(updated.Events.Single(e => e.Id == feed.Events[0].Id).Revision == 2, "Observed event gets new revision");
        var mismatch = await Collection("display-mismatch", fixture.Replace(">26.11.01 00:00<", ">26.11.02 00:00<", StringComparison.Ordinal));
        await Reject(async () => { await AutoPrepare.PrepareAsync(mismatch, old); }, "Displayed date mismatch refuses whole update");
        var noStart = await Collection("no-start", fixture.Replace("<time datetime=\"2026-10-28 17:00\">26.10.28 17:00</time>", "시작 미정", StringComparison.Ordinal));
        await Reject(async () => { await AutoPrepare.PrepareAsync(noStart, old); }, "Unknown start refuses automatic feed");
        var missingAttribute = await Collection("missing-attribute", fixture.Replace("datetime=\"2026-11-11 23:59\"", "", StringComparison.Ordinal));
        await Reject(async () => { await AutoPrepare.PrepareAsync(missingAttribute, old); }, "Missing end datetime cannot silently downgrade a structured boundary");
        var empty = await Collection("empty", "<html><body>HTTP 200 error page</body></html>");
        await Reject(async () => { await AutoPrepare.PrepareAsync(empty, old); }, "Empty/error page preserves prior feed by failing");
        var synthetic = await Collection("synthetic", fixture.Replace("<html lang=\"ko\"", "<html data-synthetic=\"true\" lang=\"ko\"", StringComparison.Ordinal));
        await Reject(async () => { await AutoPrepare.PrepareAsync(synthetic, old); }, "Synthetic source cannot be promoted automatically");
        await Reject(async () => { await AutoPrepare.PrepareAsync(next, old with { IsExample = true }); }, "Example prior feed is refused");
        var oversized = old with { Events = Enumerable.Range(0,501).Select(i => feed.Events[0] with { Id = "kr:event:retained-" + i }).ToArray() };
        await Reject(async () => { await AutoPrepare.PrepareAsync(next, oversized); }, "Retention limit fails instead of truncating history");
        var publication = Path.Combine(area, "publication"); Directory.CreateDirectory(Path.Combine(publication, "revisions"));
        var payload = JsonSerializer.SerializeToUtf8Bytes(updated with { ContentRevision = 7 }, CalendarJson.Options);
        await File.WriteAllBytesAsync(Path.Combine(publication, "revisions/000007.json"), payload);
        var pointer = new CurrentPointer { PublicationSequence = 7, GeneratedAt = DateTimeOffset.UtcNow, Content = new() { Revision = 7, Path = "revisions/000007.json", Sha256 = Collector.Hash(payload), Bytes = payload.Length }, Health = new() { Sources = [new() { Id = "kr-events" }] } };
        await File.WriteAllBytesAsync(Path.Combine(publication, "current.json"), JsonSerializer.SerializeToUtf8Bytes(pointer, CalendarJson.Options));
        Check((await AutoPrepare.VerifyCurrentAsync(publication)).Feed.ContentRevision == 7, "Previous pointer and exact payload verified");
        await AutoPrepare.MarkPartialAsync(next, publication);
        var partial = await AutoPrepare.VerifyCurrentAsync(publication);
        var health = partial.Pointer.Health.Sources.Single();
        Check(health.LastResult == "partial" && !health.CompleteCoverage && health.ComparedContentRevision == 7 && health.LastSuccessAt.HasValue, "Partial collection health is truthful and revision-bound");
        Check(!HealthFreshness.ForEvent(partial.Feed, partial.Pointer.Health, partial.Feed.Events[0], DateTimeOffset.UtcNow).IsFresh, "Partial coverage never unlocks public timed alerts");
        await File.AppendAllTextAsync(Path.Combine(publication, "revisions/000007.json"), " ");
        await Reject(async () => { await AutoPrepare.VerifyCurrentAsync(publication); }, "Previous payload tampering is rejected");
        Console.WriteLine($"PASS: {checks} Automatic feed checks; deterministic fixtures only; artifacts={area}");
        return 0;
    }
}
