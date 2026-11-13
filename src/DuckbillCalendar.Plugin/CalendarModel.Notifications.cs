using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class CalendarModel
{
    private static LocalState InitializePersonalCategory(LocalState state)
    {
        var view = state.View;
        if (view.DefaultPersonalCategoryInitialized) return state;
        view = view with { DefaultPersonalCategoryInitialized = true };
        if (view.PersonalCategoryRemoved) return state with { View = view };
        var target = view.UserCategories.FirstOrDefault(c => c.Id == "user:default-personal" || c.Name.Trim().Equals("개인 일정",StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            if (view.UserCategories.Count >= 64) return state with { View = view };
            var appearance = view.CalendarAppearances.FirstOrDefault(c => c.Group == CalendarGroup.Personal);
            target = new() { Id = "user:default-personal",Name = "개인 일정",Icon = string.IsNullOrEmpty(appearance?.Icon) ? "Person" : appearance.Icon,
                Color = string.IsNullOrEmpty(appearance?.Color) ? "#D8BB79" : appearance.Color };
            view = view with { UserCategories = view.UserCategories.Append(target).ToArray() };
        }
        var rules = state.Notifications.Categories.Where(r => r.CategoryId != "Personal").ToList();
        foreach (var rule in state.Notifications.Categories.Where(r => r.CategoryId == "Personal"))
            if (!rules.Any(r => r.ProfileId == rule.ProfileId && r.CategoryId == target.Id)) rules.Add(rule with { CategoryId = target.Id });
        return state with
        {
            Entries = state.Entries.Select(e => e.Kind == "personal" && e.CategoryId.Length == 0 ? e with { CategoryId = target.Id } : e).ToArray(),
            View = view with { CalendarAppearances = view.CalendarAppearances.Where(c => c.Group != CalendarGroup.Personal).ToArray(),
                HiddenUserCategoryIds = !view.Calendars.Personal ? view.HiddenUserCategoryIds.Append(target.Id).Distinct().ToArray() : view.HiddenUserCategoryIds,
                Calendars = view.Calendars with { Personal = true } },
            Notifications = state.Notifications with { Categories = rules.ToArray() }
        };
    }
    public int UnreadNotifications => State.NotificationState.Inbox.Count(n => !n.Read && (n.ProfileId.Length == 0 || n.ProfileId == State.ActiveProfileId));
    public int IndividualNotificationCount => State.Notifications.Schedules.Where(r => r.ProfileId == State.ActiveProfileId).Select(r => (r.EventId, r.PhaseId))
        .Concat(State.Alerts.Where(a => a.ProfileId == State.ActiveProfileId).Select(a => (a.EventId, a.PhaseId))).Distinct().Count();
    public void UpdateNotifications(Func<NotificationSettings, NotificationSettings> change) => Post(() => workspace.UpdateState(State with { Notifications = change(State.Notifications) }));
    public long EntrySaveRevision { get; private set; }
    public string? LastEntrySaveError { get; private set; }
    public void SaveEntryWithNotification(PersonalEntry entry,string mode,IReadOnlyList<int> starts,bool? enableNotifications = null,bool expectExisting = false) => Post(() =>
    {
        EntrySaveRevision++; LastEntrySaveError = null;
        if (entry.ProfileId != State.ActiveProfileId) { LastEntrySaveError = "사용 중인 계정이 바뀌어 저장하지 않았습니다."; return; }
        if (expectExisting && !State.Entries.Any(e => e.Id == entry.Id && e.ProfileId == State.ActiveProfileId))
        { LastEntrySaveError = "이 일정이 삭제되어 저장하지 않았습니다."; return; }
        var rules = State.Notifications.Schedules.Where(r => r.ProfileId != State.ActiveProfileId || r.EventId != entry.Id || r.PhaseId != "local");
        workspace.UpdateState(State with
        {
            Entries = State.Entries.Where(e => e.Id != entry.Id).Append(entry).ToArray(),
            Alerts = State.Alerts.Where(a => a.ProfileId != State.ActiveProfileId || a.EventId != entry.Id || a.PhaseId != "local").ToArray(),
            Notifications = State.Notifications with { Enabled = enableNotifications ?? State.Notifications.Enabled,
                Schedules = mode == "inherit" ? rules.ToArray() : rules.Append(new() { ProfileId = State.ActiveProfileId,EventId = entry.Id,PhaseId = "local",Mode = mode,StartMinutes = starts.ToArray() }).ToArray() }
        });
        if (CalendarEngine.BoundaryDate(entry.When) is { } date) SelectDateCore(date);
        SelectedEventId = entry.Id; SelectedPhaseId = "local"; SelectedEdge = "start";
    });
    public void SetCategoryNotification(CategoryNotificationRule rule,string? expectedProfile = null)
    {
        var profile = expectedProfile ?? State.ActiveProfileId;
        var snapshot = rule with { StartMinutes = rule.StartMinutes.ToArray(),EndMinutes = rule.EndMinutes.ToArray() };
        Post(() =>
        {
            if (State.ActiveProfileId != profile || snapshot.CategoryId.StartsWith("user:",StringComparison.Ordinal) && !State.View.UserCategories.Any(c => c.Id == snapshot.CategoryId)) return;
            workspace.UpdateState(State with { Notifications = State.Notifications with
            { Categories = State.Notifications.Categories.Where(r => r.ProfileId != profile || r.CategoryId != snapshot.CategoryId).Append(snapshot with { ProfileId = profile }).ToArray() } });
        });
    }
    public void SetCategoryNotificationEnabled(string id, bool enabled) => Post(() =>
    {
        var rule = NotificationPolicy.CategoryRule(State,id);
        if (enabled && rule.StartMinutes.Count + rule.EndMinutes.Count == 0) rule = NotificationPolicy.DefaultRule(id,State.ActiveProfileId);
        SetCategoryNotification(rule with { Enabled = enabled });
    });
    public void SetScheduleNotification(string eventId, string phaseId, string mode, IReadOnlyList<int> starts, IReadOnlyList<int> ends,string? expectedProfile = null)
    {
        var profile = expectedProfile ?? State.ActiveProfileId;
        var startSnapshot = starts.ToArray(); var endSnapshot = ends.ToArray();
        Post(() =>
    {
        if (State.ActiveProfileId != profile) return;
        var others = State.Notifications.Schedules.Where(r => r.ProfileId != State.ActiveProfileId || r.EventId != eventId || r.PhaseId != phaseId);
        workspace.UpdateState(State with
        {
            Alerts = State.Alerts.Where(a => a.ProfileId != State.ActiveProfileId || a.EventId != eventId || a.PhaseId != phaseId).ToArray(),
            Notifications = State.Notifications with { Schedules = mode == "inherit" ? others.ToArray() : others.Append(new()
            { ProfileId = State.ActiveProfileId, EventId = eventId, PhaseId = phaseId, Mode = mode, StartMinutes = startSnapshot, EndMinutes = endSnapshot }).ToArray() }
        });
    });
    }
    public void MarkNotificationsRead(string? id = null) => Post(() => workspace.UpdateState(State with { NotificationState = State.NotificationState with
    { Inbox = State.NotificationState.Inbox.Select(n => (n.ProfileId.Length == 0 || n.ProfileId == State.ActiveProfileId) && (id is null || n.Id == id) ? n with { Read = true } : n).ToArray() } }));
    public void ClearReadNotifications() => Post(() => workspace.UpdateState(State with { NotificationState = State.NotificationState with
    { Inbox = State.NotificationState.Inbox.Where(n => !n.Read || n.ProfileId.Length > 0 && n.ProfileId != State.ActiveProfileId).ToArray() } }));
    public void SaveUserCategory(UserCalendarCategory category) => UpdateView(v => v with { UserCategories = v.UserCategories.Where(c => c.Id != category.Id).Append(category).ToArray() });
    public void RemovePersonalCategory() => DeleteUserCategory("user:default-personal");
    public void SetScheduleNotificationEnabled(CalendarEvent e, EventPhase p, bool enabled) => Post(() =>
    {
        var saved = State.Notifications.Schedules.FirstOrDefault(r => r.ProfileId == State.ActiveProfileId && r.EventId == e.Id && r.PhaseId == p.Id);
        var legacy = State.Alerts.Where(a => a.ProfileId == State.ActiveProfileId && a.EventId == e.Id && a.PhaseId == p.Id).ToArray();
        var defaults = NotificationPolicy.CategoryRule(State, NotificationPolicy.CategoryKey(e));
        var starts = saved?.StartMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Edge == "start").Select(a => a.LeadMinutes).ToArray() : defaults.StartMinutes);
        var ends = saved?.EndMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Edge == "end").Select(a => a.LeadMinutes).ToArray() : defaults.EndMinutes);
        if (enabled && starts.Count + ends.Count == 0)
        {
            starts = p.Start.Kind is not ("unknown" or "condition") ? new[] { 60 } : [];
            ends = !p.Point && p.End.Kind is not ("unknown" or "condition") ? new[] { 60 } : [];
            if (starts.Count + ends.Count == 0) starts = [60];
        }
        SetScheduleNotification(e.Id, p.Id, enabled ? "custom" : "off", starts, ends);
    });
    public void DeleteUserCategory(string id, string destination = "") => Post(() =>
    {
        if (!State.View.UserCategories.Any(c => c.Id == id) || destination == id || destination.Length > 0 && !State.View.UserCategories.Any(c => c.Id == destination)) return;
        workspace.UpdateState(State with
        {
            Entries = State.Entries.Select(e => e.CategoryId == id ? e with { CategoryId = destination } : e).ToArray(),
            View = State.View with { PersonalCategoryRemoved = State.View.PersonalCategoryRemoved || id == "user:default-personal",
                UserCategories = State.View.UserCategories.Where(c => c.Id != id).ToArray(), HiddenUserCategoryIds = State.View.HiddenUserCategoryIds.Where(c => c != id).ToArray() },
            Notifications = State.Notifications with { Categories = State.Notifications.Categories.Where(c => c.CategoryId != id).ToArray() }
        });
    });
    public void SetUserCategoryVisible(string id, bool visible) => UpdateView(v => v with
    { HiddenUserCategoryIds = visible ? v.HiddenUserCategoryIds.Where(c => c != id).ToArray() : v.HiddenUserCategoryIds.Append(id).Distinct().ToArray() });
}
