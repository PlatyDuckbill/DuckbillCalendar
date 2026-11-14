using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

internal static class CalendarSearchBehaviorChecks
{
    public static async Task<int> Run(string root,byte[] example,byte[] bundle)
    {
        var count=0;
        void Check(bool value,string text){if(!value) throw new InvalidOperationException(text);count++;}
        using var workspace=new CalendarWorkspace(Path.Combine(root,"calendar-search"),example,null,bundledFeed:bundle);
        var model=new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null);
        var now=DateTimeOffset.Parse("2026-11-14T12:00:00+09:00"); model.GoToMonth(2026,11); model.Tick(now);
        var results=model.FindSchedules("백은 언제 열리지?"); var release=results.First();
        Check(release.Event.Id=="kr:curated:v8-release" && release.Date==new DateOnly(2027,2,23),"The actual bundled expansion release is found beyond the displayed month");
        Check(results.Any(r=>r.Event.Id=="kr:curated:v8-early-access"),"Search separates early access from formal release");
        model.SetCalendarVisible(CalendarGroup.Patches,false);model.SetEventHidden(release.Event.Id,true);model.Tick(now);
        Check(model.FindSchedules("백은").Any(r=>r.Event.Id==release.Event.Id),"Hidden schedules remain discoverable without silently changing visibility");
        var before=workspace.State;model.JumpToSearchResult(release);model.Tick(now);
        Check(model.Year==2027 && model.MonthNumber==2 && model.SelectedDate==new DateOnly(2027,2,23) && model.SelectedEventId==release.Event.Id && model.SelectedPhaseId=="release","Search selection navigates the calendar and preserves the exact matching phase");
        Check(ReferenceEquals(before,workspace.State) && !model.IsCalendarVisible(CalendarGroup.Patches) && workspace.State.View.HiddenEventIds.Contains(release.Event.Id),"A search jump cannot rewrite hidden-event or category settings");
        Check(model.Search=="" && model.CalendarEvents.All(e=>e.Id!=release.Event.Id),"Search navigation does not filter the entire calendar or bypass persistent visibility");
        await workspace.FlushAsync();
        return count;
    }
}
