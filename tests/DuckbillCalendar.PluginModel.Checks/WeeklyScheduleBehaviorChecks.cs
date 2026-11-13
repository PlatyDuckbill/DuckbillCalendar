using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

internal static class WeeklyScheduleBehaviorChecks
{
    public static async Task<int> Run(string root,byte[] example)
    {
        var count = 0; void Check(bool value,string name) { if (!value) throw new InvalidOperationException(name); count++; }
        var path = Path.Combine(root,"weekly-batch"); var now = DateTimeOffset.Parse("2026-11-13T12:00:00+09:00");
        var request = new WeeklyScheduleRequest { Title = "절바하",CategoryId = "user:default-raid",StartDate = new(2026,11,16),Weeks = 4,
            Days = [new(DayOfWeek.Monday,"20:00"),new(DayOfWeek.Sunday,"18:00")] };
        using (var workspace = new CalendarWorkspace(path,example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            model.RegisterWeeklySchedules(request); model.Tick(now); await workspace.FlushAsync(); model.Tick(now);
            Check(model.LastWeeklyRegistration is { Registered:8 } && model.State.Entries.Count == 8,"Weekly registration commits the complete preview in one state update");
            Check(model.State.Entries.Select(e => e.Id).Distinct().Count() == 8 && model.CalendarEvents.Count(e => e.Category == request.CategoryId) == 8,"Expanded raid dates have distinct IDs and the selected category");
            var before = model.State;
            model.RegisterWeeklySchedules(request); model.Tick(now);
            Check(ReferenceEquals(before,model.State) && model.LastWeeklyRegistration is { Registered:0,Skipped:8 },"A second submission cannot create duplicate raid dates");
            model.RegisterWeeklySchedules(request with { Days = [new(DayOfWeek.Monday,"25:00")] }); model.Tick(now);
            Check(ReferenceEquals(before,model.State) && model.LastWeeklyRegistration!.Errors.Count > 0,"An invalid batch leaves every existing schedule unchanged");
            var entry = model.State.Entries.First(); model.SaveEntry(entry with { Title = "개별 수정" }); model.Tick(now);
            Check(model.State.Entries.Count(e => e.Title == "개별 수정") == 1 && model.State.Entries.Count == 8,"Each expanded occurrence can be edited independently");
            Check(!model.State.Notifications.Enabled && NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Count == 0,"Registering a weekly raid does not enable alarms");
            await workspace.FlushAsync();
        }
        using (var workspace = new CalendarWorkspace(path,example))
            Check(workspace.State.Entries.Count == 8 && workspace.State.Entries.All(e => e.CategoryId == request.CategoryId),"The complete weekly batch survives restart");
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"weekly-delete-scopes"),example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            model.RegisterWeeklySchedules(request); model.Tick(now);
            var original = model.State.Entries.OrderBy(e => e.SeriesIndex).ToArray();
            model.RegisterWeeklySchedules(request with { Title = "다른 공격대" }); model.Tick(now);
            var other = model.State.Entries.Where(e => e.Title == "다른 공격대").ToArray();
            Check(original.Select(e => e.SeriesId).Distinct().Count() == 1 && original[0].SeriesId != other[0].SeriesId,"Separate bulk registrations have separate series identity even in one category");
            model.SaveEntryWithNotification(original[6],"custom",[10]); model.Tick(now);
            model.DeletePersonalSchedules(original[4].Id,"following"); model.Tick(now);
            Check(model.State.Entries.Count == 12 && other.All(e => model.State.Entries.Contains(e)),"Following deletion removes only the later half of the selected batch");
            Check(model.State.Notifications.Schedules.All(r => r.EventId != original[6].Id),"Deleting occurrences also removes their individual notification exceptions");
            model.DeletePersonalSchedules(original[0].Id,"series"); model.Tick(now);
            Check(model.State.Entries.Count == 8 && model.State.Entries.All(e => e.SeriesId == other[0].SeriesId),"Whole-series deletion keeps a separate raid registration intact");
            model.DeletePersonalSchedules(other[0].Id,"one"); model.Tick(now);
            Check(model.State.Entries.Count == 7 && model.State.Entries.All(e => e.Id != other[0].Id),"One-occurrence deletion preserves all other dates of its batch");
            model.SaveEntryWithNotification(other[0],"custom",[10],expectExisting:true); model.Tick(now);
            Check(model.State.Entries.Count == 7 && model.LastEntrySaveError is not null,"A stale editor cannot resurrect an occurrence already removed by batch deletion");
            await workspace.FlushAsync();
        }
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"weekly-profile-race"),example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            model.RegisterWeeklySchedules(request);
            workspace.UpdateState(model.State with { Profiles = [new(),new() { Id = "other",Alias = "다른 계정" }],ActiveProfileId = "other" });
            model.Tick(now);
            Check(model.State.Entries.Count == 0 && model.LastWeeklyRegistration!.Errors.Contains("ProfileChanged"),"A profile change between submit and commit cannot register a batch into the wrong account");
            model.SaveEntryWithNotification(new() { ProfileId = "default",Title = "이전 계정",When = CalendarBoundary.Instant(now) },"custom",[10]); model.Tick(now);
            Check(model.State.Entries.Count == 0 && model.State.Notifications.Schedules.Count == 0,"A stale edit window cannot save an entry or alarm to another profile");
            await workspace.FlushAsync();
        }
        return count;
    }
}
