using System.Globalization;
using System.Resources;
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
}
