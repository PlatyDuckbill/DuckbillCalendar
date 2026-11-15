using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

// Kept identical across the three plugins apart from the namespace. No runtime dependency between plugins.
internal sealed record GameThemePalette(bool Light, Vector4 Surface, Vector4 Inset, Vector4 Field,
    Vector4 Input, Vector4 Text, Vector4 Muted, Vector4 Border, Vector4 Accent, Vector4 Selection)
{
    public const float OverlayOpacity = .985f;
    private static readonly GameThemePalette Smoke = new(false, Hex(0x383b40, .985f), Hex(0x24272c, .58f), Hex(0x494c52), Hex(0x292c31),
        Hex(0xf2efe8), Hex(0xc0bcb5), Hex(0x85827b), Hex(0xf0cb78), Hex(0x595d64));
    private static readonly GameThemePalette Pearl = new(true, Hex(0xd0d2d3, .99f), Hex(0xb7bbbf, .50f), Hex(0xbec1c4), Hex(0xe1e3e4),
        Hex(0x40352f), Hex(0x645c56), Hex(0x8d8982), Hex(0x916521), Hex(0xaeb2b7));
    public static GameThemePalette For(bool light) => light ? Pearl : Smoke;
    public Vector4 Hover => Vector4.Lerp(Field, Light ? Vector4.One : Hex(0x8b8d90), .22f);
    public Vector4 Pressed => Vector4.Lerp(Input, Field, .35f);
    public static Vector4 Hex(uint rgb, float alpha = 1) => new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, alpha);
}

internal sealed class GameThemeScope : IDisposable
{
    [ThreadStatic] private static GameThemePalette? current;
    public static GameThemePalette? Current => current;
    private readonly GameThemePalette? previous = current;
    private int colors, vars;
    public GameThemeScope(bool light)
    {
        var p = GameThemePalette.For(light); current = p;
        Color(ImGuiCol.Text, p.Text); Color(ImGuiCol.TextDisabled, p.Muted);
        Color(ImGuiCol.WindowBg, p.Surface); Color(ImGuiCol.ChildBg, Vector4.Zero);
        Color(ImGuiCol.PopupBg, p.Surface with { W = .99f });
        Color(ImGuiCol.TitleBg, p.Inset with { W = .96f }); Color(ImGuiCol.TitleBgActive, p.Field with { W = .98f }); Color(ImGuiCol.TitleBgCollapsed, p.Inset);
        Color(ImGuiCol.Border, p.Border with { W = .4f }); Color(ImGuiCol.BorderShadow, Vector4.Zero);
        Color(ImGuiCol.MenuBarBg, p.Inset); Color(ImGuiCol.FrameBg, p.Input);
        Color(ImGuiCol.FrameBgHovered, p.Hover); Color(ImGuiCol.FrameBgActive, p.Pressed);
        Color(ImGuiCol.Button, p.Field); Color(ImGuiCol.ButtonHovered, p.Hover); Color(ImGuiCol.ButtonActive, p.Pressed);
        Color(ImGuiCol.CheckMark, p.Accent); Color(ImGuiCol.SliderGrab, p.Accent); Color(ImGuiCol.SliderGrabActive, p.Accent);
        Color(ImGuiCol.Header, p.Selection); Color(ImGuiCol.HeaderHovered, p.Hover); Color(ImGuiCol.HeaderActive, p.Pressed);
        Color(ImGuiCol.Separator, p.Border with { W = .36f }); Color(ImGuiCol.SeparatorHovered, p.Accent); Color(ImGuiCol.SeparatorActive, p.Accent);
        Color(ImGuiCol.Tab, p.Inset); Color(ImGuiCol.TabActive, p.Selection); Color(ImGuiCol.TabHovered, p.Hover);
        Color(ImGuiCol.TabUnfocused, p.Inset); Color(ImGuiCol.TabUnfocusedActive, p.Selection);
        Color(ImGuiCol.TableHeaderBg, p.Field); Color(ImGuiCol.TableBorderLight, p.Border with { W = .18f });
        Color(ImGuiCol.TableBorderStrong, p.Border with { W = .36f }); Color(ImGuiCol.TableRowBg, Vector4.Zero);
        Color(ImGuiCol.TableRowBgAlt, p.Text with { W = .025f });
        Color(ImGuiCol.ScrollbarBg, p.Inset with { W = .3f }); Color(ImGuiCol.ScrollbarGrab, p.Border with { W = .6f });
        Color(ImGuiCol.ScrollbarGrabHovered, p.Hover); Color(ImGuiCol.ScrollbarGrabActive, p.Accent);
        Color(ImGuiCol.TextSelectedBg, p.Selection); Color(ImGuiCol.NavHighlight, p.Accent);
        Color(ImGuiCol.ModalWindowDimBg, new(0, 0, 0, .45f));
        var f = ImGui.GetFontSize();
        Vector(ImGuiStyleVar.WindowPadding, new(MathF.Round(f * .75f), MathF.Round(f * .6f)));
        Vector(ImGuiStyleVar.FramePadding, new(MathF.Round(f * .55f), ImGui.GetStyle().FramePadding.Y));
        Vector(ImGuiStyleVar.ItemSpacing, new(MathF.Round(f * .5f), ImGui.GetStyle().ItemSpacing.Y));
        Vector(ImGuiStyleVar.ItemInnerSpacing, new(MathF.Round(f * .35f), MathF.Round(f * .2f)));
        Float(ImGuiStyleVar.WindowRounding, 4); Float(ImGuiStyleVar.PopupRounding, 3);
        Float(ImGuiStyleVar.FrameRounding, 2); Float(ImGuiStyleVar.ChildRounding, 3);
        Float(ImGuiStyleVar.TabRounding, 2); Float(ImGuiStyleVar.GrabRounding, 2); Float(ImGuiStyleVar.ScrollbarRounding, 3);
        Float(ImGuiStyleVar.WindowBorderSize, 1); Float(ImGuiStyleVar.ChildBorderSize, 1); Float(ImGuiStyleVar.FrameBorderSize, 1);
    }
    private void Color(ImGuiCol key, Vector4 value) { ImGui.PushStyleColor(key, value); colors++; }
    private void Float(ImGuiStyleVar key, float value) { ImGui.PushStyleVar(key, value); vars++; }
    private void Vector(ImGuiStyleVar key, Vector2 value) { ImGui.PushStyleVar(key, value); vars++; }
    public void Dispose() { ImGui.PopStyleVar(vars); ImGui.PopStyleColor(colors); current = previous; }
}

