using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuckbillCalendar.Core;

public sealed record BoundaryCondition
{
    public string Type { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string Relation { get; init; } = "before";
}

public sealed record CalendarBoundary
{
    public string Kind { get; init; } = "unknown";
    public string? Value { get; init; }
    public string? Precision { get; init; }
    public string? Certainty { get; init; }
    public string? Timezone { get; init; }
    public string? Reason { get; init; }
    public string? DisplayText { get; init; }
    public BoundaryCondition? Condition { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];

    public static CalendarBoundary Instant(DateTimeOffset value) => new()
    {
        Kind = "instant", Value = value.ToString("O", CultureInfo.InvariantCulture), Precision = "minute", Certainty = "confirmed"
    };
    public static CalendarBoundary Date(DateOnly value) => new()
    {
        Kind = "date", Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Timezone = "Asia/Seoul", Certainty = "confirmed"
    };
}

public sealed record EventPhase
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public CalendarBoundary Start { get; init; } = new();
    public CalendarBoundary End { get; init; } = new();
    public string EndInclusion { get; init; } = "exclusive";
    public bool Point { get; init; }
}

public sealed record CalendarEvent
{
    public string Id { get; init; } = "";
    public long Revision { get; init; } = 1;
    public string Category { get; init; } = "personal";
    public string Title { get; init; } = "";
    public string Lifecycle { get; init; } = "published";
    public IReadOnlyList<EventPhase> Phases { get; init; } = [];
    public bool IsPersonal { get; init; }
    public string? Note { get; init; }
    public string? ProfileId { get; init; }
}

public sealed record EvidenceRecord
{
    public string Id { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string? Url { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
    public string? SourceSnapshotSha256 { get; init; }
    public string? Note { get; init; }
}

public sealed record TargetRecord
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Resolution { get; init; } = "unresolved";
    public CalendarBoundary? ResolvedBoundary { get; init; }
}

public sealed record FrontlineRule
{
    public string Id { get; init; } = "";
    public string Region { get; init; } = "KR";
    public string Timezone { get; init; } = "Asia/Seoul";
    public string ResetLocalTime { get; init; } = "00:00";
    public string Status { get; init; } = "unverified";
    public string? ValidFrom { get; init; }
    public string? ValidUntil { get; init; }
    public string? AnchorDate { get; init; }
    public int AnchorIndex { get; init; }
    public IReadOnlyList<string> Sequence { get; init; } = [];
}

public sealed record FeedDocument
{
    public int SchemaVersion { get; init; } = 2;
    public bool IsExample { get; init; }
    public long ContentRevision { get; init; }
    public string Region { get; init; } = "KR";
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<EvidenceRecord> Evidence { get; init; } = [];
    public IReadOnlyList<CalendarEvent> Events { get; init; } = [];
    public IReadOnlyList<TargetRecord> Targets { get; init; } = [];
    public IReadOnlyList<string> Withdrawals { get; init; } = [];
    public IReadOnlyList<FrontlineRule> FrontlineRules { get; init; } = [];
}

public sealed record ContentReference
{
    public long Revision { get; init; }
    public int SchemaVersion { get; init; } = 2;
    public string Region { get; init; } = "KR";
    public string Path { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public int Bytes { get; init; }
}

public sealed record PendingChange
{
    public string Kind { get; init; } = "";
    public IReadOnlyList<string>? AffectedEventIds { get; init; }
    public DateTimeOffset FirstObservedAt { get; init; }
}

public sealed record SourceHealth
{
    public string Id { get; init; } = "";
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public DateTimeOffset? LastReviewedAt { get; init; }
    public string? NormalizedSourceHash { get; init; }
    public string? ApprovedSourceHash { get; init; }
    public string LastResult { get; init; } = "not-run";
    public bool CompleteCoverage { get; init; }
    public long? ComparedContentRevision { get; init; }
    public IReadOnlyList<PendingChange> PendingChanges { get; init; } = [];
}

public sealed record FeedHealth
{
    public IReadOnlyList<SourceHealth> Sources { get; init; } = [];
}

public sealed record CurrentPointer
{
    public int SchemaVersion { get; init; } = 2;
    public bool IsExample { get; init; }
    public string Region { get; init; } = "KR";
    public long PublicationSequence { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public ContentReference Content { get; init; } = new();
    public FeedHealth Health { get; init; } = new();
}

public static class CalendarJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        MaxDepth = 32,
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new CalendarBoundaryConverter() }
    };

    public static T Parse<T>(ReadOnlySpan<byte> data) where T : class
    {
        if (data.Length > 2 * 1024 * 1024) throw new JsonException("DocumentTooLarge");
        // Manual JSON saved by Windows editors may contain a UTF-8 BOM. Hash verification uses
        // the original bytes; only parsing skips this encoding marker.
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) data = data[3..];
        using var document = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("ExpectedObject");
        CheckDuplicateProperties(document.RootElement);
        string[] required = typeof(T) == typeof(FeedDocument)
            ? ["schemaVersion", "isExample", "contentRevision", "region", "generatedAt", "evidence", "events", "targets", "withdrawals", "frontlineRules"]
            : typeof(T) == typeof(CurrentPointer)
                ? ["schemaVersion", "isExample", "region", "publicationSequence", "generatedAt", "content", "health"]
                : typeof(T) == typeof(LocalState)
                    ? ["schemaVersion", "profiles", "activeProfileId", "entries", "alerts", "sentAlertKeys", "view"] : [];
        foreach (var name in required)
            if (!document.RootElement.TryGetProperty(name, out _)) throw new JsonException("MissingProperty:" + name);
        return document.RootElement.Deserialize<T>(Options) ?? throw new JsonException("NullDocument");
    }

    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("DuplicateProperty:" + property.Name);
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicateProperties(child);
    }
}
