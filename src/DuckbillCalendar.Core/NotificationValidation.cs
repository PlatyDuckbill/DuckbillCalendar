namespace DuckbillCalendar.Core;

internal static class NotificationValidation
{
    public static IReadOnlyList<string> Validate(LocalState state)
    {
        var errors = new List<string>();
        IReadOnlyList<T> List<T>(IReadOnlyList<T>? values, int limit, string name) where T : class
        {
            if (values is null || values.Count > limit || values.Any(v => v is null)) { errors.Add("Invalid" + name); return []; }
            return values;
        }
        bool Text(string? value, int max, bool empty = false) => value is not null && value.Length <= max && (empty || !string.IsNullOrWhiteSpace(value));
        bool Minutes(IReadOnlyList<int>? values) => values is not null && values.Count <= 6 && values.Distinct().Count() == values.Count && values.All(v => v is >= 0 and <= 525600);
        var profiles = (state.Profiles ?? []).Where(p => p is not null).Select(p => p.Id).ToHashSet();
        var custom = List(state.View?.UserCategories, 64, "UserCategories");
        var customIds = new HashSet<string>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in custom)
        {
            if (!Text(c.Id, 64) || !c.Id.StartsWith("user:", StringComparison.Ordinal) || !customIds.Add(c.Id) || !Text(c.Name, 40) || !names.Add(c.Name.Trim()) ||
                c.Icon is not ("Gift" or "Update" or "Server" or "Flag" or "Trophy" or "Tag" or "Clock" or "Person" or "Raid" or "Swords" or "Shield") ||
                c.Color is not { Length: 7 } || c.Color[0] != '#' || c.Color[1..].Any(v => !Uri.IsHexDigit(v))) errors.Add("InvalidUserCategory");
        }
        foreach (var entry in state.Entries ?? [])
            if (entry is not null && (entry.CategoryId is null || entry.CategoryId.Length > 0 && (entry.Kind != "personal" || !customIds.Contains(entry.CategoryId)))) errors.Add("InvalidEntryCategory");
        var seriesPositions = new HashSet<(string,string,int)>();
        foreach (var entry in state.Entries ?? [])
            if (entry is not null && (entry.SeriesId is null || entry.SeriesId.Length > 64 ||
                (entry.SeriesId.Length == 0 ? entry.SeriesIndex is not null : entry.Kind != "personal" || entry.SeriesIndex is not >= 0 or > 1999 ||
                entry.SeriesId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) || !seriesPositions.Add((entry.ProfileId,entry.SeriesId,entry.SeriesIndex!.Value))))) errors.Add("InvalidEntrySeries");
        var hidden = state.View?.HiddenUserCategoryIds;
        if (hidden is null || hidden.Count > 64 || hidden.Distinct().Count() != hidden.Count || hidden.Any(id => !customIds.Contains(id))) errors.Add("InvalidHiddenUserCategory");
        if (state.Notifications is not { } settings || state.NotificationState is not { } runtime) { errors.Add("NullNotifications"); return errors; }
        var categoryKeys = new HashSet<(string, string)>();
        foreach (var r in List(settings.Categories, 1600, "CategoryNotificationRules"))
            if (!profiles.Contains(r.ProfileId) || !(customIds.Contains(r.CategoryId) || Enum.GetNames<CalendarGroup>().Contains(r.CategoryId)) || !categoryKeys.Add((r.ProfileId, r.CategoryId)) || !Minutes(r.StartMinutes) || !Minutes(r.EndMinutes)) errors.Add("InvalidCategoryNotificationRule");
        var scheduleKeys = new HashSet<(string, string, string)>();
        foreach (var r in List(settings.Schedules, 10000, "ScheduleNotificationRules"))
            if (!profiles.Contains(r.ProfileId) || !Text(r.EventId, 120) || !Text(r.PhaseId, 120) || !scheduleKeys.Add((r.ProfileId, r.EventId, r.PhaseId)) || r.Mode is not ("custom" or "off") || !Minutes(r.StartMinutes) || !Minutes(r.EndMinutes)) errors.Add("InvalidScheduleNotificationRule");
        if (runtime.SeenPublicIds is null || runtime.SeenPublicIds.Count > 5000 || runtime.SeenPublicIds.Any(id => !Text(id, 120)) || runtime.SeenPublicIds.Distinct().Count() != runtime.SeenPublicIds.Count) errors.Add("InvalidPublicNotificationCursor");
        var inboxKeys = new HashSet<string>();
        foreach (var n in List(runtime.Inbox, NotificationPolicy.MaximumInbox, "NotificationInbox"))
            if (!FeedValidator.IsHash(n.Id) || !inboxKeys.Add(n.Id) || n.ProfileId is null || n.ProfileId.Length > 0 && !profiles.Contains(n.ProfileId) ||
                n.Kind is not ("schedule" or "new-notice" or "new-event") || !Text(n.EventId, 120, true) || !Text(n.PhaseId, 120, true) || !Text(n.Title, 600) || !Text(n.Message, 1600, true) || !Text(n.Url, 2048, true) ||
                n.Url.Length > 0 && !NotificationPolicy.OfficialUrl(n.Url) || n.ChatState is not ("none" or "pending" or "consumed") || n.LeadMinutes is < 0 or > 525600 ||
                n.Kind == "schedule" && (n.Edge is not ("start" or "end") || n.BoundaryAt is null || !Text(n.ProfileId, 64) || !Text(n.EventId, 120) || !Text(n.PhaseId, 120))) errors.Add("InvalidInboxNotification");
        var receipts = List(runtime.Receipts, 4096, "NotificationReceipts");
        if (receipts.Any(r => !FeedValidator.IsHash(r.Key)) || receipts.Select(r => r.Key).Distinct().Count() != receipts.Count) errors.Add("InvalidNotificationReceipts");
        return errors;
    }
}
