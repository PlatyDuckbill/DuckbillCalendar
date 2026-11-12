namespace DuckbillCalendar.Core;

public sealed record FreshnessResult(bool IsFresh, string Reason);
public sealed record PlannedAlert(string Key, string ProfileId, string EventId, string PhaseId, string Edge, string Title, DateTimeOffset DueAt, DateTimeOffset BoundaryAt);

public static class HealthFreshness
{
    public static FreshnessResult ForEvent(FeedDocument feed, FeedHealth? health, CalendarEvent item, DateTimeOffset now, TimeSpan? maxAge = null)
    {
        if (item.IsPersonal) return new(true, "LocalInput");
        if (feed.IsExample) return new(false, "ExampleFeed");
        if (health?.Sources is null) return new(false, "NoHealthObservation");
        var evidence = (feed.Evidence ?? []).Where(e => e is not null).GroupBy(e => e.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var referenced = (item.Phases ?? []).Where(p => p is not null).SelectMany(p => (p.Start?.EvidenceIds ?? []).Concat(p.End?.EvidenceIds ?? []))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (referenced.Length == 0 || referenced.Any(id => !evidence.ContainsKey(id))) return new(false, "MissingEvidence");
        var sourceIds = referenced.Select(id => evidence[id].SourceId).Distinct(StringComparer.Ordinal);
        var ageLimit = maxAge ?? TimeSpan.FromHours(24);
        foreach (var id in sourceIds)
        {
            var observations = health.Sources.Where(s => s is not null && s.Id == id).ToArray();
            if (observations.Length != 1) return new(false, "MissingSourceHealth");
            var source = observations[0];
            if (!source.CompleteCoverage || source.ComparedContentRevision != feed.ContentRevision) return new(false, "UnprovenCoverage");
            if (source.LastResult == "not-run" || source.LastSuccessAt is not { } success || source.LastAttemptAt is not { } attempt)
                return new(false, "NotCollected");
            if (success > attempt) return new(false, "InvalidObservation");
            // Compare durations instead of adding to the clock: DateTimeOffset.MaxValue is still a valid injected clock.
            if (success > now && success - now > TimeSpan.FromMinutes(5) || now - success > ageLimit)
                return new(false, "CollectionExpired");
            if (source.PendingChanges is null) return new(false, "InvalidPendingChanges");
            if (source.PendingChanges.Any(c => c is null || c.AffectedEventIds is null || c.AffectedEventIds.Contains(item.Id, StringComparer.Ordinal)))
                return new(false, "PendingChange");
        }
        return new(true, "Current");
    }
}

public static class AlertPlanner
{
    /// <summary>Returns due exact-boundary alerts only. Persistence, delivery and game-state suppression belong to the caller.</summary>
    public static IReadOnlyList<PlannedAlert> Plan(FeedDocument feed, LocalState local, DateTimeOffset now, FeedHealth? health = null, TimeSpan? dueWindow = null)
    {
        var result = new List<PlannedAlert>();
        if (local.Profiles is null || !local.Profiles.Any(p => p is not null && p.Id == local.ActiveProfileId)) return result;
        var window = dueWindow ?? TimeSpan.FromMinutes(2);
        if (window < TimeSpan.Zero || window > TimeSpan.FromHours(24)) return result;
        var sent = (local.SentAlertKeys ?? []).ToHashSet(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var items = CalendarEngine.Merge(feed, local).GroupBy(e => e.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var preference in local.Alerts ?? [])
        {
            if (preference is null || !preference.Enabled || preference.ProfileId != local.ActiveProfileId || preference.Edge is not ("start" or "end") || preference.LeadMinutes is < 0 or > 525600) continue;
            if (!items.TryGetValue(preference.EventId, out var item) || !HealthFreshness.ForEvent(feed, health, item, now).IsFresh) continue;
            var phase = (item.Phases ?? []).FirstOrDefault(p => p is not null && p.Id == preference.PhaseId);
            if (phase is null) continue;
            var boundary = preference.Edge == "start" ? phase.Start : phase.End;
            if (boundary is null || boundary.Kind != "instant" || boundary.Certainty != "confirmed") continue;
            var at = preference.Edge == "start" ? CalendarEngine.StartInstant(phase) : CalendarEngine.EndExclusive(phase);
            if (at is null) continue;
            DateTimeOffset due;
            try { due = at.Value.AddMinutes(-preference.LeadMinutes); }
            catch (ArgumentOutOfRangeException) { continue; }
            if (due > now || now - due > window) continue;
            string key = Key(local.ActiveProfileId, item.Id, phase.Id, preference.Edge, at.Value, preference.LeadMinutes);
            if (sent.Contains(key) || !emitted.Add(key)) continue;
            result.Add(new(key, local.ActiveProfileId, item.Id, phase.Id, preference.Edge, item.Title, due, at.Value));
        }
        return result.OrderBy(a => a.DueAt).ThenBy(a => a.Key, StringComparer.Ordinal).ToArray();
    }

    public static string Key(string profileId, string eventId, string phaseId, string edge, DateTimeOffset at, int leadMinutes)
    {
        // Length-prefixed components avoid collisions when source IDs contain punctuation.
        static string Part(string value) => value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;
        return Part(profileId) + Part(eventId) + Part(phaseId) + Part(edge) + Part(at.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)) + Part(leadMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
