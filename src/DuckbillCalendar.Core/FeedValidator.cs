using System.Text.RegularExpressions;

namespace DuckbillCalendar.Core;

/// <summary>Pure validation of untrusted deserialized values; never upgrades missing data to a fact.</summary>
public static partial class FeedValidator
{
    public const int MaxEvents = 10_000;
    public const int MaxPayloadBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> Categories = ["web-event", "in-game-event", "seasonal-event", "sale", "discount", "maintenance", "patch", "notice", "pvp-season", "pvp-series", "reward", "personal-demo"];

    public static IReadOnlyList<string> Validate(FeedDocument? feed, bool allowExample = false)
    {
        var errors = new List<string>();
        if (feed is null) return ["NullDocument"];
        if (feed.SchemaVersion != 2) errors.Add("UnsupportedSchema");
        if (feed.Region != "KR") errors.Add("RegionMismatch");
        if (feed.IsExample && !allowExample) errors.Add("ExampleFeedForbidden");
        if (feed.ContentRevision < 0) errors.Add("InvalidContentRevision");
        var evidence = List(feed.Evidence, "Evidence", 20_000, errors);
        var events = List(feed.Events, "Events", MaxEvents, errors);
        var targets = List(feed.Targets, "Targets", 512, errors);
        var rules = List(feed.FrontlineRules, "FrontlineRules", 256, errors);
        var withdrawals = List(feed.Withdrawals, "Withdrawals", MaxEvents, errors);
        if (feed.ContentRevision == 0)
        {
            if (events.Count + evidence.Count + targets.Count + rules.Count + withdrawals.Count != 0) errors.Add("RevisionZeroMustBeEmpty");
        }
        else if (feed.GeneratedAt == default) errors.Add("MissingGeneratedAt");
        var evidenceIds = Unique(evidence.Select(e => e.Id), "Evidence", errors);
        var eventIds = Unique(events.Select(e => e.Id), "Events", errors);
        var targetIds = Unique(targets.Select(e => e.Id), "Targets", errors);
        var targetMap = targets.Where(t => !string.IsNullOrEmpty(t.Id)).GroupBy(t => t.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Unique(withdrawals, "Withdrawals", errors);
        foreach (var value in evidence)
        {
            Text(value.SourceId, "Evidence.SourceId", 120, errors);
            Text(value.Note, "Evidence.Note", 1000, errors, false);
            if (value.Url is not null && !OfficialUrl(value.Url)) errors.Add("Evidence.InvalidUrl:" + value.Id);
            if (value.SourceSnapshotSha256 is not null && !IsHash(value.SourceSnapshotSha256)) errors.Add("Evidence.InvalidHash:" + value.Id);
            if (!feed.IsExample && (value.Url is null || value.ObservedAt is null || !IsHash(value.SourceSnapshotSha256)))
                errors.Add("Evidence.UnverifiedProvenance:" + value.Id);
        }
        foreach (var item in events)
        {
            if (item.IsPersonal || item.ProfileId is not null) errors.Add("PublicFeedContainsPersonalData:" + item.Id);
            Text(item.Title, "Event.Title", 200, errors);
            Text(item.Note, "Event.Note", 1000, errors, false);
            if (!Categories.Contains(item.Category ?? "")) errors.Add("UnknownCategory:" + item.Id);
            if (item.Revision < 1) errors.Add("InvalidEventRevision:" + item.Id);
            if (item.Lifecycle is not ("published" or "cancelled" or "postponed" or "draft")) errors.Add("UnknownLifecycle:" + item.Id);
            if (!feed.IsExample && (!(item.Id ?? "").StartsWith("kr:", StringComparison.Ordinal) || item.Lifecycle == "draft" || item.Category == "personal-demo")) errors.Add("UnpublishableEvent:" + item.Id);
            var phases = List(item.Phases, "Event.Phases:" + item.Id, 16, errors);
            if (phases.Count == 0) errors.Add("Event.EmptyPhases:" + item.Id);
            Unique(phases.Select(p => p.Id), "Event.Phases:" + item.Id, errors);
            foreach (var phase in phases)
            {
                Text(phase.Label, "Phase.Label", 120, errors);
                Boundary(phase.Start, "Start:" + item.Id + ":" + phase.Id, false, evidenceIds, targetMap, errors);
                Boundary(phase.End, "End:" + item.Id + ":" + phase.Id, false, evidenceIds, targetMap, errors);
                if (phase.EndInclusion is not ("exclusive" or "inclusive-unit")) errors.Add("InvalidEndInclusion:" + item.Id);
                var start = CalendarEngine.StartInstant(phase.Start);
                var end = CalendarEngine.EndExclusive(phase);
                if (start is not null && end is not null && end <= start && !phase.Point) errors.Add("ReversedOrEmptyInterval:" + item.Id);
                var startDay = CalendarEngine.BoundaryDate(phase.Start);
                var endDay = CalendarEngine.BoundaryDate(phase.End);
                if (startDay is not null && endDay is not null && startDay > endDay) errors.Add("ReversedDateInterval:" + item.Id);
                if (phase.Start?.Kind == "date" && phase.End?.Kind == "date" && phase.EndInclusion == "exclusive" && endDay <= startDay) errors.Add("EmptyDateInterval:" + item.Id);
                if (phase.Point && (phase.Start is null || phase.Start.Kind is not ("instant" or "date") || phase.End is null || phase.End.Kind != "unknown")) errors.Add("InvalidPoint:" + item.Id);
                if (phase.End?.Kind == "instant" && phase.EndInclusion == "inclusive-unit" && end is null) errors.Add("EndBoundaryOverflow:" + item.Id);
            }
        }
        foreach (var target in targets)
        {
            if (target.Kind is not ("patch-maintenance" or "season-end" or "patch-open")) errors.Add("UnknownTargetKind:" + target.Id);
            if (target.Resolution == "unresolved")
            {
                if (target.ResolvedBoundary is not null) errors.Add("UnresolvedTargetHasBoundary:" + target.Id);
            }
            else if (target.Resolution == "resolved")
            {
                if (target.ResolvedBoundary is null || target.ResolvedBoundary.Kind is not ("instant" or "date")) errors.Add("ResolvedTargetMissingBoundary:" + target.Id);
                Boundary(target.ResolvedBoundary, "Target:" + target.Id, false, evidenceIds, targetMap, errors);
            }
            else errors.Add("UnknownTargetResolution:" + target.Id);
        }
        foreach (var id in withdrawals)
            if (eventIds.Contains(id) && events.Any(e => e.Id == id && e.Lifecycle == "published")) errors.Add("WithdrawnEventStillPublished:" + id);
        Unique(rules.Select(r => r.Id), "FrontlineRules", errors);
        var verifiedRanges = new List<(DateOnly From, DateOnly Until, string Id)>();
        foreach (var rule in rules)
        {
            if (rule.Region != "KR" || rule.Timezone != "Asia/Seoul" || rule.ResetLocalTime != "00:00") errors.Add("Frontline.InvalidRegionOrReset:" + rule.Id);
            if (rule.Status is not ("verified" or "unverified")) errors.Add("Frontline.UnknownStatus:" + rule.Id);
            var sequence = List(rule.Sequence, "Frontline.Sequence:" + rule.Id, 64, errors);
            foreach (var name in sequence) Text(name, "Frontline.Name", 120, errors);
            if (rule.Status != "verified") continue;
            var from = CalendarEngine.ParseDate(rule.ValidFrom);
            var until = CalendarEngine.ParseDate(rule.ValidUntil);
            if (from is null || until is null || until <= from) errors.Add("Frontline.InvalidValidity:" + rule.Id);
            else verifiedRanges.Add((from.Value, until.Value, rule.Id));
            if (CalendarEngine.ParseDate(rule.AnchorDate) is null || sequence.Count == 0 || rule.AnchorIndex < 0 || rule.AnchorIndex >= sequence.Count) errors.Add("Frontline.InvalidAnchor:" + rule.Id);
        }
        var orderedRanges = verifiedRanges.OrderBy(r => r.From).ToArray();
        for (int i = 1; i < orderedRanges.Length; i++)
            if (orderedRanges.Take(i).Any(r => r.Until > orderedRanges[i].From)) errors.Add("Frontline.OverlappingRules:" + orderedRanges[i].Id);
        return errors;
    }

    public static IReadOnlyList<string> Validate(CurrentPointer? pointer, bool allowExample = false)
    {
        var errors = new List<string>();
        if (pointer is null) return ["NullPointer"];
        if (pointer.SchemaVersion != 2) errors.Add("UnsupportedPointerSchema");
        if (pointer.IsExample && !allowExample) errors.Add("ExamplePointerForbidden");
        if (pointer.Region != "KR") errors.Add("Pointer.RegionMismatch");
        if (pointer.PublicationSequence < 1 || pointer.GeneratedAt == default) errors.Add("Pointer.InvalidPublication");
        var content = pointer.Content;
        if (content is null) errors.Add("Pointer.NullContent");
        else
        {
            if (content.Revision < 1 || content.SchemaVersion != 2 || content.Region != pointer.Region) errors.Add("Pointer.InvalidContentReference");
            if (content.Bytes < 1 || content.Bytes > MaxPayloadBytes) errors.Add("Pointer.InvalidContentSize");
            if (!IsHash(content.Sha256)) errors.Add("Pointer.InvalidContentHash");
            if (content.Path is null || !PayloadPath().IsMatch(content.Path) || content.Path.Contains("..", StringComparison.Ordinal)) errors.Add("Pointer.InvalidPath");
        }
        if (pointer.Health is null) errors.Add("Pointer.NullHealth");
        else
        {
            var sources = List(pointer.Health.Sources, "Health.Sources", 32, errors);
            Unique(sources.Select(s => s.Id), "Health.Sources", errors);
            foreach (var source in sources)
            {
                Text(source.LastResult, "Health.LastResult", 32, errors);
                if (source.NormalizedSourceHash is not null && !IsHash(source.NormalizedSourceHash)) errors.Add("Health.InvalidNormalizedHash:" + source.Id);
                if (source.ApprovedSourceHash is not null && !IsHash(source.ApprovedSourceHash)) errors.Add("Health.InvalidApprovedHash:" + source.Id);
                if (source.ComparedContentRevision < 0) errors.Add("Health.InvalidComparedRevision:" + source.Id);
                if (source.CompleteCoverage && (source.ComparedContentRevision is null || source.LastAttemptAt is null || source.LastSuccessAt is null || source.LastResult == "not-run")) errors.Add("Health.UnprovenCoverage:" + source.Id);
                if (source.LastSuccessAt > source.LastAttemptAt && source.LastAttemptAt is not null) errors.Add("Health.SuccessAfterAttempt:" + source.Id);
                var changes = List(source.PendingChanges, "Health.PendingChanges:" + source.Id, 1000, errors);
                foreach (var change in changes)
                {
                    Text(change.Kind, "Pending.Kind", 40, errors);
                    if (change.FirstObservedAt == default) errors.Add("Pending.MissingObservedAt");
                    if (change.AffectedEventIds is not null) Unique(List(change.AffectedEventIds, "Pending.AffectedEventIds", MaxEvents, errors), "Pending.AffectedEventIds", errors);
                }
            }
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateLocal(LocalState? local)
    {
        var errors = new List<string>();
        if (local is null) return ["NullLocalState"];
        if (local.SchemaVersion != 1) errors.Add("UnsupportedLocalSchema");
        var profiles = List(local.Profiles, "Profiles", 20, errors);
        var profileIds = Unique(profiles.Select(p => p.Id), "Profiles", errors);
        if (profiles.Count == 0 || !profileIds.Contains(local.ActiveProfileId ?? "")) errors.Add("ProfileUnbound");
        foreach (var profile in profiles) Text(profile.Alias, "Profile.Alias", 80, errors);
        var entries = List(local.Entries, "Entries", 2000, errors);
        Unique(entries.Select(e => e.Id), "Entries", errors);
        foreach (var entry in entries)
        {
            if ((entry.Id ?? "").StartsWith("kr:", StringComparison.Ordinal)) errors.Add("PersonalIdUsesPublicNamespace");
            if (!profileIds.Contains(entry.ProfileId ?? "")) errors.Add("UnknownEntryProfile:" + entry.Id);
            Text(entry.Title, "Entry.Title", 80, errors);
            Text(entry.Note, "Entry.Note", 1000, errors, false);
            if (entry.Kind is not ("personal" or "subscription" or "retainer" or "bag")) errors.Add("UnknownEntryKind");
            Boundary(entry.When, "Entry.When", true, [], new Dictionary<string, TargetRecord>(), errors);
            if (entry.When is not null && entry.When.Kind is not ("instant" or "date")) errors.Add("Entry.RequiresKnownDate");
        }
        var preferences = List(local.Alerts, "Alerts", 10_000, errors);
        var seen = new HashSet<(string, string, string, string, int)>();
        foreach (var preference in preferences)
        {
            Text(preference.EventId, "Alert.EventId", 120, errors); Text(preference.PhaseId, "Alert.PhaseId", 120, errors);
            if (!profileIds.Contains(preference.ProfileId ?? "")) errors.Add("UnknownAlertProfile");
            if (preference.Edge is not ("start" or "end") || preference.LeadMinutes is < 0 or > 525600) errors.Add("InvalidAlertPreference");
            if (!seen.Add((preference.ProfileId ?? "", preference.EventId ?? "", preference.PhaseId ?? "", preference.Edge ?? "", preference.LeadMinutes))) errors.Add("DuplicateAlertPreference");
        }
        Unique(List(local.SentAlertKeys, "SentAlertKeys", 20_000, errors), "SentAlertKeys", errors, 512);
        if (local.View is null) errors.Add("NullViewSettings");
        else
        {
            if (local.View.Calendars is null) errors.Add("NullCalendarVisibility");
            if (local.View.Language is not ("ko" or "en") || local.View.Theme is not ("graphite" or "paper")) errors.Add("InvalidViewSettings");
            Text(local.View.CurrentUrl, "CurrentUrl", 512, errors, false);
        }
        return errors;
    }

    public static bool IsHash(string? value) => value is not null && HashPattern().IsMatch(value);

    private static void Boundary(CalendarBoundary? boundary, string path, bool local, HashSet<string> evidence, IReadOnlyDictionary<string, TargetRecord> targets, List<string> errors)
    {
        if (boundary is null) { errors.Add("NullBoundary:" + path); return; }
        var ids = List(boundary.EvidenceIds, path + ".EvidenceIds", 16, errors);
        Unique(ids, path + ".EvidenceIds", errors);
        if (boundary.Kind == "unknown")
        {
            if (!local && boundary.Reason is null) errors.Add("MissingUnknownReason:" + path);
            if (boundary.Reason is not ("not-announced" or "source-ambiguous" or "unsupported") && boundary.Reason is not null) errors.Add("UnknownBoundaryReason:" + path);
            if (boundary.Value is not null || boundary.Condition is not null || boundary.Precision is not null || boundary.Certainty is not null || boundary.Timezone is not null || boundary.DisplayText is not null || ids.Count != 0) errors.Add("MixedUnknownBoundary:" + path);
            return;
        }
        if (boundary.Certainty is not ("confirmed" or "tentative" or "inferred")) errors.Add("InvalidBoundaryCertainty:" + path);
        if (!local && (ids.Count == 0 || ids.Any(id => !evidence.Contains(id)))) errors.Add("UnresolvedEvidence:" + path);
        if (boundary.Reason is not null) errors.Add("MixedBoundaryReason:" + path);
        switch (boundary.Kind)
        {
            case "instant":
                if (CalendarEngine.ParseInstant(boundary.Value) is null || boundary.Precision is not ("minute" or "second")) errors.Add("InvalidInstant:" + path);
                if (boundary.Condition is not null || boundary.Timezone is not null || boundary.DisplayText is not null) errors.Add("MixedInstantBoundary:" + path);
                break;
            case "date":
                if (CalendarEngine.ParseDate(boundary.Value) is null || boundary.Timezone != "Asia/Seoul") errors.Add("InvalidDate:" + path);
                if (boundary.Precision is not null || boundary.Condition is not null || boundary.DisplayText is not null) errors.Add("MixedDateBoundary:" + path);
                break;
            case "condition":
                var condition = boundary.Condition;
                if (condition is null) errors.Add("MissingCondition:" + path);
                else
                {
                    if (condition.Relation is not ("before" or "after")) errors.Add("InvalidConditionRelation:" + path);
                    if (!targets.TryGetValue(condition.TargetId ?? "", out var target) || target.Kind != condition.Type) errors.Add("UnknownOrMismatchedTarget:" + path);
                }
                Text(boundary.DisplayText, "Boundary.DisplayText", 200, errors);
                if (boundary.Value is not null || boundary.Precision is not null || boundary.Timezone is not null) errors.Add("MixedConditionBoundary:" + path);
                break;
            default: errors.Add("UnknownBoundaryKind:" + path); break;
        }
    }

    private static IReadOnlyList<T> List<T>(IReadOnlyList<T>? values, string path, int limit, List<string> errors) where T : class
    {
        if (values is null) { errors.Add("NullCollection:" + path); return []; }
        if (values.Count > limit) errors.Add("LimitExceeded:" + path);
        if (values.Take(limit).Any(value => value is null)) errors.Add("NullElement:" + path);
        return values.Take(limit).OfType<T>().ToArray();
    }
    private static HashSet<string> Unique(IEnumerable<string> values, string path, List<string> errors, int maxLength = 120)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            Text(value, path + ".Id", maxLength, errors);
            if (value is not null && !result.Add(value)) errors.Add("DuplicateId:" + path);
        }
        return result;
    }
    private static void Text(string? value, string path, int limit, List<string> errors, bool required = true)
    {
        if (required && string.IsNullOrWhiteSpace(value)) errors.Add("MissingText:" + path);
        if (value is not null && (value.Length > limit || value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))) errors.Add("InvalidText:" + path);
    }
    private static bool OfficialUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && uri.Host is "www.ff14.co.kr" or "ff14.co.kr" or "guide.ff14.co.kr";
    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex HashPattern();
    [GeneratedRegex("^revisions/[A-Za-z0-9_-]+\\.json$", RegexOptions.CultureInvariant)] private static partial Regex PayloadPath();
}
