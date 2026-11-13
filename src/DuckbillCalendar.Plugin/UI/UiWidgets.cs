using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DuckbillCalendar.Plugin;

internal enum UiIcon { Calendar, News, Settings, Left, Right, Filter, Plus, Link, Swords, Refresh, Gift, Server, Update, Flag, Trophy, Tag, Person, Clock, More, Bell, Raid, Shield }

internal static class UiWidgets
{
    public static void Heading(string text, float scale = 1.5f)
    {
        var previous = ImGui.GetCurrentContext().CurrentWindow.FontWindowScale;
        ImGui.SetWindowFontScale(previous * scale);
        try { ImGui.TextUnformatted(text); }
        finally { ImGui.SetWindowFontScale(previous); }
    }
    public static Vector2 IconButtonSize(string label, bool compact = false) => new(
        ImGui.GetFontSize() * (compact ? 1.9f : 2.1f) + (label.Length > 0 ? ImGui.CalcTextSize(label).X + ImGui.GetFontSize() * .4f : 0),
        ImGui.GetFontSize() * (compact ? 1.9f : 2.1f));
    public static bool HeadingButton(string id, string text, float scale, float height)
    {
        var f = ImGui.GetFontSize();
        var textSize = ImGui.CalcTextSize(text) * scale;
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("###" + id, new(textSize.X + f * .35f, height));
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), f * scale,
            pos + new Vector2(0, (height - textSize.Y) / 2), ImGui.GetColorU32(ImGuiCol.Text), text);
        return clicked;
    }
    public static float StatusBadgeWidth(string label) => ImGui.CalcTextSize(label).X * .8f + ImGui.GetFontSize() * .65f;
    public static void NumberBadge(Vector2 center,string label,float diameter,Vector4 background,Vector4 foreground,float textScale,Vector4? ring = null)
    {
        var f = ImGui.GetFontSize(); var measured = ImGui.CalcTextSize(label) * textScale;
        var size = new Vector2(MathF.Ceiling(Math.Max(diameter,measured.X + f * .4f)),MathF.Ceiling(diameter));
        var min = new Vector2(MathF.Round(center.X - size.X / 2),MathF.Round(center.Y - size.Y / 2));
        var draw = ImGui.GetWindowDrawList();
        if (ring is { } border)
        {
            var inset = Math.Max(1,MathF.Round(f * .08f));
            draw.AddRectFilled(min - new Vector2(inset),min + size + new Vector2(inset),ImGui.ColorConvertFloat4ToU32(border),size.Y / 2 + inset);
        }
        draw.AddRectFilled(min,min + size,ImGui.ColorConvertFloat4ToU32(background),size.Y / 2);
        var pos = min + (size - measured) / 2;
        pos = new(MathF.Round(pos.X),MathF.Round(pos.Y - f * .035f));
        draw.AddText(ImGui.GetFont(),f * textScale,pos,ImGui.ColorConvertFloat4ToU32(foreground),label);
    }
    public static void StatusBadge(Vector2 pos, float height, string label, Vector4 background, Vector4 foreground)
    {
        var f = ImGui.GetFontSize(); var width = StatusBadgeWidth(label);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(background), 3);
        draw.AddText(ImGui.GetFont(), f * .8f, pos + new Vector2(f * .325f, (height - f * .8f) / 2), ImGui.ColorConvertFloat4ToU32(foreground), label);
    }
    public static bool IconButton(string id, UiIcon icon, string label, bool selected, Vector4 accent, bool compact = false)
    {
        var f = ImGui.GetFontSize();
        var pos = ImGui.GetCursorScreenPos();
        var size = IconButtonSize(label, compact);
        if (selected) { ImGui.PushStyleColor(ImGuiCol.Button, accent with { W = .17f }); ImGui.PushStyleColor(ImGuiCol.Text, accent); }
        bool clicked;
        try { clicked = ImGui.Button("###" + id, size); }
        finally { if (selected) ImGui.PopStyleColor(2); }
        var color = ImGui.ColorConvertFloat4ToU32(selected ? accent : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        var inset = (size.Y - f * .9f) / 2;
        DrawIcon(icon, pos + new Vector2(inset), f * .9f, color);
        if (label.Length > 0) ImGui.GetWindowDrawList().AddText(pos + new Vector2(inset + f * 1.3f, (size.Y - f) / 2), color, label);
        return clicked;
    }
    public static void DrawIcon(UiIcon icon, Vector2 p, float s, uint color)
    {
        var d = ImGui.GetWindowDrawList(); var t = Math.Max(1.2f, s * .085f);
        Vector2 At(float x, float y) => p + new Vector2(x * s, y * s);
        void Line(float x, float y, float a, float b) => d.AddLine(At(x, y), At(a, b), color, t);
        switch (icon)
        {
            case UiIcon.Raid:
                d.AddCircle(At(.5f,.19f),s*.13f,color,12,t);
                d.AddCircle(At(.15f,.35f),s*.1f,color,12,t); d.AddCircle(At(.85f,.35f),s*.1f,color,12,t);
                d.AddBezierCubic(At(.27f,.78f),At(.27f,.35f),At(.73f,.35f),At(.73f,.78f),color,t);
                Line(.04f,.84f,.04f,.65f); Line(.04f,.65f,.19f,.52f); Line(.96f,.84f,.96f,.65f); Line(.96f,.65f,.81f,.52f);
                Line(.34f,.95f,.66f,.95f); break;
            case UiIcon.Shield:
                Line(.08f,.12f,.5f,.03f); Line(.5f,.03f,.92f,.12f); Line(.08f,.12f,.16f,.67f); Line(.92f,.12f,.84f,.67f);
                Line(.16f,.67f,.5f,.98f); Line(.84f,.67f,.5f,.98f); Line(.5f,.25f,.5f,.72f); Line(.3f,.46f,.7f,.46f); break;
            case UiIcon.Bell:
                d.AddBezierCubic(At(.18f,.78f), At(.28f,.05f), At(.72f,.05f), At(.82f,.78f), color, t);
                Line(.08f,.8f,.92f,.8f); d.AddCircleFilled(At(.5f,.96f),s*.07f,color); break;
            case UiIcon.Left: Line(.65f, .1f, .25f, .5f); Line(.25f, .5f, .65f, .9f); break;
            case UiIcon.Right: Line(.35f, .1f, .75f, .5f); Line(.75f, .5f, .35f, .9f); break;
            case UiIcon.Plus: Line(.5f, .05f, .5f, .95f); Line(.05f, .5f, .95f, .5f); break;
            case UiIcon.Calendar:
                d.AddRect(At(.05f, .15f), At(.95f, .95f), color, s * .12f, ImDrawFlags.None, t);
                Line(.05f, .4f, .95f, .4f); Line(.3f, 0, .3f, .28f); Line(.7f, 0, .7f, .28f); break;
            case UiIcon.News:
                d.AddRect(At(.05f, .05f), At(.95f, .95f), color, s * .12f, ImDrawFlags.None, t);
                Line(.25f, .3f, .75f, .3f); Line(.25f, .5f, .75f, .5f); Line(.25f, .7f, .6f, .7f); break;
            case UiIcon.Settings:
                d.AddCircle(At(.5f, .5f), s * .3f, color, 16, t); d.AddCircle(At(.5f, .5f), s * .09f, color, 12, t);
                for (var i = 0; i < 8; i++) { var a = i * MathF.PI / 4; var v = new Vector2(MathF.Cos(a), MathF.Sin(a)); d.AddLine(At(.5f, .5f) + v * s * .3f, At(.5f, .5f) + v * s * .47f, color, t * 1.4f); } break;
            case UiIcon.Filter:
                Line(.05f, .15f, .95f, .15f); Line(.05f, .15f, .4f, .55f); Line(.95f, .15f, .6f, .55f); Line(.4f, .55f, .4f, .95f); Line(.6f, .55f, .6f, .8f); Line(.4f, .95f, .6f, .8f); break;
            case UiIcon.Link:
                Line(.15f, .1f, .9f, .1f); Line(.9f, .1f, .9f, .85f); Line(.9f, .1f, .15f, .85f); Line(.05f, .5f, .05f, .95f); Line(.05f, .95f, .5f, .95f); break;
            case UiIcon.Swords:
                Line(.05f, .05f, .95f, .95f); Line(.95f, .05f, .05f, .95f); Line(.05f, .65f, .35f, .95f); Line(.65f, .95f, .95f, .65f); break;
            case UiIcon.Refresh:
                d.AddCircle(At(.5f, .5f), s * .35f, color, 18, t); Line(.85f, .1f, .85f, .45f); Line(.85f, .45f, .5f, .45f); break;
            case UiIcon.Person:
                d.AddCircle(At(.5f, .27f), s * .2f, color, 16, t); d.AddBezierCubic(At(.08f, .95f), At(.08f, .36f), At(.92f, .36f), At(.92f, .95f), color, t); break;
            case UiIcon.Clock:
                d.AddCircle(At(.5f, .5f), s * .44f, color, 20, t); Line(.5f, .2f, .5f, .5f); Line(.5f, .5f, .75f, .65f); break;
            case UiIcon.Server:
                d.AddRect(At(.05f, .05f), At(.95f, .45f), color, s * .08f, ImDrawFlags.None, t);
                d.AddRect(At(.05f, .55f), At(.95f, .95f), color, s * .08f, ImDrawFlags.None, t);
                d.AddCircleFilled(At(.22f, .25f), s * .045f, color); d.AddCircleFilled(At(.22f, .75f), s * .045f, color); Line(.5f, .25f, .78f, .25f); Line(.5f, .75f, .78f, .75f); break;
            case UiIcon.Update:
                Line(.5f, .02f, .5f, .68f); Line(.22f, .4f, .5f, .68f); Line(.5f, .68f, .78f, .4f);
                Line(.08f, .65f, .08f, .95f); Line(.08f, .95f, .92f, .95f); Line(.92f, .95f, .92f, .65f); break;
            case UiIcon.Flag:
                Line(.15f, .05f, .15f, .98f); Line(.15f, .08f, .9f, .08f); Line(.9f, .08f, .65f, .35f); Line(.65f, .35f, .9f, .6f); Line(.9f, .6f, .15f, .6f); break;
            case UiIcon.Trophy:
                Line(.25f, .05f, .75f, .05f); Line(.25f, .05f, .3f, .5f); Line(.75f, .05f, .7f, .5f); Line(.3f, .5f, .5f, .67f); Line(.5f, .67f, .7f, .5f);
                Line(.5f, .67f, .5f, .95f); Line(.25f, .95f, .75f, .95f); Line(.25f, .15f, .05f, .15f); Line(.05f, .15f, .1f, .45f); Line(.1f, .45f, .3f, .5f); Line(.75f, .15f, .95f, .15f); Line(.95f, .15f, .9f, .45f); Line(.9f, .45f, .7f, .5f); break;
            case UiIcon.Tag:
                Line(.05f, .05f, .52f, .05f); Line(.52f, .05f, .95f, .52f); Line(.95f, .52f, .52f, .95f); Line(.52f, .95f, .05f, .52f); Line(.05f, .52f, .05f, .05f); d.AddCircle(At(.26f, .26f), s * .06f, color, 10, t); break;
            case UiIcon.Gift:
                d.AddRect(At(.12f, .4f), At(.88f, .95f), color, s * .03f, ImDrawFlags.None, t); d.AddRect(At(.05f, .25f), At(.95f, .42f), color, 0, ImDrawFlags.None, t);
                Line(.5f, .25f, .5f, .95f); Line(.5f, .25f, .2f, .05f); Line(.2f, .05f, .15f, .25f); Line(.5f, .25f, .8f, .05f); Line(.8f, .05f, .85f, .25f); break;
            case UiIcon.More:
                for (var i = 0; i < 3; i++) d.AddCircleFilled(At(.5f, .15f + .35f * i), s * .07f, color); break;
        }
    }
    public static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { ImGui.TextUnformatted(text); }
        finally { ImGui.PopTextWrapPos(); }
    }
    public static bool WrappedCheckbox(string id, string label, ref bool value)
    {
        var changed = ImGui.Checkbox("###" + id, ref value);
        ImGui.SameLine(); ImGui.AlignTextToFramePadding(); Wrapped(label);
        if (ImGui.IsItemClicked()) { value = !value; changed = true; }
        return changed;
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
