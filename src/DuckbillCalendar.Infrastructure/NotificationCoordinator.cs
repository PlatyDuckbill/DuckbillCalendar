using DuckbillCalendar.Core;

namespace DuckbillCalendar.Infrastructure;

/// <summary>Stages durable inbox records independently of chat. Chat is consumed durably before the callback.</summary>
public sealed class NotificationCoordinator(CalendarWorkspace workspace) : IDisposable
{
    private readonly List<string> reserved = [];
    private DateTimeOffset nextScan, nextChat;
    private DateTimeOffset? lastClock;
    private string? session;
    private bool disposed;
    public void Tick(DateTimeOffset now, string? sessionKey, bool canDeliver, Action<IReadOnlyList<InboxNotification>> deliver)
    {
        if (disposed || string.IsNullOrEmpty(sessionKey)) { reserved.Clear(); session = sessionKey; return; }
        if (session != sessionKey) { reserved.Clear(); session = sessionKey; nextScan = now; }
        if (lastClock > now) nextScan = nextChat = now;
        lastClock = now;
        var state = workspace.State;
        if (now >= nextScan)
        {
            nextScan = After(now, TimeSpan.FromSeconds(1));
            var next = NotificationPolicy.Stage(workspace.Feed, state, workspace.ActiveHealth,
                workspace.FeedSource == "remote" && workspace.ActiveHealth is not null, now);
            if (!ReferenceEquals(state, next)) { workspace.UpdateState(next); state = next; }
        }
        if (!state.View.LocalChatAlerts || !state.Notifications.Enabled)
        {
            reserved.Clear();
            if (state.NotificationState.Inbox.Any(n => n.ChatState == "pending")) workspace.UpdateState(state with { NotificationState = state.NotificationState with
            { Inbox = state.NotificationState.Inbox.Select(n => n.ChatState == "pending" ? n with { ChatState = "none" } : n).ToArray() } });
            return;
        }
        if (workspace.HasUnsavedChanges || !canDeliver) return;
        if (reserved.Count > 0)
        {
            var items = state.NotificationState.Inbox.Where(n => reserved.Contains(n.Id) && n.ChatState == "consumed" &&
                NotificationPolicy.Relevant(n, workspace.Feed, state, workspace.ActiveHealth, now)).ToArray();
            reserved.Clear();
            if (items.Length > 0) { nextChat = After(now, TimeSpan.FromSeconds(30)); deliver(items); }
            return;
        }
        if (now < nextChat) return;
        var pending = state.NotificationState.Inbox.Where(n => n.ChatState == "pending" && (n.ProfileId.Length == 0 || n.ProfileId == state.ActiveProfileId)).Take(5).ToArray();
        if (pending.Length == 0) return;
        var ids = pending.Select(n => n.Id).ToHashSet(); reserved.AddRange(ids);
        workspace.UpdateState(state with { NotificationState = state.NotificationState with
        { Inbox = state.NotificationState.Inbox.Select(n => ids.Contains(n.Id) ? n with { ChatState = "consumed" } : n).ToArray() } });
    }
    public void Dispose() { disposed = true; reserved.Clear(); }
    private static DateTimeOffset After(DateTimeOffset now, TimeSpan duration) => DateTimeOffset.MaxValue - now < duration ? DateTimeOffset.MaxValue : now.Add(duration);
}
