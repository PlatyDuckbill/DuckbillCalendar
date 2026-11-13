namespace DuckbillCalendar.Core;

public sealed record GameActivity(bool LoggedIn, bool PlayerLoaded, bool InCombat, bool InDuty, bool InPvP,
    bool Loading, bool Cutscene, uint TerritoryId = 0);

public enum AutomaticUpdateHold { None, LoggedOut, Loading, Cutscene, Combat, Duty, Settling }

/// <summary>Session-only policy: it never changes consent, due times, or the user's open-window state.</summary>
public sealed class GameActivityPolicy
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(15);
    private DateTimeOffset? quietSince;
    private bool wasHeld;
    public AutomaticUpdateHold Hold { get; private set; }
    public bool AllowAutomaticUpdates => Hold == AutomaticUpdateHold.None;

    public void Update(DateTimeOffset now, GameActivity activity, ViewSettings settings)
    {
        var reason = !activity.LoggedIn || !activity.PlayerLoaded ? AutomaticUpdateHold.LoggedOut
            : activity.Loading ? AutomaticUpdateHold.Loading : activity.Cutscene ? AutomaticUpdateHold.Cutscene
            : settings.PauseAutomaticUpdatesWhenBusy && activity.InCombat ? AutomaticUpdateHold.Combat
            : settings.PauseAutomaticUpdatesWhenBusy && (activity.InDuty || activity.InPvP) ? AutomaticUpdateHold.Duty
            : AutomaticUpdateHold.None;
        if (reason == AutomaticUpdateHold.None && !settings.PauseAutomaticUpdatesWhenBusy && (activity.InCombat || activity.InDuty || activity.InPvP)) { wasHeld = false; quietSince = null; }
        if (reason != AutomaticUpdateHold.None) { wasHeld = true; quietSince = null; }
        else if (wasHeld)
        {
            if (quietSince is null || now < quietSince) quietSince = now;
            if (now - quietSince < QuietPeriod) reason = AutomaticUpdateHold.Settling;
            else { wasHeld = false; quietSince = null; }
        }
        Hold = reason;
    }
}