internal static class GameThemeChrome
{
    private static uint Color(Vector4 value) => ImGui.ColorConvertFloat4ToU32(value with { W = value.W * ImGui.GetStyle().Alpha });
    public static void WindowSurface()
    {
        if (GameThemeScope.Current is not { } p) return;
        var min = Vector2.Round(ImGui.GetWindowPos()); var max = min + ImGui.GetWindowSize();
        var top = min + new Vector2(1, ImGui.GetFrameHeight() + 1);
        var bottom = max - Vector2.One;
        if (bottom.Y <= top.Y) return;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(top, bottom, Color(new(1, 1, 1, p.Light ? .025f : .015f)), Color(new(1, 1, 1, p.Light ? .025f : .015f)),
            Color(new(0, 0, 0, p.Light ? .015f : .025f)), Color(new(0, 0, 0, p.Light ? .015f : .025f)));
        draw.AddRect(min + Vector2.One, max - Vector2.One, Color(p.Border with { W = .3f }), 3);
    }
    public static void ControlSurface(Vector2 min, Vector2 max, bool hovered, bool pressed, bool header = false)
    {
        if (GameThemeScope.Current is not { } p || max.X <= min.X || max.Y <= min.Y) return;
        var draw = ImGui.GetWindowDrawList();
        var face = pressed ? p.Pressed : hovered ? p.Hover : header ? Vector4.Lerp(p.Inset, p.Field, .45f) : p.Field;
        face.W = 1;
        draw.AddRectFilled(min, max, Color(face), header ? 2 : 3);
        var top = Vector4.Lerp(face, Vector4.One, pressed ? .025f : p.Light ? .06f : .04f);
        var bottom = Vector4.Lerp(face, new(0, 0, 0, 1), pressed ? .05f : .06f);
        draw.AddRectFilledMultiColor(min + Vector2.One, max - Vector2.One, Color(top), Color(top), Color(bottom), Color(bottom));
        draw.AddRect(min, max, Color(p.Border with { W = .45f }), header ? 2 : 3);
    }
    public static bool Button(string label, Vector2 size = default)
    {
        var pad = ImGui.GetStyle().FramePadding;
        var text = ImGui.CalcTextSize(label, true);
        if (size.X == 0) size.X = text.X + pad.X * 2;
        if (size.Y == 0) size.Y = ImGui.GetFrameHeight();
        var min = Vector2.Round(ImGui.GetCursorScreenPos());
        var width = size.X < 0 ? Math.Max(1, ImGui.GetContentRegionAvail().X + size.X) : size.X;
        var max = min + new Vector2(width, size.Y);
        var hover = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);
        ControlSurface(min, max, hover, hover && ImGui.IsMouseDown(ImGuiMouseButton.Left));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        try { return ImGui.Button(label, size); }
        finally { ImGui.PopStyleVar(); ImGui.PopStyleColor(3); }
    }
    private static void PrepareCombo(ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        var min = Vector2.Round(ImGui.GetCursorScreenPos());
        var width = (flags & ImGuiComboFlags.NoPreview) != 0 ? ImGui.GetFrameHeight() : ImGui.CalcItemWidth();
        var max = min + new Vector2(width, ImGui.GetFrameHeight());
        var hover = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);
        ControlSurface(min, max, hover, hover && ImGui.IsMouseDown(ImGuiMouseButton.Left), true);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
    }
    private static void FinishCombo() { ImGui.PopStyleVar(); ImGui.PopStyleColor(6); }
    public static bool BeginCombo(string label, string preview, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        PrepareCombo(flags);
        try { return ImGui.BeginCombo(label, preview, flags); } finally { FinishCombo(); }
    }
    public static bool Combo(string label, ref int selected, string[] items, int count)
    {
        PrepareCombo();
        try { return ImGui.Combo(label, ref selected, items, count); } finally { FinishCombo(); }
    }
    public static bool SmallButton(string label)
    {
        var pad = ImGui.GetStyle().FramePadding;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(pad.X, 0));
        try { return Button(label); } finally { ImGui.PopStyleVar(); }
    }
    public static bool Disclosure(string label, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
    {
        var min = Vector2.Round(ImGui.GetCursorScreenPos());
        var max = min + new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), ImGui.GetFrameHeight());
        var hover = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);
        ControlSurface(min, max, hover, hover && ImGui.IsMouseDown(ImGuiMouseButton.Left), true);
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero); ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        try { return ImGui.CollapsingHeader(label, flags); } finally { ImGui.PopStyleColor(3); }
    }
    public static void FeatureTitle(string text)
    {
        var p = GameThemeScope.Current;
        var f = ImGui.GetFontSize(); var at = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new(f * .7f, f));
        var c = p?.Accent ?? Vector4.One; var draw = ImGui.GetWindowDrawList();
        var center = Vector2.Round(at + new Vector2(f * .25f, f * .5f));
        draw.AddTriangleFilled(center + new Vector2(-2, -4), center + new Vector2(3, 0), center + new Vector2(-2, 4), Color(c));
        ImGui.SameLine(); ImGui.TextColored(c, text);
    }
    public static void Note(string text)
    {
        var f = ImGui.GetFontSize(); var at = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new(f * .65f, f));
        var c = GameThemeScope.Current?.Accent ?? Vector4.One;
        var center = Vector2.Round(at + new Vector2(f * .22f, f * .5f)); var d = Math.Max(2, MathF.Round(f * .18f));
        var draw = ImGui.GetWindowDrawList();
        draw.AddQuadFilled(center + new Vector2(-d, 0), center + new Vector2(0, -d), center + new Vector2(d, 0), center + new Vector2(0, d), Color(c));
        ImGui.SameLine(); ImGui.BeginGroup(); ImGui.TextWrapped(text); ImGui.EndGroup(); ImGui.Spacing();
    }
    public static void SectionLabel(string text)
    {
        ImGui.Spacing(); var at = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        ImGui.TextDisabled(text); var start = at.X + ImGui.CalcTextSize(text).X + ImGui.GetStyle().ItemSpacing.X;
        if (start < at.X + width) ImGui.GetWindowDrawList().AddLine(Vector2.Round(new(start, at.Y + ImGui.GetFontSize() / 2)),
            Vector2.Round(new(at.X + width, at.Y + ImGui.GetFontSize() / 2)), Color((GameThemeScope.Current?.Border ?? Vector4.One) with { W = .4f }));
        ImGui.Spacing();
    }

}
