using System.Collections.Concurrent;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

namespace DuckbillCalendar.Plugin;

internal sealed record ScheduleHighlight(CalendarEvent Event, EventPhase Phase, string Edge, CalendarBoundary Boundary, DateOnly Date);

internal sealed class CalendarModel
{
    private readonly CalendarWorkspace workspace;
    private readonly ConcurrentQueue<Action> commands = new();
    private LocalState? lastState;
    private long lastRevision = -1;
    private long lastMinute = -1;
    private bool dirty = true;
    private DateTimeOffset nextRefresh;
    public CalendarModel(CalendarWorkspace workspace)
    {
        this.workspace = workspace;
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
    public bool ListView { get; private set; }
    public IReadOnlyList<CalendarEvent> Events { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> FilteredEvents { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> CalendarEvents { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> NewsEvents { get; private set; } = [];
    public IReadOnlyList<EvidenceRecord> SelectedEvidence { get; private set; } = [];
    public IReadOnlyList<ScheduleHighlight> UpcomingHighlights { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> PendingHighlights { get; private set; } = [];
    public IReadOnlyList<CalendarEvent> MonthEvents { get; private set; } = [];
    public IReadOnlyList<CalendarItem> DayItems { get; private set; } = [];
    public IReadOnlyList<CalendarItem> TodayDeadlines { get; private set; } = [];
    public PersonalEntry? NextSubscription { get; private set; }
    public CalendarMonth? Calendar { get; private set; }
    public FrontlineResult? TodayFrontline { get; private set; }
    public FrontlineResult? TomorrowFrontline { get; private set; }
    public CalendarEvent? SelectedEvent => Events.FirstOrDefault(e => e.Id == SelectedEventId);
    public bool SelectionFiltered => SelectedEventId != null && !FilteredEvents.Any(e => e.Id == SelectedEventId);
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
        if (State.View.RemoteUpdatesEnabled && IsPublishedEndpoint(State.View.CurrentUrl) && !workspace.IsBusy && now >= nextRefresh)
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
        CalendarEvents = FilteredEvents.Select(e => e.Category == "notice" && !State.View.ShowNoticePublications && CategoryFilter != 6
            ? e with { Phases = e.Phases.Where(p => !IsPublicationPhase(p)).ToArray() } : e).Where(e => e.Phases.Count > 0).ToArray();
        NewsEvents = FilteredEvents.Where(e => !e.IsPersonal).OrderByDescending(e => e.Phases.Select(p => CalendarEngine.BoundaryDate(p.Start)).Max()).ToArray();
        Calendar = CalendarEngine.BuildMonth(CalendarEvents, Year, MonthNumber, State.View.PeriodBars, State.View.ShowRewardBars, 3);
        MonthEvents = CalendarEngine.MonthEvents(CalendarEvents, Year, MonthNumber);
        DayItems = CalendarEngine.DayEvents(CalendarEvents, SelectedDate, includeOngoing: true);
        UpcomingHighlights = Events.Where(IsHighlightEvent).SelectMany(e => e.Phases.Where(p => !IsPublicationPhase(p)).SelectMany(p =>
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
        PendingHighlights = Events.Where(IsHighlightEvent).Where(e => e.Phases.All(p => CalendarEngine.BoundaryDate(p.Start) is null)).ToArray();
        TodayDeadlines = CalendarEngine.DayEvents(Events, Today, includeOngoing: false)
            .Where(i => i.Edge == "end" && (i.Phase.End.Kind == "date" || CalendarEngine.EndExclusive(i.Phase) > now))
            .OrderBy(i => CalendarEngine.EndExclusive(i.Phase) ?? DateTimeOffset.MaxValue).ToArray();
        NextSubscription = State.Entries.Where(e => e.ProfileId == State.ActiveProfileId && e.Kind is "subscription" or "retainer" or "bag")
            .Where(e => CalendarEngine.BoundaryDate(e.When) >= Today && (e.When.Kind == "date" || CalendarEngine.StartInstant(e.When) > now))
            .OrderBy(e => CalendarEngine.BoundaryDate(e.When)).ThenBy(e => CalendarEngine.StartInstant(e.When)).FirstOrDefault();
        TodayFrontline = CalendarEngine.FrontlineFor(Feed, Today);
        TomorrowFrontline = CalendarEngine.FrontlineFor(Feed, Today.AddDays(1));
        var selected = SelectedEvent;
        if (selected == null) { SelectedEventId = null; SelectedPhaseId = null; }
        else if (!selected.Phases.Any(p => p.Id == SelectedPhaseId)) SelectedPhaseId = null;
        var selectedEvidenceIds = selected?.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds)).ToHashSet(StringComparer.Ordinal) ?? [];
        SelectedEvidence = Feed.Evidence.Where(e => selectedEvidenceIds.Contains(e.Id)).ToArray();
        lastState = State;
        lastRevision = workspace.Revision;
        dirty = false;
    }
    public static bool IsPublishedEndpoint(string url) => string.Equals(url,
        "https://raw.githubusercontent.com/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/current.json", StringComparison.Ordinal);

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
    public bool HasAlert(CalendarEvent e) => State.Alerts.Any(a => a.Enabled && a.ProfileId == State.ActiveProfileId && a.EventId == e.Id);
    public bool HasAlert(CalendarEvent e, EventPhase p, string edge) => State.Alerts.Any(a => a.Enabled && a.ProfileId == State.ActiveProfileId && a.EventId == e.Id && a.PhaseId == p.Id && a.Edge == edge);
    public void ToggleAlert(CalendarEvent e, EventPhase p, string edge) => Post(() =>
    {
        var alerts = State.Alerts.Where(a => !(a.ProfileId == State.ActiveProfileId && a.EventId == e.Id && a.PhaseId == p.Id && a.Edge == edge)).ToList();
        if (!HasAlert(e, p, edge)) alerts.Add(new() { ProfileId = State.ActiveProfileId, EventId = e.Id, PhaseId = p.Id, Edge = edge, LeadMinutes = 60 });
        workspace.UpdateState(State with { Alerts = alerts });
    });
    public void SetQuery(string search, int category, bool onlyAlerts) => Post(() => { Search = search; CategoryFilter = category; OnlyAlerts = onlyAlerts; });
    public void SetListView(bool list) => Post(() => ListView = list);
    public void ChangeMonth(int delta) => Post(() =>
    {
        var next = new DateOnly(Year, MonthNumber, 1).AddMonths(delta);
        if (next.Year is < 2020 or > 2100) return;
        Year = next.Year; MonthNumber = next.Month;
    });
    public void GoToday() => Post(() => { Year = Today.Year; MonthNumber = Today.Month; SelectDateCore(Today); });
    public void GoToMonth(int year, int month) => Post(() =>
    {
        if (year is < 2020 or > 2100 || month is < 1 or > 12) return;
        Year = year; MonthNumber = month;
        SelectDateCore(new(year, month, Math.Min(SelectedDate.Day, DateTime.DaysInMonth(year, month))));
    });
    public void SelectDate(DateOnly date) => Post(() => SelectDateCore(date));
    private void SelectDateCore(DateOnly date)
    {
        if (date.Year is < 2020 or > 2100) return;
        SelectedDate = date;
        var item = CalendarEngine.DayEvents(CalendarEvents, date, true).FirstOrDefault();
        SelectedEventId = item?.Event.Id;
        SelectedPhaseId = item?.Phase.Id;
    }
    public void Select(CalendarEvent e, EventPhase? p = null, DateOnly? date = null) => Post(() =>
    {
        SelectedEventId = e.Id;
        SelectedPhaseId = p?.Id;
        if (date.HasValue) SelectedDate = date.Value;
    });
    public void JumpToStart(CalendarEvent e) => Post(() =>
    {
        var phase = e.Phases.FirstOrDefault(p => CalendarEngine.BoundaryDate(p.Start).HasValue);
        if (phase == null || CalendarEngine.BoundaryDate(phase.Start) is not { } date || date.Year is < 2020 or > 2100) return;
        Year = date.Year; MonthNumber = date.Month; SelectedDate = date;
        SelectedEventId = e.Id; SelectedPhaseId = phase.Id;
        CategoryFilter = 0; OnlyAlerts = false; Search = "";
    });
    public void JumpToHighlight(ScheduleHighlight highlight) => Post(() =>
    {
        if (highlight.Date.Year is < 2020 or > 2100) return;
        Year = highlight.Date.Year; MonthNumber = highlight.Date.Month; SelectedDate = highlight.Date;
        SelectedEventId = highlight.Event.Id; SelectedPhaseId = highlight.Phase.Id;
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
    });
    public void DeleteEntry(string id) => Post(() =>
    {
        workspace.UpdateState(State with { Entries = State.Entries.Where(e => e.Id != id).ToArray(), Alerts = State.Alerts.Where(a => a.EventId != id).ToArray() });
        if (SelectedEventId == id) { SelectedEventId = null; SelectedPhaseId = null; }
    });
    private bool Matches(CalendarEvent e) => (string.IsNullOrWhiteSpace(Search) || e.Title.Contains(Search, StringComparison.OrdinalIgnoreCase))
        && (!OnlyAlerts || HasAlert(e)) && (CategoryFilter switch
        {
            1 => e.Category is "web-event" or "in-game-event" or "seasonal-event",
            2 => e.Category is "patch" or "maintenance",
            3 => e.Category is "pvp-season" or "pvp-series" or "reward",
            4 => e.Category is "sale" or "discount", 5 => e.IsPersonal, 6 => e.Category == "notice", _ => true
        });
    public static bool IsPublicationPhase(EventPhase phase) => phase.Id == "published" || phase.Label.Contains("공지 게시", StringComparison.Ordinal);
    private static bool IsHighlightEvent(CalendarEvent e) => !e.IsPersonal && e.Lifecycle == "published" &&
        (e.Category is "patch" or "maintenance" or "pvp-season" or "pvp-series" or "sale" or "discount" ||
         e.Id.StartsWith("kr:curated:", StringComparison.Ordinal) || e.Phases.Any(p => p.Point && !IsPublicationPhase(p)));
}
