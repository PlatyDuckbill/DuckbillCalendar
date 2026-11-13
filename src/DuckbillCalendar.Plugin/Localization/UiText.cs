using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal static class UiText
{
    private static readonly ResourceManager Resources = new("DuckbillCalendar.Plugin.Localization.Strings", typeof(UiText).Assembly);
    private static CultureInfo culture = CultureInfo.GetCultureInfo("ko");
    public static void SetLanguage(string language) => culture = CultureInfo.GetCultureInfo("ko");
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
    public static string LocalBoundary(CalendarBoundary boundary) => boundary.Kind == "date" && CalendarEngine.BoundaryDate(boundary) is { } date ? Date(date) : Boundary(boundary);
    public static string SubscriptionRemaining(CalendarBoundary boundary, DateTimeOffset now)
    {
        if (boundary.Kind == "instant" && CalendarEngine.StartInstant(boundary) is { } instant)
        {
            var remaining = instant - now;
            if (remaining <= TimeSpan.Zero) return "만료됨";
            var minutes = (long)Math.Ceiling(remaining.TotalMinutes);
            if (minutes >= 1440) return minutes / 1440 + "일" + (minutes % 1440 / 60 > 0 ? " " + minutes % 1440 / 60 + "시간" : "") + " 남음";
            if (minutes >= 60) return minutes / 60 + "시간" + (minutes % 60 > 0 ? " " + minutes % 60 + "분" : "") + " 남음";
            return minutes + "분 남음";
        }
        var days = CalendarEngine.BoundaryDate(boundary)?.DayNumber - CalendarEngine.Today(now).DayNumber;
        return days > 0 ? days + "일 남음" : days == 0 ? "오늘 만료 · 시각 미등록" : "만료됨";
    }
    public static string Period(EventPhase phase, bool personal = false)
    {
        string Format(CalendarBoundary b) => b.Kind == "date" ? LocalBoundary(b) + (!personal ? b.Certainty switch { "inferred" => Get("InferredSuffix"), "tentative" => Get("TentativeSuffix"), _ => "" } : "") : Boundary(b);
        if (phase.Point) return Format(phase.Start) + (!personal && phase.Start.Kind == "date" ? " · 시각 미발표" : "");
        if (personal && phase.Start.Kind == "unknown") return "만료일  " + Format(phase.End);
        if (phase.Start.Kind == "instant" && phase.End.Kind == "instant" && phase.Start.Certainty == "confirmed" && phase.End.Certainty == "confirmed" && CalendarEngine.BoundaryDate(phase.Start) == CalendarEngine.BoundaryDate(phase.End))
            return Date(CalendarEngine.BoundaryDate(phase.Start)!.Value) + "  " + ClockLabel(phase.Start) + "–" + ClockLabel(phase.End) + " KST";
        var dateOnly = !personal && (phase.Start.Kind == "date" || phase.End.Kind == "date");
        return Format(phase.Start) + " ~ " + Format(phase.End) + (dateOnly ? phase.Start.Kind == "date" && phase.End.Kind == "date" ? " · 시각 미발표" : " · 일부 시각 미정" : "");
    }
    public static string DisplayTitle(CalendarDisplayEntry entry) => entry.Group switch
    {
        CalendarDisplayGroup.Update => VersionTitle(entry.Primary.Event.Title) + " 업데이트·점검",
        CalendarDisplayGroup.PurchaseCampaign => "백은의 탐구자 예약·구매 혜택",
        _ => entry.Primary.Event.Title
    };
    public static string VersionTitle(string title) => Regex.Match(title, @"V\d+\.\d+", RegexOptions.CultureInvariant) is { Success: true } m ? m.Value : "게임";
    public static string DisplayMarker(CalendarDisplayEntry entry)
    {
        var label = entry.Group switch
        {
            CalendarDisplayGroup.Update => VersionTitle(entry.Primary.Event.Title) + " 점검·패치",
            CalendarDisplayGroup.PurchaseCampaign => "CE 예약·혜택",
            _ => CalendarMarker(entry.Primary with { Edge = entry.Edge })
        };
        if (entry.Ongoing || entry.Edge != "end") return label;
        if (SubscriptionTime.IsSubscription(entry.Category)) return SubscriptionTime.Name(entry.Category);
        if (entry.Category == "maintenance") return VersionTitle(entry.Primary.Event.Title) + " 점검";
        if (label.StartsWith("마감 ", StringComparison.Ordinal)) label = label[3..];
        foreach (var suffix in new[] { " 마감", " 종료" })
            if (label.EndsWith(suffix, StringComparison.Ordinal)) label = label[..^suffix.Length];
        return label;
    }
    public static string PurchaseRole(CalendarEvent item) => item.Id switch
    {
        "kr:event:771" => "콜렉터즈 에디션 예약", "kr:curated:v8-reservation-items" => "예약 특전 대상 구매", "kr:curated:v8-ce-discount" => "20% 할인", _ => item.Title
    };
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
    public static string AgendaRole(CalendarEvent item, EventPhase phase, string edge)
    {
        if (phase.Point && edge != "ongoing")
            return phase.Id switch { "early-access" => Get("HighlightEarlyAccess"), "release" => Get("HighlightRelease"), _ => Phase(item, phase) };
        var role = Get(edge == "ongoing" ? "OngoingRole" : edge == "end" ? "HighlightDeadline" : "StartShort");
        return item.Phases.Count > 1 ? role + " · " + Phase(item, phase) : role;
    }
    public static string CompactHighlight(ScheduleHighlight item, bool includeDate)
    {
        var role = item.Phase.Id == "early-access" ? Get("ShortEarlyAccess") : Get("ShortFormalRelease");
        return (includeDate ? item.Date.ToString("M/d", CultureInfo.InvariantCulture) + " " : "") +
            (item.Boundary.Kind == "instant" ? ClockLabel(item.Boundary) + " " : "") + role;
    }
    public static string CalendarMarker(CalendarItem item)
    {
        var e = item.Event; var p = item.Phase;
        if (e.IsPersonal && SubscriptionTime.IsSubscription(e.Category)) return SubscriptionTime.Name(e.Category) + " 만료";
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
        var title = e.Category == "maintenance" ? VersionTitle(e.Title) + " 점검" : e.Category == "patch" ? VersionTitle(e.Title) + " 업데이트" : e.Title;
        return item.Edge == "end" ? e.Category == "maintenance" ? title + " 종료" : "마감 " + title : title;
    }
}
