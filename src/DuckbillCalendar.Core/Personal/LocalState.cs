namespace DuckbillCalendar.Core;

public sealed record AccountProfile
{
    public string Id { get; init; } = "default";
    public string Alias { get; init; } = "내 계정";
}

public sealed record PersonalEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProfileId { get; init; } = "default";
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "personal";
    public CalendarBoundary When { get; init; } = new();
    public string Note { get; init; } = "";
    public string CategoryId { get; init; } = "";
    public string SeriesId { get; init; } = "";
    public int? SeriesIndex { get; init; }
}

public sealed record AlertPreference
{
    public string ProfileId { get; init; } = "default";
    public string EventId { get; init; } = "";
    public string PhaseId { get; init; } = "";
    public string Edge { get; init; } = "end";
    public int LeadMinutes { get; init; } = 60;
    public bool Enabled { get; init; } = true;
}

public enum CalendarGroup { Events, Patches, Maintenance, Pvp, Competitions, Sales, Subscriptions, Personal, Notices }

public sealed record CalendarAppearance
{
    public CalendarGroup Group { get; init; }
    public string Icon { get; init; } = "";
    public string Color { get; init; } = "";
}

public sealed record HiddenSchedulePhase
{
    public string EventId { get; init; } = "";
    public string PhaseId { get; init; } = "";
}

public sealed record CalendarVisibility
{
    public bool Events { get; init; } = true;
    public bool Patches { get; init; } = true;
    public bool Maintenance { get; init; } = true;
    public bool Pvp { get; init; } = true;
    public bool Competitions { get; init; } = true;
    public bool Sales { get; init; } = true;
    public bool Subscriptions { get; init; } = true;
    public bool Personal { get; init; } = true;
}

public sealed record ViewSettings
{
    public CalendarVisibility Calendars { get; init; } = new();
    public bool ShowNoticePublications { get; init; }
    public string Language { get; init; } = "ko";
    public string Theme { get; init; } = "midnight";
    public bool FrontlineUpdatesEnabled { get; init; } = true;
    public bool ShowFrontlineInCalendar { get; init; }
    public IReadOnlyList<string> HiddenEventIds { get; init; } = [];
    public IReadOnlyList<HiddenSchedulePhase> HiddenPhases { get; init; } = [];
    public IReadOnlyList<CalendarAppearance> CalendarAppearances { get; init; } = [];
    public bool PeriodBars { get; init; }
    public bool PeriodBarsConfigured { get; init; }
    public bool ShowRewardBars { get; init; }
    public bool RemoteUpdatesEnabled { get; init; } = true;
    public int RemoteRefreshMinutes { get; init; } = 60;
    public bool PauseAutomaticUpdatesWhenBusy { get; init; } = true;
    // Read and discard removed settings so strict JSON parsing can restore older state.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideWindowInCombat { get => false; init { } }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideWindowInDuty { get => false; init { } }
    public string CurrentUrl { get; init; } = "";
    public bool LocalChatAlerts { get; init; }
    public bool ExampleMode { get; init; }
    public IReadOnlyList<UserCalendarCategory> UserCategories { get; init; } = [];
    public bool DefaultRaidCategoryInitialized { get; init; }
    public bool DefaultPersonalCategoryInitialized { get; init; }
    public bool PersonalCategoryRemoved { get; init; }
    public IReadOnlyList<string> HiddenUserCategoryIds { get; init; } = [];
}

public sealed record LocalState
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<AccountProfile> Profiles { get; init; } = [new()];
    public string ActiveProfileId { get; init; } = "default";
    public IReadOnlyList<PersonalEntry> Entries { get; init; } = [];
    public IReadOnlyList<AlertPreference> Alerts { get; init; } = [];
    public IReadOnlyList<string> SentAlertKeys { get; init; } = [];
    public ViewSettings View { get; init; } = new();
    public NotificationSettings Notifications { get; init; } = new();
    public NotificationState NotificationState { get; init; } = new();
}
