using System.Security.Cryptography;
using System.Text;

namespace DuckbillCalendar.Core;

public sealed record UserCalendarCategory
{
    public string Id { get; init; } = "user:" + Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "Person";
    public string Color { get; init; } = "#91AEFF";
}
public sealed record CategoryNotificationRule
{
    public string ProfileId { get; init; } = "default";
    public string CategoryId { get; init; } = "Personal";
    public bool Enabled { get; init; }
    public IReadOnlyList<int> StartMinutes { get; init; } = [60];
    public IReadOnlyList<int> EndMinutes { get; init; } = [];
}
public sealed record ScheduleNotificationRule
{
    public string ProfileId { get; init; } = "default";
    public string EventId { get; init; } = "";
    public string PhaseId { get; init; } = "";
    public string Mode { get; init; } = "custom";
    public IReadOnlyList<int> StartMinutes { get; init; } = [];
    public IReadOnlyList<int> EndMinutes { get; init; } = [];
}
public sealed record NotificationSettings
{
    public bool Enabled { get; init; }
    public bool NewNotices { get; init; }
    public bool NewEvents { get; init; }
    public IReadOnlyList<CategoryNotificationRule> Categories { get; init; } = [];
    public IReadOnlyList<ScheduleNotificationRule> Schedules { get; init; } = [];
}
public sealed record InboxNotification
{
    public string Id { get; init; } = "";
    public string ProfileId { get; init; } = "";
    public string Kind { get; init; } = "schedule";
    public string EventId { get; init; } = "";
    public string PhaseId { get; init; } = "";
    public string Edge { get; init; } = "";
    public int LeadMinutes { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Url { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? BoundaryAt { get; init; }
    public bool Read { get; init; }
    public string ChatState { get; init; } = "none";
}
public sealed record NotificationReceipt
{
    public string Key { get; init; } = "";
    public DateTimeOffset At { get; init; }
}
public sealed record NotificationState
{
    public bool PublicInitialized { get; init; }
    public IReadOnlyList<string> SeenPublicIds { get; init; } = [];
    public IReadOnlyList<InboxNotification> Inbox { get; init; } = [];
    public IReadOnlyList<NotificationReceipt> Receipts { get; init; } = [];
}

public static class NotificationPolicy
{
    public const int MaximumInbox = 200;
    public static CalendarGroup Group(CalendarEvent e) => e.Category switch
    {
        "personal" => CalendarGroup.Personal,
        "subscription" or "retainer" or "bag" => CalendarGroup.Subscriptions,
        "patch" => CalendarGroup.Patches, "maintenance" => CalendarGroup.Maintenance,
        "pvp-season" or "pvp-series" or "reward" => CalendarGroup.Pvp,
        "competition" => CalendarGroup.Competitions,
        "notice" when e.Id == "kr:curated:ccrc-2027-korea" || e.Id.StartsWith("kr:curated:competition", StringComparison.Ordinal) => CalendarGroup.Competitions,
        "notice" => CalendarGroup.Notices,
        "sale" or "discount" => CalendarGroup.Sales,
        _ => e.IsPersonal ? CalendarGroup.Personal : CalendarGroup.Events
    };
    public static string CategoryKey(CalendarEvent e) => e.Category.StartsWith("user:", StringComparison.Ordinal) ? e.Category : Group(e).ToString();
    public static CategoryNotificationRule DefaultRule(string categoryId, string profileId) => new()
    {
        CategoryId = categoryId, ProfileId = profileId,
        StartMinutes = categoryId == nameof(CalendarGroup.Subscriptions) ? [] : [60],
        EndMinutes = categoryId == nameof(CalendarGroup.Subscriptions) ? [1440, 60] : categoryId == nameof(CalendarGroup.Personal) || categoryId.StartsWith("user:", StringComparison.Ordinal) ? [] : [1440]
    };
    public static CategoryNotificationRule CategoryRule(LocalState state, string categoryId) =>
        state.Notifications.Categories.FirstOrDefault(r => r.ProfileId == state.ActiveProfileId && r.CategoryId == categoryId) ?? DefaultRule(categoryId, state.ActiveProfileId);
    public static IReadOnlyList<AlertPreference> EffectiveAlerts(FeedDocument feed, LocalState state)
    {
        var result = new List<AlertPreference>();
        var overrides = state.Notifications.Schedules.Where(r => r.ProfileId == state.ActiveProfileId).ToDictionary(r => (r.EventId, r.PhaseId));
        var legacy = state.Alerts.Where(r => r.ProfileId == state.ActiveProfileId).GroupBy(r => (r.EventId, r.PhaseId)).ToDictionary(g => g.Key, g => g.ToArray());
        var categories = state.Notifications.Categories.Where(r => r.ProfileId == state.ActiveProfileId).ToDictionary(r => r.CategoryId);
        foreach (var e in CalendarEngine.Merge(feed, state))
        foreach (var p in e.Phases.Where(p => CalendarContentPolicy.Includes(e, p)))
        {
            IReadOnlyList<int> starts = [], ends = [];
            if (overrides.TryGetValue((e.Id, p.Id), out var rule))
            {
                if (rule.Mode == "off") continue;
                starts = rule.StartMinutes; ends = rule.EndMinutes;
            }
            else if (legacy.TryGetValue((e.Id, p.Id), out var choices)) { result.AddRange(choices.Where(a => a.Enabled)); continue; }
            else if (categories.TryGetValue(CategoryKey(e), out var category) && category.Enabled) { starts = category.StartMinutes; ends = category.EndMinutes; }
            foreach (var (edge, minutes) in new[] { ("start", starts), ("end", ends) })
                foreach (var lead in minutes)
                    result.Add(new() { ProfileId = state.ActiveProfileId, EventId = e.Id, PhaseId = p.Id, Edge = edge, LeadMinutes = lead });
        }
        return result;
    }
    public static string BlockReason(FeedDocument feed, FeedHealth? health, CalendarEvent e, EventPhase p, string edge, DateTimeOffset now)
    {
        var boundary = edge == "start" ? p.Start : p.End;
        if (boundary.Kind != "instant") return "시각 미등록·미발표";
        if (boundary.Certainty != "confirmed") return "확정 시각 대기";
        if (!FreshForNotification(feed, health, e, now)) return "출처 확인 대기";
        return "";
    }
    public static bool FreshForNotification(FeedDocument feed, FeedHealth? health, CalendarEvent e, DateTimeOffset now)
    {
        if (HealthFreshness.ForEvent(feed, health, e, now).IsFresh) return true;
        if (e.IsPersonal) return true;
        if (feed.IsExample || health is null || e.Note?.Contains("공식 원문 재검토 대기", StringComparison.Ordinal) == true) return false;
        // Partial coverage may prove an observed item, never one merely retained from an earlier crawl.
        var ids = e.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds)).Distinct().ToArray();
        if (ids.Length == 0) return false;
        foreach (var id in ids)
        {
            var proof = feed.Evidence.FirstOrDefault(r => r.Id == id);
            if (proof?.ObservedAt is not { } observed || now - observed > TimeSpan.FromHours(24) || observed - now > TimeSpan.FromMinutes(5)) return false;
            var sources = health.Sources.Where(s => s.Id == proof.SourceId).ToArray();
            if (sources.Length != 1) return false;
            var source = sources[0];
            if (source.ComparedContentRevision != feed.ContentRevision || source.LastResult is not ("partial" or "success") || source.LastSuccessAt is not { } success || source.LastAttemptAt is not { } attempt ||
                success > attempt || (observed - success).Duration() > TimeSpan.FromMinutes(5) || source.PendingChanges.Any(c => c.AffectedEventIds is null || c.AffectedEventIds.Contains(e.Id))) return false;
        }
        return true;
    }
    public static string SourceUrl(FeedDocument feed, CalendarEvent e)
    {
        return OfficialEventLinks.Resolve(feed,e) ?? "";
    }
    public static bool OfficialUrl(string? value) => OfficialEventLinks.IsOfficialUrl(value);
    public static string Id(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    // Creates inbox records only; persistence and game-state-aware chat delivery belong to the coordinator.
    public static LocalState Stage(FeedDocument feed, LocalState state, FeedHealth? health, bool trustedRemote, DateTimeOffset now)
    {
        var runtime = state.NotificationState;
        var inbox = runtime.Inbox.ToList(); var receipts = runtime.Receipts.ToList();
        var seen = runtime.SeenPublicIds.ToHashSet(StringComparer.Ordinal); var initialized = runtime.PublicInitialized;
        var changed = false;
        void Add(string key, InboxNotification item)
        {
            var id = Id(key);
            if (receipts.Any(r => r.Key == id)) return;
            receipts.Add(new() { Key = id, At = now });
            inbox.Add(item with { Id = id, CreatedAt = now, ChatState = state.View.LocalChatAlerts ? "pending" : "none" }); changed = true;
        }
        if (trustedRemote && !feed.IsExample)
        {
            var current = feed.Events.Where(e => e.Lifecycle == "published").ToArray();
            if (initialized && state.Notifications.Enabled)
            {
                foreach (var kind in new[] { "notice", "event" })
                {
                    if (kind == "notice" && !state.Notifications.NewNotices || kind == "event" && !state.Notifications.NewEvents) continue;
                    var fresh = current.Where(e => !seen.Contains(e.Id) && (kind == "notice" ? e.Phases.Any(CalendarContentPolicy.IsPublication) : e.Id.StartsWith("kr:event:", StringComparison.Ordinal))).ToArray();
                    if (fresh.Length > 5)
                        Add("new-batch:" + kind + ":" + string.Join(",", fresh.Select(e => e.Id).Order(StringComparer.Ordinal)), new()
                        { Kind = "new-" + kind, Title = kind == "notice" ? "새 공지 " + fresh.Length + "건" : "새 이벤트 " + fresh.Length + "건", Message = string.Join(" · ", fresh.Take(3).Select(e => e.Title)) });
                    else foreach (var e in fresh)
                        Add("new:" + kind + ":" + e.Id, new() { Kind = "new-" + kind, EventId = e.Id, Title = e.Title, Message = kind == "notice" ? "새 공지가 확인되었습니다." : "새 이벤트가 확인되었습니다.", Url = SourceUrl(feed, e) });
                }
            }
            if (!initialized || current.Any(e => !seen.Contains(e.Id)))
            { initialized = true; seen.UnionWith(current.Select(e => e.Id)); changed = true; }
        }
        if (state.Notifications.Enabled)
        {
            var effective = EffectiveAlerts(feed, state);
            var planningState = state with { Alerts = effective };
            var items = CalendarEngine.Merge(feed, state).ToDictionary(e => e.Id);
            var receiptIds = receipts.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var alert in AlertPlanner.Plan(feed, planningState, now, health, canAlert: e => FreshForNotification(feed, health, e, now)).Where(a => !receiptIds.Contains(Id("schedule:" + a.Key))).Take(20))
            {
                var preference = effective.First(p => p.EventId == alert.EventId && p.PhaseId == alert.PhaseId && p.Edge == alert.Edge &&
                    AlertPlanner.Key(p.ProfileId, p.EventId, p.PhaseId, p.Edge, alert.BoundaryAt, p.LeadMinutes) == alert.Key);
                Add("schedule:" + alert.Key, new() { Kind = "schedule", ProfileId = alert.ProfileId, EventId = alert.EventId, PhaseId = alert.PhaseId,
                    Edge = alert.Edge, LeadMinutes = preference.LeadMinutes, BoundaryAt = alert.BoundaryAt, Title = alert.Title,
                    Message = (alert.Edge == "start" ? "시작" : SubscriptionTime.IsSubscription(items[alert.EventId].Category) ? "만료" : "마감") + " " + LeadLabel(preference.LeadMinutes), Url = SourceUrl(feed, items[alert.EventId]) });
            }
        }
        if (!changed) return state;
        return state with { NotificationState = runtime with { PublicInitialized = initialized,
            SeenPublicIds = seen.TakeLast(5000).ToArray(), Inbox = inbox.TakeLast(MaximumInbox).ToArray(),
            Receipts = receipts.Where(r => now - r.At < TimeSpan.FromDays(7) || r.At > now).TakeLast(4096).ToArray() } };
    }
    public static string LeadLabel(int minutes) => minutes == 0 ? "정각" : minutes % 1440 == 0 ? minutes / 1440 + "일 전" : minutes % 60 == 0 ? minutes / 60 + "시간 전" : minutes + "분 전";
    public static bool Relevant(InboxNotification item, FeedDocument feed, LocalState state, FeedHealth? health, DateTimeOffset now)
    {
        if (!state.Notifications.Enabled) return false;
        if (item.Kind == "new-notice") return state.Notifications.NewNotices && now - item.CreatedAt <= TimeSpan.FromHours(24);
        if (item.Kind == "new-event") return state.Notifications.NewEvents && now - item.CreatedAt <= TimeSpan.FromHours(24);
        if (item.ProfileId != state.ActiveProfileId || item.BoundaryAt is not { } at || now - at > TimeSpan.FromMinutes(15)) return false;
        var e = CalendarEngine.Merge(feed, state).FirstOrDefault(e => e.Id == item.EventId);
        var p = e?.Phases.FirstOrDefault(p => p.Id == item.PhaseId);
        if (e is null || p is null || BlockReason(feed, health, e, p, item.Edge, now).Length > 0) return false;
        var current = item.Edge == "start" ? CalendarEngine.StartInstant(p) : CalendarEngine.EndExclusive(p);
        return current == at && EffectiveAlerts(feed, state).Any(r => r.EventId == item.EventId && r.PhaseId == item.PhaseId && r.Edge == item.Edge && r.LeadMinutes == item.LeadMinutes);
    }
}
