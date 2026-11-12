using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

if (args.SequenceEqual(new[] { "--live-smoke" })) { await LiveFeedSmoke.Run(); return; }

var checks = new List<string>();
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAIL: " + label);
    checks.Add(label);
}
async Task Throws<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, label); return; }
    throw new InvalidOperationException("FAIL: " + label);
}
var now = DateTimeOffset.Parse("2026-11-12T05:00:00Z");
byte[] Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CalendarJson.Options);
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
FeedDocument Feed(long revision = 1) => new()
{
    ContentRevision = revision, GeneratedAt = now,
    Evidence = [new() { Id = "ev:1", SourceId = "kr-events", Url = "https://www.ff14.co.kr/news/event", ObservedAt = now, SourceSnapshotSha256 = new string('a', 64) }],
    Events = [new() { Id = "kr:test", Title = "Fixture event " + revision, Category = "web-event", Phases = [new()
    {
        Id = "participation", Label = "Participation",
        Start = new() { Kind = "instant", Value = "2026-11-01T00:00:00+09:00", Precision = "minute", Certainty = "confirmed", EvidenceIds = ["ev:1"] },
        End = new() { Kind = "instant", Value = "2026-11-30T23:59:00+09:00", Precision = "minute", Certainty = "confirmed", EvidenceIds = ["ev:1"] }, EndInclusion = "inclusive-unit"
    }] }]
};
CurrentPointer Pointer(FeedDocument feed, long publication, byte[]? bytes = null) => new()
{
    GeneratedAt = now, PublicationSequence = publication,
    Content = new() { Revision = feed.ContentRevision, Path = $"revisions/{feed.ContentRevision:D6}.json", Sha256 = Hash(bytes ?? Json(feed)), Bytes = (bytes ?? Json(feed)).Length },
    Health = new() { Sources = [new() { Id = "kr-events" }] }
};
var example = Json(Feed() with { IsExample = true });
Check(FeedValidator.Validate(Feed()).Count == 0, "Synthetic production fixture has valid provenance structure");
var workspaceRoot = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(workspaceRoot, "DuckbillCalendar.slnx")))
    workspaceRoot = Directory.GetParent(workspaceRoot)?.FullName ?? throw new InvalidOperationException("Workspace not found");
