using System.Collections.Concurrent;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

namespace DuckbillCalendar.Plugin;

internal sealed record ScheduleHighlight(CalendarEvent Event, EventPhase Phase, string Edge, CalendarBoundary Boundary, DateOnly Date);
internal enum CalendarPanelMode { Month, Day, Event, Undated }
internal enum CalendarViewMode { Grid, Agenda, Undated }

internal sealed partial class CalendarModel
{
    private readonly CalendarWorkspace workspace;
    private readonly ConcurrentQueue<Action> commands = new();
    private LocalState? lastState;
    private long lastRevision = -1;
    private long lastMinute = -1;
    private bool dirty = true;
    private DateTimeOffset nextRefresh;
    public bool AutomaticUpdatesAllowed { get; private set; } = true;
    public string? AutomaticUpdateHoldMessage { get; private set; }
    public void SetAutomaticUpdateState(bool allowed, string? message)
    {
        AutomaticUpdatesAllowed = allowed; AutomaticUpdateHoldMessage = message;
        workspace.SetAutomaticRefreshAllowed(allowed);
    }
    public CalendarModel(CalendarWorkspace workspace)
    {
        this.workspace = workspace;
        // Earlier builds enabled bars by default. Apply the requested endpoint default
        // once, then preserve explicit choices made with the current controls.
        var local = InitializePersonalCategory(workspace.State);
        var view = local.View;
        if (!view.DefaultRaidCategoryInitialized)
            view = view with { DefaultRaidCategoryInitialized = true, UserCategories = view.UserCategories.Count < 64 && !view.UserCategories.Any(c => c.Id == "user:default-raid" || c.Name.Trim().Equals("공격대",StringComparison.OrdinalIgnoreCase))
                ? view.UserCategories.Append(new() { Id = "user:default-raid", Name = "공격대", Icon = "Raid", Color = "#C2A1EB" }).ToArray() : view.UserCategories };
        if (view.PeriodBars && !view.PeriodBarsConfigured) view = view with { PeriodBars = false, PeriodBarsConfigured = true };
        if (view.Language != "ko" || view.ShowNoticePublications || view.Theme == "paper")
            view = view with { Language = "ko", ShowNoticePublications = false, Theme = view.Theme == "paper" ? "cloud" : view.Theme };
        if (view != workspace.State.View || !ReferenceEquals(local,workspace.State)) workspace.UpdateState(local with { View = view });
        Now = DateTimeOffset.UtcNow;
        Today = CalendarEngine.Today(Now);
        Year = Today.Year;
        MonthNumber = Today.Month;
        SelectedDate = Today;
    }
    public CalendarWorkspace Workspace => workspace;
    public LocalState State => workspace.State;
    public FeedDocument Feed => workspace.Feed;
    public DateTimeOffset OfficialEventsObservedAt { get; private set; }
    public DateTimeOffset Now { get; private set; }
    public DateOnly Today { get; private set; }
    public int Year { get; private set; }
    public int MonthNumber { get; private set; }
    public DateOnly SelectedDate { get; private set; }
    public string? SelectedEventId { get; private set; }
    public string? SelectedPhaseId { get; private set; }
    public string Search { get; private set; } = "";
    public int CategoryFilter { get; private set; }
    public bool OnlyAlerts { get; private set; }
    private string newsSearch = "";
    private int newsCategory;
    public CalendarViewMode ViewMode { get; private set; } = CalendarViewMode.Grid;
    public CalendarPanelMode PanelMode { get; private set; } = CalendarPanelMode.Month;
    public CalendarPanelMode PreviousPanelMode { get; private set; } = CalendarPanelMode.Month;
    public bool ListView => ViewMode == CalendarViewMode.Agenda;
    public bool HasDateFocus => PanelMode is CalendarPanelMode.Day or CalendarPanelMode.Event;
    public string? SelectedEdge { get; private set; }
    public IReadOnlyList<CalendarEvent> Events { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> FilteredEvents { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> CalendarEvents { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> VisibleCalendarEvents { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> NewsEvents { get; private set; } = [];
    public IReadOnlyList<EvidenceRecord> SelectedEvidence { get; private set; } = [];
    public IReadOnlyList<ScheduleHighlight> UpcomingHighlights { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> PendingHighlights { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> MonthEvents { get; private set; } = [];
    public IReadOnlyList<CalendarItem> DayItems { get; private set; } = [];
    public IReadOnlyList<ScheduleHighlight> MonthAgenda { get; private set; } = [];
    public IReadOnlyList<CalendarItem> DayBoundaryItems { get; private set; } = [];
    public IReadOnlyList<CalendarItem> DayOngoingItems { get; private set; } = [];
    public IReadOnlyList<CalendarDisplayEntry> DayDisplayEntries { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> UndatedEvents { get; private set; } = [];
    public CalendarMonth? Calendar { get; private set; }
    public FrontlineResult? TodayFrontline { get; private set; }
    public FrontlineResult? TomorrowFrontline { get; private set; }
    public CalendarEvent? SelectedEvent => Events.FirstOrDefault(e => e.Id == SelectedEventId);
    public bool SelectionFiltered => SelectedEventId != null && (!FilteredEvents.Any(e => e.Id == SelectedEventId) || !VisibleCalendarEvents.Any(e => e.Id == SelectedEventId));
    public void Post(Action action) => commands.Enqueue(action);
    public void Invalidate() => dirty = true;
    public void UpdateView(Func<ViewSettings, ViewSettings> change) => Post(() =>
    {
        workspace.UpdateState(State with { View = change(State.View) });
        dirty = true;
    });
    public void Tick(DateTimeOffset now)
    {
        Now = now;
        while (commands.TryDequeue(out var action)) { action(); dirty = true; }
        workspace.Pump();
        if (!ReferenceEquals(lastState, State) || lastRevision != workspace.Revision) dirty = true;
        var minute = now.ToUnixTimeSeconds() / 60;
        if (minute != lastMinute)
        {
            lastMinute = minute;
            Today = CalendarEngine.Today(now);
            dirty = true;
        }
        if (AutomaticUpdatesAllowed && State.View.RemoteUpdatesEnabled && IsPublishedEndpoint(State.View.CurrentUrl) && !workspace.IsBusy && now >= nextRefresh &&
            workspace.NextAutomaticRefreshAt is { } due && now >= due)
        {
            // The workspace owns TTL, cooldown and failure backoff. A second hourly timer here
            // would accidentally delay its 15-minute recovery attempt until an hour has passed.
            nextRefresh = now.AddMinutes(1);
            workspace.Refresh();
        }
        if (!dirty) return;
        UiText.SetLanguage(State.View.Language);
        OfficialEventsObservedAt = Feed.Evidence.Where(e => e.ObservedAt.HasValue)
            .Select(e => e.ObservedAt!.Value).DefaultIfEmpty(Feed.GeneratedAt).Max();
        Events = CalendarEngine.Merge(Feed, State);
        FilteredEvents = Events.Where(Matches).ToArray();
        var hidden = State.View.HiddenEventIds.ToHashSet(StringComparer.Ordinal);
        var hiddenPhases = State.View.HiddenPhases.Select(p => (p.EventId, p.PhaseId)).ToHashSet();
        VisibleCalendarEvents = Events.Where(e => !hidden.Contains(e.Id)).Select(e => e with { Phases = e.Phases.Where(p => !hiddenPhases.Contains((e.Id, p.Id)) && CalendarContentPolicy.Includes(e, p) &&
            (e.Category.StartsWith("user:", StringComparison.Ordinal) ? !State.View.HiddenUserCategoryIds.Contains(e.Category) : IsCalendarVisible(GroupFor(e, p)))).ToArray() })
            .Where(e => e.Phases.Count > 0).ToArray();
        CalendarEvents = VisibleCalendarEvents.Where(MatchesSearchAndAlerts).ToArray();
        NewsEvents = Events.Where(e => !e.IsPersonal && (string.IsNullOrWhiteSpace(newsSearch) || e.Title.Contains(newsSearch, StringComparison.OrdinalIgnoreCase)))
            .Where(e => newsCategory switch { 1 => e.Category is "web-event" or "in-game-event" or "seasonal-event", 2 => e.Category is "patch" or "maintenance", 3 => e.Category is "pvp-season" or "pvp-series" or "reward", 4 => e.Category is "sale" or "discount", 5 => e.Category == "notice", _ => true })
            .OrderByDescending(NewsDate).ToArray();
        Calendar = CalendarEngine.BuildMonth(CalendarEvents, Year, MonthNumber, State.View.PeriodBars, State.View.ShowRewardBars, 8);
        MonthEvents = CalendarEngine.MonthEvents(CalendarEvents, Year, MonthNumber);
        DayItems = CalendarEngine.DayEvents(CalendarEvents, SelectedDate, includeOngoing: true);
        DayBoundaryItems = DayItems.Where(i => !i.Ongoing).OrderBy(i => (i.Edge == "start" ? CalendarEngine.StartInstant(i.Phase.Start) : CalendarEngine.StartInstant(i.Phase.End)) ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.Edge == "end" ? 0 : 1).ThenBy(i => i.Event.Title, StringComparer.Ordinal).ToArray();
        DayOngoingItems = DayItems.Where(i => i.Ongoing).ToArray();
        DayDisplayEntries = CalendarPresentation.Build(DayItems, SelectedDate);
        MonthAgenda = CalendarEvents.SelectMany(e => e.Phases.SelectMany(p =>
            (p.Point ? new[] { "start" } : ["start", "end"]).SelectMany(edge =>
            {
                var boundary = edge == "end" ? p.End : p.Start;
                return CalendarEngine.BoundaryDate(boundary) is { } date && date.Year == Year && date.Month == MonthNumber
                    ? new[] { new ScheduleHighlight(e, p, edge, boundary, date) } : [];
            }))).OrderBy(h => h.Date).ThenBy(h => CalendarEngine.StartInstant(h.Boundary) ?? DateTimeOffset.MaxValue)
            .ThenBy(h => h.Edge == "end" ? 0 : 1).ThenBy(h => h.Event.Id, StringComparer.Ordinal).ToArray();
        UndatedEvents = CalendarEvents.Where(e => !(e.IsPersonal && e.Category is "subscription" or "retainer" or "bag"))
            .Select(e => e with { Phases = e.Phases.Where(p => !p.Point &&
                (p.Start.Kind is "unknown" or "condition" || p.End.Kind is "unknown" or "condition")).ToArray() })
            .Where(e => e.Phases.Count > 0).ToArray();
        UpcomingHighlights = VisibleCalendarEvents.Where(IsHighlightEvent).SelectMany(e => e.Phases.Where(p => !IsPublicationPhase(p)).SelectMany(p =>
        {
            var edges = e.Category is "sale" or "discount" ? new[] { "end" } : p.Point ? ["start"] : ["start", "end"];
            return edges.SelectMany(edge =>
            {
                var boundary = edge == "end" ? p.End : p.Start;
                var stillUpcoming = boundary.Kind != "instant" || (edge == "end" ? CalendarEngine.EndExclusive(p) > Now : CalendarEngine.StartInstant(boundary) >= Now);
                return CalendarEngine.BoundaryDate(boundary) is { } date && date >= Today && stillUpcoming
                    ? new[] { new ScheduleHighlight(e, p, edge, boundary, date) } : [];
            });
        })).OrderBy(h => h.Date).ThenBy(h => CalendarEngine.StartInstant(h.Boundary)).ThenBy(h => h.Event.Id, StringComparer.Ordinal).ToArray();
        PendingHighlights = VisibleCalendarEvents.Where(IsHighlightEvent).Where(e => e.Phases.All(p => CalendarEngine.BoundaryDate(p.Start) is null)).ToArray();
        TodayFrontline = CalendarEngine.FrontlineFor(Feed, Today);
        TomorrowFrontline = CalendarEngine.FrontlineFor(Feed, Today.AddDays(1));
        var selected = SelectedEvent;
        if (selected == null)
        {
            ClearEventSelection();
            if (PanelMode == CalendarPanelMode.Event) PanelMode = PreviousPanelMode;
        }
        else if (!selected.Phases.Any(p => p.Id == SelectedPhaseId)) SelectedPhaseId = null;
        var selectedEvidenceIds = selected?.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds)).ToHashSet(StringComparer.Ordinal) ?? [];
        SelectedEvidence = Feed.Evidence.Where(e => selectedEvidenceIds.Contains(e.Id)).ToArray();
        lastState = State;
        lastRevision = workspace.Revision;
        dirty = false;
    }
    public static bool IsPublishedEndpoint(string url) => string.IsNullOrWhiteSpace(url) ||
        string.Equals(url, BoundedFeedTransport.CurrentAddress, StringComparison.Ordinal);

    public void DrainPendingActions(Action<Exception> onError)
    {
        // No drawing, projection or refresh while unloading. Move already requested edits into
        // the workspace's durable write queue before its nonblocking shutdown begins.
        while (commands.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { onError(ex); }
        }
    }
    public bool HasAlert(CalendarEvent e) => NotificationPolicy.EffectiveAlerts(Feed, State).Any(a => a.EventId == e.Id);
    public bool HasAlert(CalendarEvent e, EventPhase p, string edge) => NotificationPolicy.EffectiveAlerts(Feed, State).Any(a => a.EventId == e.Id && a.PhaseId == p.Id && a.Edge == edge);
    public void ToggleAlert(CalendarEvent e, EventPhase p, string edge) => Post(() =>
    {
        if (State.Notifications.Schedules.Any(r => r.ProfileId == State.ActiveProfileId && r.EventId == e.Id && r.PhaseId == p.Id) ||
            NotificationPolicy.CategoryRule(State, NotificationPolicy.CategoryKey(e)).Enabled)
        {
            var effective = NotificationPolicy.EffectiveAlerts(Feed, State).Where(a => a.EventId == e.Id && a.PhaseId == p.Id).ToArray();
            var remaining = effective.Where(a => a.Edge != edge).ToList();
            if (!effective.Any(a => a.Edge == edge)) remaining.Add(new() { Edge = edge, LeadMinutes = 60 });
            SetScheduleNotification(e.Id, p.Id, remaining.Count == 0 ? "off" : "custom",
                remaining.Where(a => a.Edge == "start").Select(a => a.LeadMinutes).ToArray(), remaining.Where(a => a.Edge == "end").Select(a => a.LeadMinutes).ToArray());
            return;
        }
        var alerts = State.Alerts.Where(a => !(a.ProfileId == State.ActiveProfileId && a.EventId == e.Id && a.PhaseId == p.Id && a.Edge == edge)).ToList();
        if (!HasAlert(e, p, edge)) alerts.Add(new() { ProfileId = State.ActiveProfileId, EventId = e.Id, PhaseId = p.Id, Edge = edge, LeadMinutes = 60 });
        workspace.UpdateState(State with { Alerts = alerts });
    });
    public void SetQuery(string search, int category, bool onlyAlerts) => Post(() => { Search = search; CategoryFilter = category; OnlyAlerts = onlyAlerts; });
    public void SetNewsQuery(string search, int category) => Post(() => { newsSearch = search; newsCategory = category; });
    public void SetEventHidden(string id, bool hidden) => UpdateView(v => v with
    {
        HiddenEventIds = hidden ? v.HiddenEventIds.Append(id).Distinct(StringComparer.Ordinal).ToArray() : v.HiddenEventIds.Where(value => value != id).ToArray(),
        HiddenPhases = hidden ? v.HiddenPhases.Where(p => p.EventId != id).ToArray() : v.HiddenPhases
    });
    public void SetPhaseHidden(string eventId, string phaseId, bool hidden) => UpdateView(v => v with
    {
        HiddenPhases = hidden ? v.HiddenPhases.Append(new() { EventId = eventId, PhaseId = phaseId }).DistinctBy(p => (p.EventId, p.PhaseId)).ToArray()
            : v.HiddenPhases.Where(p => p.EventId != eventId || p.PhaseId != phaseId).ToArray()
    });
    public IReadOnlyList<string> RelatedEventIds(IEnumerable<string> ids) => CalendarPresentation.RelatedEventIds(Events, ids);
    public void RestoreHiddenEvents() => UpdateView(v => v with { HiddenEventIds = [], HiddenPhases = [] });
    public void SetPeriodBars(bool enabled) => UpdateView(v => v with { PeriodBars = enabled, PeriodBarsConfigured = true });
    public bool IsCalendarVisible(CalendarGroup group) => group switch
    {
        CalendarGroup.Events => State.View.Calendars.Events, CalendarGroup.Patches => State.View.Calendars.Patches,
        CalendarGroup.Maintenance => State.View.Calendars.Maintenance, CalendarGroup.Pvp => State.View.Calendars.Pvp,
        CalendarGroup.Competitions => State.View.Calendars.Competitions, CalendarGroup.Sales => State.View.Calendars.Sales,
        CalendarGroup.Subscriptions => State.View.Calendars.Subscriptions, CalendarGroup.Personal => State.View.Calendars.Personal,
        CalendarGroup.Notices => false, _ => false
    };
    public void SetCalendarVisible(CalendarGroup group, bool enabled) => UpdateView(v => group switch
    {
        CalendarGroup.Events => v with { Calendars = v.Calendars with { Events = enabled } },
        CalendarGroup.Patches => v with { Calendars = v.Calendars with { Patches = enabled } },
        CalendarGroup.Maintenance => v with { Calendars = v.Calendars with { Maintenance = enabled } },
        CalendarGroup.Pvp => v with { Calendars = v.Calendars with { Pvp = enabled } },
        CalendarGroup.Competitions => v with { Calendars = v.Calendars with { Competitions = enabled } },
        CalendarGroup.Sales => v with { Calendars = v.Calendars with { Sales = enabled } },
        CalendarGroup.Subscriptions => v with { Calendars = v.Calendars with { Subscriptions = enabled } },
        CalendarGroup.Personal => v with { Calendars = v.Calendars with { Personal = enabled } },
        CalendarGroup.Notices => v with { ShowNoticePublications = enabled }, _ => v
    });
    public void SetAllCalendars(bool enabled) => UpdateView(v => v with
    {
        Calendars = new() { Events = enabled, Patches = enabled, Maintenance = enabled, Pvp = enabled, Competitions = enabled, Sales = enabled, Subscriptions = enabled, Personal = enabled },
        ShowNoticePublications = enabled
    });
    public static CalendarGroup GroupFor(CalendarEvent e, EventPhase p) => e.IsPersonal
        ? e.Category is "subscription" or "retainer" or "bag" ? CalendarGroup.Subscriptions : CalendarGroup.Personal
        : e.Category switch
        {
            "patch" => CalendarGroup.Patches, "maintenance" => CalendarGroup.Maintenance,
            "pvp-season" or "pvp-series" or "reward" => CalendarGroup.Pvp, "sale" or "discount" => CalendarGroup.Sales,
            "notice" => IsPublicationPhase(p) ? CalendarGroup.Notices : CalendarGroup.Competitions, _ => CalendarGroup.Events
        };
    public void SetListView(bool list) => Post(() => { ViewMode = list ? CalendarViewMode.Agenda : CalendarViewMode.Grid; SetMonthFocus(); });
    public void ShowUndatedAgenda() => Post(() => { ViewMode = CalendarViewMode.Undated; ClearEventSelection(); PanelMode = CalendarPanelMode.Undated; });
    public void ShowMonthOverview() => Post(SetMonthFocus);
    public void BackToAgenda() => Post(() =>
    {
        ClearEventSelection();
        PanelMode = PreviousPanelMode;
        if (PanelMode == CalendarPanelMode.Undated) ViewMode = CalendarViewMode.Undated;
    });
    private void ClearEventSelection() { SelectedEventId = null; SelectedPhaseId = null; SelectedEdge = null; }
    private void SetMonthFocus()
    {
        if (ViewMode == CalendarViewMode.Undated) ViewMode = CalendarViewMode.Grid;
        SelectedDate = new(Year, MonthNumber, 1);
        ClearEventSelection();
        PanelMode = CalendarPanelMode.Month;
    }
    public void ChangeMonth(int delta) => Post(() =>
    {
        var next = new DateOnly(Year, MonthNumber, 1).AddMonths(delta);
        if (next.Year is < 2020 or > 2100) return;
        Year = next.Year; MonthNumber = next.Month;
        if (ViewMode == CalendarViewMode.Undated) ViewMode = CalendarViewMode.Grid;
        SetMonthFocus();
    });
    public void GoToday() => Post(() => { Year = Today.Year; MonthNumber = Today.Month; SelectDateCore(Today); });
    public void GoToMonth(int year, int month) => Post(() =>
    {
        if (year is < 2020 or > 2100 || month is < 1 or > 12) return;
        Year = year; MonthNumber = month;
        if (ViewMode == CalendarViewMode.Undated) ViewMode = CalendarViewMode.Grid;
        SetMonthFocus();
    });
    public void SelectDate(DateOnly date) => Post(() => SelectDateCore(date));
    private void SelectDateCore(DateOnly date)
    {
        if (date.Year is < 2020 or > 2100) return;
        if (ViewMode == CalendarViewMode.Undated) ViewMode = CalendarViewMode.Grid;
        SelectedDate = date;
        Year = date.Year; MonthNumber = date.Month;
        ClearEventSelection();
        PanelMode = CalendarPanelMode.Day;
    }
    public void Select(CalendarEvent e, EventPhase? p = null, DateOnly? date = null, string? edge = null) => Post(() =>
    {
        if (PanelMode != CalendarPanelMode.Event) PreviousPanelMode = PanelMode;
        PanelMode = CalendarPanelMode.Event;
        SelectedEventId = e.Id;
        SelectedPhaseId = p?.Id;
        SelectedEdge = edge;
        var target = date ?? CalendarEngine.BoundaryDate(p?.Start) ?? CalendarEngine.BoundaryDate(p?.End);
        if (target is { } value) { SelectedDate = value; Year = value.Year; MonthNumber = value.Month; }
    });
    public void SelectAgenda(ScheduleHighlight highlight) => Select(highlight.Event, highlight.Phase, highlight.Date, highlight.Edge);
    public void JumpToStart(CalendarEvent e) => Post(() =>
    {
        var phase = e.Phases.FirstOrDefault(p => CalendarEngine.BoundaryDate(p.Start).HasValue);
        if (phase == null || CalendarEngine.BoundaryDate(phase.Start) is not { } date || date.Year is < 2020 or > 2100) return;
        Year = date.Year; MonthNumber = date.Month; SelectedDate = date;
        SelectedEventId = e.Id; SelectedPhaseId = phase.Id;
        SelectedEdge = "start"; PreviousPanelMode = CalendarPanelMode.Month; PanelMode = CalendarPanelMode.Event;
        ViewMode = CalendarViewMode.Grid;
        CategoryFilter = 0; OnlyAlerts = false; Search = "";
    });
    public void JumpToHighlight(ScheduleHighlight highlight) => Post(() =>
    {
        if (highlight.Date.Year is < 2020 or > 2100) return;
        Year = highlight.Date.Year; MonthNumber = highlight.Date.Month; SelectedDate = highlight.Date;
        SelectedEventId = highlight.Event.Id; SelectedPhaseId = highlight.Phase.Id;
        SelectedEdge = highlight.Edge; PreviousPanelMode = CalendarPanelMode.Month; PanelMode = CalendarPanelMode.Event;
        ViewMode = CalendarViewMode.Grid;
        CategoryFilter = 0; OnlyAlerts = false; Search = "";
    });
    public void SaveEntry(PersonalEntry entry) => Post(() =>
    {
        var entries = State.Entries.Where(e => e.Id != entry.Id).Append(entry).ToArray();
        workspace.UpdateState(State with { Entries = entries });
        if (CalendarEngine.BoundaryDate(entry.When) is { } date)
        {
            SelectedDate = date; Year = date.Year; MonthNumber = date.Month;
        }
        SelectedEventId = entry.Id; SelectedPhaseId = "local";
        SelectedEdge = entry.Kind is "subscription" or "retainer" or "bag" ? "end" : "start";
        PreviousPanelMode = CalendarPanelMode.Day; PanelMode = CalendarPanelMode.Event;
    });
    public void SaveSubscription(PersonalEntry entry) => Post(() =>
    {
        if (!SubscriptionTime.IsSubscription(entry.Kind)) return;
        workspace.UpdateState(State with { Entries = State.Entries.Where(e => e.Id != entry.Id).Append(entry with { Title = SubscriptionTime.Name(entry.Kind) }).ToArray() });
    });
    public void ExtendSubscription(string id, int days) => Post(() =>
    {
        var entry = State.Entries.FirstOrDefault(e => e.Id == id && e.ProfileId == State.ActiveProfileId && SubscriptionTime.IsSubscription(e.Kind));
        if (entry is null) return;
        var updated = entry with { Title = SubscriptionTime.Name(entry.Kind), When = SubscriptionTime.Extend(entry.When, days, Now) };
        workspace.UpdateState(State with { Entries = State.Entries.Select(e => e.Id == id ? updated : e).ToArray() });
    });
    public void ResetSubscription(string id) => Post(() =>
    {
        var profile = State.ActiveProfileId;
        if (!State.Entries.Any(e => e.Id == id && e.ProfileId == profile && SubscriptionTime.IsSubscription(e.Kind))) return;
        workspace.UpdateState(State with
        {
            Entries = State.Entries.Where(e => e.Id != id).ToArray(),
            Alerts = State.Alerts.Where(a => a.ProfileId != profile || a.EventId != id).ToArray(),
            View = State.View with
            {
                HiddenEventIds = State.View.HiddenEventIds.Where(hidden => hidden != id).ToArray(),
                HiddenPhases = State.View.HiddenPhases.Where(hidden => hidden.EventId != id).ToArray()
            },
            Notifications = State.Notifications with { Schedules = State.Notifications.Schedules.Where(r => r.EventId != id).ToArray() }
        });
        if (SelectedEventId == id) { ClearEventSelection(); PanelMode = CalendarPanelMode.Day; }
    });
    public void SetEntryReminder(string id, bool enabled) => Post(() =>
    {
        var entry = State.Entries.FirstOrDefault(e => e.Id == id && e.ProfileId == State.ActiveProfileId);
        if (entry is null) return;
        var existing = State.Alerts.Where(a => a.ProfileId == State.ActiveProfileId && a.EventId == id).ToArray();
        if (enabled && existing.Any(a => a.Enabled)) return;
        var remaining = State.Alerts.Where(a => a.ProfileId != State.ActiveProfileId || a.EventId != id);
        workspace.UpdateState(State with { Alerts = enabled
            ? remaining.Append(existing.FirstOrDefault() is { } prior ? prior with { Enabled = true } : new() { ProfileId = State.ActiveProfileId, EventId = id, PhaseId = "local", Edge = SubscriptionTime.IsSubscription(entry.Kind) ? "end" : "start", LeadMinutes = 60 }).ToArray()
            : remaining.ToArray() });
    });
    public void DeleteEntry(string id) => Post(() =>
    {
        workspace.UpdateState(State with { Entries = State.Entries.Where(e => e.Id != id).ToArray(), Alerts = State.Alerts.Where(a => a.EventId != id).ToArray(),
            Notifications = State.Notifications with { Schedules = State.Notifications.Schedules.Where(r => r.EventId != id).ToArray() } });
        if (SelectedEventId == id) { ClearEventSelection(); PanelMode = CalendarPanelMode.Day; }
    });
    private bool MatchesSearchAndAlerts(CalendarEvent e) => (string.IsNullOrWhiteSpace(Search) || e.Title.Contains(Search, StringComparison.OrdinalIgnoreCase)) && (!OnlyAlerts || HasAlert(e));
    private bool Matches(CalendarEvent e) => MatchesSearchAndAlerts(e) && (CategoryFilter switch
        {
            1 => e.Category is "web-event" or "in-game-event" or "seasonal-event",
            2 => e.Category is "patch" or "maintenance",
            3 => e.Category is "pvp-season" or "pvp-series" or "reward",
            4 => e.Category is "sale" or "discount", 5 => e.IsPersonal, 6 => e.Category == "notice", _ => true
        });
    public static bool IsPublicationPhase(EventPhase phase) => phase.Id == "published" || phase.Label.Contains("공지 게시", StringComparison.Ordinal);
    public static DateOnly? NewsDate(CalendarEvent item) => CalendarEngine.BoundaryDate(item.Phases.FirstOrDefault(IsPublicationPhase)?.Start)
        ?? item.Phases.Select(p => CalendarEngine.BoundaryDate(p.Start)).Min();
    private static bool IsHighlightEvent(CalendarEvent e) => !e.IsPersonal && e.Lifecycle == "published" &&
        (e.Category is "patch" or "maintenance" or "pvp-season" or "pvp-series" or "sale" or "discount" ||
         e.Id.StartsWith("kr:curated:", StringComparison.Ordinal) || e.Phases.Any(p => p.Point && !IsPublicationPhase(p)));
}
