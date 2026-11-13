using System.Globalization;
using System.Numerics;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal static class CalendarAppearanceStyle
{
    internal static readonly (CalendarGroup Group, string Label, string Category, UiIcon Icon)[] Groups =
    [
        (CalendarGroup.Events, "이벤트", "in-game-event", UiIcon.Gift),
        (CalendarGroup.Patches, "업데이트·출시", "patch", UiIcon.Update),
        (CalendarGroup.Maintenance, "게임 서버 점검", "maintenance", UiIcon.Server),
        (CalendarGroup.Pvp, "PvP 시즌·보상", "pvp-season", UiIcon.Flag),
        (CalendarGroup.Competitions, "대회", "competition", UiIcon.Trophy),
        (CalendarGroup.Sales, "할인·예약", "sale", UiIcon.Tag),
        (CalendarGroup.Subscriptions, "이용권 만료", "subscription", UiIcon.Clock),
        (CalendarGroup.Personal, "미분류", "personal", UiIcon.Person)
    ];
    internal static readonly (UiIcon Icon, string Label)[] Icons =
    [(UiIcon.Gift, "선물"), (UiIcon.Update, "업데이트"), (UiIcon.Server, "서버"), (UiIcon.Flag, "깃발"),
        (UiIcon.Trophy, "트로피"), (UiIcon.Tag, "태그"), (UiIcon.Clock, "시계"), (UiIcon.Person, "사람"),
        (UiIcon.Raid, "공격대"), (UiIcon.Swords, "교차 검"), (UiIcon.Shield, "방패")];

    internal static CalendarGroup Group(string category) => category switch
    {
        "personal" => CalendarGroup.Personal, "subscription" or "retainer" or "bag" => CalendarGroup.Subscriptions,
        "maintenance" => CalendarGroup.Maintenance, "patch" => CalendarGroup.Patches,
        "pvp-season" or "pvp-series" or "reward" => CalendarGroup.Pvp, "competition" or "notice" => CalendarGroup.Competitions,
        "sale" or "discount" => CalendarGroup.Sales, _ => CalendarGroup.Events
    };
    internal static (UiIcon Icon, Vector4 Color) Resolve(ViewSettings view, string category)
    {
        if (view.UserCategories.FirstOrDefault(c => c.Id == category) is { } custom)
            return (Enum.Parse<UiIcon>(custom.Icon), ThemeScope.Hex(int.Parse(custom.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        var group = Group(category);
        var appearance = view.CalendarAppearances.FirstOrDefault(a => a.Group == group);
        var icon = appearance is not null && Enum.TryParse<UiIcon>(appearance.Icon, out var selected)
            ? selected : Groups.First(g => g.Group == group).Icon;
        var color = appearance?.Color is { Length: 7 } value
            ? ThemeScope.Hex(int.Parse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            : ThemeScope.EventColor(Groups.First(g => g.Group == group).Category, ThemeScope.IsLight(view.Theme));
        return (icon, color);
    }
}