var root = Path.Combine(workspaceRoot, ".preview", "infrastructure-checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); // Deliberately kept for diagnosis; never recursively deletes a computed path.
string Area(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
async Task Drain(CalendarWorkspace workspace) { await workspace.FlushAsync(); workspace.Pump(); }
CalendarWorkspace Workspace(string area, ScriptHandler handler, TimeSpan? deadline = null) =>
    new(area, example, new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, () => now, deadline);
HttpResponseMessage Response(byte[] bytes, string? etag = null, bool lengthKnown = true)
{
    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = lengthKnown ? new ByteArrayContent(bytes) : new StreamContentFixture(bytes) };
    response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    if (etag != null) response.Headers.ETag = new EntityTagHeaderValue(etag);
    return response;
}
void Enable(CalendarWorkspace workspace) => workspace.UpdateState(workspace.State with { View = workspace.State.View with { RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress } });

// Durable personal state, corruption recovery and bounds.
var store = new AtomicJsonStore(Area("store"));
var stateA = new LocalState { Profiles = [new() { Alias = "A" }] };
var stateB = stateA with { Profiles = [new() { Alias = "B" }] };
await store.SaveAsync("state.json", stateA);
await store.SaveAsync("state.json", stateB);
Check(store.Load<LocalState>("state.json", s => FeedValidator.ValidateLocal(s).Count == 0, out var loadedStatus)?.Profiles[0].Alias == "B" && loadedStatus == "Loaded", "Primary local state roundtrip");
await File.WriteAllTextAsync(store.Resolve("state.json"), "{broken");
Check(store.Load<LocalState>("state.json", s => FeedValidator.ValidateLocal(s).Count == 0, out var recoveredStatus)?.Profiles[0].Alias == "A" && recoveredStatus == "BackupRecovered", "Corrupt primary falls back to prior valid backup");
Check(Directory.EnumerateFiles(store.DirectoryPath, "state.json.corrupt-*").Any(), "Corrupt local state preserved for diagnosis");
await store.SaveAsync("state.json", stateB);
Check(CalendarJson.Parse<LocalState>(File.ReadAllBytes(store.Resolve("state.json.bak"))).Profiles[0].Alias == "A", "Recovered good backup is not overwritten with known bad primary");
await Throws<InvalidDataException>(() => store.WriteAsync("state.json", new byte[AtomicJsonStore.MaximumFileBytes + 1], true), "Oversized write rejected");
Check(store.Load<LocalState>("state.json", _ => true, out _)?.Profiles[0].Alias == "B", "Rejected write retains current state");
await Throws<InvalidDataException>(() => Task.Run(() => store.Resolve("../outside.json")), "Storage path traversal rejected");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    await Throws<OperationCanceledException>(() => store.SaveAsync("state.json", stateA, cancelled.Token), "Cancelled write does not commit");
}
Check(store.Load<LocalState>("state.json", _ => true, out _)?.Profiles[0].Alias == "B", "Cancellation keeps prior primary intact");
Check(!Directory.EnumerateFiles(store.DirectoryPath, "*.tmp-*").Any(), "Temporary write files cleaned after cancellation");
var contentBytes = Json(Feed());
var contentSha = await store.PutContentAsync(contentBytes, CancellationToken.None);
var contentPath = store.Resolve(Path.Combine("cache", "content", contentSha + ".json"));
await File.WriteAllTextAsync(contentPath, "bad cache");
await store.PutContentAsync(contentBytes, CancellationToken.None);
Check(Hash(File.ReadAllBytes(contentPath)) == contentSha, "Small corrupt content-addressed file repaired from verified bytes");
await File.WriteAllBytesAsync(contentPath, new byte[AtomicJsonStore.MaximumFileBytes + 1]);
await store.PutContentAsync(contentBytes, CancellationToken.None);
Check(Hash(File.ReadAllBytes(contentPath)) == contentSha, "Oversized corrupt content-addressed file repaired");
await Throws<JsonException>(() => Task.Run(() => CalendarJson.Parse<WorkspaceCache>("{}"u8)), "Empty cache cannot silently reset replay watermarks");

// Public transport has no real network in this suite.
var directHandler = new ScriptHandler((_, _) => Task.FromResult(Response("123456"u8.ToArray(), lengthKnown: false)));
using (var http = new HttpClient(directHandler))
using (var transport = new BoundedFeedTransport(http))
{
    await Throws<InvalidDataException>(() => transport.GetAsync(new(BoundedFeedTransport.CurrentAddress), 5, null, CancellationToken.None), "Stream cap enforced without Content-Length");
    var before = directHandler.Calls;
    await Throws<InvalidDataException>(() => transport.GetAsync(new("https://example.com/current.json"), 100, null, CancellationToken.None), "Foreign origin rejected before HTTP");
    Check(directHandler.Calls == before, "Rejected origin makes no request");
    await Throws<ArgumentOutOfRangeException>(() => transport.GetAsync(new(BoundedFeedTransport.CurrentAddress), 0, null, CancellationToken.None), "Invalid response size limit rejected");
}
foreach (var path in new[] { "../secret", "revisions/%2e%2e/file.json", "revisions/a.json?x=1", "https://example.com/a.json", "revisions\\a.json", "/revisions/a.json" })
    await Throws<InvalidDataException>(() => Task.Run(() => BoundedFeedTransport.GetPayloadUri(path)), "Untrusted payload path rejected: " + path);
Check(BoundedFeedTransport.GetPayloadUri("revisions/000001.json").Host == "raw.githubusercontent.com", "Allowed relative payload remains on fixed origin");
var redirects = new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com") } }));
using (var transport = new BoundedFeedTransport(new HttpClient(redirects)))
    await Throws<InvalidDataException>(() => transport.GetAsync(new(BoundedFeedTransport.CurrentAddress), 100, null, CancellationToken.None), "Redirect response rejected");
