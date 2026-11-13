using System.Numerics;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    private int notificationScreen, categoryReturnTab = 2;
    private bool categoryDeleteRequest;
    private string deleteCategoryId = "", deleteDestinationId = "";
    internal Dictionary<string, Vector2> NotificationToggleTargets { get; } = [];
    internal Dictionary<string, string> NotificationSummaryLabels { get; } = [];
    internal Dictionary<string, Vector2> CategoryDeleteTargets { get; } = [];
    internal Vector2 CategoryDeleteConfirmTarget { get; private set; }
    internal Vector2 CategoryDeleteCancelTarget { get; private set; }
    internal bool CategoryDeleteConfirmHovered { get; private set; }
    private static Vector2 ItemCenter() => (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
    private void RouteNotificationRequests()
    {
        if (inboxRequest) { activeTab = 3; notificationScreen = 0; inboxPage = 0; inboxRequest = false; }
        if (choicesRequest) { activeTab = 3; notificationScreen = 3; choicesRequest = false; }
        if (ruleRequest) { ruleWindowOpen = ruleWindowFocus = true; ruleRequest = false; }
        if (userCategoryRequest) { activeTab = 4; userCategoryRequest = false; }
    }
    private void DrawNotifications()
    {
        if (!Compact) UiWidgets.Heading("알림", 1.3f);
        if (ImGui.Button("알림 내역")) notificationScreen = 0;
        ImGui.SameLine(); if (ImGui.Button("분류별 설정")) notificationScreen = 1;
        ContinueRow(ImGui.CalcTextSize("일정별 설정 (" + model.IndividualNotificationCount + ")").X + 20);
        if (ImGui.Button("일정별 설정 (" + model.IndividualNotificationCount + ")")) { notificationScreen = 3; choicesMode = 0; choicesPage = 0; }
        ImGui.Separator();
        if (notificationScreen == 0) DrawNotificationInbox();
        else if (notificationScreen == 1) DrawCategoryNotificationList();
        else DrawNotificationChoices();
    }
    private void DrawCategoryNotificationList()
    {
        ImGui.BeginChild("###CategoryNotificationRows", new(0, Math.Max(1, ImGui.GetContentRegionAvail().Y)));
        try
        {
            DrawNotificationSettingsCompact(); ImGui.Spacing(); ImGui.Separator();
            var rows = CalendarAppearanceStyle.Groups
                .Select(g => (Id: g.Group.ToString(), g.Label, g.Category))
                .Concat(model.State.View.UserCategories.Select(c => (Id: c.Id, Label: c.Name, Category: c.Id)));
            NotificationToggleTargets.Clear(); NotificationSummaryLabels.Clear();
            if (!ImGui.BeginTable("###NotificationCategoryTable",3,ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) return;
            try
            {
            ImGui.TableSetupColumn("###enabled",ImGuiTableColumnFlags.WidthFixed,ImGui.GetFrameHeight());
            ImGui.TableSetupColumn("###summary",ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("###details",ImGuiTableColumnFlags.WidthFixed,ImGui.GetFontSize() * 5);
            foreach (var (id, label, category) in rows)
            {
                ImGui.PushID(id);
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                var rule = NotificationPolicy.CategoryRule(model.State, id); var enabled = rule.Enabled;
                if (ImGui.Checkbox("###enabled", ref enabled)) model.SetCategoryNotificationEnabled(id, enabled);
                NotificationToggleTargets[id] = ItemCenter(); ImGui.TableNextColumn(); DrawAppearanceLabel(category, label, ImGui.GetFrameHeight());
                NotificationSummaryLabels[id] = (enabled ? "켜짐 · " : "꺼짐 · ") + RuleSummary(id, null, rule.StartMinutes, rule.EndMinutes);
                MutedWrapped(NotificationSummaryLabels[id]);
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("세부 설정")) OpenCategoryNotification(id);
                ImGui.PopID();
            }
            }
            finally { ImGui.EndTable(); }
        }
        finally { ImGui.EndChild(); }
    }
    private void DrawNotificationSettingsCompact()
    {
        var enabled = model.State.Notifications.Enabled;
        if (ImGui.Checkbox("알림 사용", ref enabled)) model.UpdateNotifications(n => n with { Enabled = enabled });
        ContinueRow(ImGui.CalcTextSize("채팅으로도 알리기").X + ImGui.GetFrameHeight() + 10);
        var chat = model.State.View.LocalChatAlerts;
        if (ImGui.Checkbox("채팅으로도 알리기", ref chat)) model.UpdateView(v => v with { LocalChatAlerts = chat });
        var notices = model.State.Notifications.NewNotices; var events = model.State.Notifications.NewEvents;
        if (ImGui.Checkbox("새 공지", ref notices)) model.UpdateNotifications(n => n with { NewNotices = notices });
        ImGui.SameLine(); if (ImGui.Checkbox("새 이벤트", ref events)) model.UpdateNotifications(n => n with { NewEvents = events });
        MutedWrapped("새 글은 다음 일정 수신 때 확인합니다. 첫 수신은 기준만 저장합니다.");
        if (!enabled) UiWidgets.Wrapped("전체 알림이 꺼져 있습니다. 아래 개별 설정은 보존됩니다.");
    }
    internal static (string Start, string End) TimingLabels(string category, EventPhase? phase = null)
    {
        if (category == nameof(CalendarGroup.Subscriptions)) return ("", "이용권 만료");
        if (category == nameof(CalendarGroup.Personal) || category.StartsWith("user:", StringComparison.Ordinal)) return ("약속 시각", "");
        if (phase?.Point == true || category == nameof(CalendarGroup.Patches)) return ("예정 시각", "");
        return category switch
        {
            nameof(CalendarGroup.Events) => ("이벤트 시작", "참여 종료"),
            nameof(CalendarGroup.Maintenance) => ("점검 시작", "점검 종료"),
            nameof(CalendarGroup.Sales) => ("판매·예약 개시", "구매·예약 기한"),
            nameof(CalendarGroup.Pvp) => ("시즌·보상 개시", "종료·수령 기한"),
            nameof(CalendarGroup.Competitions) => ("단계 시작", "단계 종료"),
            _ => ("예정 시각", "")
        };
    }
    private static string RuleSummary(string category, EventPhase? phase, IReadOnlyList<int> starts, IReadOnlyList<int> ends)
    {
        var labels = TimingLabels(category, phase); var parts = new List<string>();
        if (labels.Start.Length > 0 && starts.Count > 0) parts.Add(labels.Start + " " + LeadSummary(starts));
        if (labels.End.Length > 0 && ends.Count > 0) parts.Add(labels.End + " " + LeadSummary(ends));
        return parts.Count == 0 ? "알릴 시점 없음" : string.Join(" / ", parts);
    }
    private void DrawCategoryDeleteConfirmation()
    {
        if (categoryDeleteRequest) { ImGui.OpenPopup("###DeleteLocalCategory"); categoryDeleteRequest = false; }
        var work = ImGui.GetMainViewport().WorkSize; var f = ImGui.GetFontSize();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos + work / 2,ImGuiCond.Always,new(.5f));
        ImGui.SetNextWindowSize(new(Math.Min(f * 32,work.X - 24),Math.Min(f * 20,work.Y - 24)),ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new(Math.Min(f * 22,work.X - 24),Math.Min(f * 12,work.Y - 24)),work - new Vector2(24));
        if (!ImGui.BeginPopup("분류 삭제 확인###DeleteLocalCategory",ImGuiWindowFlags.NoSavedSettings)) return;
        try
        {
            ImGui.BeginChild("###CategoryDeleteBody",new(0,Math.Max(1,ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 1.5f)));
            try
            {
                UiWidgets.Wrapped(CategoryName(deleteCategoryId) + " 분류를 삭제할까요?");
                var count = model.State.Entries.Count(e => e.Kind == "personal" && e.CategoryId == deleteCategoryId);
                UiWidgets.Wrapped("일정 " + count + "건과 개별 알림은 보존합니다.");
                var destinations = model.State.View.UserCategories.Where(c => c.Id != deleteCategoryId).ToArray();
                var selected = Array.FindIndex(destinations,c => c.Id == deleteDestinationId) + 1;
                ImGui.TextUnformatted("일정 이동 위치"); ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("###categoryMoveDestination",ref selected,new[] { "미분류 (분류 없음)" }.Concat(destinations.Select(c => c.Name)).ToArray(),destinations.Length + 1))
                    deleteDestinationId = selected == 0 ? "" : destinations[selected - 1].Id;
                MutedWrapped("미분류는 일정 추가·수정에서도 선택할 수 있습니다. 분류 기본 알림은 삭제하고 이동한 위치의 기본값을 적용합니다.");
            }
            finally { ImGui.EndChild(); }
            ImGui.PushStyleColor(ImGuiCol.Button,ThemeScope.Hex(Light ? 0xb92d43 : 0x8a3446));
            if (ImGui.Button("분류 삭제")) { model.DeleteUserCategory(deleteCategoryId,deleteDestinationId); ImGui.CloseCurrentPopup(); }
            ImGui.PopStyleColor(); CategoryDeleteConfirmTarget = ItemCenter(); CategoryDeleteConfirmHovered = ImGui.IsItemHovered();
            ImGui.SameLine(); if (ImGui.Button("취소")) ImGui.CloseCurrentPopup(); CategoryDeleteCancelTarget = ItemCenter();
        }
        finally { ImGui.EndPopup(); }
    }
}
