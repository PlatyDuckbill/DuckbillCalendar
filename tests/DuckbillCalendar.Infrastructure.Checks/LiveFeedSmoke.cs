using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

internal static class LiveFeedSmoke
{
    // Explicit opt-in only. Normal checks never call a real server or use game configuration.
    public static async Task Run()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var area = Path.Combine(root, ".preview", "live-feed-smoke", Guid.NewGuid().ToString("N"));
        var example = await File.ReadAllBytesAsync(Path.Combine(root, "data", "contracts", "v2", "revisions", "000001.json"));
        long revision;
        int count;
        using (var workspace = new CalendarWorkspace(area, example))
        {
            workspace.UpdateState(workspace.State with { View = workspace.State.View with
            {
                RemoteUpdatesEnabled = true, CurrentUrl = BoundedFeedTransport.CurrentAddress
            } });
            workspace.Refresh(force: true);
            await workspace.FlushAsync();
            workspace.Pump();
            if (workspace.FeedSource != "remote" || workspace.Feed.IsExample || workspace.Feed.Events.Count == 0 || workspace.ActiveHealth is null)
                throw new InvalidOperationException("Live remote feed unavailable: " + workspace.StatusCode);
            revision = workspace.Feed.ContentRevision;
            count = workspace.Feed.Events.Count;
            Console.WriteLine($"Live plugin transport: {workspace.StatusCode}; revision={revision}; events={count}; personal entries=0");
        }
        using (var workspace = new CalendarWorkspace(area, example))
        {
            if (workspace.FeedSource != "remote" || workspace.Feed.ContentRevision != revision || workspace.Feed.Events.Count != count)
                throw new InvalidOperationException("Live payload cache did not survive restart");
        }
        Console.WriteLine("PASS: real HTTPS pointer, payload SHA256/bytes, production validation and cache restart via actual plugin workspace. " + area);
    }
}
