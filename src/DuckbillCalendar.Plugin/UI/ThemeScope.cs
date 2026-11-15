using System.Numerics;

namespace DuckbillCalendar.Plugin;

internal sealed class ThemeScope : IDisposable
{
    private readonly GameThemeScope scope;
    public static bool IsLight(string theme) => theme is "pearl" or "paper" or "cloud" or "light";
    private static GameThemePalette Palette(string theme) => GameThemeScope.Current is { } p && p.Light == IsLight(theme) ? p : GameThemePalette.For(IsLight(theme));
    public static Vector4 Accent(string theme) => Palette(theme).Accent;
    public static Vector4 DateSelection(string theme) => Accent(theme);
    public static Vector4 CalendarSurface(string theme, bool inMonth)
    {
        var p = Palette(theme);
        return (inMonth ? p.Surface : p.Inset) with { W = inMonth ? .34f : .4f };
    }
    public static Vector4 BorderColor(string theme) => Palette(theme).Border;
    public static Vector4 WeekendColor(string theme, int column) => Hex(column == 0
        ? IsLight(theme) ? 0x904e55 : 0xd6a4a8 : IsLight(theme) ? 0x4f6383 : 0xa7bad3);
    public static Vector4 CalendarDaySurface(string theme, bool inMonth, int column)
    {
        var surface = CalendarSurface(theme, inMonth);
        if (column is not (0 or 6)) return surface;
        var mixed = Vector4.Lerp(surface, WeekendColor(theme, column), inMonth ? .06f : .025f);
        return mixed with { W = surface.W };
    }
    public static (Vector4 Background, Vector4 Foreground) StatusBadgeColors(string status, bool light) => status switch
    {
        "만료" => (Hex(light ? 0x9e3840 : 0xe3999e), Hex(light ? 0xffffff : 0x35121d)),
        "마감" => (Hex(light ? 0x856022 : 0xdec188), Hex(light ? 0xffffff : 0x30220c)),
        _ => (Hex(light ? 0x60636a : 0xa6abb4), Hex(light ? 0xffffff : 0x24272c))
    };
    public ThemeScope(string theme, int brightness = GameThemePalette.DefaultBrightness, int depth = GameThemePalette.DefaultDepth) => scope = new GameThemeScope(IsLight(theme), brightness, depth);
    public static Vector4 Hex(int value, float alpha = 1) => GameThemePalette.Hex((uint)value, alpha);
    public static Vector4 EventColor(string category, bool light) => Hex(category switch
    {
        "housing" => light ? 0x866126 : 0xe2c080,
        "personal" => light ? 0x805d25 : 0xd8bb79,
        "subscription" or "retainer" or "bag" => light ? 0xb53746 : 0xff8d9b,
        "patch" => light ? 0x365f80 : 0x9dbddc,
        "maintenance" => light ? 0x4d6375 : 0x93a8b8,
        "pvp-season" or "pvp-series" or "reward" => light ? 0x8d4e62 : 0xdba3bd,
        "in-game-event" => light ? 0x356c64 : 0x91cbbf,
        "seasonal-event" => light ? 0x9a6234 : 0xe0b27e,
        "sale" or "discount" => light ? 0x715785 : 0xc4afd8,
        "competition" => light ? 0x8a6221 : 0xe7be74,
        "notice" => light ? 0x59665f : 0xa5adae, _ => light ? 0x38684b : 0xa4c8ae
    });
    public void Dispose() => scope.Dispose();
}
