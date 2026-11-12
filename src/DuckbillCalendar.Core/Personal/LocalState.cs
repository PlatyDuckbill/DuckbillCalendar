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
    public string Theme { get; init; } = "graphite";
    public bool PeriodBars { get; init; }
    public bool PeriodBarsConfigured { get; init; }
    public bool ShowRewardBars { get; init; }
    public bool RemoteUpdatesEnabled { get; init; }
    public string CurrentUrl { get; init; } = "";
    public bool LocalChatAlerts { get; init; }
    public bool ExampleMode { get; init; }
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
}
