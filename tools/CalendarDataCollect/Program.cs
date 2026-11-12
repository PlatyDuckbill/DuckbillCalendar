using System.Text.Json;
using DuckbillCalendar.Collect;
using DuckbillCalendar.Core;

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
    if (args.Length == 1 && args[0] is "check" or "--check") return await CollectorChecks.RunAsync();
    Console.WriteLine("CalendarDataCollect collect <new-collection-directory>");
    Console.WriteLine("CalendarDataCollect prepare <collection-directory> <review.json> <new-feed.json>");
    Console.WriteLine("CalendarDataCollect check (offline checks only)");
    Console.WriteLine("Fixed public event-list first page only. Collection requires explicit review before feed preparation. Nothing is uploaded.");
    return args.Length == 0 ? 0 : 2;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine($"CollectionFailed:{ex.GetType().Name}:{ex.Message}");
    return 2;
}
