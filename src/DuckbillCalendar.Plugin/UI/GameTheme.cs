using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

// Kept identical across the three plugins apart from the namespace. No runtime dependency between plugins.
internal sealed record GameThemePalette(bool Light, Vector4 Surface, Vector4 Inset, Vector4 Field,
    Vector4 Input, Vector4 Text, Vector4 Muted, Vector4 Border, Vector4 Accent, Vector4 Selection)
{
    public const float OverlayOpacity = .985f;
    public const int DefaultBrightness = 100, DefaultDepth = 50;
    public const int MinBrightness = 80, MaxBrightness = 120;
    public float Depth { get; init; } = .5f;
    private static readonly GameThemePalette Smoke = new(false, Hex(0x383b40, .985f), Hex(0x24272c, .58f), Hex(0x494c52), Hex(0x292c31),
        Hex(0xf2efe8), Hex(0xc0bcb5), Hex(0x85827b), Hex(0xf0cb78), Hex(0x595d64));
    private static readonly GameThemePalette Pearl = new(true, Hex(0xd0d2d3, .99f), Hex(0xb7bbbf, .50f), Hex(0xbec1c4), Hex(0xe1e3e4),
        Hex(0x40352f), Hex(0x645c56), Hex(0x8d8982), Hex(0x916521), Hex(0xaeb2b7));
    private static (int Brightness, int Depth, GameThemePalette Palette)? smokeCache, pearlCache;
    public static int NormalizeBrightness(int value) => Math.Clamp(value, MinBrightness, MaxBrightness);
    public static int NormalizeDepth(int value) => Math.Clamp(value, 0, 100);
    public static GameThemePalette For(bool light, int brightness = DefaultBrightness, int depth = DefaultDepth)
    {
        brightness = NormalizeBrightness(brightness); depth = NormalizeDepth(depth);
        ref var cache = ref (light ? ref pearlCache : ref smokeCache);
        if (cache is { } hit && hit.Brightness == brightness && hit.Depth == depth) return hit.Palette;
        var basis = light ? Pearl : Smoke;
        var amount = (brightness - DefaultBrightness) / 20f;
        Vector4 SurfaceColor(Vector4 color) => Vector4.Lerp(color,
            (amount >= 0 ? Vector4.One : Vector4.Zero) with { W = color.W }, Math.Abs(amount) * (amount >= 0 ? .15f : .22f));
        Vector4 TextColor(Vector4 color) => Vector4.Lerp(color,
            (light ? Vector4.Zero : Vector4.One) with { W = color.W }, Math.Max(0, light ? -amount : amount) * .15f);
        var palette = basis with { Surface = SurfaceColor(basis.Surface), Inset = SurfaceColor(basis.Inset),
            Field = SurfaceColor(basis.Field), Input = SurfaceColor(basis.Input), Selection = SurfaceColor(basis.Selection),
            Border = SurfaceColor(basis.Border), Text = TextColor(basis.Text), Muted = TextColor(basis.Muted), Depth = depth / 100f };
        cache = (brightness, depth, palette);
        return palette;
    }
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
    public GameThemeScope(bool light, int brightness = GameThemePalette.DefaultBrightness, int depth = GameThemePalette.DefaultDepth)
    {
        var p = GameThemePalette.For(light, brightness, depth); current = p;
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
        var shine = (p.Light ? .13f : .055f) * p.Depth;
        var shade = (p.Light ? .055f : .14f) * p.Depth;
        draw.AddRectFilledMultiColor(top, bottom, Color(new(1, 1, 1, shine)), Color(new(1, 1, 1, shine)),
            Color(new(0, 0, 0, shade)), Color(new(0, 0, 0, shade)));
        draw.AddRect(min + Vector2.One, max - Vector2.One, Color(p.Border with { W = .25f + .3f * p.Depth }), 3);
    }
    public static void ControlSurface(Vector2 min, Vector2 max, bool hovered, bool pressed, bool header = false)
    {
        if (GameThemeScope.Current is not { } p || max.X <= min.X || max.Y <= min.Y) return;
        var draw = ImGui.GetWindowDrawList();
        var face = pressed ? p.Pressed : hovered ? p.Hover : header ? Vector4.Lerp(p.Inset, p.Field, .45f) : p.Field;
        face.W = 1;
        draw.AddRectFilled(min, max, Color(face), header ? 2 : 3);
        var top = Vector4.Lerp(face, Vector4.One, (pressed ? .04f : p.Light ? .24f : .18f) * p.Depth);
        var bottom = Vector4.Lerp(face, new(0, 0, 0, 1), (pressed ? .16f : .2f) * p.Depth);
        draw.AddRectFilledMultiColor(min + Vector2.One, max - Vector2.One, Color(top), Color(top), Color(bottom), Color(bottom));
        draw.AddRect(min, max, Color(p.Border with { W = .35f + .35f * p.Depth }), header ? 2 : 3);
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

// Drafts stay local while dragging; the caller saves only the completed adjustment.
internal sealed class ThemeAdjustmentEditor
{
    private int brightness = GameThemePalette.DefaultBrightness, depth = GameThemePalette.DefaultDepth;
    private (int Brightness, int Depth)? source;
    private bool pendingEdit;
    public (int Brightness, int Depth)? Draw(bool light, int savedBrightness, int savedDepth,
        string brightnessLabel, string depthLabel, string hint, string resetLabel, string previewLabel)
    {
        var saved = (GameThemePalette.NormalizeBrightness(savedBrightness), GameThemePalette.NormalizeDepth(savedDepth));
        if (source != saved) { (brightness, depth) = saved; source = saved; pendingEdit = false; }
        ImGui.PushID("ThemeAdjustments");
        try
        {
            var active = false;
            var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 32 ? 2 : 1;
            if (ImGui.BeginTable("Controls", columns, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn(); ImGui.TextUnformatted(brightnessLabel); ImGui.SetNextItemWidth(-1);
                pendingEdit |= ImGui.SliderInt("###Brightness", ref brightness, GameThemePalette.MinBrightness, GameThemePalette.MaxBrightness, "%d%%", ImGuiSliderFlags.AlwaysClamp);
                active |= ImGui.IsItemActive();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(depthLabel); ImGui.SetNextItemWidth(-1);
                pendingEdit |= ImGui.SliderInt("###Depth", ref depth, 0, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp);
                active |= ImGui.IsItemActive();
                ImGui.EndTable();
            }
            ImGui.TextWrapped(hint);
            using (new GameThemeScope(light, brightness, depth))
            {
                var p = GameThemeScope.Current!;
                var min = Vector2.Round(ImGui.GetCursorScreenPos());
                var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), ImGui.GetFrameHeight() * 2.2f);
                ImGui.Dummy(size);
                var draw = ImGui.GetWindowDrawList();
                draw.AddRectFilled(min, min + size, ImGui.GetColorU32(p.Surface), 3);
                var pad = ImGui.GetStyle().FramePadding;
                var text = ImGui.CalcTextSize(previewLabel);
                var button = new Vector2(Math.Min(size.X - 8, text.X + pad.X * 2), ImGui.GetFrameHeight());
                var start = Vector2.Round(min + (size - button) / 2);
                GameThemeChrome.ControlSurface(start, start + button, false, false);
                draw.PushClipRect(min, min + size, true);
                draw.AddText(Vector2.Round(start + (button - text) / 2), ImGui.GetColorU32(p.Text), previewLabel);
                draw.PopClipRect();
            }
            ImGui.BeginDisabled(brightness == GameThemePalette.DefaultBrightness && depth == GameThemePalette.DefaultDepth);
            var resetWidth = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var resetPadding = ImGui.GetStyle().FramePadding;
            bool reset;
            if (ImGui.CalcTextSize(resetLabel).X + resetPadding.X * 2 <= resetWidth)
                reset = GameThemeChrome.Button(resetLabel + "###Reset");
            else
            {
                var wrapWidth = Math.Max(1, resetWidth - resetPadding.X * 2);
                var textSize = ImGui.CalcTextSize(resetLabel, false, wrapWidth);
                var start = ImGui.GetCursorScreenPos();
                reset = GameThemeChrome.Button("###Reset", new(resetWidth, textSize.Y + resetPadding.Y * 2));
                var next = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(start + resetPadding);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
                ImGui.TextWrapped(resetLabel);
                ImGui.PopTextWrapPos();
                ImGui.SetCursorScreenPos(next);
            }
            if (reset)
            {
                brightness = GameThemePalette.DefaultBrightness; depth = GameThemePalette.DefaultDepth;
                pendingEdit = true;
            }
            ImGui.EndDisabled();
            if (!active && pendingEdit)
            {
                pendingEdit = false;
                if ((brightness, depth) != saved) return (brightness, depth);
            }
            return null;
        }
        finally { ImGui.PopID(); }
    }
}
