using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

internal static class NotificationCoordinatorChecks
{
    public static async Task Run(Action<bool,string> check, string root, byte[] example)
    {
        var now = DateTimeOffset.Parse("2026-11-13T12:00:00Z");
        LocalState Seed(bool chat = true) => new()
        {
            View = new() { RemoteUpdatesEnabled = false, LocalChatAlerts = chat },
            Entries = [new() { Id = "raid", Title = "공대 출발", When = CalendarBoundary.Instant(now.AddHours(1)) }],
            Notifications = new() { Enabled = true, Categories = [new() { CategoryId = "Personal", Enabled = true, StartMinutes = [60] }] }
        };
        async Task Drain(CalendarWorkspace workspace) { await workspace.FlushAsync(); workspace.Pump(); }
        var durablePath = Path.Combine(root,"notifications-durable");
        using (var workspace = new CalendarWorkspace(durablePath,example))
        using (var notifications = new NotificationCoordinator(workspace))
        {
            workspace.UpdateState(Seed()); await Drain(workspace); var delivered = 0;
            notifications.Tick(now,null,true, _ => delivered++);
            check(workspace.State.NotificationState.Inbox.Count == 0,"Logged-out notification ticks do not stage schedules");
            notifications.Tick(now,"session",false,_ => delivered++);
            check(workspace.State.NotificationState.Inbox.Count == 1 && delivered == 0,"Combat stages a durable inbox notification without chatting");
            notifications.Tick(now,"session",true,_ => delivered++);
            check(delivered == 0,"Unsaved inbox records cannot be delivered");
            await Drain(workspace); notifications.Tick(now,"session",false,_ => delivered++);
            check(delivered == 0,"Combat continues to defer saved pending chat");
            notifications.Tick(now,"session",true,_ => delivered++);
            check(workspace.HasUnsavedChanges && delivered == 0,"Chat consumption is saved before invoking delivery");
            await Drain(workspace); notifications.Tick(now,"session",true,items => delivered += items.Count);
            check(delivered == 1 && workspace.State.NotificationState.Inbox.Single().ChatState == "consumed","Leaving combat delivers one consumed notification");
            notifications.Tick(now.AddSeconds(31),"session",true,_ => delivered++); await Drain(workspace);
            check(delivered == 1,"Subsequent scans do not replay chat");
        }
        using (var workspace = new CalendarWorkspace(durablePath,example))
        using (var notifications = new NotificationCoordinator(workspace))
        {
            var delivered = 0; notifications.Tick(now,"new-session",true,_ => delivered++); await Drain(workspace);
            notifications.Tick(now.AddSeconds(31),"new-session",true,_ => delivered++);
            check(delivered == 0 && workspace.State.NotificationState.Inbox.Count == 1,"Restart preserves inbox and consumed receipts without replay");
        }
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"notifications-panel-only"),example))
        using (var notifications = new NotificationCoordinator(workspace))
        {
            workspace.UpdateState(Seed(false)); await Drain(workspace); var delivered = 0;
            notifications.Tick(now,"s",true,_ => delivered++); await Drain(workspace);
            notifications.Tick(now.AddSeconds(1),"s",true,_ => delivered++);
            check(workspace.State.NotificationState.Inbox.Count == 1 && delivered == 0,"Panel notifications work independently of chat opt-in");
        }
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"notifications-cancel-chat"),example))
        using (var notifications = new NotificationCoordinator(workspace))
        {
            workspace.UpdateState(Seed()); await Drain(workspace); var delivered = 0;
            notifications.Tick(now,"s",false,_ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { LocalChatAlerts = false } }); await Drain(workspace);
            notifications.Tick(now.AddSeconds(1),"s",true,_ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { LocalChatAlerts = true } }); await Drain(workspace);
            notifications.Tick(now.AddSeconds(2),"s",true,_ => delivered++);
            check(delivered == 0 && workspace.State.NotificationState.Inbox.Single().ChatState == "none","Turning chat off cancels its backlog without deleting inbox history");
        }
        using (var workspace = new CalendarWorkspace(Path.Combine(root,"notifications-revalidate"),example))
        using (var notifications = new NotificationCoordinator(workspace))
        {
            workspace.UpdateState(Seed()); await Drain(workspace); var delivered = 0;
            notifications.Tick(now,"s",true,_ => delivered++); await Drain(workspace);
            notifications.Tick(now,"s",true,_ => delivered++); await Drain(workspace);
            workspace.UpdateState(workspace.State with { Entries = [workspace.State.Entries[0] with { When = CalendarBoundary.Instant(now.AddHours(2)) }] }); await Drain(workspace);
            notifications.Tick(now,"s",true,_ => delivered++);
            check(delivered == 0,"Changing the event time invalidates chat already waiting for persistence");
            notifications.Tick(now.AddHours(1),"s",true,_ => delivered++); await Drain(workspace);
            notifications.Tick(now.AddHours(1),"s",true,_ => delivered++); await Drain(workspace);
            notifications.Tick(now.AddHours(1),"s",true,items => delivered += items.Count);
            check(delivered == 1 && workspace.State.NotificationState.Inbox.Count == 2,"The updated occurrence later produces its own notification");
        }
        var failedPath = Path.Combine(root,"notifications-failed-save");
        using (var workspace = new CalendarWorkspace(failedPath,example))
        using (var notifications = new NotificationCoordinator(workspace))
        {
            workspace.UpdateState(Seed()); await Drain(workspace); var delivered = 0;
            using (var hold = new FileStream(Path.Combine(failedPath,"state.json"),FileMode.Open,FileAccess.Read,FileShare.None))
            {
                notifications.Tick(now,"s",true,_ => delivered++); await Drain(workspace);
                notifications.Tick(now.AddSeconds(1),"s",true,_ => delivered++);
                check(delivered == 0 && workspace.HasUnsavedChanges,"Failed durable storage prevents chat delivery");
            }
            workspace.UpdateState(workspace.State); await Drain(workspace);
            notifications.Tick(now.AddSeconds(2),"s",true,_ => delivered++); await Drain(workspace);
            notifications.Tick(now.AddSeconds(2),"s",true,items => delivered += items.Count);
            check(delivered == 1,"Recovery can deliver the saved inbox notification once");
        }
        var migrationPath = Path.Combine(root,"notification-migration"); Directory.CreateDirectory(migrationPath);
        var legacyNode = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(Seed(),CalendarJson.Options))!;
        legacyNode.AsObject().Remove("notifications"); legacyNode.AsObject().Remove("notificationState");
        legacyNode["view"]!.AsObject().Remove("userCategories"); legacyNode["view"]!.AsObject().Remove("hiddenUserCategoryIds");
        foreach (var entry in legacyNode["entries"]!.AsArray()) entry!.AsObject().Remove("categoryId");
        await File.WriteAllBytesAsync(Path.Combine(migrationPath,"state.json"),JsonSerializer.SerializeToUtf8Bytes(legacyNode,CalendarJson.Options));
        using (var workspace = new CalendarWorkspace(migrationPath,example))
        {
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { Theme = "graphite" } }); await Drain(workspace);
            var backupBytes = await File.ReadAllBytesAsync(Path.Combine(migrationPath,"state.pre-notifications-v1.json"));
            using var backup = JsonDocument.Parse(backupBytes);
            check(!backup.RootElement.TryGetProperty("notifications",out _) && backup.RootElement.GetProperty("entries").GetArrayLength() == 1,"Upgrade keeps an original-format snapshot before writing new notification fields");
            var beforeRelease = await File.ReadAllBytesAsync(Path.Combine(migrationPath,"state.before-v032.json"));
            using var releaseBackup = JsonDocument.Parse(beforeRelease);
            check(releaseBackup.RootElement.GetProperty("entries").GetArrayLength() == 1,"Release transition preserves a valid pre-write state independently of ordinary rolling backups");
            workspace.UpdateState(workspace.State with { Entries = [] }); await Drain(workspace);
            check((await File.ReadAllBytesAsync(Path.Combine(migrationPath,"state.pre-notifications-v1.json"))).SequenceEqual(backupBytes),"Later saves cannot overwrite the pre-upgrade snapshot");
            check((await File.ReadAllBytesAsync(Path.Combine(migrationPath,"state.before-v032.json"))).SequenceEqual(beforeRelease),"Later recurring changes cannot overwrite the release transition snapshot");
        }
    }
}
