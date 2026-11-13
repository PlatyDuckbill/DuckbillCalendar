namespace DuckbillCalendar.Core;

public static class SubscriptionTime
{
    public static bool IsSubscription(string kind) => kind is "subscription" or "retainer" or "bag";
    public static string Name(string kind) => kind switch { "retainer" => "집사 이용권", "bag" => "초코보 가방 이용권", _ => "정액제" };

    // An active service extends from its expiry. An expired service starts from today/now.
    // Preserve known precision; a date-only expiry must never acquire an invented time.
    public static CalendarBoundary Extend(CalendarBoundary? expiry, int days, DateTimeOffset now) => ExtendDuration(expiry, days, 0, now);

    public static bool NeedsExistingExpiryTime(CalendarBoundary? expiry, int hours, DateTimeOffset now, int minutes = 0) =>
        (hours > 0 || minutes > 0) && expiry?.Kind == "date" && CalendarEngine.BoundaryDate(expiry) is { } date && date >= CalendarEngine.Today(now);

    public static CalendarBoundary ExtendDuration(CalendarBoundary? expiry, int days, int hours, DateTimeOffset now, TimeOnly? existingExpiryTime = null)
        => ExtendDuration(expiry, days, hours, 0, now, existingExpiryTime);

    public static CalendarBoundary ExtendDuration(CalendarBoundary? expiry, int days, int hours, int minutes, DateTimeOffset now, TimeOnly? existingExpiryTime = null)
    {
        var totalMinutes = days * 1440L + hours * 60L + minutes;
        if (days < 0 || hours < 0 || minutes < 0 || totalMinutes is < 1 or > 3650 * 1440L) throw new ArgumentOutOfRangeException(nameof(days));
        if (expiry?.Kind == "instant" && CalendarEngine.StartInstant(expiry) is { } instant)
            return CalendarBoundary.Instant((instant > now ? instant : now).AddMinutes(totalMinutes));
        var today = CalendarEngine.Today(now);
        var date = expiry is not null ? CalendarEngine.BoundaryDate(expiry) : null;
        if (hours == 0 && minutes == 0) return CalendarBoundary.Date((date > today ? date.Value : today).AddDays(days));
        var basis = now;
        if (NeedsExistingExpiryTime(expiry, hours, now, minutes))
        {
            if (existingExpiryTime is not { } time) throw new InvalidOperationException("ExistingExpiryTimeRequired");
            var knownExpiry = new DateTimeOffset(date!.Value.ToDateTime(time), TimeSpan.FromHours(9));
            if (knownExpiry > basis) basis = knownExpiry;
        }
        return CalendarBoundary.Instant(basis.AddMinutes(totalMinutes));
    }
}
