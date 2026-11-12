using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal static class PagedEventsChecks
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
        var area = Path.Combine(root, ".preview/paged-event-checks", Guid.NewGuid().ToString("N"));
        string Card(string id, string title) => $"<a href='/news/event/view/{id}'><h3 class='title'>{title}</h3><div class='period'><div class='term'><span class='lbl'>이벤트 기간</span><time datetime='2026-11-01 00:00'>26.11.01 00:00</time> ~ <time datetime='2026-11-30 23:59'>26.11.30 23:59</time></div></div></a>";
        string Nav(int page) => $"<nav class='page'><a href='/news/event?category=1&amp;page={page}'>{page}</a></nav>";
        var first = Card("100", "First event") + Nav(2);
        var second = Card("100", "First event") + Card("200", "Second-page event") + Nav(1) + Nav(2);
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var address = request.RequestUri!.AbsoluteUri;
            requests.Add(address);
            return Html(address == EventPageParser.Source ? first : address == EventPageParser.PageAddress(2) ? second : throw new InvalidOperationException("Unexpected page request"));
        }));
        var collection = Path.Combine(area, "complete");
        await PagedEvents.CollectAsync(collection, http, default);
        Check(requests.SequenceEqual(new[] { EventPageParser.Source, EventPageParser.PageAddress(2) }), "Pagination requests each exact official page once");
        var indexPath = Path.Combine(collection, "pages.json");
        var indexBytes = await File.ReadAllBytesAsync(indexPath);
        var index = CalendarJson.Parse<EventPageManifest>(indexBytes);
        Check(index.Pages.SequenceEqual(new[] { 1, 2 }), "Page manifest records every discovered page");
        var secondPart = Path.Combine(collection, "page-02");
        var secondManifestPath = Path.Combine(secondPart, "collection.json");
        var secondManifestBytes = await File.ReadAllBytesAsync(secondManifestPath);
        var secondManifest = CalendarJson.Parse<CollectionManifest>(secondManifestBytes);
        var secondBytes = await File.ReadAllBytesAsync(Path.Combine(secondPart, "source.html"));
        Check(secondManifest.Page == 2 && secondManifest.Source == EventPageParser.PageAddress(2), "Second page provenance names its own URL and number");
        Check(secondManifest.SnapshotSha256 == Collector.Hash(secondBytes) && secondManifest.Bytes == secondBytes.Length, "Second page provenance binds exact bytes and SHA256");
        var feed = await PagedEvents.PrepareAsync(collection, null);
        Check(feed.Events.Count == 2 && FeedValidator.Validate(feed).Count == 0, "Identical cross-page cards deduplicate to one valid event");
        var secondEvent = feed.Events.Single(e => e.Id == "kr:event:200");
        var secondEvidenceIds = secondEvent.Phases[0].Start.EvidenceIds;
        Check(feed.Evidence.Any(e => secondEvidenceIds.Contains(e.Id) && e.Url == EventPageParser.PageAddress(2) && e.SourceSnapshotSha256 == secondManifest.SnapshotSha256), "Second-page event retains second-page source evidence");
        var previous = feed with { Events = [secondEvent with { Category = "seasonal-event" }] };
        var merged = await PagedEvents.PrepareAsync(collection, previous);
        var observedOnSecond = merged.Events.Single(e => e.Id == "kr:event:200");
        Check(observedOnSecond.Category == "seasonal-event" && !(observedOnSecond.Note ?? "").Contains("미확인", StringComparison.Ordinal), "Event observed on page two is never marked missing from page one");

        await File.WriteAllBytesAsync(indexPath, JsonSerializer.SerializeToUtf8Bytes(new EventPageManifest { Pages = [1] }, CalendarJson.Options));
        await Reject(async () => { await PagedEvents.PrepareAsync(collection, null); }, "Omitting a linked second page from pages.json fails closed");
        await File.WriteAllBytesAsync(indexPath, indexBytes);
        await File.WriteAllBytesAsync(secondManifestPath, JsonSerializer.SerializeToUtf8Bytes(secondManifest with { Page = 1 }, CalendarJson.Options));
        await Reject(async () => { await PagedEvents.PrepareAsync(collection, null); }, "Relabeled second-page manifest cannot borrow first-page provenance");
        await File.WriteAllBytesAsync(secondManifestPath, secondManifestBytes);
        await File.AppendAllTextAsync(Path.Combine(secondPart, "source.html"), "changed");
        await Reject(async () => { await PagedEvents.PrepareAsync(collection, null); }, "Modified second-page source breaks its own hash proof");
        await File.WriteAllBytesAsync(Path.Combine(secondPart, "source.html"), secondBytes);
        File.Move(secondManifestPath, secondManifestPath + ".saved");
        await Reject(async () => { await PagedEvents.PrepareAsync(collection, null); }, "Listed page with missing collection file is not treated as empty success");
        File.Move(secondManifestPath + ".saved", secondManifestPath);

        using (var conflicting = new HttpClient(new Handler(request => Html(request.RequestUri!.AbsoluteUri == EventPageParser.Source ? first : second.Replace("First event", "Conflicting event", StringComparison.Ordinal)))))
        {
            var conflictArea = Path.Combine(area, "conflict");
            await PagedEvents.CollectAsync(conflictArea, conflicting, default);
            await Reject(async () => { await PagedEvents.PrepareAsync(conflictArea, null); }, "Conflicting normalized hashes for one cross-page ID reject entire update");
        }
        await Reject(() => Task.Run(() => PagedEvents.LinkedPages(Card("100", "First event") + Nav(9))), "Navigation outside bounded page range is rejected");
        using (var tooManyPages = new HttpClient(new Handler(_ => Html(Card("100", "First event") + Nav(9)))))
        {
            var limitArea = Path.Combine(area, "page-limit");
            await Reject(async () => { await PagedEvents.CollectAsync(limitArea, tooManyPages, default); }, "Out-of-range discovered page prevents collection completion");
            Check(!File.Exists(Path.Combine(limitArea, "pages.json")), "Rejected collection never writes a completed page manifest");
        }
        Console.WriteLine($"PASS: {checks} Paged event checks; fake HTTP only; artifacts={area}");
        return checks;
    }

    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" } }
        }
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }
}
