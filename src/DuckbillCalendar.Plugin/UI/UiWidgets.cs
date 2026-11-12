using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal static class UiWidgets
{
    public static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { ImGui.TextUnformatted(text); }
        finally { ImGui.PopTextWrapPos(); }
    }

    public static void Secondary(string text) => Colored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text);

    public static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        try { ImGui.TextUnformatted(text); }
        finally { ImGui.PopStyleColor(); }
    }

    public static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        try
        {
            // Auto-sized windows have no usable content width on their first frame.
            // A positive wrap position lets the tooltip measure itself instead of
            // shrinking to a single Korean glyph and extending below the screen.
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetFontSize() * 28);
            try { ImGui.TextUnformatted(text); }
            finally { ImGui.PopTextWrapPos(); }
        }
        finally { ImGui.EndTooltip(); }
    }
}
