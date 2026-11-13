using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal sealed class ThemeScope : IDisposable
{
    private int colors;
    private int variables;
    public static bool IsLight(string theme) => theme is "paper" or "cloud";
    public static Vector4 Accent(string theme) => Hex(IsLight(theme) ? 0x3869d4 : theme == "graphite" ? 0x7edbc3 : 0x91aeff);
    public static Vector4 CalendarSurface(string theme, bool inMonth) => Hex(IsLight(theme)
        ? inMonth ? 0xffffff : 0xf1f4f9
        : theme == "graphite" ? inMonth ? 0x232629 : 0x1b1d20 : inMonth ? 0x131d36 : 0x0c1327);
    public static Vector4 BorderColor(string theme) => Hex(IsLight(theme) ? 0xdce3ee : theme == "graphite" ? 0x3b4144 : 0x2a385a);
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
        var background = light ? 0xf5f7fb : charcoal ? 0x191b1e : 0x0b1124;
        var surface = CalendarSurface(theme, true);
        var control = light ? 0xe8eef8 : charcoal ? 0x303538 : 0x233458;
        var muted = light ? 0x62718b : charcoal ? 0xadb7b6 : 0xa2b4dc;
        var accent = Accent(theme);
        Add(ImGuiCol.Text, Hex(light ? 0x233451 : charcoal ? 0xedf0ed : 0xe5edfc));
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f); ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f); ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f); ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        variables = 10;
    }
    private void Add(ImGuiCol key, Vector4 value) { ImGui.PushStyleColor(key, value); colors++; }
    public static Vector4 Hex(int value, float alpha = 1) => new((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f, alpha);
    public static Vector4 EventColor(string category, bool light) => Hex(category switch
    {
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
