using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal static class CollectorChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("FAIL: " + name); checks++; }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or OperationCanceledException or DecoderFallbackException) { checks++; return; }
            throw new InvalidOperationException("FAIL: " + name);
        }
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var fixture = (await File.ReadAllTextAsync(Path.Combine(root, "data/fixtures/events.synthetic.html"))).Replace(" data-synthetic=\"true\"", "", StringComparison.Ordinal);
        var html = Encoding.UTF8.GetBytes(fixture);
        HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(bytes)) { Headers = { ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" } } } };
        using var http = new HttpClient(new Handler(request =>
        {
            Check(request.RequestUri?.AbsoluteUri == EventPageParser.Source && request.Headers.AcceptEncoding.Single().Value == "identity", "Fixed source and identity request");
            return Response(html);
        }));
        Check((await Collector.FetchAsync(http, CancellationToken.None)).AsSpan().SequenceEqual(html), "Chunked body exact bytes");
        using (var redirect = new HttpClient(new Handler(_ => new(HttpStatusCode.Found)))) await Reject(async () => { await Collector.FetchAsync(redirect, CancellationToken.None); }, "Redirect refused");
        using (var wrongType = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("error") }))) await Reject(async () => { await Collector.FetchAsync(wrongType, CancellationToken.None); }, "Wrong media type refused");
        using (var compressed = new HttpClient(new Handler(_ => { var response = Response(html); response.Content.Headers.ContentEncoding.Add("gzip"); return response; })))
            await Reject(async () => { await Collector.FetchAsync(compressed, CancellationToken.None); }, "Unexpected compression refused");
        using (var large = new HttpClient(new Handler(_ => Response(new byte[Collector.MaximumBytes + 1])))) await Reject(async () => { await Collector.FetchAsync(large, CancellationToken.None); }, "Body stream is bounded without Content-Length");
        using (var invalid = new HttpClient(new Handler(_ => Response([0xff, 0xfe, 0xfd])))) await Reject(async () => { await Collector.FetchAsync(invalid, CancellationToken.None); }, "Invalid UTF8 refused");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Reject(async () => { await Collector.FetchAsync(http, cancelled.Token); }, "Cancellation respected");
        }
        var area = Path.Combine(root, ".preview/collector-checks", Guid.NewGuid().ToString("N"));
        var collection = Path.Combine(area, "collection");
        Check(await Collector.CollectAsync(collection, http, CancellationToken.None) == 0, "Collection emits no warning for valid fixture");
        await Reject(async () => { await Collector.CollectAsync(collection, http, CancellationToken.None); }, "Existing collection not overwritten");
        var manifest = CalendarJson.Parse<CollectionManifest>(await File.ReadAllBytesAsync(Path.Combine(collection, "collection.json")));
        var candidate = CalendarJson.Parse<CandidateDocument>(await File.ReadAllBytesAsync(Path.Combine(collection, "candidates.json")));
        Check(manifest.SnapshotSha256 == Collector.Hash(html) && manifest.Bytes == html.Length && !manifest.CompleteCoverage && !candidate.CanPublish && candidate.FetchedAt == manifest.FetchedAt, "Snapshot provenance and partial unreviewed status");
        var reviewPath = Path.Combine(area, "review.json");
        var review = new ReviewDocument { SnapshotSha256 = manifest.SnapshotSha256, ReviewedAt = DateTimeOffset.UtcNow, ReviewedBy = "Offline checker", Approved = candidate.Candidates.Select(c => new ReviewedCandidate { Id = c.Id, NormalizedRecordHash = c.NormalizedRecordHash }).ToArray() };
        async Task Review(ReviewDocument value) => await File.WriteAllBytesAsync(reviewPath, JsonSerializer.SerializeToUtf8Bytes(value, CalendarJson.Options));
        await Review(review with { Approved = [] }); await Reject(async () => { await Collector.PrepareAsync(collection, reviewPath); }, "Unreviewed collection cannot become feed");
        await Review(review with { SnapshotSha256 = new string('0',64) }); await Reject(async () => { await Collector.PrepareAsync(collection, reviewPath); }, "Review must bind snapshot bytes");
        await Review(review with { Approved = [review.Approved[0] with { NormalizedRecordHash = new string('0',64) }] }); await Reject(async () => { await Collector.PrepareAsync(collection, reviewPath); }, "Approval must bind exact candidate record");
        await Review(review);
        var feed = await Collector.PrepareAsync(collection, reviewPath);
        Check(feed.Events.Count == 2 && feed.Events[0].Phases.Count == 2 && !feed.IsExample && FeedValidator.Validate(feed).Count == 0, "Reviewed candidates produce valid nonexample feed");
        Check(feed.Events[1].Phases[0].End.Kind == "unknown" && feed.Events[1].Note!.Contains("V8.0", StringComparison.Ordinal), "Conditional end remains undated with raw wording");
        await File.AppendAllTextAsync(Path.Combine(collection, "source.html"), "tampered");
        await Reject(async () => { await Collector.PrepareAsync(collection, reviewPath); }, "Source alteration breaks provenance");
        Console.WriteLine($"PASS: {checks} Collector offline checks; fake HTTP only; artifacts={area}");
        return 0;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }
}
