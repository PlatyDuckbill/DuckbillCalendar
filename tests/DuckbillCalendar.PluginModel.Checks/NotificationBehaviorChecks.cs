using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

internal static class NotificationBehaviorChecks
{
    public static async Task<int> Run(string root, byte[] example)
    {
        var count = 0; void Check(bool value,string name) { if (!value) throw new InvalidOperationException(name); count++; }
        var now = DateTimeOffset.Parse("2026-11-13T12:00:00Z"); var path = Path.Combine(root,"notification-categories");
        var category = new UserCalendarCategory { Id = "user:raid", Name = "절바하 공대", Icon = "Trophy", Color = "#AABBCC" };
        using (var workspace = new CalendarWorkspace(path,example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            async Task Settle() { model.Tick(now); await workspace.FlushAsync(); model.Tick(now); }
            Check(model.State.View.UserCategories.Any(c => c.Id == "user:default-raid" && c.Icon == "Raid"),"An editable raid category is provided once on upgrade");
            model.SaveUserCategory(category); await Settle();
            model.SaveEntry(new() { Id = "raid", Title = "공대", CategoryId = category.Id, When = CalendarBoundary.Instant(now.AddHours(1)) }); await Settle();
            Check(model.CalendarEvents.Single(e => e.Id == "raid").Category == category.Id,"A custom personal category projects into the actual calendar");
            model.SetCategoryNotification(new() { CategoryId = category.Id, Enabled = true, StartMinutes = [60,10] }); await Settle();
            Check(NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Count == 2,"Category notification defaults apply to assigned raid schedules");
            model.SetQuery("",0,true); await Settle();
            Check(model.HasAlert(model.Events.Single(e => e.Id == "raid")) && model.CalendarEvents.Any(e => e.Id == "raid"),"Alert-only projection includes inherited category notification rules");
            model.SetScheduleNotification("raid","local","custom",[30],[]); await Settle();
            Check(model.IndividualNotificationCount == 1 && NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Single().LeadMinutes == 30,"Individual overrides are counted once and supersede the category");
            var toggleRaid = model.Events.Single(e => e.Id == "raid");
            model.SetScheduleNotificationEnabled(toggleRaid,toggleRaid.Phases[0],false); await Settle();
            Check(model.State.Notifications.Schedules.Single().Mode == "off" && model.State.Notifications.Schedules.Single().StartMinutes.SequenceEqual(new[]{30}),"List toggle retains custom times while disabled");
            model.SetScheduleNotificationEnabled(toggleRaid,toggleRaid.Phases[0],true); await Settle();
            Check(NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Single().LeadMinutes == 30,"Re-enabling a list row restores the custom time");
            model.SetCategoryNotificationEnabled(category.Id,false); await Settle();
            Check(!model.State.Notifications.Categories.Single().Enabled && model.State.Notifications.Categories.Single().StartMinutes.SequenceEqual(new[]{60,10}),"Category list toggle retains its multiple times");
            model.SetCategoryNotificationEnabled(category.Id,true); await Settle();
            model.SetScheduleNotification("raid","local","off",[],[]); await Settle();
            Check(NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Count == 0,"A single raid occurrence can silence its category");
            Check(!model.HasAlert(model.Events.Single(e => e.Id == "raid")) && model.CalendarEvents.All(e => e.Id != "raid"),"Explicitly silenced schedules are absent from alert-only projection");
            model.SetQuery("",0,false); await Settle();
            model.SetScheduleNotification("raid","local","inherit",[],[]); await Settle();
            Check(model.IndividualNotificationCount == 0 && NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Count == 2,"Restoring inheritance removes individual settings rather than making copies");
            var raid = model.Events.Single(e => e.Id == "raid");
            model.ToggleAlert(raid,raid.Phases[0],"start"); await Settle();
            Check(NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Count == 0,"Compatibility toggle can silence inherited alerts");
            model.ToggleAlert(raid,raid.Phases[0],"start"); await Settle();
            Check(NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Single().LeadMinutes == 60,"Compatibility toggle can restore an explicitly silenced schedule");
            model.SetScheduleNotification("raid","local","inherit",[],[]); await Settle();
            model.SaveUserCategory(category with { Name = "영식 고정팟" }); await Settle();
            Check(model.State.Entries.Single().CategoryId == category.Id && model.State.Notifications.Categories.Single().CategoryId == category.Id,"Renaming a category preserves schedule and rule identity");
            model.SetUserCategoryVisible(category.Id,false); await Settle();
            Check(model.CalendarEvents.All(e => e.Id != "raid") && NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Count == 2,"Hiding a custom category does not disable its notifications");
            model.SetUserCategoryVisible(category.Id,true); await Settle();
            model.SetScheduleNotification("raid","local","custom",[15],[]); await Settle();
            model.DeleteUserCategory(category.Id); await Settle();
            Check(model.State.View.UserCategories.All(c => c.Id != category.Id) && model.State.Entries.Single().CategoryId == "" && model.CalendarEvents.Single(e => e.Id == "raid").Category == "personal","Deleting a category keeps schedules and returns them to the personal category");
            Check(model.State.Notifications.Categories.Count == 0 && model.State.Notifications.Schedules.Count == 1 && NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Single().LeadMinutes == 15,"Deleting a category clears defaults but keeps individual overrides");
            var item = new InboxNotification { Id = NotificationPolicy.Id("test"), ProfileId = "default", Kind = "new-event", Title = "이벤트", CreatedAt = now };
            workspace.UpdateState(workspace.State with { NotificationState = new() { Inbox = [item] } }); model.Tick(now);
            Check(model.UnreadNotifications == 1,"Unread badge counts the active profile's inbox");
            model.MarkNotificationsRead(item.Id); await Settle(); model.ClearReadNotifications(); await Settle();
            Check(model.UnreadNotifications == 0 && model.State.NotificationState.Inbox.Count == 0,"Reading and clearing inbox history does not require deleting schedules");
            model.DeleteUserCategory("user:default-raid"); await Settle();
            Check(model.State.View.DefaultRaidCategoryInitialized && model.State.View.UserCategories.All(c => c.Id != "user:default-raid"),"Default raid category can be removed");
            model.RemovePersonalCategory(); await Settle();
            Check(model.State.View.PersonalCategoryRemoved && model.State.Entries.Single().Id == "raid" && model.State.Notifications.Schedules.Count == 1,"Removing the personal category preserves unclassified schedules and individual alerts");
        }
        using (var workspace = new CalendarWorkspace(path,example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            Check(workspace.State.Entries.Single().Id == "raid" && workspace.State.Notifications.Schedules.Single().StartMinutes.SequenceEqual(new[] {15}),"Custom category changes and schedule overrides survive restart");
            Check(workspace.State.View.UserCategories.All(c => c.Id != "user:default-raid") && workspace.State.View.PersonalCategoryRemoved,"Deleted default categories do not reappear on restart");
        }
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"category-migration"),example))
        {
            workspace.UpdateState(new LocalState { Entries = [new() { Id = "old-personal",Title = "기존 일정",When = CalendarBoundary.Instant(now.AddDays(1)) }],
                View = new() { Calendars = new() { Personal = false },CalendarAppearances = [new() { Group = CalendarGroup.Personal,Icon = "Shield",Color = "#123456" }] },
                Notifications = new() { Categories = [new() { CategoryId = "Personal",Enabled = true,StartMinutes = [30] }] } });
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            Check(model.State.Entries.Single().CategoryId == "user:default-personal" && model.State.View.UserCategories.Single(c => c.Id == "user:default-personal").Color == "#123456","Upgrade moves the old personal category into an ordinary removable category while preserving its style");
            Check(model.State.Notifications.Categories.Single().CategoryId == "user:default-personal" && model.State.View.HiddenUserCategoryIds.Contains("user:default-personal"),"Upgrade preserves personal category alarm and visibility choices");
            model.SaveEntry(new() { Id = "unclassified",Title = "분류 없는 일정",When = CalendarBoundary.Instant(now.AddDays(2)) }); model.Tick(now);
            Check(model.CalendarEvents.Any(e => e.Id == "unclassified" && e.Category == "personal"),"Unclassified schedules exist independently of the default personal category");
            model.DeleteUserCategory("user:default-personal","user:default-raid"); model.Tick(now);
            Check(model.State.Entries.Single(e => e.Id == "old-personal").CategoryId == "user:default-raid" && model.State.Entries.Single(e => e.Id == "unclassified").CategoryId.Length == 0,"Deleting a category moves only its schedules into the selected destination");
            model.DeleteUserCategory("user:default-raid","user:missing"); model.Tick(now);
            Check(model.State.View.UserCategories.Any(c => c.Id == "user:default-raid"),"An invalid move destination cannot silently discard a category");
            await workspace.FlushAsync();
        }
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"notification-profile-races"),example))
        {
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false,null); model.Tick(now);
            workspace.UpdateState(workspace.State with { Profiles = workspace.State.Profiles.Append(new() { Id = "other",Alias = "다른 계정" }).ToArray() });
            model.SetCategoryNotification(new() { CategoryId = "Events",Enabled = true,StartMinutes = [10] });
            model.SetScheduleNotification("test-schedule","phase","custom",[10],[]);
            workspace.UpdateState(workspace.State with { ActiveProfileId = "other" }); model.Tick(now);
            Check(model.State.Notifications.Categories.Count == 0 && model.State.Notifications.Schedules.Count == 0,"Queued notification edits cannot leak into a newly active profile");
            model.SetCategoryNotification(new() { CategoryId = "Events",Enabled = true,StartMinutes = [10] },"default");
            model.SetScheduleNotification("test-schedule","phase","custom",[10],[],"default"); model.Tick(now);
            Check(model.State.Notifications.Categories.Count == 0 && model.State.Notifications.Schedules.Count == 0,"A stale independent editor cannot save its original profile draft into another profile");
            model.SetCategoryNotification(new() { CategoryId = "user:deleted",Enabled = true,StartMinutes = [10] }); model.Tick(now);
            Check(model.State.Notifications.Categories.Count == 0,"A deleted category cannot be resurrected as an orphan notification rule");
            await workspace.FlushAsync();
        }
        return count;
    }
}
