using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace DuckbillCalendar.Tools;

internal sealed record PhaseCandidate(string Label, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? UnresolvedEndText, string RawText);
internal sealed record EventCandidate(string Id, string Title, string SourceUrl, string NormalizedRecordHash, IReadOnlyList<PhaseCandidate> Phases);
internal sealed record CandidateDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string Type { get; init; } = "review-candidates";
    public string SourceId { get; init; } = "kr-events";
    public string Source { get; init; } = EventPageParser.Source;
    public DateTimeOffset ParsedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FetchedAt { get; init; }
    public bool IsSynthetic { get; init; }
    public string ReviewStatus { get; init; } = "unreviewed";
    public bool CompleteCoverage { get; init; }
    public bool CanPublish { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<EventCandidate> Candidates { get; init; } = [];
}

internal static class EventPageParser
{
    public const string Source = "https://www.ff14.co.kr/news/event";
    public static async Task<CandidateDocument> ParseAsync(string html, DateTimeOffset? fetchedAt = null, bool strictAuto = false)
    {
        var document = await new HtmlParser().ParseDocumentAsync(html); // No scripts or resource loading.
        var candidates = new List<EventCandidate>();
        var errors = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var anchor in document.QuerySelectorAll("a[href*='/news/event/view/']"))
        {
            if (anchor.QuerySelector(".period") is null) continue; // Banners must not reserve real-card IDs.
            if (!Uri.TryCreate(new Uri(Source), anchor.GetAttribute("href") ?? "", out var url) ||
                url.Scheme != "https" || url.Host != "www.ff14.co.kr" || url.Port != 443 || url.UserInfo.Length != 0) continue;
            var idMatch = Regex.Match(url.AbsolutePath, @"^/news/event/view/(\d+)$", RegexOptions.CultureInvariant);
            if (!idMatch.Success) continue;
            var id = "kr:event:" + idMatch.Groups[1].Value;
            var title = Text(anchor.QuerySelector(".tit, .title, .subject, h4, h3")?.TextContent ?? "");
            if (string.IsNullOrWhiteSpace(title)) { errors.Add("MissingTitle:" + id); continue; }
            if (title.Length > 300) { errors.Add("TitleSizeLimit:" + id); continue; }
            var phases = new List<PhaseCandidate>();
            foreach (var term in anchor.QuerySelectorAll(".period .term"))
            {
                if (phases.Count >= 16) { errors.Add("PhaseCountLimit:" + id); break; }
                var label = Text(term.QuerySelector(".lbl")?.TextContent ?? "");
                var raw = Text(term.TextContent);
                if (label.Length > 120 || raw.Length > 2000) { errors.Add("PhaseTextLimit:" + id); continue; }
                var timeNodes = term.QuerySelectorAll("time[datetime]").ToArray();
                var times = timeNodes.Select(x => x.GetAttribute("datetime") ?? "").ToArray();
                if (times.Length > 2) { errors.Add("AmbiguousDateCount:" + id); continue; }
                if (strictAuto && term.QuerySelectorAll("time").Length != timeNodes.Length) errors.Add("MissingStructuredTimeAttribute:" + id);
                if (strictAuto && (times.Length == 0 || string.IsNullOrWhiteSpace(label))) errors.Add("UnsupportedAutoPeriodShape:" + id);
                DateTimeOffset? start = times.Length > 0 ? ParseKoreanMinute(times[0]) : null;
                DateTimeOffset? end = times.Length > 1 ? ParseKoreanMinute(times[1]) : null;
                if ((times.Length > 0 && start is null) || (times.Length > 1 && end is null)) errors.Add("DateParseFailure:" + id);
                if (start > end) errors.Add("ReversedRange:" + id);
                if (strictAuto && timeNodes.Any(node => !VisibleDateMatches(node.TextContent, node.GetAttribute("datetime") ?? ""))) errors.Add("VisibleDateMismatch:" + id);
                phases.Add(new(label, start, end, end is null ? raw : null, raw));
            }
            if (phases.Count == 0) errors.Add("MissingPeriods:" + id);
            var normalized = title + "\n" + string.Join("\n", phases.Select(p => p.RawText));
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            if (seen.TryGetValue(id, out var previous)) { if (previous != hash) errors.Add("ConflictingDuplicateCandidate:" + id); continue; }
            seen.Add(id, hash);
            candidates.Add(new(id, title, url.ToString(), hash, phases));
            if (candidates.Count > 1000) throw new InvalidDataException("CandidateCountLimit");
        }
        if (candidates.Count == 0) errors.Add("UnexpectedEmptyResult");
        return new() { FetchedAt = fetchedAt, IsSynthetic = document.DocumentElement.HasAttribute("data-synthetic"), Errors = errors, Candidates = candidates };
    }
    private static string Text(string value) => Regex.Replace(value, @"\s+", " ").Trim();
    private static bool VisibleDateMatches(string text, string attribute)
    {
        var match = Regex.Match(Text(text), @"^(?<y>\d{2}|\d{4})\.\s*(?<m>\d{1,2})\.\s*(?<d>\d{1,2})\s+(?<t>\d{2}:\d{2})$", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        if (year < 100) year += 2000;
        var visible = FormattableString.Invariant($"{year:D4}-{int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture):D2}-{int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture):D2} {match.Groups["t"].Value}");
        return ParseKoreanMinute(visible) is { } parsed && parsed == ParseKoreanMinute(attribute);
    }
    private static DateTimeOffset? ParseKoreanMinute(string value) =>
        DateTime.TryParseExact(value, ["yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(9)) : null;
}
