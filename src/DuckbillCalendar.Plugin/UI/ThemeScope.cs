using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal sealed class ThemeScope : IDisposable
{
    private int colors;
    public ThemeScope(string theme)
    {
        var light = theme == "paper";
        Add(ImGuiCol.Text, Hex(light ? 0x2a342f : 0xeeeee8));
        Add(ImGuiCol.TextDisabled, Hex(light ? 0x59665f : 0xa5adae));
        Add(ImGuiCol.WindowBg, Hex(light ? 0xeeece5 : 0x171a1c));
        Add(ImGuiCol.ChildBg, Hex(light ? 0xfaf9f4 : 0x202426));
        Add(ImGuiCol.PopupBg, Hex(light ? 0xfaf9f4 : 0x202426));
        Add(ImGuiCol.FrameBg, Hex(light ? 0xe0e4dc : 0x2b3032));
        Add(ImGuiCol.Button, Hex(light ? 0xe0e4dc : 0x2b3032));
        Add(ImGuiCol.ButtonHovered, Hex(light ? 0xd0dcd2 : 0x343a3d));
        Add(ImGuiCol.ButtonActive, Hex(light ? 0xc0d1c5 : 0x454c4e));
        Add(ImGuiCol.Header, Hex(light ? 0xdce5dc : 0x343a3d));
        Add(ImGuiCol.HeaderHovered, Hex(light ? 0xd0dcd2 : 0x3d4548));
        Add(ImGuiCol.Tab, Hex(light ? 0xe0e4dc : 0x202426));
        Add(ImGuiCol.TabActive, Hex(light ? 0xd0dcd2 : 0x343a3d));
        Add(ImGuiCol.TabHovered, Hex(light ? 0xc0d1c5 : 0x454c4e));
        Add(ImGuiCol.Border, Hex(light ? 0xb3bcb4 : 0x424a4e));
        Add(ImGuiCol.CheckMark, Hex(light ? 0x356c64 : 0xd8bb79));
    }
    private void Add(ImGuiCol key, Vector4 value) { ImGui.PushStyleColor(key, value); colors++; }
    public static Vector4 Hex(int value, float alpha = 1) => new((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f, alpha);
    public static Vector4 EventColor(string category, bool light) => Hex(category switch
    {
        "personal" or "subscription" or "retainer" or "bag" => light ? 0x805d25 : 0xd8bb79,
        "patch" => light ? 0x365f80 : 0x9dbddc,
        "maintenance" => light ? 0x4d6375 : 0x93a8b8,
        "pvp-season" or "pvp-series" or "reward" => light ? 0x8d4e62 : 0xdba3bd,
        "in-game-event" => light ? 0x356c64 : 0x91cbbf,
        "seasonal-event" => light ? 0x9a6234 : 0xe0b27e,
        "sale" or "discount" => light ? 0x715785 : 0xc4afd8,
        "notice" => light ? 0x59665f : 0xa5adae, _ => light ? 0x38684b : 0xa4c8ae
    });
    public void Dispose() { if (colors > 0) ImGui.PopStyleColor(colors); colors = 0; }
}
