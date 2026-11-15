using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal sealed class ThemeScope : IDisposable
{
    private int colors;
    private int variables;
    public static bool IsLight(string theme) => theme is "paper" or "cloud";
    public static Vector4 Accent(string theme) => Hex(IsLight(theme) ? 0x3869d4 : theme == "graphite" ? 0x8acdbd : 0x91aeff);
    public static Vector4 DateSelection(string theme) => Hex(IsLight(theme) ? 0x285bc5 : theme == "graphite" ? 0x8cbfff : 0xb9a7ff);
    public static Vector4 CalendarSurface(string theme, bool inMonth) => Hex(IsLight(theme)
        ? inMonth ? 0xffffff : 0xf1f4f9
        : theme == "graphite" ? inMonth ? 0x24282a : 0x1c2022 : inMonth ? 0x131d33 : 0x10182c);
    public static Vector4 BorderColor(string theme) => Hex(IsLight(theme) ? 0xdce3ee : theme == "graphite" ? 0x454e51 : 0x2a3854);
    public static Vector4 WeekendColor(string theme, int column) => Hex(column == 0
        ? IsLight(theme) ? 0xb34e64 : 0xe0a0ad
        : IsLight(theme) ? 0x436cba : 0x9ab9ef);
    public static Vector4 CalendarDaySurface(string theme, bool inMonth, int column)
    {
        var surface = CalendarSurface(theme, inMonth);
        return column is 0 or 6
            ? Vector4.Lerp(surface, WeekendColor(theme, column), inMonth ? IsLight(theme) ? .06f : .07f : .035f)
            : surface;
    }
    public static (Vector4 Background, Vector4 Foreground) StatusBadgeColors(string status, bool light) => status switch
    {
        "만료" => (Hex(light ? 0xb92d43 : 0xf58b9b), Hex(light ? 0xffffff : 0x35121d)),
        "마감" => (Hex(light ? 0x92570d : 0xe9b75e), Hex(light ? 0xffffff : 0x30220c)),
        _ => (Hex(light ? 0x536274 : 0x8fa6bd), Hex(light ? 0xffffff : 0x142434))
    };
    public ThemeScope(string theme)
    {
        var light = IsLight(theme);
        // Own the complete palette; game/Dalamud title, table and separator colors must not leak into a theme.
        var charcoal = theme == "graphite";
        var background = light ? 0xf5f7fb : charcoal ? 0x191c1e : 0x0c1323;
        var surface = CalendarSurface(theme, true);
        var control = light ? 0xe8eef8 : charcoal ? 0x303639 : 0x233458;
        var muted = light ? 0x62718b : charcoal ? 0xa8b1b2 : 0xa2b4dc;
        var accent = Accent(theme);
        Add(ImGuiCol.Text, Hex(light ? 0x233451 : charcoal ? 0xeef0ec : 0xe5edfc));
        Add(ImGuiCol.TextDisabled, Hex(muted));
        Add(ImGuiCol.WindowBg, Hex(background)); Add(ImGuiCol.ChildBg, Hex(background)); Add(ImGuiCol.PopupBg, surface);
        Add(ImGuiCol.TitleBg, Hex(background)); Add(ImGuiCol.TitleBgActive, Hex(background)); Add(ImGuiCol.TitleBgCollapsed, Hex(background));
        Add(ImGuiCol.FrameBg, Hex(control)); Add(ImGuiCol.FrameBgHovered, accent with { W = .22f }); Add(ImGuiCol.FrameBgActive, accent with { W = .3f });
        Add(ImGuiCol.Button, Hex(control)); Add(ImGuiCol.ButtonHovered, accent with { W = .22f }); Add(ImGuiCol.ButtonActive, accent with { W = .34f });
        Add(ImGuiCol.Header, accent with { W = .16f }); Add(ImGuiCol.HeaderHovered, accent with { W = .23f }); Add(ImGuiCol.HeaderActive, accent with { W = .3f });
        Add(ImGuiCol.Border, BorderColor(theme)); Add(ImGuiCol.Separator, BorderColor(theme));
        Add(ImGuiCol.CheckMark, accent); Add(ImGuiCol.SliderGrab, accent); Add(ImGuiCol.SliderGrabActive, accent);
        Add(ImGuiCol.TableHeaderBg, Hex(control)); Add(ImGuiCol.TableRowBg, surface with { W = .3f }); Add(ImGuiCol.TableRowBgAlt, Hex(control, .22f));
        Add(ImGuiCol.TableBorderLight, BorderColor(theme) with { W = .7f }); Add(ImGuiCol.TableBorderStrong, BorderColor(theme));
        Add(ImGuiCol.ScrollbarBg, Hex(background)); Add(ImGuiCol.ScrollbarGrab, Hex(light ? 0xb7c6dc : charcoal ? 0x555f62 : 0x3e527d));
        Add(ImGuiCol.ModalWindowDimBg, Hex(0x030609, .55f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ImGui.GetFontSize() * .2f); ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, ImGui.GetFontSize() * .28f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, ImGui.GetFontSize() * .28f); ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, ImGui.GetFontSize() * .28f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f); ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        variables = 12;
    }
    private void Add(ImGuiCol key, Vector4 value) { ImGui.PushStyleColor(key, value); colors++; }
    public static Vector4 Hex(int value, float alpha = 1) => new((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f, alpha);
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
    public void Dispose() { if (colors > 0) ImGui.PopStyleColor(colors); if (variables > 0) ImGui.PopStyleVar(variables); colors = variables = 0; }
}
