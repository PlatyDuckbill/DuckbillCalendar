using DuckbillCalendar.Core;

namespace DuckbillCalendar.Infrastructure;

/// <summary>
/// UI/framework-thread local delivery coordinator. Durable consumption precedes delivery:
/// a crash or session change after persistence can lose an alert, but cannot replay it.
/// No game API, network request or background delivery callback is used here.
/// </summary>
public sealed class LocalAlertCoordinator(CalendarWorkspace workspace) : IDisposable
{
    public const int MaximumLedgerKeys = 10_000;
    public const int MaximumPending = 20;
    public const int BatchSize = 5;
    private readonly List<PlannedAlert> pending = [];
    private string? session;
    private string? profile;
    private DateTimeOffset nextScan;
    private DateTimeOffset nextStage;
    private DateTimeOffset nextDelivery;
    private DateTimeOffset? previousClock;
    private bool disposed;

    public string StatusCode { get; private set; } = "Idle";
    public int PendingCount => pending.Count;

    /// <remarks>The caller must Pump the workspace before each Tick, and invoke on one thread.</remarks>
    public void Tick(DateTimeOffset now, string? sessionKey, bool canDeliver, Action<PlannedAlert> deliver)
    {
        if (disposed) return;
        var state = workspace.State;
        if (string.IsNullOrWhiteSpace(sessionKey) || !state.View.LocalChatAlerts)
        {
            pending.Clear(); session = sessionKey; profile = state.ActiveProfileId;
            StatusCode = string.IsNullOrWhiteSpace(sessionKey) ? "NoSession" : "Disabled";
            return;
        }
        if (session != sessionKey || profile != state.ActiveProfileId)
        {
            pending.Clear(); session = sessionKey; profile = state.ActiveProfileId;
            nextScan = now;
        }
        if (previousClock is { } previous && now < previous)
            nextScan = nextStage = nextDelivery = now;
        previousClock = now;

        bool deliveryFailed = false;
        if (pending.Count > 0)
        {
            if (workspace.HasUnsavedChanges) StatusCode = "AwaitingPersistence";
            else if (!canDeliver) StatusCode = "Suppressed";
            else if (now < nextDelivery) StatusCode = "RateLimited";
            else
            {
                var ledger = state.SentAlertKeys.ToHashSet(StringComparer.Ordinal);
                var events = CalendarEngine.Merge(workspace.Feed, state).ToDictionary(e => e.Id, StringComparer.Ordinal);
                int delivered = 0;
                foreach (var staged in pending.ToArray())
                {
                    if (!ReferenceEquals(state, workspace.State) || workspace.HasUnsavedChanges) break;
                    if (!ledger.Contains(staged.Key) || !TryRevalidate(staged, state, events, now, out var current))
                    {
                        pending.Remove(staged);
                        continue;
                    }
                    if (delivered >= BatchSize) break;
                    // Removal and the durable key both precede a possibly partly-successful callback.
                    pending.Remove(staged);
                    delivered++;
                    try { deliver(current); }
                    catch (Exception) { deliveryFailed = true; }
                }
                if (delivered > 0)
                {
                    nextDelivery = After(now, TimeSpan.FromSeconds(30));
                    StatusCode = deliveryFailed ? "DeliveryFailed" : "Delivered";
                }
                else if (pending.Count == 0) StatusCode = "Idle";
            }
        }

        // Planning happens at most once a second, with five newly consumed keys per thirty seconds.
        if (now >= nextScan && now >= nextStage)
        {
            nextScan = After(now, TimeSpan.FromSeconds(1));
            state = workspace.State;
            if (state.SentAlertKeys.Count >= MaximumLedgerKeys) StatusCode = "LedgerFull";
            else if (pending.Count >= MaximumPending) StatusCode = "QueueFull";
            else
            {
                var available = Math.Min(BatchSize, Math.Min(MaximumPending - pending.Count, MaximumLedgerKeys - state.SentAlertKeys.Count));
                var candidates = AlertPlanner.Plan(workspace.Feed, state, now, workspace.ActiveHealth)
                    .Where(a => pending.All(p => p.Key != a.Key)).Take(available).ToArray();
                if (candidates.Length > 0)
                {
                    try
                    {
                        workspace.UpdateState(state with { SentAlertKeys = state.SentAlertKeys.Concat(candidates.Select(a => a.Key)).ToArray() });
                        pending.AddRange(candidates);
                        nextStage = After(now, TimeSpan.FromSeconds(30));
                        StatusCode = "AwaitingPersistence";
                    }
                    catch (InvalidDataException) { StatusCode = "StateRejected"; }
                }
            }
        }
        if (deliveryFailed) StatusCode = "DeliveryFailed";
    }

    private bool TryRevalidate(PlannedAlert staged, LocalState state, IReadOnlyDictionary<string, CalendarEvent> events, DateTimeOffset now, out PlannedAlert current)
    {
        current = staged;
        if (state.ActiveProfileId != staged.ProfileId || !events.TryGetValue(staged.EventId, out var item) ||
            !HealthFreshness.ForEvent(workspace.Feed, workspace.ActiveHealth, item, now).IsFresh) return false;
        var phase = item.Phases.FirstOrDefault(p => p.Id == staged.PhaseId);
        if (phase == null) return false;
        var boundary = staged.Edge == "start" ? phase.Start : phase.End;
        if (boundary.Kind != "instant" || boundary.Certainty != "confirmed") return false;
        var at = staged.Edge == "start" ? CalendarEngine.StartInstant(phase) : CalendarEngine.EndExclusive(phase);
        if (at != staged.BoundaryAt) return false;
        if (!state.Alerts.Any(a => a.Enabled && a.ProfileId == staged.ProfileId && a.EventId == staged.EventId &&
            a.PhaseId == staged.PhaseId && a.Edge == staged.Edge &&
            AlertPlanner.Key(a.ProfileId, a.EventId, a.PhaseId, a.Edge, at.Value, a.LeadMinutes) == staged.Key)) return false;
        current = staged with { Title = item.Title };
        return true;
    }

    private static DateTimeOffset After(DateTimeOffset now, TimeSpan duration) =>
        DateTimeOffset.MaxValue - now < duration ? DateTimeOffset.MaxValue : now.Add(duration);

    public void Dispose()
    {
        disposed = true;
        pending.Clear();
        StatusCode = "Disposed";
    }
}
