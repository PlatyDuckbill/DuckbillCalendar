using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal static class UiText
{
    private static readonly ResourceManager Resources = new("DuckbillCalendar.Plugin.Localization.Strings", typeof(UiText).Assembly);
    private static CultureInfo culture = CultureInfo.GetCultureInfo("ko");
    public static void SetLanguage(string language) => culture = CultureInfo.GetCultureInfo(language == "en" ? "en" : "ko");
    public static string Get(string key, params object[] args)
    {
        var text = Resources.GetString(key, culture) ?? key;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Time(DateTimeOffset time)
    {
        try { return time.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
        catch (ArgumentException) { return "—"; }
    }
    public static string Boundary(CalendarBoundary boundary)
    {
        var qualifier = boundary.Certainty switch { "inferred" => Get("InferredSuffix"), "tentative" => Get("TentativeSuffix"), _ => "" };
        if (boundary.Kind == "instant" && DateTimeOffset.TryParse(boundary.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            return Time(time) + " KST" + qualifier;
        if (boundary.Kind == "date" && DateOnly.TryParseExact(boundary.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Get("DateOnlyValue", Date(date)) + qualifier;
        return (boundary.DisplayText ?? Get(boundary.Kind == "condition" ? "ConditionalBoundary" : "UnknownBoundary")) + qualifier;
    }
    public static string Category(string category) => Get(category switch
    {
        "personal" => "CategoryPersonal", "subscription" or "retainer" or "bag" => "CategorySubscription",
        "maintenance" or "patch" => "CategoryPatch", "sale" or "discount" => "CategorySale",
        "pvp-season" or "pvp-series" or "reward" => "CategoryPvp", "notice" => "CategoryNotice", _ => "CategoryEvent"
    });
    public static string HighlightRole(ScheduleHighlight highlight)
    {
        if (highlight.Edge == "end" && highlight.Event.Category is not ("sale" or "discount"))
            return highlight.Phase.Label + " · " + Get("HighlightDeadline");
        var key = highlight.Edge == "end" ? "HighlightDeadline" : highlight.Phase.Id switch
        {
            "early-access" => "HighlightEarlyAccess", "release" => "HighlightRelease", _ => highlight.Event.Category switch
            {
                "pvp-season" => "HighlightPvpSeason", "pvp-series" => "HighlightPvpSeries",
                "maintenance" => "HighlightMaintenance", "patch" => "HighlightPatch", _ => null
            }
        };
        return key is null ? highlight.Phase.Label : Get(key);
    }
    public static string Phase(CalendarEvent e, EventPhase phase) => e.IsPersonal
        ? Get(e.Category is "subscription" or "retainer" or "bag" ? "LocalExpiryPhase" : "LocalPersonalPhase")
        : phase.Label;
    public static string DayLabel(DateOnly date) => date.ToString(culture.Name == "ko" ? "M월 d일 (ddd)" : "MMM d (ddd)", culture);
    public static string ClockLabel(CalendarBoundary boundary)
    {
        if (boundary.Kind == "instant" && DateTimeOffset.TryParse(boundary.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            return time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm", CultureInfo.InvariantCulture) + (boundary.Certainty == "tentative" ? Get("TentativeSuffix") : "");
        return boundary.Kind == "date" ? Get("TimeNotAnnouncedShort") : Boundary(boundary);
    }
    public static string AgendaRole(CalendarEvent item, EventPhase phase, string edge) => edge == "ongoing"
        ? Get("OngoingRole") + " · " + Phase(item, phase)
        : phase.Point ? phase.Id switch { "early-access" => Get("HighlightEarlyAccess"), "release" => Get("HighlightRelease"), _ => Phase(item, phase) }
        : Get(edge == "end" ? "EndShort" : "StartShort") + " · " + Phase(item, phase);
    public static string CompactHighlight(ScheduleHighlight item, bool includeDate)
    {
        var role = item.Phase.Id == "early-access" ? Get("ShortEarlyAccess") : Get("ShortFormalRelease");
        return (includeDate ? item.Date.ToString("M/d", CultureInfo.InvariantCulture) + " " : "") +
            (item.Boundary.Kind == "instant" ? ClockLabel(item.Boundary) + " " : "") + role;
    }
    public static string CalendarMarker(CalendarItem item)
    {
        var e = item.Event; var p = item.Phase;
        if (p.Id == "early-access") return ClockLabel(p.Start) + " " + Get("ShortEarlyAccess");
        if (p.Id == "release" && e.Id.StartsWith("kr:curated:v8-", StringComparison.Ordinal)) return "8.0 " + Get("ShortFormalRelease");
        if (e.Id == "kr:event:771") return Get(item.Edge == "end" ? "ShortPreorderDeadline" : "ShortPreorderStart");
        if (e.Id == "kr:curated:v8-reservation-items") return Get(item.Edge == "end" ? "ShortBonusDeadline" : "ShortBonusStart");
        if (e.Id == "kr:curated:v8-ce-discount") return Get(item.Edge == "end" ? "ShortDiscountDeadline" : "ShortDiscountStart");
        if (e.Id == "kr:curated:ccrc-2027-korea") return p.Id switch
        {
            "preliminaries" => Get("ShortCcrcPreliminary"), "finals" => Get("ShortCcrcFinal"),
            "registration" => Get(item.Edge == "end" ? "ShortCcrcRegistrationEnd" : "ShortCcrcRegistrationStart"),
            _ => Get(item.Edge == "end" ? "ShortCcrcSelectionEnd" : "ShortCcrcSelectionStart")
        };
        if (e.Category == "pvp-season" && p.Point)
        {
            var season = Regex.Match(e.Title, @"시즌\s*(\d+)", RegexOptions.CultureInvariant);
            if (season.Success) return Get("ShortPvpSeasonStart", season.Groups[1].Value);
        }
        return (p.Point ? Phase(e, p) : Get(item.Edge == "end" ? "EndShort" : "StartShort")) + " · " + e.Title;
    }
}
