using System.Text.Json;
using DuckbillCalendar.Collect;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

try
{
    if (args.Length == 4 && args[0] == "merge-feed")
    {
        var previous = CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(args[1]));
        var next = CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(args[2]));
        var merged = AllSources.Merge(previous, next);
        if (FeedValidator.Validate(merged).Count != 0) throw new InvalidDataException("InvalidMergedFeed");
        await Collector.WriteNewAsync(args[3], JsonSerializer.SerializeToUtf8Bytes(merged, CalendarJson.Options));
        return 0;
    }
    if (args.Length == 4 && args[0] == "collect-all")
    {
        using var http = Collector.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var previous = args[2] == "-" ? null : CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(Path.GetFullPath(args[2])));
        await AllSources.CollectAsync(args[1], args[3], previous, http, deadline.Token);
        return 0;
    }
    if (args.Length == 3 && args[0] == "mark-all-health")
    {
        await AllSources.MarkHealthAsync(args[1], args[2]);
        return 0;
    }
    if (args.Length == 4 && args[0] == "collect-curated")
    {
        using var http = Collector.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var previous = args[3] == "-" ? null : CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(Path.GetFullPath(args[3])));
        var result = await CuratedSources.CollectAsync(args[1], args[2], previous, http, deadline.Token);
        Console.WriteLine($"Official mapped milestones: {result.Feed.Events.Count}; pending={result.PendingEventIds.Count}");
        return result.PendingEventIds.Count == 0 ? 0 : 1;
    }
    if (args.Length == 2 && args[0] == "collect-pages")
    {
        using var http = Collector.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await PagedEvents.CollectAsync(args[1], http, deadline.Token);
        var feed = await PagedEvents.PrepareAsync(args[1], null);
        await Collector.WriteNewAsync(Path.Combine(args[1], "feed.json"), JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        Console.WriteLine($"All linked ongoing event pages: {feed.Events.Count} events");
        return 0;
    }
    if (args.Length == 2 && args[0] == "collect-notices")
    {
        using var http = Collector.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        var count = await NoticeCollector.CollectAsync(args[1], http, deadline.Token);
        Console.WriteLine($"Collected official notices: {count}");
        return 0;
    }
    if (args.Length == 4 && args[0] == "prepare-notices")
    {
        var previous = args[2] == "-" ? null : CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(Path.GetFullPath(args[2])));
        var feed = await NoticeCollector.PrepareAsync(args[1], previous);
        await Collector.WriteNewAsync(Path.GetFullPath(args[3]), JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        Console.WriteLine($"Prepared notices and explicit milestones: {feed.Events.Count}");
        return 0;
    }
    if (args.Length == 2 && args[0] == "collect")
    {
        using var http = Collector.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await Collector.CollectAsync(args[1], http, deadline.Token);
    }
    if (args.Length == 4 && args[0] == "prepare")
    {
        var output = Path.GetFullPath(args[3]);
        if (File.Exists(output)) throw new IOException("OutputAlreadyExists");
        var feed = await Collector.PrepareAsync(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        await Collector.WriteNewAsync(output, JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        Console.WriteLine($"Prepared reviewed feed: events={feed.Events.Count}; completeCoverage=false; alertsWithoutFreshness=false; no upload");
        return 0;
    }
    if (args.Length == 4 && args[0] == "prepare-auto")
    {
        var previous = args[2] == "-" ? null : CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(Path.GetFullPath(args[2])));
        var feed = await AutoPrepare.PrepareAsync(Path.GetFullPath(args[1]), previous);
        await Collector.WriteNewAsync(Path.GetFullPath(args[3]), JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options));
        Console.WriteLine($"Machine-validated feed: events={feed.Events.Count}; completeCoverage=false; no human/LLM approval claimed; nothing uploaded");
        return 0;
    }
    if (args.Length == 3 && args[0] == "verify-current")
    {
        var verified = await AutoPrepare.VerifyCurrentAsync(Path.GetFullPath(args[1]));
        await Collector.WriteNewAsync(Path.GetFullPath(args[2]), verified.Payload);
        Console.WriteLine("Previous current/payload hash, byte count, revision and production schema verified");
        return 0;
    }
    if (args.Length == 3 && args[0] == "mark-partial")
    {
        await AutoPrepare.MarkPartialAsync(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
        Console.WriteLine("Partial collection health recorded; completeCoverage=false; public timed alerts remain blocked");
        return 0;
    }
    if (args.Length == 1 && args[0] is "check" or "--check") { await CollectorChecks.RunAsync(); await AutoPrepareChecks.RunAsync(); await NoticeChecks.RunAsync(); await CuratedSourcesChecks.RunAsync(); await PagedEventsChecks.RunAsync(); await AllSourcesChecks.RunAsync(); return 0; }
    Console.WriteLine("CalendarDataCollect collect <new-collection-directory>");
    Console.WriteLine("CalendarDataCollect collect-all <curated-rules.json> <previous-feed-or-dash> <new-directory>");
    Console.WriteLine("CalendarDataCollect collect-pages <new-directory>");
    Console.WriteLine("CalendarDataCollect collect-notices <new-directory>");
    Console.WriteLine("CalendarDataCollect collect-curated <rules.json> <new-directory> <previous-feed-or-dash>");
    Console.WriteLine("CalendarDataCollect prepare <collection-directory> <review.json> <new-feed.json>");
    Console.WriteLine("CalendarDataCollect prepare-auto <collection-directory> <previous-feed-or-dash> <new-feed.json>");
    Console.WriteLine("CalendarDataCollect verify-current <publication-directory> <new-verified-payload.json>");
    Console.WriteLine("CalendarDataCollect mark-partial <collection-directory> <publication-directory>");
    Console.WriteLine("CalendarDataCollect check (offline checks only)");
    Console.WriteLine("Official ongoing events, bounded notices/patch catalog, and guarded release/PvP milestones. Explicit manual review is also supported. Nothing is uploaded by this CLI.");
    return args.Length == 0 ? 0 : 2;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine($"CollectionFailed:{ex.GetType().Name}:{ex.Message}");
    return 2;
}
