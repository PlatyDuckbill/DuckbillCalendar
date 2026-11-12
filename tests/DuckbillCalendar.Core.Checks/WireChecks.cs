using System.Text.Json;
using System.Text.Json.Nodes;
using DuckbillCalendar.Core;

internal static class WireChecks
{
    public static void Run(Action<bool, string> check)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new FeedDocument(), CalendarJson.Options);
        check(bytes.Contains((byte)'\n') && !bytes.Contains((byte)'\r'), "Canonical LF JSON bytes are stable across Windows builds and Linux publication");
        var bom = new byte[bytes.Length + 3];
        bom[0] = 0xEF; bom[1] = 0xBB; bom[2] = 0xBF;
        bytes.CopyTo(bom, 3);
        check(CalendarJson.Parse<FeedDocument>(bom).Region == "KR", "UTF8 BOM from manual editor accepted without changing hash input");
        foreach (var property in new[] { "region", "events" })
        {
            var node = JsonNode.Parse(bytes)!.AsObject();
            node[property] = null;
            check(Reject<FeedDocument>(node), "Explicit null in nonnullable feed field rejected: " + property);
        }
        var eventNode = JsonSerializer.SerializeToNode(new FeedDocument { Events = [new() { Id = "kr:test", Title = "Test" }] }, CalendarJson.Options)!;
        eventNode["events"]![0]!["id"] = null;
        check(Reject<FeedDocument>(eventNode), "Explicit null ID rejected before domain validation can dereference it");
        var localNode = JsonSerializer.SerializeToNode(new LocalState(), CalendarJson.Options)!;
        localNode["view"] = null;
        check(Reject<LocalState>(localNode), "Explicit null settings object rejected");
        var pointerNode = JsonSerializer.SerializeToNode(new CurrentPointer(), CalendarJson.Options)!;
        pointerNode["content"] = null;
        check(Reject<CurrentPointer>(pointerNode), "Explicit null content reference rejected");
        var optional = new FeedDocument { Evidence = [new() { Id = "example", SourceId = "example", Url = null }] };
        var parsed = CalendarJson.Parse<FeedDocument>(JsonSerializer.SerializeToUtf8Bytes(optional, CalendarJson.Options));
        check(parsed.Evidence.Single().Url is null, "Nullable evidence field remains explicitly unknown");
    }

    private static bool Reject<T>(JsonNode node) where T : class
    {
        try { CalendarJson.Parse<T>(JsonSerializer.SerializeToUtf8Bytes(node)); return false; }
        catch (JsonException) { return true; }
    }
}
