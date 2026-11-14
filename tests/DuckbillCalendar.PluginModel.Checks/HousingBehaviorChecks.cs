using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

internal static class HousingBehaviorChecks
{
    public static async Task<int> Run(string root,byte[] example)
    {
        var count=0;
        void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); count++; }
        var now=DateTimeOffset.Parse("2026-11-14T12:00:00+09:00");
        var path=Path.Combine(root,"housing");
        using(var workspace=new CalendarWorkspace(path,example))
        {
            var model=new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            Check(model.Housing is {Phase:HousingLotteryPhase.Results} && model.HousingForDate(new(2026,11,14)) is not null,"A fresh install immediately displays the external KR cycle without game UI input");
            Check(model.NewsEvents.Count==0 && model.Events.Count==0 && workspace.State.Entries.Count==0,"Housing remains a calendar layer without news or duplicate event cards");
            Check(workspace.State.HousingObservations.Count==0,"The sourced pattern is not stored as a game observation");
            model.GoToMonth(2026,12); model.Tick(now);
            Check(model.Calendar!.Weeks.SelectMany(w=>w.Days).All(d=>model.HousingForDate(d.Date) is not null),"Month navigation fills the entire grid using the published cycle");
            model.SetCalendarVisible(CalendarGroup.Events,false); model.Tick(now);
            Check(model.HousingForDate(new(2026,11,14)) is not null,"Housing is independent of the event category filter");
            model.SetCalendarVisible(CalendarGroup.Housing,false); model.Tick(now);
            Check(model.HousingForDate(new(2026,11,14)) is null,"Housing can be switched off");
            model.SetCalendarVisible(CalendarGroup.Housing,true); model.Tick(DateTimeOffset.Parse("2026-11-15T00:00:00+09:00"));
            Check(model.Housing?.Phase==HousingLotteryPhase.Entry,"The current phase changes at the Korean midnight boundary without reopening a tab");
            Check(NotificationPolicy.EffectiveAlerts(workspace.Feed,workspace.State).Count==0,"The external cycle does not fabricate official schedule notifications");
            await workspace.FlushAsync();
        }
        using(var workspace=new CalendarWorkspace(path,example))
        {
            var model=new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            Check(model.HousingForDate(new(2026,11,14)) is not null,"Housing is still immediately available after restart");
        }
        return count;
    }
}
