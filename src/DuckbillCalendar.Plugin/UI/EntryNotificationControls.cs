using System.Numerics;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    private int editNotificationMode;
    private string editNotificationMinutes = "60";
    private bool editGlobalNotification,editGlobalNotificationChanged,ruleWindowOpen,ruleWindowFocus;
    private bool entryDeleteRequest,entryDeleteWindowOpen;
    private long pendingEntrySaveRevision;
    private string entryDeleteId = "",entryDeleteScope = "one";
    internal Vector2 EntryDeleteConfirmTarget { get; private set; }
    internal Dictionary<string,Vector2> EntryDeleteScopeTargets { get; } = [];
    public void DrawFloatingWindows()
    {
        if (pendingEntrySaveRevision > 0 && model.EntrySaveRevision >= pendingEntrySaveRevision)
        {
            if (model.LastEntrySaveError is { } error) { editorError = error; editorOpen = true; }
            pendingEntrySaveRevision = 0;
        }
        if (editorRequest) { editorOpen = true; editorRequest = false; }
        if (ruleRequest) { ruleWindowOpen = ruleWindowFocus = true; ruleRequest = false; }
        DrawEditor(); DrawEntryDeletionConfirmation(); DrawFloatingNotificationRule();
    }
    private void SetupEditNotifications(PersonalEntry? entry)
    {
        var saved = model.State.Notifications.Schedules.FirstOrDefault(r => r.ProfileId == model.State.ActiveProfileId && r.EventId == entry?.Id && r.PhaseId == "local");
        var legacy = model.State.Alerts.Where(a => a.ProfileId == model.State.ActiveProfileId && a.EventId == entry?.Id && a.PhaseId == "local").ToArray();
        editNotificationMode = saved is not null ? saved.Mode == "off" ? 2 : 1 : legacy.Length > 0 ? legacy.Any(a => a.Enabled) ? 1 : 2 : 0;
        editNotificationMinutes = string.Join(", ",saved?.StartMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Edge == "start").Select(a => a.LeadMinutes).ToArray() : new[] {60}));
        editGlobalNotification = model.State.Notifications.Enabled; editGlobalNotificationChanged = false;
    }
    private void DrawEditNotifications()
    {
        ImGui.Spacing(); ImGui.Separator(); ImGui.TextUnformatted("알림");
        if (!editGlobalNotificationChanged) editGlobalNotification = model.State.Notifications.Enabled;
        if (ImGui.Checkbox("전체 알림 사용",ref editGlobalNotification)) editGlobalNotificationChanged = true;
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("###editNotificationMode",ref editNotificationMode,new[] { "분류 기본값 따름","이 일정만 시점 지정","이 일정 알림 끄기" },3);
        if (editNotificationMode == 0)
        {
            var key = editCategoryId.Length == 0 ? "Personal" : editCategoryId;
            var rule = NotificationPolicy.CategoryRule(model.State,key);
            MutedWrapped(CategoryName(key) + " 기본값 · " + (rule.Enabled ? RuleSummary(key,null,rule.StartMinutes,rule.EndMinutes) : "꺼짐"));
        }
        else if (editNotificationMode == 1) DrawLeadEditor("약속 시각 알림",ref editNotificationMinutes);
        if (!editGlobalNotification) MutedWrapped("전체 알림이 꺼져 있어 시점만 저장합니다.");
        if (editDateOnly) MutedWrapped("시각을 지정해야 시간 알림을 받을 수 있습니다.");
    }
    private void DrawFloatingNotificationRule()
    {
        if (!ruleWindowOpen) return;
        var work = ImGui.GetMainViewport().WorkSize; var f = ImGui.GetFontSize();
        var labels = TimingLabels(ruleCategory,model.Events.FirstOrDefault(e => e.Id == ruleEvent)?.Phases.FirstOrDefault(p => p.Id == rulePhase));
        var height = ruleEvent.Length > 0 && ruleMode != 1 ? 15 : labels.Start.Length == 0 || labels.End.Length == 0 ? 23 : 31;
        ImGui.SetNextWindowSize(new(Math.Min(f * 32,work.X - 24),Math.Min(f * height,work.Y - 24)),ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new(Math.Min(f * 20,work.X - 24),Math.Min(f * 12,work.Y - 24)),work - new Vector2(24));
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos + work / 2,ImGuiCond.Appearing,new(.5f));
        if (ruleWindowFocus) { ImGui.SetNextWindowFocus(); ruleWindowFocus = false; }
        if (ImGui.Begin("알림 설정###DuckbillCalendar.NotificationRule",ref ruleWindowOpen,ImGuiWindowFlags.NoSavedSettings))
        { try { DrawNotificationRule(); } finally { ImGui.End(); } }
        else ImGui.End();
    }
    private void RequestEntryDelete(string id)
    { entryDeleteId = id; entryDeleteScope = "one"; entryDeleteRequest = true; }
    private void DrawEntryDeletionConfirmation()
    {
        if (entryDeleteRequest) { entryDeleteWindowOpen = true; ImGui.SetNextWindowFocus(); entryDeleteRequest = false; }
        if (!entryDeleteWindowOpen) return;
        var work = ImGui.GetMainViewport().WorkSize; var f = ImGui.GetFontSize();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos + work / 2,ImGuiCond.Always,new(.5f));
        ImGui.SetNextWindowSize(new(Math.Min(f * 33,work.X - 24),Math.Min(f * 21,work.Y - 24)),ImGuiCond.Always);
        if (!ImGui.Begin("일정 삭제###DuckbillCalendar.DeletePersonalSchedules",ref entryDeleteWindowOpen,ImGuiWindowFlags.NoSavedSettings)) { ImGui.End(); return; }
        try
        {
            ImGui.BeginChild("###EntryDeleteBody",new(0,Math.Max(1,ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 1.5f)));
            try
            {
                var entry = model.State.Entries.FirstOrDefault(e => e.Id == entryDeleteId && e.ProfileId == model.State.ActiveProfileId);
                UiWidgets.Wrapped(entry?.Title ?? "현재 없는 일정");
                UiWidgets.Wrapped("어떤 일정을 삭제할까요?"); EntryDeleteScopeTargets.Clear();
                var all = PersonalDeletionPolicy.Targets(model.State,entryDeleteId,"series");
                foreach (var (scope,title) in all.Count > 1 ? new[] { ("one","이 일정만"),("following","이 일정과 이후 일정"),("series","함께 등록한 전체 일정") } : new[] { ("one","이 일정만") })
                {
                    var count = PersonalDeletionPolicy.Targets(model.State,entryDeleteId,scope).Count;
                    if (ImGui.RadioButton("###deleteScope." + scope,entryDeleteScope == scope)) entryDeleteScope = scope;
                    EntryDeleteScopeTargets[scope] = ItemCenter();
                    ImGui.SameLine(); UiWidgets.Wrapped(title + " (" + count + "건)");
                }
                if (all.Count > 1) MutedWrapped("‘이후’는 처음 함께 등록한 순서 기준입니다.");
                MutedWrapped("같은 분류의 다른 일정은 삭제하지 않습니다. 선택한 일정의 개별 알림 설정도 함께 제거합니다.");
            }
            finally { ImGui.EndChild(); }
            var targets = PersonalDeletionPolicy.Targets(model.State,entryDeleteId,entryDeleteScope);
            ImGui.BeginDisabled(targets.Count == 0);
            if (ImGui.Button(targets.Count + "개 일정 삭제")) { model.DeletePersonalSchedules(entryDeleteId,entryDeleteScope); entryDeleteWindowOpen = false; }
            ImGui.EndDisabled(); EntryDeleteConfirmTarget = ItemCenter();
            ImGui.SameLine(); if (ImGui.Button("취소")) entryDeleteWindowOpen = false;
        }
        finally { ImGui.End(); }
    }
}
