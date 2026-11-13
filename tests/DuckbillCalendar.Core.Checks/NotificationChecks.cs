using System.Text.Json;
using DuckbillCalendar.Core;

internal static class NotificationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-11-13T12:00:00Z");
        var empty = new FeedDocument { GeneratedAt = now };
        var category = new UserCalendarCategory { Id = "user:raid", Name = "절바하 공대", Color = "#AABBCC", Icon = "Trophy" };
        var entry = new PersonalEntry { Id = "party", Title = "공대 출발", When = CalendarBoundary.Instant(now.AddHours(1)), CategoryId = category.Id };
        var state = new LocalState { View = new() { UserCategories = [category] }, Entries = [entry], Notifications = new()
        { Enabled = true, Categories = [new() { CategoryId = category.Id, Enabled = true, StartMinutes = [60, 10] }] } };
        check(FeedValidator.ValidateLocal(state).Count == 0, "Custom categories and notification defaults validate");
        check(CalendarEngine.Merge(empty, state).Single().Category == category.Id, "Projection preserves a stable raid category identity");
        check(NotificationPolicy.EffectiveAlerts(empty, state).Count == 2, "A category can supply multiple lead times");
        var first = NotificationPolicy.Stage(empty, state, null, false, now);
        check(first.NotificationState.Inbox.Count == 1 && first.NotificationState.Inbox[0].LeadMinutes == 60, "First category lead creates an inbox notification");
        check(ReferenceEquals(first, NotificationPolicy.Stage(empty, first, null, false, now)), "Repeated scans do not duplicate a notification");
        var second = NotificationPolicy.Stage(empty, first, null, false, now.AddMinutes(50));
        check(second.NotificationState.Inbox.Count == 2 && second.NotificationState.Inbox[1].LeadMinutes == 10, "Each lead time has an independent receipt");
        var off = state with { Notifications = state.Notifications with { Schedules = [new() { EventId = entry.Id, PhaseId = "local", Mode = "off" }] } };
        check(NotificationPolicy.EffectiveAlerts(empty, off).Count == 0, "A single schedule can opt out of a category");
        var custom = state with { Notifications = state.Notifications with { Schedules = [new() { EventId = entry.Id, PhaseId = "local", StartMinutes = [30] }] } };
        check(NotificationPolicy.EffectiveAlerts(empty, custom).Single().LeadMinutes == 30, "An override replaces category times");
        var legacy = state with { Alerts = [new() { EventId = entry.Id, PhaseId = "local", Edge = "start", LeadMinutes = 15 }] };
        check(NotificationPolicy.EffectiveAlerts(empty, legacy).Single().LeadMinutes == 15, "Existing individual lead times are preserved");
        check(NotificationPolicy.EffectiveAlerts(empty, legacy with { Alerts = [legacy.Alerts[0] with { Enabled = false }] }).Count == 0, "Disabled old choices suppress category inheritance");
        check(NotificationPolicy.Stage(empty, first with { Entries = [entry with { When = CalendarBoundary.Instant(now.AddHours(2)) }] }, null, false, now.AddHours(1)).NotificationState.Inbox.Count == 2, "Moving an occurrence permits a new correctly timed notification");
        check(NotificationPolicy.Stage(empty, state with { Entries = [entry with { When = CalendarBoundary.Date(new(2026,11,14)) }] }, null, false, now).NotificationState.Inbox.Count == 0, "Date-only entries never acquire guessed alarms");
        var copy = CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(second, CalendarJson.Options));
        check(copy.NotificationState.Inbox.Count == 2 && copy.Entries[0].CategoryId == category.Id && FeedValidator.ValidateLocal(copy).Count == 0, "Rules, receipts, categories and inbox survive valid serialization");
        check(FeedValidator.ValidateLocal(state with { View = state.View with { UserCategories = [category,category] } }).Contains("InvalidUserCategory"), "Duplicate category identities are rejected");
        check(FeedValidator.ValidateLocal(state with { Entries = [entry with { CategoryId = "user:missing" }] }).Contains("InvalidEntryCategory"), "Unknown category references are rejected");
        check(FeedValidator.ValidateLocal(custom with { Notifications = custom.Notifications with { Schedules = [custom.Notifications.Schedules[0] with { StartMinutes = [-1] }] } }).Contains("InvalidScheduleNotificationRule"), "Invalid lead times are rejected");
        var notice = new CalendarEvent { Id = "kr:notice:1", Title = "공지1", Category = "notice", Phases = [new() { Id = "published", Label = "공지 게시", Point = true, Start = CalendarBoundary.Instant(now) }] };
        var feed = empty with { ContentRevision = 1, Events = [notice] };
        var baseline = NotificationPolicy.Stage(feed, new() { Notifications = new() { Enabled = true,NewNotices = true,NewEvents = true } }, null, true, now);
        check(baseline.NotificationState.PublicInitialized && baseline.NotificationState.Inbox.Count == 0, "First synchronization records a baseline without flooding old posts");
        var eventItem = notice with { Id = "kr:event:2", Title = "새 이벤트", Category = "web-event", Phases = [new() { Id = "period", Label = "참여", Start = CalendarBoundary.Instant(now.AddDays(1)) }] };
        var updated = feed with { Events = [notice,notice with { Id = "kr:notice:2" },eventItem] };
        var arrivals = NotificationPolicy.Stage(updated, baseline, null, true, now);
        check(arrivals.NotificationState.Inbox.Count == 2 && arrivals.NotificationState.Inbox.Any(n => n.Kind == "new-notice") && arrivals.NotificationState.Inbox.Any(n => n.Kind == "new-event"), "New notices and events create distinct records");
        check(ReferenceEquals(arrivals, NotificationPolicy.Stage(updated, arrivals, null, true, now)), "Public IDs do not notify twice");
        check(NotificationPolicy.Stage(feed with { Events = [notice with { Title = "제목 수정" }] }, baseline, null, true, now).NotificationState.Inbox.Count == 0, "Title edits are not new posts");
        var observedOff = NotificationPolicy.Stage(updated, baseline with { Notifications = new() { NewNotices = false, NewEvents = false } }, null, true, now);
        check(NotificationPolicy.Stage(updated, observedOff with { Notifications = new() { Enabled = true,NewNotices = true,NewEvents = true } }, null, true, now).NotificationState.Inbox.Count == 0, "Re-enabling does not replay posts observed while disabled");
        check(!NotificationPolicy.Stage(feed, new(), null, false, now).NotificationState.PublicInitialized, "Bundled/imported data cannot seed trusted discovery");
        check(!NotificationPolicy.Stage(feed with { IsExample = true }, new(), null, true, now).NotificationState.PublicInitialized, "Examples never seed public discovery");
        var burst = NotificationPolicy.Stage(feed with { Events = Enumerable.Range(0,30).Select(i => notice with { Id = "kr:notice:batch-" + i }).ToArray() }, baseline, null, true, now);
        check(burst.NotificationState.Inbox.Count == 1 && burst.NotificationState.Inbox[0].Title.Contains("30건", StringComparison.Ordinal) && FeedValidator.ValidateLocal(burst).Count == 0, "A large discovery burst becomes one valid summary");
        var proof = new EvidenceRecord { Id = "proof", SourceId = "source", Url = "https://www.ff14.co.kr/news/event", ObservedAt = now, SourceSnapshotSha256 = new string('a',64) };
        var timed = eventItem with { Phases = [new() { Id = "start", Point = true, Start = CalendarBoundary.Instant(now.AddMinutes(30)) with { EvidenceIds = [proof.Id] } }] };
        var timedFeed = feed with { Events = [timed], Evidence = [proof] };
        var health = new FeedHealth { Sources = [new() { Id = proof.SourceId, LastResult = "partial", LastSuccessAt = now, LastAttemptAt = now, ComparedContentRevision = 1 }] };
        check(NotificationPolicy.FreshForNotification(timedFeed, health, timed, now), "Partial collection can prove an item observed in the same successful crawl");
        check(!NotificationPolicy.FreshForNotification(timedFeed with { Evidence = [proof with { ObservedAt = now.AddHours(-1) }] }, health, timed, now), "Retained stale evidence cannot borrow current source health");
        check(!NotificationPolicy.FreshForNotification(timedFeed, health with { Sources = [health.Sources[0] with { LastResult = "failed" }] }, timed, now), "Failed collection cannot approve public time alerts");
        var choice = new LocalState { Notifications = new() { Enabled = true }, Alerts = [new() { EventId = timed.Id, PhaseId = "start", Edge = "start", LeadMinutes = 30 }] };
        check(NotificationPolicy.Stage(timedFeed, choice, health, false, now).NotificationState.Inbox.Count == 1, "Exact verified public schedule choices can notify");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(new LocalState(), CalendarJson.Options))!;
        node.AsObject().Remove("notifications"); node.AsObject().Remove("notificationState"); node["view"]!.AsObject().Remove("userCategories"); node["view"]!.AsObject().Remove("hiddenUserCategoryIds");
        var old = CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(node, CalendarJson.Options));
        check(FeedValidator.ValidateLocal(old).Count == 0 && !old.NotificationState.PublicInitialized, "Older local files load safely without inventing a discovery baseline");
    }
}
