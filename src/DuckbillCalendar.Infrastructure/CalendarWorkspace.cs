using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Infrastructure;

public sealed record WorkspaceCache
{
    [System.Text.Json.Serialization.JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    public string? PayloadSha256 { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public string Source { get; init; } = "none";
    [System.Text.Json.Serialization.JsonRequired]
    public long ActiveContentRevision { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public long HighestAcceptedRemoteRevision { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public long HighestAdvertisedRevision { get; init; }
    public string? HighestAdvertisedHash { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public long PublicationSequence { get; init; }
    public string? PointerDigest { get; init; }
    public CurrentPointer? RemotePointer { get; init; }
    public DateTimeOffset? LastClientCheckedAt { get; init; }
    public string? ETag { get; init; }
}

/// <summary>
/// UI-thread facade. File/network work is serialized on a worker. Pump applies completed snapshots.
/// Constructor reads existing local state once; Refresh never runs without explicit remote opt-in.
/// </summary>
public sealed class CalendarWorkspace : IDisposable
{
    private const int MaximumPointerBytes = 64 * 1024;
    private readonly AtomicJsonStore store;
    private readonly BoundedFeedTransport transport;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeSpan deadline;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentQueue<Action> completions = new();
    private readonly object queueLock = new();
    private Task tail = Task.CompletedTask;
    private WorkspaceCache cache;
    private WorkspaceCache acceptedCache;
    private FeedDocument workingFeed = EmptyFeed();
    private FeedDocument acceptedFeed = EmptyFeed();
    private readonly FeedDocument example;
    private int pending;
    private int refreshQueued;
    private bool disposed;
    private long stateVersion;
    private DateTimeOffset? lastRefreshAttempt;
    private DateTimeOffset nextAutomatic = DateTimeOffset.MinValue;
    private int consecutiveFailures;
    private CancellationTokenSource? activeRemoteRequest;
    private DateTimeOffset remoteNotBefore = DateTimeOffset.MinValue;

    public LocalState State { get; private set; }
    public FeedDocument Feed { get; private set; }
    public string FeedSource => Feed.IsExample ? "example" : acceptedCache.Source == "none" && Feed.ContentRevision > 0 ? "bundled" : acceptedCache.Source;
    public CurrentPointer? RemotePointer { get; private set; }
    // Only the UI-applied snapshot can prove which bytes the displayed feed came from.
    // An imported file must never borrow health from a previously accepted remote revision.
    public FeedHealth? ActiveHealth => !Feed.IsExample && acceptedCache.Source == "remote" &&
        acceptedCache.RemotePointer is { } pointer && Feed.ContentRevision == acceptedCache.ActiveContentRevision &&
        Feed.ContentRevision == pointer.Content.Revision &&
        string.Equals(acceptedCache.PayloadSha256, pointer.Content.Sha256, StringComparison.OrdinalIgnoreCase)
            ? pointer.Health : null;
    public string StatusCode { get; private set; } = "Ready";
    public bool IsBusy => Volatile.Read(ref pending) != 0;
    public bool HasUnsavedChanges { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset? LastClientCheckedAt { get; private set; }
    public Task BackgroundTask { get { lock (queueLock) return tail; } }

    public CalendarWorkspace(string configDirectory, byte[] exampleFeed) : this(configDirectory, exampleFeed, null) { }

    // Injection is for offline checks; production uses the redirect/cookie-free default transport.
    public CalendarWorkspace(string configDirectory, byte[] exampleFeed, HttpClient? httpClient, Func<DateTimeOffset>? clock = null, TimeSpan? totalDeadline = null, byte[]? bundledFeed = null)
    {
        store = new(configDirectory);
        transport = new(httpClient);
        utcNow = clock ?? (() => DateTimeOffset.UtcNow);
        deadline = totalDeadline ?? TimeSpan.FromSeconds(10);
        if (deadline <= TimeSpan.Zero || deadline > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(totalDeadline));
        example = ReadExample(exampleFeed);
        State = store.Load<LocalState>("state.json", s => FeedValidator.ValidateLocal(s).Count == 0, out var stateStatus) ?? new();
        cache = store.Load<WorkspaceCache>(Path.Combine("cache", "active.json"), ValidCache, out var cacheStatus) ?? new();
        RestoreFeed();
        if (workingFeed.ContentRevision == 0 && bundledFeed is not null)
        {
            var snapshot = CalendarJson.Parse<FeedDocument>(bundledFeed);
            ThrowErrors(FeedValidator.Validate(snapshot, false));
            if (snapshot.ContentRevision < 1) throw new InvalidDataException("BundledSnapshotRequiresRevision");
            workingFeed = snapshot;
            // Keep remote replay watermarks, but never attach an old cache proof to
            // fallback bytes. The bundle has observation evidence, not live health.
            cache = cache with { Source = "none", PayloadSha256 = null, ActiveContentRevision = 0 };
            StatusCode = "BundledSnapshot";
        }
        acceptedCache = cache;
        acceptedFeed = workingFeed;
        Feed = State.View.ExampleMode ? example : acceptedFeed;
        RemotePointer = cache.RemotePointer;
        LastClientCheckedAt = cache.LastClientCheckedAt;
        if (stateStatus is "BackupRecovered" or "CorruptPreserved") StatusCode = "State" + stateStatus;
        else if (cacheStatus is "BackupRecovered" or "CorruptPreserved") StatusCode = "Cache" + cacheStatus;
        else if (cache.PayloadSha256 is not null && workingFeed.ContentRevision == 0) StatusCode = "CachePayloadUnavailable";
    }

    private static FeedDocument EmptyFeed() => new() { SchemaVersion = 2, ContentRevision = 0, Region = "KR" };
    private static bool IsSha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool ValidCache(WorkspaceCache value) =>
        value.SchemaVersion == 1 && value.Source is "none" or "file" or "remote" &&
        value.PublicationSequence >= 0 && value.ActiveContentRevision >= 0 &&
        value.HighestAcceptedRemoteRevision >= 0 && value.HighestAdvertisedRevision >= value.HighestAcceptedRemoteRevision &&
        (value.PayloadSha256 is null ? value.ActiveContentRevision == 0 : IsSha(value.PayloadSha256) && value.ActiveContentRevision > 0) &&
        (value.HighestAdvertisedRevision == 0 ? value.HighestAdvertisedHash is null : IsSha(value.HighestAdvertisedHash)) &&
        (value.Source != "remote" || value.ActiveContentRevision <= value.HighestAcceptedRemoteRevision) &&
        (value.RemotePointer is null
            ? value.PublicationSequence == 0 && value.PointerDigest is null
            : IsSha(value.PointerDigest) && FeedValidator.Validate(value.RemotePointer, false).Count == 0 &&
              value.RemotePointer.PublicationSequence == value.PublicationSequence &&
              value.RemotePointer.Content.Revision == value.HighestAdvertisedRevision &&
              string.Equals(value.RemotePointer.Content.Sha256, value.HighestAdvertisedHash, StringComparison.OrdinalIgnoreCase));

    private static FeedDocument ReadExample(byte[] bytes)
    {
        if (bytes.Length > AtomicJsonStore.MaximumFileBytes) throw new InvalidDataException("ExampleTooLarge");
        var feed = CalendarJson.Parse<FeedDocument>(bytes);
        ThrowErrors(FeedValidator.Validate(feed, true));
        if (!feed.IsExample) throw new InvalidDataException("ExpectedExampleFeed");
        return feed;
    }

    private void RestoreFeed()
    {
        if (cache.PayloadSha256 is null) return;
        try
        {
            var loaded = LoadCachedPayload(cache.PayloadSha256);
            if (loaded.ContentRevision != cache.ActiveContentRevision) throw new InvalidDataException("CacheRevisionMismatch");
            workingFeed = loaded;
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
        // A previous accepted payload can be displayed while retaining the newer remote high-water.
        var backup = store.Load<WorkspaceCache>(Path.Combine("cache", "active.json.bak"), ValidCache, out _);
        if (backup?.PayloadSha256 is not { } previous) return;
        try
        {
            workingFeed = LoadCachedPayload(previous);
            if (workingFeed.ContentRevision != backup.ActiveContentRevision) throw new InvalidDataException("BackupCacheRevisionMismatch");
            cache = cache with { PayloadSha256 = previous, ActiveContentRevision = workingFeed.ContentRevision, Source = backup.Source };
            StatusCode = "CachePayloadRecovered";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
    }

    private FeedDocument LoadCachedPayload(string sha)
    {
        if (!IsSha(sha)) throw new InvalidDataException("InvalidCacheHash");
        var bytes = AtomicJsonStore.ReadBounded(store.Resolve(Path.Combine("cache", "content", sha + ".json")));
        if (!Hash(bytes).Equals(sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("CacheHashMismatch");
        var feed = CalendarJson.Parse<FeedDocument>(bytes);
        ThrowErrors(FeedValidator.Validate(feed, false));
        return feed;
    }

    public void UpdateState(LocalState next)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ThrowErrors(FeedValidator.ValidateLocal(next));
        next = next with
        {
            Profiles = next.Profiles.ToArray(), Entries = next.Entries.Select(e => e with { When = e.When with { EvidenceIds = e.When.EvidenceIds.ToArray() } }).ToArray(),
            Alerts = next.Alerts.ToArray(), SentAlertKeys = next.SentAlertKeys.ToArray()
        };
        var revokedRemotePermission = State.View.RemoteUpdatesEnabled && !next.View.RemoteUpdatesEnabled;
        State = next;
        if (revokedRemotePermission) CancelRemoteRequest();
        Feed = State.View.ExampleMode ? example : acceptedFeed;
        HasUnsavedChanges = true;
        var version = ++stateVersion;
        Revision++;
        Enqueue(async ct =>
        {
            await store.SaveAsync("state.json", next, ct);
            completions.Enqueue(() => { if (version == stateVersion) HasUnsavedChanges = false; });
            return "StateSaved";
        }, "SaveFailed", preserveOnDispose: true);
    }

    public void LoadExample(bool enabled)
    {
        UpdateState(State with { View = State.View with { ExampleMode = enabled } });
        StatusCode = enabled ? "ExampleModeNoPublicAlerts" : "ExampleModeDisabled";
    }

    public void ImportFile(string path, bool allowExample = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Enqueue(async ct =>
        {
            ct.ThrowIfCancellationRequested();
            var bytes = AtomicJsonStore.ReadBounded(Path.GetFullPath(path));
            var feed = CalendarJson.Parse<FeedDocument>(bytes);
            ThrowErrors(FeedValidator.Validate(feed, allowExample));
            if (feed.IsExample) throw new InvalidDataException("UseBundledExampleMode");
            if (feed.ContentRevision < 1) throw new InvalidDataException("ImportRequiresPublishedRevision");
            var sha = await store.PutContentAsync(bytes, ct);
            var next = cache with { PayloadSha256 = sha, ActiveContentRevision = feed.ContentRevision, Source = "file" };
            await store.SaveAsync(Path.Combine("cache", "active.json"), next, ct);
            cache = next;
            workingFeed = feed;
            return "FileImported";
        }, "ImportFailed");
    }

    public void Refresh(bool force = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!State.View.RemoteUpdatesEnabled) { StatusCode = "RemoteDisabled"; return; }
        Uri uri;
        try { uri = BoundedFeedTransport.GetCurrentUri(State.View.CurrentUrl); }
        catch (InvalidDataException) { StatusCode = "UntrustedCurrentAddress"; return; }
        CancellationTokenSource requestCancellation;
        lock (queueLock)
        {
            var now = utcNow();
            if (now < remoteNotBefore) { StatusCode = "RetryAfter"; return; }
            if (lastRefreshAttempt is { } previous && now - previous < TimeSpan.FromMinutes(5)) { StatusCode = "RefreshCooldown"; return; }
            if (!force && now < nextAutomatic) return;
            if (Interlocked.CompareExchange(ref refreshQueued, 1, 0) != 0) return;
            lastRefreshAttempt = now;
            requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            activeRemoteRequest = requestCancellation;
        }
        Enqueue(async ct =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, requestCancellation.Token);
                timeout.CancelAfter(deadline);
                var result = await RefreshCore(uri, timeout.Token);
                lock (queueLock)
                {
                    consecutiveFailures = 0;
                    nextAutomatic = utcNow().AddHours(1).AddMinutes(Random.Shared.NextDouble() * 10);
                }
                return result;
            }
            catch (Exception ex)
            {
                lock (queueLock)
                {
                    consecutiveFailures++;
                    var now = utcNow();
                    var delay = consecutiveFailures == 1 ? TimeSpan.FromMinutes(15) : consecutiveFailures == 2 ? TimeSpan.FromHours(1) : TimeSpan.FromHours(6);
                    if (ex is FeedHttpException { Status: 429, RetryAfter: { } retry } && retry > TimeSpan.Zero)
                    {
                        remoteNotBefore = now.Add(retry > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : retry);
                        if (remoteNotBefore > now.Add(delay)) delay = remoteNotBefore - now;
                    }
                    nextAutomatic = now.Add(delay);
                }
                throw;
            }
            finally
            {
                lock (queueLock)
                {
                    if (ReferenceEquals(activeRemoteRequest, requestCancellation)) activeRemoteRequest = null;
                    requestCancellation.Dispose();
                }
                Interlocked.Exchange(ref refreshQueued, 0);
            }
        }, "RefreshFailed");
    }

    private async Task<string> RefreshCore(Uri uri, CancellationToken ct)
    {
        var response = await transport.GetAsync(uri, MaximumPointerBytes, cache.ETag, ct);
        var pointer = response.NotModified ? cache.RemotePointer ?? throw new InvalidDataException("UnexpectedNotModified") : CalendarJson.Parse<CurrentPointer>(response.Bytes!);
        ThrowErrors(FeedValidator.Validate(pointer, false));
        var digest = response.NotModified ? cache.PointerDigest! : Hash(response.Bytes!);
        if (pointer.PublicationSequence < cache.PublicationSequence) throw new InvalidDataException("PublicationDowngrade");
        if (pointer.PublicationSequence == cache.PublicationSequence && cache.PointerDigest is not null && digest != cache.PointerDigest) throw new InvalidDataException("PublicationConflict");
        if (pointer.Content.Revision < cache.HighestAcceptedRemoteRevision || pointer.Content.Revision < cache.HighestAdvertisedRevision) throw new InvalidDataException("ContentDowngrade");
        if (pointer.Content.Revision == cache.HighestAdvertisedRevision && cache.HighestAdvertisedHash is not null && !pointer.Content.Sha256.Equals(cache.HighestAdvertisedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ContentRevisionConflict");
        var payloadUri = BoundedFeedTransport.GetPayloadUri(pointer.Content.Path);
        var observed = cache with
        {
            PublicationSequence = pointer.PublicationSequence, PointerDigest = digest, RemotePointer = pointer,
            HighestAdvertisedRevision = pointer.Content.Revision, HighestAdvertisedHash = pointer.Content.Sha256,
            LastClientCheckedAt = utcNow(), ETag = response.NotModified ? response.ETag ?? cache.ETag : response.ETag
        };
        // Commit advertised health independently. It does not claim the new payload was applied.
        await store.SaveAsync(Path.Combine("cache", "active.json"), observed, ct);
        cache = observed;
        if (cache.Source == "remote" && cache.ActiveContentRevision == pointer.Content.Revision && cache.PayloadSha256?.Equals(pointer.Content.Sha256, StringComparison.OrdinalIgnoreCase) == true && workingFeed.ContentRevision == pointer.Content.Revision)
            return response.NotModified ? "RemoteNotModified" : "RemoteUnchanged";
        byte[] bytes;
        var local = store.Resolve(Path.Combine("cache", "content", pointer.Content.Sha256.ToLowerInvariant() + ".json"));
        byte[]? cachedBytes = null;
        if (File.Exists(local))
        {
            try
            {
                var candidate = AtomicJsonStore.ReadBounded(local);
                if (candidate.Length == pointer.Content.Bytes && Hash(candidate).Equals(pointer.Content.Sha256, StringComparison.OrdinalIgnoreCase)) cachedBytes = candidate;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        bytes = cachedBytes ?? (await transport.GetAsync(payloadUri, AtomicJsonStore.MaximumFileBytes, null, ct)).Bytes ?? throw new InvalidDataException("UnexpectedNotModified");
        if (bytes.Length != pointer.Content.Bytes) throw new InvalidDataException("PayloadLengthMismatch");
        if (!Hash(bytes).Equals(pointer.Content.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("PayloadHashMismatch");
        var feed = CalendarJson.Parse<FeedDocument>(bytes);
        ThrowErrors(FeedValidator.Validate(feed, false));
        if (feed.ContentRevision != pointer.Content.Revision || feed.SchemaVersion != pointer.Content.SchemaVersion || feed.Region != pointer.Content.Region) throw new InvalidDataException("PayloadReferenceMismatch");
        var sha = await store.PutContentAsync(bytes, ct);
        var accepted = cache with { PayloadSha256 = sha, ActiveContentRevision = feed.ContentRevision, HighestAcceptedRemoteRevision = feed.ContentRevision, Source = "remote" };
        await store.SaveAsync(Path.Combine("cache", "active.json"), accepted, ct);
        cache = accepted;
        workingFeed = feed;
        return "RemoteUpdated";
    }

    private void Enqueue(Func<CancellationToken, Task<string>> operation, string failureCode, bool preserveOnDispose = false)
    {
        Interlocked.Increment(ref pending);
        lock (queueLock)
        {
            tail = tail.ContinueWith(async _ =>
            {
                string status;
                try
                {
                    var token = preserveOnDispose ? CancellationToken.None : lifetime.Token;
                    token.ThrowIfCancellationRequested();
                    status = await operation(token);
                }
                catch (OperationCanceledException) { status = lifetime.IsCancellationRequested ? "Cancelled" : "DeadlineExceeded"; }
                catch (FeedHttpException ex) { status = "Http" + ex.Status; }
                catch (InvalidDataException ex) { status = ex.Message.Length <= 100 ? ex.Message : failureCode; }
                catch (JsonException) { status = "InvalidJson"; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or ArgumentException) { status = failureCode; }
                catch (Exception) { status = failureCode; }
                var snapshot = workingFeed;
                var cacheSnapshot = cache;
                var pointer = cache.RemotePointer;
                var checkedAt = cache.LastClientCheckedAt;
                completions.Enqueue(() =>
                {
                    acceptedFeed = snapshot;
                    acceptedCache = cacheSnapshot;
                    Feed = State.View.ExampleMode ? example : snapshot;
                    RemotePointer = pointer;
                    LastClientCheckedAt = checkedAt;
                    StatusCode = status;
                    Revision++;
                });
                Interlocked.Decrement(ref pending);
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    public void Pump()
    {
        if (disposed) return;
        while (completions.TryDequeue(out var apply)) apply();
    }

    public async Task FlushAsync()
    {
        Task task;
        lock (queueLock) task = tail;
        await task.ConfigureAwait(false);
    }

    private static void ThrowErrors(IReadOnlyList<string> errors)
    {
        if (errors.Count != 0) throw new InvalidDataException("ValidationFailed:" + errors[0]);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        // Do not block the game/UI thread on IO during unload.
        Task task;
        lock (queueLock) task = tail;
        _ = task.ContinueWith(_ =>
        {
            lock (queueLock)
            {
                // A queued request cancelled before entering its operation never reaches that
                // operation's finally block. Drain then release its linked cancellation source.
                activeRemoteRequest?.Dispose();
                activeRemoteRequest = null;
            }
            transport.Dispose();
            lifetime.Dispose();
        }, TaskScheduler.Default);
    }

    private void CancelRemoteRequest()
    {
        lock (queueLock) activeRemoteRequest?.Cancel();
    }
}