var compressed = new ScriptHandler((_, _) => { var r = Response("{}"u8.ToArray()); r.Content.Headers.ContentEncoding.Add("gzip"); return Task.FromResult(r); });
using (var transport = new BoundedFeedTransport(new HttpClient(compressed)))
    await Throws<InvalidDataException>(() => transport.GetAsync(new(BoundedFeedTransport.CurrentAddress), 100, null, CancellationToken.None), "Unexpected compression rejected");

// Remote opt-in, partial success, equal-pointer retry, 304 and persisted replay boundaries.
var feed1 = Feed(); var payload1 = Json(feed1); var pointer1 = Json(Pointer(feed1, 1, payload1));
int pointers = 0, payloads = 0;
var retryHandler = new ScriptHandler((request, _) =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal))
    {
        pointers++;
        if (pointers == 1) return Task.FromResult(Response(pointer1, "\"p1\""));
        Check(request.Headers.IfNoneMatch.Any(v => v.Tag == "\"p1\""), "Current ETag sent on retry");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
    }
    payloads++;
    return Task.FromResult(payloads == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response(payload1));
});
var retryArea = Area("retry");
using (var workspace = Workspace(retryArea, retryHandler))
{
    workspace.Refresh(true); await Drain(workspace);
    Check(retryHandler.Calls == 0 && workspace.StatusCode == "RemoteDisabled", "No opt-in means no network request");
    Enable(workspace); await Drain(workspace);
    workspace.Refresh(true); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 0 && workspace.RemotePointer?.Content.Revision == 1 && workspace.StatusCode == "Http503", "Payload failure keeps active content and accepts advertised pointer separately");
    now = now.AddMinutes(6);
    workspace.Refresh(true); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 1 && workspace.StatusCode == "RemoteUpdated" && payloads == 2, "Same publication plus 304 retries unfinished payload successfully");
    now = now.AddMinutes(6);
    workspace.Refresh(true); await Drain(workspace);
    Check(workspace.StatusCode == "RemoteNotModified" && payloads == 2, "Accepted cached payload reused on unchanged 304");
    Check(workspace.RemotePointer!.Health.Sources[0].LastSuccessAt == null && workspace.LastClientCheckedAt == now, "Client 304 does not fabricate source success");
    var callCount = retryHandler.Calls;
    workspace.Refresh(true); await Drain(workspace);
    Check(retryHandler.Calls == callCount && workspace.StatusCode == "RefreshCooldown", "Manual refresh obeys five-minute cooldown");
}
var noRequest = new ScriptHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP"));
using (var restored = Workspace(retryArea, noRequest))
    Check(restored.Feed.ContentRevision == 1 && noRequest.Calls == 0, "Restart restores accepted cache without making HTTP request");

var conflictMode = 0;
var conflictHandler = new ScriptHandler((request, _) =>
{
    if (!request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal)) return Task.FromResult(Response(payload1));
    return Task.FromResult(Response(conflictMode switch
    {
        0 => Json(Pointer(feed1, 2, payload1)),
        1 => Json(Pointer(feed1, 1, payload1)),
        _ => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Json(Pointer(feed1, 2, payload1))) + "\n")
    }));
});
using (var workspace = Workspace(Area("conflicts"), conflictHandler))
{
    Enable(workspace); await Drain(workspace); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 1, "Conflict fixture first revision accepted");
    conflictMode = 1; now = now.AddMinutes(6); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.StatusCode == "PublicationDowngrade" && workspace.Feed.ContentRevision == 1, "Publication replay rejected without deleting active content");
    conflictMode = 2; now = now.AddMinutes(6); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.StatusCode == "PublicationConflict", "Same publication with different raw pointer bytes rejected");
}

