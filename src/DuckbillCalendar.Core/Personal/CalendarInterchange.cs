using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DuckbillCalendar.Core;

public sealed record CalendarImport(IReadOnlyList<PersonalEntry> Entries, int Duplicates, int Unsupported);

/// <summary>RFC 5545 point/period events. Recurrence and custom time-zone rules are never guessed.</summary>
public static class CalendarInterchange
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static string Uid(PersonalEntry entry) => !string.IsNullOrEmpty(entry.CalendarUid) ? entry.CalendarUid : entry.Id + "@duckbillcalendar.local";

    public static string Export(LocalState state, DateTimeOffset now)
    {
        var result = new StringBuilder();
        void Line(string text)
        {
            var bytes = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); bytes = 1; }
                result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
            }
            result.Append("\r\n");
        }
        Line("BEGIN:VCALENDAR"); Line("VERSION:2.0"); Line("PRODID:-//DuckbillCalendar//Calendar Exchange//KO"); Line("CALSCALE:GREGORIAN");
        foreach (var entry in state.Entries.Where(e => e.ProfileId == state.ActiveProfileId))
        {
            Line("BEGIN:VEVENT"); Line("UID:" + Escape(Uid(entry)));
            Line("DTSTAMP:" + now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", Invariant));
            Line("SUMMARY:" + Escape(entry.Title));
            if (entry.Note.Length > 0) Line("DESCRIPTION:" + Escape(entry.Note));
            Line(Boundary("DTSTART", entry.When));
            if (entry.End is { } end) Line(Boundary("DTEND", end));
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return result.ToString();
    }

    private static string Boundary(string name, CalendarBoundary value) => value.Kind == "date"
        ? name + ";VALUE=DATE:" + CalendarEngine.BoundaryDate(value)!.Value.ToString("yyyyMMdd", Invariant)
        : name + ":" + CalendarEngine.StartInstant(value)!.Value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", Invariant);

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n")
        .Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\') { result.Append(text[i]); continue; }
            if (++i == text.Length) throw new InvalidDataException("InvalidCalendarText");
            result.Append(text[i] switch { 'n' or 'N' => '\n', '\\' => '\\', ';' => ';', ',' => ',', _ => throw new InvalidDataException("InvalidCalendarText") });
        }
        return result.ToString();
    }

    public static CalendarImport Import(string text, LocalState state)
    {
        // Folding is removed before interpreting content; UTF-8 is decoded strictly by the file boundary.
        text = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
        var lines = text.Split('\n').Where(l => l.Length > 0).ToArray();
        if (lines.Length < 3 || lines[0] != "BEGIN:VCALENDAR" || lines[^1] != "END:VCALENDAR" || !lines.Contains("VERSION:2.0"))
            throw new InvalidDataException("InvalidCalendarFile");
        var entries = new List<PersonalEntry>();
        var known = state.Entries.Where(e => e.ProfileId == state.ActiveProfileId).Select(Uid).ToHashSet(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var fields = new Dictionary<string, (string Parameters, string Value)>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0; var unsupported = 0; var eventCount = 0; var rejected = false;
        foreach (var line in lines)
        {
            var colon = ContentColon(line);
            if (colon < 1) throw new InvalidDataException("InvalidCalendarLine");
            var header = line[..colon]; var value = line[(colon + 1)..];
            var semicolon = header.IndexOf(';');
            var name = (semicolon < 0 ? header : header[..semicolon]).ToUpperInvariant();
            if (name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count == 0 && value != "VCALENDAR" || stack.Count > 0 && value == "VCALENDAR") throw new InvalidDataException("InvalidCalendarNesting");
                if (value == "VEVENT")
                {
                    if (stack.Count != 1 || stack.Peek() != "VCALENDAR" || ++eventCount > 2000) throw new InvalidDataException("CalendarEventLimit");
                    fields.Clear(); rejected = false;
                }
                stack.Push(value); continue;
            }
            if (name.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (!stack.TryPop(out var component) || component != value) throw new InvalidDataException("InvalidCalendarNesting");
                if (value != "VEVENT") continue;
                if (rejected || fields.ContainsKey("RRULE") || fields.ContainsKey("RDATE") || fields.ContainsKey("EXDATE") || fields.ContainsKey("RECURRENCE-ID") || fields.GetValueOrDefault("STATUS").Value == "CANCELLED")
                { unsupported++; continue; }
                try
                {
                    if (!fields.TryGetValue("UID", out var uidField) || !fields.TryGetValue("DTSTART", out var startField) || !fields.TryGetValue("SUMMARY", out var summary))
                        throw new InvalidDataException("CalendarRequiredField");
                    var uid = Unescape(uidField.Value); var title = Unescape(summary.Value).Trim();
                    if (uid.Length is < 1 or > 512 || title.Length is < 1 or > 80) throw new InvalidDataException("CalendarTextLimit");
                    var start = ParseBoundary(startField); CalendarBoundary? end = null;
                    if (fields.TryGetValue("DTEND", out var endField)) end = ParseBoundary(endField);
                    if (fields.ContainsKey("DURATION")) throw new InvalidDataException("CalendarDurationUnsupported");
                    if (end is not null && (end.Kind != start.Kind || (end.Kind == "instant" ? CalendarEngine.StartInstant(end) <= CalendarEngine.StartInstant(start)
                        : CalendarEngine.BoundaryDate(end) <= CalendarEngine.BoundaryDate(start)))) throw new InvalidDataException("CalendarInvalidPeriod");
                    var note = fields.TryGetValue("DESCRIPTION", out var description) ? Unescape(description.Value) : "";
                    if (fields.TryGetValue("LOCATION", out var location)) note += (note.Length > 0 ? "\n" : "") + Unescape(location.Value);
                    if (note.Length > 1000) throw new InvalidDataException("CalendarTextLimit");
                    if (!known.Add(uid)) { duplicates++; continue; }
                    var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state.ActiveProfileId + "\n" + uid)));
                    entries.Add(new() { Id = "ics:" + hash, ProfileId = state.ActiveProfileId, Title = title, Note = note, When = start, End = end, CalendarUid = uid });
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException or TimeZoneNotFoundException or InvalidTimeZoneException)
                { unsupported++; }
                continue;
            }
            if (stack.Count == 0) throw new InvalidDataException("InvalidCalendarNesting");
            if (stack.Peek() != "VEVENT") continue;
            if (name is "UID" or "DTSTART" or "DTEND" or "SUMMARY" or "DESCRIPTION" or "LOCATION" or "STATUS" or "DURATION" or "RRULE" or "RDATE" or "EXDATE" or "RECURRENCE-ID")
                if (!fields.TryAdd(name, (semicolon < 0 ? "" : header[(semicolon + 1)..], value))) rejected = true;
        }
        if (stack.Count != 0 || eventCount == 0) throw new InvalidDataException("InvalidCalendarFile");
        if (state.Entries.Count + entries.Count > 2000) throw new InvalidDataException("CalendarEntryLimit");
        return new(entries, duplicates, unsupported);
    }

    private static int ContentColon(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        { if (line[i] == '"') quoted = !quoted; else if (line[i] == ':' && !quoted) return i; }
        return -1;
    }

    private static CalendarBoundary ParseBoundary((string Parameters, string Value) field)
    {
        var parameters = field.Parameters.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToArray();
        if (parameters.Any(p => p.Length != 2)) throw new InvalidDataException("InvalidCalendarParameters");
        var dateOnly = parameters.Any(p => p[0].Equals("VALUE", StringComparison.OrdinalIgnoreCase) && p[1].Equals("DATE", StringComparison.OrdinalIgnoreCase));
        if (parameters.Any(p => p[0].Equals("VALUE", StringComparison.OrdinalIgnoreCase) && p[1] is not ("DATE" or "DATE-TIME"))) throw new InvalidDataException("InvalidCalendarValue");
        if (dateOnly)
        {
            if (!DateOnly.TryParseExact(field.Value, "yyyyMMdd", Invariant, DateTimeStyles.None, out var day) || day.Year is < 2020 or > 2100) throw new InvalidDataException("InvalidCalendarDate");
            return CalendarBoundary.Date(day);
        }
        var utc = field.Value.EndsWith('Z');
        if (!DateTime.TryParseExact(utc ? field.Value[..^1] : field.Value, "yyyyMMdd'T'HHmmss", Invariant, DateTimeStyles.None, out var local) || local.Year is < 2020 or > 2100)
            throw new InvalidDataException("InvalidCalendarTime");
        if (utc) return CalendarBoundary.Instant(new DateTimeOffset(local, TimeSpan.Zero));
        var zoneId = parameters.FirstOrDefault(p => p[0].Equals("TZID", StringComparison.OrdinalIgnoreCase))?[1].Trim('"');
        // A floating time has no portable instant. Require an explicit zone rather than use this PC's zone.
        if (string.IsNullOrEmpty(zoneId)) throw new InvalidDataException("CalendarTimezoneRequired");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) throw new InvalidDataException("CalendarAmbiguousTime");
        return CalendarBoundary.Instant(new DateTimeOffset(local, zone.GetUtcOffset(local)));
    }
}
