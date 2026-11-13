using System.Numerics;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    internal Dictionary<DateOnly,Vector2> OguDayTargets { get; } = [];
    private Vector4 OguColor => ThemeScope.Hex(Light ? 0x8b641d : 0xe3c284);
    private void DrawDuckbill(Vector2 pos,float size,float alpha = 1)
    {
        if (duckbillIcon?.Invoke(pos,size,alpha) == true) return;
        // A local fallback remains visible while the shared PNG texture is being loaded.
        var draw = ImGui.GetWindowDrawList();
        var fur = ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(0xbfa783,alpha));
        var bill = ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(0x735c42,alpha));
        draw.AddCircleFilled(pos + new Vector2(size * .6f,size * .64f),size * .29f,fur);
        draw.AddCircleFilled(pos + new Vector2(size * .42f,size * .35f),size * .25f,fur);
        draw.AddRectFilled(pos + new Vector2(size * .05f,size * .4f),pos + new Vector2(size * .48f,size * .6f),bill,size * .1f);
        draw.AddCircleFilled(pos + new Vector2(size * .42f,size * .3f),Math.Max(1,size * .035f),ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(0x30271e,alpha)));
    }
    private void DrawOguDayDecoration(DateOnly date,Vector2 pos,Vector2 end)
    {
        var f = ImGui.GetFontSize(); var width = end.X - pos.X;
        var size = Math.Clamp(f * 1.05f,14,22); var gap = f * .25f;
        var textSize = ImGui.CalcTextSize("오구데이") * .75f;
        var room = width - f * 2.1f - 12;
        var label = room >= size + gap + textSize.X;
        var iconPos = new Vector2(end.X - 6 - size - (label ? textSize.X + gap : 0),pos.Y + f * .3f);
        if (room < size)
        { size = Math.Max(10,Math.Min(18,width * .2f)); iconPos = end - new Vector2(size + 4); }
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(pos,end,true);
        DrawDuckbill(iconPos,size);
        if (label) draw.AddText(ImGui.GetFont(),f * .75f,iconPos + new Vector2(size + gap,(size - textSize.Y) / 2),ImGui.ColorConvertFloat4ToU32(OguColor),"오구데이");
        draw.AddLine(new(pos.X + 1,end.Y - 2),new(end.X - 1,end.Y - 2),ImGui.ColorConvertFloat4ToU32(OguColor with { W = .45f }),1.5f);
        draw.PopClipRect(); OguDayTargets[date] = iconPos + new Vector2(size / 2);
        if (ImGui.IsMouseHoveringRect(iconPos,iconPos + new Vector2(size)) && ImGui.IsWindowHovered()) UiWidgets.Tooltip("오구데이 · 매년 5월 9일");
    }
    private void DrawOguDayHeading()
    {
        var f = ImGui.GetFontSize(); var pos = ImGui.GetCursorScreenPos();
        DrawDuckbill(pos,f * 1.5f); ImGui.Dummy(new(f * 1.5f)); ImGui.SameLine(); UiWidgets.Colored(OguColor,"오구데이");
    }
}
