using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;

internal static class PublicationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var feed = new FeedDocument { ContentRevision = 7, GeneratedAt = DateTimeOffset.Parse("2026-11-13T00:00:00Z") };
        byte[] Bytes(FeedDocument value) => JsonSerializer.SerializeToUtf8Bytes(value, CalendarJson.Options);
        CurrentPointer Pointer(byte[] bytes) => new() { GeneratedAt = feed.GeneratedAt, PublicationSequence = 10,
            Content = new() { Revision = 7, Path = "revisions/000007.json", Bytes = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } };
        void Reject(CurrentPointer pointer, byte[] bytes, string label, bool allowExample = false)
        {
            try { FeedPublication.ReadPayload(pointer, bytes, allowExample); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { check(true, label); return; }
            check(false, label);
        }
        var bytes = Bytes(feed); var pointer = Pointer(bytes);
        check(FeedPublication.ReadPayload(pointer, bytes).ContentRevision == 7, "Shared publication verifier accepts exact validated bytes");
        Reject(pointer, bytes[..^1], "Shared verifier rejects incomplete payload bytes");
        Reject(pointer with { Content = pointer.Content with { Sha256 = new string('a', 64) } }, bytes, "Shared verifier rejects wrong SHA even for valid JSON");
        Reject(pointer with { Content = pointer.Content with { Revision = 8 } }, bytes, "Shared verifier rejects pointer/payload revision disagreement");
        Reject(pointer with { IsExample = true }, bytes, "Shared verifier rejects production/example disagreement even in example-enabled tools", true);
        var invalidFeed = Bytes(feed with { SchemaVersion = 99 });
        Reject(Pointer(invalidFeed), invalidFeed, "Shared verifier rejects unsupported payload schema with a matching hash");
        Reject(pointer with { Content = pointer.Content with { Path = "../escape.json" } }, bytes, "Shared verifier rejects unsupported publication paths before accepting bytes");
        var example = Bytes(feed with { IsExample = true });
        Reject(Pointer(example) with { IsExample = true }, example, "Shared verifier never enables example data in a production consumer");
        check(FeedPublication.ReadPayload(Pointer(example) with { IsExample = true }, example, true).IsExample, "Explicit offline example validation remains supported");
    }
}
