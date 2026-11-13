using System.Net;

namespace DuckbillCalendar.Core;

/// <summary>Separates the page proving dates from the detail page a user wants to open.</summary>
public static class OfficialEventLinks
{
    public static bool IsOfficialUrl(string? value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Host is "www.ff14.co.kr" or "guide.ff14.co.kr";
    public static bool IsDetailUrl(string? value)
    {
        if (!IsOfficialUrl(value) || !Uri.TryCreate(value,UriKind.Absolute,out var uri)) return false;
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.StartsWith("/news/event/view/",StringComparison.OrdinalIgnoreCase) && !path[17..].All(char.IsAsciiDigit)) return false;
        return path.Length > 0 && path.ToLowerInvariant() is not ("/news" or "/news/event" or "/news/event/view" or "/news/notice" or "/news/patchnote" or "/lodestone");
    }
    public static string? Resolve(FeedDocument feed,CalendarEvent item,EventPhase? phase = null)
    {
        if (item.IsPersonal) return null;
        var preferred = phase is null ? new HashSet<string>() : phase.Start.EvidenceIds.Concat(phase.End.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        var referenced = item.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds)).ToHashSet(StringComparer.Ordinal);
        var links = new List<(int Rank,string Url)>();
        foreach (var proof in feed.Evidence.Where(e => referenced.Contains(e.Id)))
        {
            var scope = preferred.Count == 0 || preferred.Contains(proof.Id) ? 0 : 100;
            Add(proof.Url,scope);
            // Earlier valid feeds recorded the actual href beside a list-page evidence URL.
            // Preserve that proof URL; never claim the detail page itself was collected.
            const string marker = "개별 안내:";
            var note = proof.Note ?? ""; var start = note.IndexOf(marker,StringComparison.Ordinal);
            if (start < 0) continue;
            var tail = note[(start + marker.Length)..].TrimStart();
            var end = 0; while (end < tail.Length && !char.IsWhiteSpace(tail[end]) && tail[end] is not ('<' or '>' or '"' or '\'')) end++;
            Add(WebUtility.HtmlDecode(tail[..end]).TrimEnd('.',',',';',')',']'),scope + 5);
        }
        return links.OrderBy(c => c.Rank).ThenBy(c => c.Url,StringComparer.Ordinal).Select(c => c.Url).FirstOrDefault();

        void Add(string? value,int scope)
        {
            if (!IsDetailUrl(value) || !Uri.TryCreate(value,UriKind.Absolute,out var uri)) return;
            var path = uri.AbsolutePath.TrimEnd('/');
            var eventView = path.StartsWith("/news/event/view/",StringComparison.OrdinalIgnoreCase);
            if (eventView && item.Id.StartsWith("kr:event:",StringComparison.Ordinal))
            {
                var id = item.Id[9..];
                if (id.Length == 0 || !id.All(char.IsAsciiDigit) || path[17..] != id) return;
            }
            var rank = eventView ? 0 : uri.Host == "guide.ff14.co.kr" ? 30 : 10;
            links.Add((scope + rank,uri.AbsoluteUri));
        }
    }
}
