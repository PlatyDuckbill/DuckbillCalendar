using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed class CompactWindow : Window
{
    private readonly CalendarModel model;
    public Action? OpenMain { get; set; }

    public CompactWindow(CalendarModel model) : base("DuckbillCalendar###DuckbillCalendar.Compact")
    {
        this.model = model;
        Size = new(310, 245);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(245, 170), MaximumSize = new(float.MaxValue) };
    }

    public override void Draw()
    {
        if (model.State.View.ExampleMode) UiWidgets.Colored(ThemeScope.Hex(0xd8bb79), UiText.Get("ExampleRecord"));
        ImGui.TextUnformatted(UiText.Get("CompactToday", UiText.Date(model.Today)));
        var due = model.TodayDeadlines;
        if (due.Count == 0) UiWidgets.Secondary(UiText.Get("NoTodayDeadlines"));
        foreach (var item in due.Take(3))
        {
            if (ImGui.Selectable(item.Event.Title + "###compact." + item.Event.Id + "." + item.Phase.Id))
            {
                model.Select(item.Event, item.Phase, model.Today);
                OpenMain?.Invoke();
            }
            UiWidgets.Secondary(UiText.Phase(item.Event, item.Phase) + " · " + UiText.Boundary(item.Phase.End));
        }
        if (due.Count > 3) UiWidgets.Secondary(UiText.Get("MoreCount", due.Count - 3));
        ImGui.Separator();
        if (model.NextSubscription is { } subscription)
        {
            ImGui.TextUnformatted(UiText.Get("NextSubscription"));
            UiWidgets.Wrapped(subscription.Title);
            UiWidgets.Secondary(UiText.Boundary(subscription.When));
        }
        else UiWidgets.Secondary(UiText.Get("NoSubscriptions"));
        ImGui.Separator();
        UiWidgets.Secondary(UiText.Get("AlertsUnavailableCompact"));
        if (ImGui.SmallButton(UiText.Get("OpenMain") + "###compact.open")) OpenMain?.Invoke();
    }
}