int hashMode = 0;
var hashHandler = new ScriptHandler((request, _) =>
{
    if (!request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal)) return Task.FromResult(Response("bad payload"u8.ToArray()));
    var pointer = Pointer(feed1, hashMode == 0 ? 1 : 2, payload1);
    if (hashMode != 0) pointer = pointer with { Content = pointer.Content with { Sha256 = new string('b', 64) } };
    return Task.FromResult(Response(Json(pointer)));
});
using (var workspace = Workspace(Area("hash"), hashHandler))
{
    Enable(workspace); await Drain(workspace); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 0 && workspace.StatusCode == "PayloadLengthMismatch", "Invalid payload retains empty active content");
    hashMode = 1; now = now.AddMinutes(6); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.StatusCode == "ContentRevisionConflict", "Advertised but unaccepted revision hash cannot change");
}

// Explicit file imports, consent cancellation, bounded deadline and unload draining.
var importArea = Area("import");
var importFile = Path.Combine(root, "manual-feed.json"); await File.WriteAllBytesAsync(importFile, payload1);
using (var workspace = Workspace(importArea, noRequest))
{
    workspace.ImportFile(importFile); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 1 && workspace.StatusCode == "FileImported", "Valid explicit local feed imports without HTTP");
    await File.WriteAllTextAsync(importFile, "{bad"); workspace.ImportFile(importFile); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 1 && workspace.StatusCode == "InvalidJson", "Bad import keeps last accepted feed");
    workspace.LoadExample(true); await Drain(workspace);
    Check(workspace.Feed.IsExample, "Example display is explicitly enabled");
    workspace.LoadExample(false); await Drain(workspace);
    Check(!workspace.Feed.IsExample && workspace.Feed.ContentRevision == 1, "Leaving example mode restores accepted feed");
}
var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var wasCancelled = false;
var revokeHandler = new ScriptHandler(async (_, token) =>
{
    started.TrySetResult();
    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
    catch (OperationCanceledException) { wasCancelled = true; throw; }
    throw new InvalidOperationException();
});
using (var workspace = Workspace(Area("revoke"), revokeHandler))
{
    Enable(workspace); await Drain(workspace); workspace.Refresh(true);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
    workspace.UpdateState(workspace.State with { View = workspace.State.View with { RemoteUpdatesEnabled = false } });
    await Drain(workspace);
    Check(wasCancelled && !workspace.State.View.RemoteUpdatesEnabled, "Revoking opt-in cancels active HTTP request");
    Check(workspace.Feed.ContentRevision == 0 && !workspace.HasUnsavedChanges, "Revocation persists while cancelled remote result cannot replace content");
}
var slowHandler = new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowBodyContent() }));
using (var workspace = Workspace(Area("deadline"), slowHandler, TimeSpan.FromMilliseconds(50)))
{
    Enable(workspace); await Drain(workspace); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.StatusCode == "DeadlineExceeded" && !workspace.IsBusy, "Total deadline covers stalled response body");
}
var durableArea = Area("unload");
var durable = Workspace(durableArea, noRequest);
for (int i = 0; i < 8; i++) durable.UpdateState(durable.State with { Profiles = [new() { Alias = "saved-" + i }] });
durable.Dispose();
await durable.FlushAsync();
Check(CalendarJson.Parse<LocalState>(await File.ReadAllBytesAsync(Path.Combine(durableArea, "state.json"))).Profiles[0].Alias == "saved-7", "Dispose drains queued personal writes without blocking caller");

// A corrupt newer payload can fall back to the prior content without losing remote high-waters,
// and the same advertised revision must remain recoverable on the next explicit refresh.
var feed2 = Feed(2); var payload2 = Json(feed2);
var pointer2 = Json(Pointer(feed2, 2, payload2));
int recoveryRevision = 1;
var recoveryHandler = new ScriptHandler((request, _) => Task.FromResult(
    request.RequestUri!.AbsolutePath.EndsWith("current.json", StringComparison.Ordinal)
        ? Response(recoveryRevision == 1 ? pointer1 : pointer2)
        : Response(recoveryRevision == 1 ? payload1 : payload2)));
