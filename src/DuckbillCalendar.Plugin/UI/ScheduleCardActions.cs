using System.Numerics;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    internal Dictionary<string,Vector2> CardAlertTargets { get; } = [];
    internal Dictionary<string,Vector2> CardEditTargets { get; } = [];
    internal Dictionary<string,Vector2> CardDeleteTargets { get; } = [];
    private static float CardPrimaryActionsWidth(CalendarDisplayEntry entry)
    {
        var f = ImGui.GetFontSize();
        var labels = entry.Primary.Event.IsPersonal ? new[] { "수정","숨기기","삭제" } : new[] { "알림 설정","숨기기" };
        return labels.Sum(s => ImGui.CalcTextSize(s).X + f * .9f) + (labels.Length - 1) * f * .3f;
    }
    private void DrawCardPrimaryActions(CalendarDisplayEntry entry,CalendarItem[] members)
    {
        var f = ImGui.GetFontSize();
        ImGui.PushID("cardPrimary." + entry.Key);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(f * .45f,f * .15f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(f * .3f,ImGui.GetStyle().ItemSpacing.Y));
        try
        {
            if (entry.Primary.Event.IsPersonal)
            {
                if (ImGui.Button("수정")) OpenEditor(model.State.Entries.FirstOrDefault(e => e.Id == entry.Primary.Event.Id));
                CardEditTargets[entry.Key] = ItemCenter();
            }
            else
            {
                if (ImGui.Button("알림 설정"))
                {
                    if (members.Length == 1) OpenScheduleNotification(members[0].Event,members[0].Phase);
                    else ImGui.OpenPopup("###CardNotificationMembers");
                }
                CardAlertTargets[entry.Key] = ItemCenter();
                if (ImGui.BeginPopup("###CardNotificationMembers"))
                {
                    try { foreach (var member in members) if (ImGui.MenuItem(member.Event.Title + (member.Event.Phases.Count > 1 ? " · " + member.Phase.Label : ""))) OpenScheduleNotification(member.Event,member.Phase); }
                    finally { ImGui.EndPopup(); }
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("숨기기"))
            {
                if (members.Length == 1) model.SetPhaseHidden(members[0].Event.Id,members[0].Phase.Id,true);
                else ImGui.OpenPopup("###CardHideMembers");
            }
            foreach (var member in members) HideItemTargets[member.Event.Id] = ItemCenter();
            if (ImGui.BeginPopup("###CardHideMembers"))
            {
                try
                {
                    if (entry.Group == CalendarDisplayGroup.Update) { UiWidgets.Secondary("같은 버전·날짜·공식 근거가 확인된 점검과 패치"); ImGui.Separator(); }
                    else if (entry.Group == CalendarDisplayGroup.PurchaseCampaign) { UiWidgets.Secondary("같은 예약 상품의 구매·혜택 일정"); ImGui.Separator(); }
                    if (ImGui.MenuItem("이 카드의 일정 모두 숨기기 (" + members.Length + "개)")) foreach (var member in members) model.SetPhaseHidden(member.Event.Id,member.Phase.Id,true);
                    ImGui.Separator(); UiWidgets.Secondary("한 일정만 숨기기");
                    foreach (var member in members)
                        if (ImGui.MenuItem(member.Event.Title + (member.Event.Phases.Count > 1 ? " · " + member.Phase.Label : ""))) model.SetPhaseHidden(member.Event.Id,member.Phase.Id,true);
                }
                finally { ImGui.EndPopup(); }
            }
            if (entry.Primary.Event.IsPersonal)
            {
                ImGui.SameLine(); if (ImGui.Button("삭제")) RequestEntryDelete(entry.Primary.Event.Id);
                CardDeleteTargets[entry.Key] = ItemCenter();
            }
        }
        finally { ImGui.PopStyleVar(2); ImGui.PopID(); }
    }
}
