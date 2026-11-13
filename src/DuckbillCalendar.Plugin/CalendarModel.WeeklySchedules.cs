using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class CalendarModel
{
    public WeeklyRegistrationResult? LastWeeklyRegistration { get; private set; }
    public void RegisterWeeklySchedules(WeeklyScheduleRequest request)
    {
        var snapshot = request with { Days = request.Days?.ToArray() ?? [] };
        var profile = State.ActiveProfileId;
        Post(() =>
        {
            if (State.ActiveProfileId != profile) { LastWeeklyRegistration = new(0,0,null,["ProfileChanged"]); return; }
            var plan = WeeklySchedulePlanner.Plan(State,snapshot);
            if (!plan.IsValid) { LastWeeklyRegistration = new(0,plan.ExistingCount,null,plan.Errors); return; }
            var series = Guid.NewGuid().ToString("N");
            var entries = plan.Occurrences.Where(o => !o.AlreadyExists).Select((o,index) => new PersonalEntry
            {
                ProfileId = State.ActiveProfileId,Title = snapshot.Title.Trim(),CategoryId = snapshot.CategoryId,
                When = CalendarBoundary.Instant(o.When),Note = snapshot.Note.Trim(),SeriesId = series,SeriesIndex = index
            }).ToArray();
            if (entries.Length > 0) workspace.UpdateState(State with { Entries = State.Entries.Concat(entries).ToArray() });
            var first = entries.Length == 0 ? (DateOnly?)null : CalendarEngine.BoundaryDate(entries[0].When);
            LastWeeklyRegistration = new(entries.Length,plan.ExistingCount,first,[]);
            if (first is { } date) SelectDateCore(date);
        });
    }
    public void DeletePersonalSchedules(string id,string scope) => Post(() =>
    {
        var ids = PersonalDeletionPolicy.Targets(State,id,scope).Select(e => e.Id).ToHashSet();
        if (ids.Count == 0) return;
        workspace.UpdateState(State with
        {
            Entries = State.Entries.Where(e => !ids.Contains(e.Id)).ToArray(),
            Alerts = State.Alerts.Where(a => a.ProfileId != State.ActiveProfileId || !ids.Contains(a.EventId)).ToArray(),
            Notifications = State.Notifications with { Schedules = State.Notifications.Schedules.Where(r => r.ProfileId != State.ActiveProfileId || !ids.Contains(r.EventId)).ToArray() },
            View = State.View with { HiddenEventIds = State.View.HiddenEventIds.Where(value => !ids.Contains(value)).ToArray(),
                HiddenPhases = State.View.HiddenPhases.Where(p => !ids.Contains(p.EventId)).ToArray() }
        });
    });
}