var recoveryArea = Area("recovery");
using (var workspace = Workspace(recoveryArea, recoveryHandler))
{
    Enable(workspace); await Drain(workspace); workspace.Refresh(true); await Drain(workspace);
    recoveryRevision = 2; now = now.AddMinutes(6); workspace.Refresh(true); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 2, "Newer remote revision accepted before corruption fixture");
}
var brokenPayloadPath = Path.Combine(recoveryArea, "cache", "content", Hash(payload2) + ".json");
await File.WriteAllBytesAsync(brokenPayloadPath, new byte[AtomicJsonStore.MaximumFileBytes + 1]);
using (var workspace = Workspace(recoveryArea, recoveryHandler))
{
    Check(workspace.Feed.ContentRevision == 1 && workspace.RemotePointer?.Content.Revision == 2, "Corrupt newer cache falls back to previous content while retaining latest advertisement");
    var persisted = CalendarJson.Parse<WorkspaceCache>(await File.ReadAllBytesAsync(Path.Combine(recoveryArea, "cache", "active.json")));
    Check(persisted.HighestAcceptedRemoteRevision == 2 && persisted.HighestAdvertisedRevision == 2, "Cache fallback does not lower persisted replay high-waters");
    workspace.Refresh(true); await Drain(workspace);
    Check(workspace.Feed.ContentRevision == 2 && workspace.StatusCode == "RemoteUpdated", "Same advertised revision downloads and repairs oversized corrupt cache");
}
Check(Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.AllDirectories).Count() == 0, "All isolated atomic writes leave no temporary files");

// A reviewed bundled snapshot makes first-run data useful without manufacturing
// remote coverage or overriding an explicitly imported/accepted payload.
var bundleArea = Area("bundled-startup");
var bundledBytes = Json(Feed(7));
var noBundleNetwork = new ScriptHandler((_, _) => throw new InvalidOperationException("Bundled startup must not use HTTP"));
using (var http = new HttpClient(noBundleNetwork))
using (var workspace = new CalendarWorkspace(bundleArea, example, http, () => now, bundledFeed: bundledBytes))
{
    Check(workspace.Feed.ContentRevision == 7 && workspace.FeedSource == "bundled", "Fresh startup displays reviewed bundled data without remote opt-in");
    Check(workspace.ActiveHealth is null && workspace.RemotePointer is null, "Bundle never fabricates remote health");
    workspace.LoadExample(true);
    Check(workspace.Feed.IsExample && workspace.FeedSource == "example", "Example mode remains explicitly selectable over real bundle");
    workspace.LoadExample(false); await Drain(workspace);
    Check(!workspace.Feed.IsExample && workspace.Feed.ContentRevision == 7, "Leaving example mode restores actual bundle");
    var input = Path.Combine(bundleArea, "manual.json");
    await File.WriteAllBytesAsync(input, Json(Feed(2)));
    workspace.ImportFile(input); await Drain(workspace);
    Check(workspace.FeedSource == "file" && workspace.Feed.ContentRevision == 2, "Explicit import replaces bundled fallback");
}
using (var http = new HttpClient(noBundleNetwork))
using (var workspace = new CalendarWorkspace(bundleArea, example, http, () => now, bundledFeed: bundledBytes))
    Check(workspace.FeedSource == "file" && workspace.Feed.ContentRevision == 2, "New bundle does not overwrite user-imported cache on restart");
Check(noBundleNetwork.Calls == 0, "Bundled startup and mode switching make zero HTTP requests");
await Throws<InvalidDataException>(() => Task.Run(() =>
{
    using var http = new HttpClient(new ScriptHandler((_, _) => throw new InvalidOperationException()));
    using var workspace = new CalendarWorkspace(Area("bundled-invalid"), example, http, bundledFeed: example);
}), "Production bundle rejects example feed");

checks.AddRange(await LocalAlertChecks.Run(root, example));
Console.WriteLine($"PASS: {checks.Count} Infrastructure checks; fake HTTP only; artifacts={root}");

sealed class ScriptHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> script) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return script(request, cancellationToken);
    }
}
sealed class StreamContentFixture(byte[] bytes) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => Task.FromResult<Stream>(new MemoryStream(bytes));
}
sealed class SlowBodyContent : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new SlowStream());
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => Task.FromResult<Stream>(new SlowStream());
}
sealed class SlowStream : Stream
{
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0; }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
