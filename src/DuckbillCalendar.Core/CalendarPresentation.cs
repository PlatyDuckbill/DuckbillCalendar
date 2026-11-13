using System.Text.RegularExpressions;

namespace DuckbillCalendar.Core;

public enum CalendarDisplayGroup { Single, Update, PurchaseCampaign }

/// <summary>A reversible display grouping. Members retain every original event, phase and boundary.</summary>
public sealed record CalendarDisplayEntry(string Key, CalendarDisplayGroup Group, string Edge, IReadOnlyList<CalendarItem> Members)
{
    public CalendarItem Primary => Group == CalendarDisplayGroup.Update ? Members.First(i => i.Event.Category == "maintenance") : Members[0];
    public bool Ongoing => Members.All(i => i.Ongoing);
    public string Category => Group switch
    {
        CalendarDisplayGroup.Update => "patch", CalendarDisplayGroup.PurchaseCampaign => "sale",
        _ => Primary.Event.Category == "notice" ? "competition" : Primary.Event.Category
    };
}

public static partial class CalendarPresentation
{
    private static readonly HashSet<string> PurchaseCampaign = ["kr:event:771", "kr:curated:v8-reservation-items", "kr:curated:v8-ce-discount"];

    public static IReadOnlyList<string> RelatedEventIds(IEnumerable<CalendarEvent> events, IEnumerable<string> selectedIds)
    {
        var all = events.ToArray();
        var result = selectedIds.ToHashSet(StringComparer.Ordinal);
        if (result.Overlaps(PurchaseCampaign)) result.UnionWith(all.Where(e => !e.IsPersonal && PurchaseCampaign.Contains(e.Id)).Select(e => e.Id));
        // Relationship identity is separate from display grouping. A planned update may
        // retain separate cards, while its uniquely proven patch/downtime are still related.
        var versions = all.Where(e => result.Contains(e.Id)).Select(e => Version(e.Id)).Where(v => v is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var patch in all.Where(e => !e.IsPersonal && e.Category == "patch" && Version(e.Id) is { } version && versions.Contains(version)))
        foreach (var phase in patch.Phases.Where(p => p.Point && p.Start.Kind == "date"))
        {
            var date = CalendarEngine.BoundaryDate(phase.Start);
            var proof = phase.Start.EvidenceIds.Concat(phase.End.EvidenceIds).ToHashSet(StringComparer.Ordinal);
            var matches = all.Where(e => !e.IsPersonal && e.Category == "maintenance" && Version(e.Id) == Version(patch.Id))
                .SelectMany(e => e.Phases.Select(p => (Event: e, Phase: p)))
                .Where(m => date is not null && !m.Phase.Point && CalendarEngine.BoundaryDate(m.Phase.Start) == date && CalendarEngine.BoundaryDate(m.Phase.End) == date &&
                    m.Phase.Start.EvidenceIds.Concat(m.Phase.End.EvidenceIds).Any(proof.Contains)).ToArray();
            if (matches.Length == 1 && (result.Contains(patch.Id) || result.Contains(matches[0].Event.Id)))
            { result.Add(patch.Id); result.Add(matches[0].Event.Id); }
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<CalendarDisplayEntry> Build(IEnumerable<CalendarItem> items, DateOnly date)
    {
        var units = items.Where(i => i.Date == date).GroupBy(i => (i.Event.Id, i.Phase.Id, i.Ongoing)).Select(group =>
        {
            var members = group.ToArray();
            var edge = members.Any(i => i.Edge == "start") && members.Any(i => i.Edge == "end") ? "period" : members[0].Edge;
            return new CalendarDisplayEntry(members[0].Event.Id + "." + members[0].Phase.Id, CalendarDisplayGroup.Single, edge, members);
        }).ToList();

        // A date-only patch marker and its same-day downtime are one planning unit only
        // when the structured KR version and an actual evidence record both agree.
        foreach (var patch in units.Where(u => !u.Primary.Event.IsPersonal && u.Primary.Event.Category == "patch" && u.Primary.Phase.Point &&
                     u.Primary.Phase.Start.Kind == "date" && Version(u.Primary.Event.Id) is not null).ToArray())
        {
            var matches = units.Where(u => !u.Primary.Event.IsPersonal && u.Primary.Event.Category == "maintenance" && !u.Ongoing &&
                Version(u.Primary.Event.Id) == Version(patch.Primary.Event.Id) &&
                CalendarEngine.BoundaryDate(u.Primary.Phase.Start) == date && CalendarEngine.BoundaryDate(u.Primary.Phase.End) == date &&
                Confirmed(u) && Confirmed(patch) && Evidence(u).Intersect(Evidence(patch), StringComparer.Ordinal).Any()).ToArray();
            if (matches.Length != 1) continue;
            var maintenance = matches[0]; units.Remove(patch); units.Remove(maintenance);
            units.Add(new("update." + Version(patch.Primary.Event.Id) + "." + date, CalendarDisplayGroup.Update, "period", maintenance.Members.Concat(patch.Members).ToArray()));
        }

        // This relationship was reviewed against the official purchase/benefit table.
        // Never merge early access, formal release, unrelated campaigns, or different edges.
        foreach (var purchase in units.Where(u => !u.Primary.Event.IsPersonal && PurchaseCampaign.Contains(u.Primary.Event.Id) && Confirmed(u)).GroupBy(u => (u.Edge, u.Ongoing)).ToArray())
        {
            var group = purchase.ToArray(); if (group.Length < 2) continue;
            var instants = group.SelectMany(u => u.Members).Select(i => CalendarEngine.StartInstant(purchase.Key.Edge == "end" ? i.Phase.End : i.Phase.Start))
                .Where(t => t.HasValue).Distinct().ToArray();
            if (!purchase.Key.Ongoing && instants.Length > 1) continue;
            foreach (var unit in group) units.Remove(unit);
            units.Add(new("campaign.v8-purchase." + purchase.Key.Edge, CalendarDisplayGroup.PurchaseCampaign, purchase.Key.Edge, group.SelectMany(u => u.Members).ToArray()));
        }
        var direct = units.Where(u => !u.Ongoing).OrderBy(Priority).ThenBy(Clock).ThenBy(u => u.Key, StringComparer.Ordinal);
        var ongoing = units.Where(u => u.Ongoing).OrderBy(u => u.Members.Select(i => CalendarEngine.BoundaryDate(i.Phase.End)?.DayNumber ?? int.MaxValue).Min())
            .ThenBy(Priority).ThenBy(u => u.Key, StringComparer.Ordinal);
        return direct.Concat(ongoing).ToArray();
    }
    private static int Priority(CalendarDisplayEntry unit) => unit.Primary.Event.IsPersonal ? 0 : unit.Edge == "end" ? 1 : unit.Category switch
    {
        "maintenance" => 2, "patch" => 3, "pvp-season" or "pvp-series" or "reward" => 4,
        "competition" => 5, "sale" or "discount" => 7, _ => 6
    };
    private static DateTimeOffset Clock(CalendarDisplayEntry unit) => unit.Members.Select(i => CalendarEngine.StartInstant(unit.Edge == "end" ? i.Phase.End : i.Phase.Start) ?? DateTimeOffset.MaxValue).Min();
    private static bool Confirmed(CalendarDisplayEntry unit) => unit.Members.All(i =>
        i.Event.Note?.Contains("공식 원문 재검토 대기", StringComparison.Ordinal) != true &&
        BoundaryConfirmed(i.Phase.Start) && BoundaryConfirmed(i.Phase.End));
    private static bool BoundaryConfirmed(CalendarBoundary boundary) => boundary.Kind is "unknown" or "condition" || boundary.Certainty == "confirmed";
    private static IEnumerable<string> Evidence(CalendarDisplayEntry unit) => unit.Members.SelectMany(i => i.Phase.Start.EvidenceIds.Concat(i.Phase.End.EvidenceIds));
    private static string? Version(string id)
    {
        var match = VersionId().Match(id); return match.Success ? match.Groups["version"].Value : null;
    }
    [GeneratedRegex(@"^kr:(?:patch|maintenance:game):v(?<version>\d+(?:-\d+)+)(?::|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionId();
}
