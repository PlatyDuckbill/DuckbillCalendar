using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuckbillCalendar.Core;

/// <summary>Preserves the tagged wire union instead of serializing every variant's default fields.</summary>
internal sealed class CalendarBoundaryConverter : JsonConverter<CalendarBoundary>
{
    public override CalendarBoundary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("BoundaryExpectedObject");
        var kind = String(root, "kind");
        string[] fields = kind switch
        {
            "instant" => ["kind", "value", "precision", "certainty", "evidenceIds"],
            "date" => ["kind", "value", "timezone", "certainty", "evidenceIds"],
            "condition" => ["kind", "condition", "displayText", "certainty", "evidenceIds"],
            "unknown" => ["kind", "reason"],
            _ => throw new JsonException("UnknownBoundaryKind")
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.Contains(property.Name, StringComparer.Ordinal)) throw new JsonException("MixedBoundaryField:" + property.Name);
            if (!seen.Add(property.Name)) throw new JsonException("DuplicateBoundaryField:" + property.Name);
        }
        foreach (var name in fields)
            if (!seen.Contains(name)) throw new JsonException("MissingBoundaryField:" + name);
        if (kind == "unknown") return new() { Kind = kind, Reason = String(root, "reason") };
        var ids = root.GetProperty("evidenceIds").Deserialize<string[]>(options) ?? throw new JsonException("NullEvidenceIds");
        var boundary = new CalendarBoundary { Kind = kind, Certainty = String(root, "certainty"), EvidenceIds = ids };
        return kind switch
        {
            "instant" => boundary with { Value = String(root, "value"), Precision = String(root, "precision") },
            "date" => boundary with { Value = String(root, "value"), Timezone = String(root, "timezone") },
            "condition" => boundary with { Condition = ReadCondition(root.GetProperty("condition")), DisplayText = String(root, "displayText") },
            _ => throw new JsonException("UnknownBoundaryKind")
        };
    }

    private static BoundaryCondition ReadCondition(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 3)
            throw new JsonException("InvalidConditionShape");
        return new() { Type = String(value, "type"), TargetId = String(value, "targetId"), Relation = String(value, "relation") };
    }

    private static string String(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException("ExpectedBoundaryString:" + name);
        return value.GetString()!;
    }

    public override void Write(Utf8JsonWriter writer, CalendarBoundary value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        switch (value.Kind)
        {
            case "instant":
                writer.WriteString("value", value.Value);
                writer.WriteString("precision", value.Precision);
                break;
            case "date":
                writer.WriteString("value", value.Value);
                writer.WriteString("timezone", value.Timezone);
                break;
            case "condition":
                writer.WritePropertyName("condition");
                JsonSerializer.Serialize(writer, value.Condition, options);
                writer.WriteString("displayText", value.DisplayText);
                break;
            case "unknown":
                writer.WriteString("reason", value.Reason ?? "not-announced");
                writer.WriteEndObject();
                return;
            default: throw new JsonException("UnknownBoundaryKind");
        }
        writer.WriteString("certainty", value.Certainty);
        writer.WritePropertyName("evidenceIds");
        JsonSerializer.Serialize(writer, value.EvidenceIds, options);
        writer.WriteEndObject();
    }
}
