using System.Text.Json;
using DuckbillCalendar.Collect;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

try
{
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
    if (args.Length == 1 && args[0] is "check" or "--check") { await CollectorChecks.RunAsync(); return await AutoPrepareChecks.RunAsync(); }
    Console.WriteLine("CalendarDataCollect collect <new-collection-directory>");
    Console.WriteLine("CalendarDataCollect prepare <collection-directory> <review.json> <new-feed.json>");
    Console.WriteLine("CalendarDataCollect prepare-auto <collection-directory> <previous-feed-or-dash> <new-feed.json>");
    Console.WriteLine("CalendarDataCollect verify-current <publication-directory> <new-verified-payload.json>");
    Console.WriteLine("CalendarDataCollect mark-partial <collection-directory> <publication-directory>");
    Console.WriteLine("CalendarDataCollect check (offline checks only)");
    Console.WriteLine("Fixed public event-list first page only. Strict deterministic auto preparation and explicit manual review are separate paths. Nothing is uploaded.");
    return args.Length == 0 ? 0 : 2;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine($"CollectionFailed:{ex.GetType().Name}:{ex.Message}");
    return 2;
}
