using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

internal static class PublicationSmoke
{
    public static async Task Run(string publication)
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var area = Path.Combine(root, ".preview", "publication-smoke", Guid.NewGuid().ToString("N"));
        var pointerBytes = await File.ReadAllBytesAsync(Path.Combine(publication, "current.json"));
        var pointer = CalendarJson.Parse<CurrentPointer>(pointerBytes);
        if (FeedValidator.Validate(pointer).Count != 0) throw new InvalidDataException("InvalidSmokePointer");
        var payload = await AtomicRead(publication, pointer.Content.Path);
        var expected = FeedPublication.ReadPayload(pointer, payload);
        var example = await File.ReadAllBytesAsync(Path.Combine(root, "data/contracts/v2/revisions/000001.json"));
        var now = DateTimeOffset.UtcNow;
        var personal = new PersonalEntry { Id = "local-audit", Title = "데이터 갱신 보존 검사", When = CalendarBoundary.Date(CalendarEngine.Today(now)) };
        var nextFeed = expected with { ContentRevision = checked(expected.ContentRevision + 1) };
        var nextBytes = JsonSerializer.SerializeToUtf8Bytes(nextFeed, CalendarJson.Options);
        var corruptPointer = pointer with { PublicationSequence = checked(pointer.PublicationSequence + 1),
            Content = pointer.Content with { Revision = nextFeed.ContentRevision, Path = $"revisions/{nextFeed.ContentRevision:D6}.json",
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(nextBytes)), Bytes = nextBytes.Length } };
        bool corrupt = false;
        using var http = new HttpClient(new PublicationHttp(request =>
        {
            var bytes = request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal)
                ? corrupt ? JsonSerializer.SerializeToUtf8Bytes(corruptPointer, CalendarJson.Options) : pointerBytes
                : corrupt ? nextBytes[..^1] : payload;
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } };
        }));
        using (var workspace = new CalendarWorkspace(area, example, http, () => now))
        {
            workspace.UpdateState(workspace.State with { Entries = [personal], View = workspace.State.View with { RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress } });
            workspace.Refresh(true); await workspace.FlushAsync(); workspace.Pump();
            if (workspace.StatusCode != "RemoteUpdated" || workspace.Feed.ContentRevision != expected.ContentRevision || workspace.Feed.Events.Count != expected.Events.Count ||
                workspace.State.Entries.Single().Id != personal.Id || CalendarEngine.Merge(workspace.Feed, workspace.State).Count != expected.Events.Count + 1)
                throw new InvalidOperationException("Publication did not reach the actual client or overwrote local data");
            Console.WriteLine($"PASS: local publisher → actual transport/workspace → calendar merge; revision={expected.ContentRevision}; events={expected.Events.Count}; personal preserved=1");
            corrupt = true; now = now.AddMinutes(6); workspace.Refresh(true); await workspace.FlushAsync(); workspace.Pump();
            if (workspace.StatusCode != "PayloadLengthMismatch" || workspace.Feed.ContentRevision != expected.ContentRevision || workspace.State.Entries.Count != 1)
                throw new InvalidOperationException("Broken publication replaced previously accepted data");
            Console.WriteLine("PASS: interrupted next payload retains the previous feed and personal state");
        }
        using (var restarted = new CalendarWorkspace(area, example, http, () => now))
        {
            if (restarted.Feed.ContentRevision != expected.ContentRevision || restarted.State.Entries.Single().Id != personal.Id || restarted.RemotePointer?.Content.Revision != nextFeed.ContentRevision)
                throw new InvalidOperationException("Restart lost last-good bytes, local state or the newer replay watermark");
            Console.WriteLine("PASS: offline cache restart preserves last-good data, personal state and newer advertisement separately; " + area);
        }
    }
    private static Task<byte[]> AtomicRead(string directory, string path)
    {
        var resolved = new AtomicJsonStore(directory).Resolve(path);
        return File.ReadAllBytesAsync(resolved);
    }
    private sealed class PublicationHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
