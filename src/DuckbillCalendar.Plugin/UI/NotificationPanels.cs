using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    private string editCategoryId = "";
    private bool inboxRequest, choicesRequest, ruleRequest, userCategoryRequest;
    private int inboxPage, choicesPage, choicesMode;
    private bool inboxUnreadOnly;
    private string choicesSearch = "", notificationError = "";
    private string ruleCategory = "", ruleEvent = "", rulePhase = "", ruleTitle = "", ruleProfile = "";
    private bool ruleCategoryEnabled;
    private int ruleMode;
    private string ruleStarts = "", ruleEnds = "";
    private string userCategoryId = "", userCategoryName = "", userCategoryIcon = "Person";
    private Vector3 userCategoryColor = new(.57f, .68f, 1);
    internal Vector2 NotificationBellTarget { get; private set; }
    internal Dictionary<string, Vector2> CategoryNotificationTargets { get; } = [];
    internal Vector2 NotificationChoicesTarget { get; private set; }
    internal Vector2 NotificationRuleSaveTarget { get; private set; }
    internal Vector2 NotificationRuleCancelTarget { get; private set; }
    internal Vector2 UserCategorySaveTarget { get; private set; }
    internal Vector2 NotificationPageNextTarget { get; private set; }
    internal int NotificationChoiceRenderedRows { get; private set; }

    private string CategoryName(string key) => model.State.View.UserCategories.FirstOrDefault(c => c.Id == key)?.Name ??
        (CalendarAppearanceStyle.Groups.FirstOrDefault(g => g.Group.ToString() == key).Label ?? "공지");
    private void DrawUnreadBadge()
    {
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        NotificationBellTarget = (min + max) / 2;
        var count = model.UnreadNotifications; if (count == 0) return;
        var f = ImGui.GetFontSize(); var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var diameter = MathF.Ceiling(f * .95f);
        var border = Math.Max(1,MathF.Round(f * .08f));
        UiWidgets.NumberBadge(new(max.X + ImGui.GetStyle().ItemSpacing.X * .5f - border - diameter / 2,min.Y + diameter / 2),text,diameter,
            ThemeScope.Hex(Light ? 0xb92d43 : 0xf58b9b),Light ? Vector4.One : ThemeScope.Hex(0x35121d),text.Length > 2 ? .6f : .68f,
            ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg]);
    }
    private void DrawNotificationSettings()
    {
        UiWidgets.Heading("알림", 1.15f);
        var enabled = model.State.Notifications.Enabled;
        if (ImGui.Checkbox("알림 사용", ref enabled)) model.UpdateNotifications(n => n with { Enabled = enabled });
        var chat = model.State.View.LocalChatAlerts;
        ContinueRow(ImGui.CalcTextSize("채팅으로도 알리기").X + ImGui.GetFrameHeight() + 10);
        if (ImGui.Checkbox("채팅으로도 알리기", ref chat)) model.UpdateView(v => v with { LocalChatAlerts = chat });
        var notices = model.State.Notifications.NewNotices;
        if (ImGui.Checkbox("새 공지", ref notices)) model.UpdateNotifications(n => n with { NewNotices = notices });
        ImGui.SameLine(); var events = model.State.Notifications.NewEvents;
        if (ImGui.Checkbox("새 이벤트", ref events)) model.UpdateNotifications(n => n with { NewEvents = events });
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("일정 정보를 새로 받았을 때 처음 확인된 공지·이벤트를 알려줍니다. 첫 수신은 기준만 저장하며 기존 글을 한꺼번에 알리지 않습니다.");
        UiWidgets.Wrapped("알림 설정 목록에서 켜짐 상태와 알릴 시점을 바로 확인하고 변경합니다.");
        if (ImGui.Button("개별 알림 설정 보기 (" + model.IndividualNotificationCount + "건)")) { choicesRequest = true; choicesMode = 0; choicesPage = 0; }
        NotificationChoicesTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        ContinueRow(ImGui.CalcTextSize("분류별 알림 설정").X + 20);
        if (ImGui.Button("분류별 알림 설정")) { activeTab = 3; notificationScreen = 1; }
        ContinueRow(ImGui.CalcTextSize("일정 찾아 알림 추가").X + 20);
        if (ImGui.Button("일정 찾아 알림 추가")) { choicesRequest = true; choicesMode = 1; choicesPage = 0; }
    }
    private void OpenCategoryNotification(string category)
    {
        var rule = NotificationPolicy.CategoryRule(model.State, category);
        ruleCategory = category; ruleEvent = rulePhase = ""; ruleTitle = CategoryName(category);
        ruleCategoryEnabled = rule.Enabled; ruleMode = 1;
        ruleStarts = string.Join(", ", rule.StartMinutes); ruleEnds = string.Join(", ", rule.EndMinutes);
        notificationError = ""; ruleRequest = true; ruleProfile = model.State.ActiveProfileId;
    }
    private void OpenScheduleNotification(CalendarEvent e, EventPhase p, bool fromChoices = false)
    {
        ruleCategory = NotificationPolicy.CategoryKey(e); ruleEvent = e.Id; rulePhase = p.Id; ruleTitle = e.Title + (e.Phases.Count > 1 ? " · " + p.Label : "");
        var custom = model.State.Notifications.Schedules.FirstOrDefault(r => r.ProfileId == model.State.ActiveProfileId && r.EventId == e.Id && r.PhaseId == p.Id);
        var legacy = model.State.Alerts.Where(r => r.ProfileId == model.State.ActiveProfileId && r.EventId == e.Id && r.PhaseId == p.Id).ToArray();
        var defaults = NotificationPolicy.CategoryRule(model.State, ruleCategory);
        ruleMode = custom is not null ? custom.Mode == "off" ? 2 : 1 : legacy.Length > 0 ? legacy.Any(a => a.Enabled) ? 1 : 2 : 0;
        ruleStarts = string.Join(", ", custom?.StartMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Enabled && a.Edge == "start").Select(a => a.LeadMinutes).ToArray() : defaults.StartMinutes));
        ruleEnds = string.Join(", ", custom?.EndMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Enabled && a.Edge == "end").Select(a => a.LeadMinutes).ToArray() : defaults.EndMinutes));
        notificationError = ""; ruleRequest = true; ruleProfile = model.State.ActiveProfileId;
    }
    private void OpenUserCategory(UserCalendarCategory? category = null)
    {
        categoryReturnTab = activeTab == 3 ? 3 : 2;
        userCategoryId = category?.Id ?? "user:" + Guid.NewGuid().ToString("N");
        userCategoryName = category?.Name ?? ""; userCategoryIcon = category?.Icon ?? "Person";
        var color = category is null ? ThemeScope.Hex(0x91aeff) : CalendarAppearanceStyle.Resolve(model.State.View, category.Id).Color;
        userCategoryColor = new(color.X, color.Y, color.Z); notificationError = ""; userCategoryRequest = true;
    }
    private void DrawNotificationWindows() => DrawCategoryDeleteConfirmation();
    private void DrawNotificationInbox()
    {
            if (ImGui.Checkbox("미확인만", ref inboxUnreadOnly)) inboxPage = 0;
            ImGui.SameLine(); if (ImGui.SmallButton("모두 읽음")) model.MarkNotificationsRead();
            ContinueRow(ImGui.CalcTextSize("읽은 내역 지우기").X + 20);
            if (ImGui.SmallButton("읽은 내역 지우기")) model.ClearReadNotifications();
            var items = model.State.NotificationState.Inbox.Where(n => (n.ProfileId.Length == 0 || n.ProfileId == model.State.ActiveProfileId) && (!inboxUnreadOnly || !n.Read)).Reverse().ToArray();
            UiWidgets.Secondary("최근 " + NotificationPolicy.MaximumInbox + "건 보관 · " + items.Length + "건 표시");
            inboxPage = Math.Clamp(inboxPage, 0, Math.Max(0, (items.Length - 1) / 12));
            if (items.Length == 0) { ImGui.Spacing(); UiWidgets.Wrapped("새 알림이 없습니다."); MutedWrapped("새 공지·이벤트나 설정한 시점의 알림을 여기에 표시합니다."); return; }
            ImGui.BeginChild("###InboxRows", new(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2)));
            try
            {
                if (items.Length == 0) UiWidgets.Wrapped("새 알림이 없습니다.");
                foreach (var n in items.Skip(inboxPage * 12).Take(12))
                {
                    ImGui.PushID(n.Id);
                    try
                    {
                        UiWidgets.Colored(n.Read ? ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled] : Accent,
                            (n.Read ? "" : "● ") + (n.Kind == "new-notice" ? "새 공지" : n.Kind == "new-event" ? "새 이벤트" : "일정 알림") + " · " + SettingsTime(n.CreatedAt));
                        UiWidgets.Wrapped(n.Title); MutedWrapped(n.Message);
                        if (n.BoundaryAt is { } at) UiWidgets.Secondary(UiText.Time(at) + " KST");
                        if (ImGui.SmallButton("열기"))
                        {
                            model.MarkNotificationsRead(n.Id);
                            var e = model.Events.FirstOrDefault(e => e.Id == n.EventId);
                            var url = e is null ? n.Url : SourceFor(e, e.Phases.FirstOrDefault());
                            if (n.Kind.StartsWith("new-", StringComparison.Ordinal))
                            {
                                if (OfficialEventLinks.IsDetailUrl(url)) model.Post(() => Util.OpenLink(url!));
                                else activeTab = 1;
                            }
                            else if (n.BoundaryAt is { } boundary) { activeTab = 0; OpenDay(CalendarEngine.Today(boundary)); }
                        }
                        if (!n.Read) { ImGui.SameLine(); if (ImGui.SmallButton("읽음")) model.MarkNotificationsRead(n.Id); }
                        ImGui.Separator();
                    }
                    finally { ImGui.PopID(); }
                }
            }
            finally { ImGui.EndChild(); }
            DrawNotificationPaging(ref inboxPage, items.Length, 12);

    }

    private void DrawNotificationPaging(ref int page, int count, int size)
    {
        if (count <= size) return;
        ImGui.BeginDisabled(page == 0); if (ImGui.Button("이전")) page--; ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.TextUnformatted((page + 1) + " / " + Math.Max(1, (count + size - 1) / size)); ImGui.SameLine();
        ImGui.BeginDisabled((page + 1) * size >= count); if (ImGui.Button("다음")) page++; ImGui.EndDisabled();
        NotificationPageNextTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
    }
    private void DrawNotificationChoices()
    {
            if (!model.State.Notifications.Enabled)
            {
                UiWidgets.Secondary("전체 알림 꺼짐");
                ImGui.SameLine(); if (ImGui.SmallButton("전체 알림 켜기")) model.UpdateNotifications(s => s with { Enabled = true });
            }
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 11);
            if (ImGui.Combo("###notificationListMode", ref choicesMode, new[] { "개별 변경한 일정", "모든 일정에서 찾기" }, 2)) choicesPage = 0;
            ImGui.SetNextItemWidth(-1); if (ImGui.InputTextWithHint("###notificationSearch", "일정·분류 이름 검색", ref choicesSearch, 100)) choicesPage = 0;
            var explicitKeys = model.State.Notifications.Schedules.Where(r => r.ProfileId == model.State.ActiveProfileId).Select(r => (r.EventId, r.PhaseId))
                .Concat(model.State.Alerts.Where(r => r.ProfileId == model.State.ActiveProfileId).Select(r => (r.EventId, r.PhaseId))).Distinct().ToArray();
            var candidates = model.Events.SelectMany(e => e.Phases.Where(p => CalendarContentPolicy.Includes(e, p)).Select(p => (Event: e, Phase: p)))
                .Where(pair => choicesMode == 1 || explicitKeys.Contains((pair.Event.Id, pair.Phase.Id)))
                .Where(pair => string.IsNullOrWhiteSpace(choicesSearch) || (pair.Event.Title + pair.Phase.Label + CategoryName(NotificationPolicy.CategoryKey(pair.Event))).Contains(choicesSearch, StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Event.IsPersonal ? 0 : 1).ThenBy(pair => CalendarEngine.BoundaryDate(pair.Phase.Start) ?? CalendarEngine.BoundaryDate(pair.Phase.End)).ToArray();
            var missing = choicesMode == 0 ? explicitKeys.Where(key => model.Events.All(e => e.Id != key.EventId || e.Phases.All(p => p.Id != key.PhaseId)))
                .Where(key => string.IsNullOrWhiteSpace(choicesSearch) || (key.EventId + " " + key.PhaseId).Contains(choicesSearch, StringComparison.OrdinalIgnoreCase)).ToArray() : [];
            var total = candidates.Length + missing.Length;
            choicesPage = Math.Clamp(choicesPage, 0, Math.Max(0, (total - 1) / 12));
            UiWidgets.Secondary(total + (choicesMode == 0 ? "건 · 개별 설정" : "건 · 알림을 설정할 일정"));
            ImGui.BeginChild("###NotificationChoiceRows", new(0, Math.Max(1, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2)));
            NotificationChoiceRenderedRows = 0; NotificationToggleTargets.Clear(); NotificationSummaryLabels.Clear();
            var effective = NotificationPolicy.EffectiveAlerts(model.Feed,model.State);
            try
            {
                if (total == 0) UiWidgets.Wrapped("개별 설정이 없습니다. ‘모든 일정에서 찾기’를 선택하거나 날짜의 일정 메뉴를 이용하세요.");
                for (var i = choicesPage * 12; i < Math.Min(total, (choicesPage + 1) * 12); i++)
                {
                    NotificationChoiceRenderedRows++;
                    if (i >= candidates.Length)
                    {
                        var key = missing[i - candidates.Length]; ImGui.PushID(key.EventId + key.PhaseId);
                        UiWidgets.Wrapped("현재 자료에 없는 일정 · " + key.EventId);
                        if (ImGui.SmallButton("개별 설정 해제")) model.SetScheduleNotification(key.EventId, key.PhaseId, "inherit", [], []);
                        ImGui.Separator(); ImGui.PopID(); continue;
                    }
                    var (e, p) = candidates[i]; ImGui.PushID(e.Id + "." + p.Id);
                    UiWidgets.Wrapped(e.Title + (e.Phases.Count > 1 ? " · " + p.Label : ""));
                    MutedWrapped(CategoryName(NotificationPolicy.CategoryKey(e)) + " · " + UiText.Period(p, e.IsPersonal));
                    var rule = model.State.Notifications.Schedules.FirstOrDefault(r => r.ProfileId == model.State.ActiveProfileId && r.EventId == e.Id && r.PhaseId == p.Id);
                    var alerts = effective.Where(a => a.EventId == e.Id && a.PhaseId == p.Id).ToArray(); var enabled = alerts.Length > 0;
                    if (ImGui.Checkbox("알림 사용###scheduleEnabled", ref enabled)) model.SetScheduleNotificationEnabled(e,p,enabled);
                    NotificationToggleTargets[e.Id + "/" + p.Id] = ItemCenter();
                    var origin = rule is not null || explicitKeys.Contains((e.Id,p.Id)) ? "개별 설정" : "분류 기본값";
                    var defaults = NotificationPolicy.CategoryRule(model.State,NotificationPolicy.CategoryKey(e));
                    var legacy = model.State.Alerts.Where(a => a.ProfileId == model.State.ActiveProfileId && a.EventId == e.Id && a.PhaseId == p.Id).ToArray();
                    var times = RuleSummary(NotificationPolicy.CategoryKey(e),p,
                        enabled ? alerts.Where(a => a.Edge == "start").Select(a => a.LeadMinutes).ToArray() : rule?.StartMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Edge == "start").Select(a => a.LeadMinutes).ToArray() : defaults.StartMinutes),
                        enabled ? alerts.Where(a => a.Edge == "end").Select(a => a.LeadMinutes).ToArray() : rule?.EndMinutes ?? (legacy.Length > 0 ? legacy.Where(a => a.Edge == "end").Select(a => a.LeadMinutes).ToArray() : defaults.EndMinutes));
                    var summary = origin + " · " + (enabled ? "켜짐 · " : "꺼짐 · ") + times;
                    NotificationSummaryLabels[e.Id + "/" + p.Id] = summary; MutedWrapped(summary);
                    if (enabled && alerts.All(a => NotificationPolicy.BlockReason(model.Feed,model.Workspace.ActiveHealth,e,p,a.Edge,model.Now).Length > 0)) MutedWrapped("확정 시각·출처 확인 대기");
                    if (ImGui.SmallButton("세부 설정")) OpenScheduleNotification(e,p,true);
                    if (choicesMode == 0) { ImGui.SameLine(); if (ImGui.SmallButton("분류 기본값으로")) model.SetScheduleNotification(e.Id, p.Id, "inherit", [], []); }
                    ImGui.Separator(); ImGui.PopID();
                }
            }
            finally { ImGui.EndChild(); }
            DrawNotificationPaging(ref choicesPage, total, 12);
    }

    private static bool ParseLeads(string text, out int[] values)
    {
        var list = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        { if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 525600) { values = []; return false; } list.Add(value); }
        values = list.Distinct().Order().ToArray(); return values.Length <= 6;
    }
    private void DrawLeadEditor(string label, ref string text)
    {
        var active = text.Length > 0;
        if (ImGui.Checkbox(label, ref active)) { text = active ? "60" : ""; notificationError = ""; }
        if (!active) return;
        ParseLeads(text, out var selected);
        ImGui.PushID(label);
        try
        {
            foreach (var value in new[] { 0, 10, 30, 60, 1440 })
            {
                var title = value == 0 ? "정각" : value == 1440 ? "하루 전" : value == 60 ? "1시간 전" : value + "분 전";
                ContinueRow(ImGui.CalcTextSize(title).X + 20);
                var on = selected.Contains(value); if (on) ImGui.PushStyleColor(ImGuiCol.Button, Accent with { W = .25f });
                if (ImGui.SmallButton(title)) { text = string.Join(", ", (on ? selected.Where(v => v != value) : selected.Append(value)).Order()); notificationError = ""; }
                if (on) ImGui.PopStyleColor();
            }
            ImGui.SetNextItemWidth(-1); if (ImGui.InputTextWithHint("###leadMinutes", "직접 입력: 분 단위, 쉼표로 구분 (최대 6개)", ref text, 80)) notificationError = "";
            if (ParseLeads(text, out var values) && values.Length > 0) UiWidgets.Wrapped(string.Join(" · ", values.Select(NotificationPolicy.LeadLabel)));
        }
        finally { ImGui.PopID(); }
    }
    private void DrawNotificationRule()
    {
        UiWidgets.Heading(ruleTitle,1.15f);
        var schedule = ruleEvent.Length > 0;
        var e = model.Events.FirstOrDefault(e => e.Id == ruleEvent); var p = e?.Phases.FirstOrDefault(p => p.Id == rulePhase);
        var category = model.State.View.UserCategories.FirstOrDefault(c => c.Id == ruleCategory);
        var stale = ruleProfile != model.State.ActiveProfileId ? "사용 중인 계정이 바뀌었습니다. 설정 창을 다시 열어 주세요." :
            ruleCategory.StartsWith("user:",StringComparison.Ordinal) && category is null ? "이 분류가 삭제되었습니다. 설정 창을 닫아 주세요." :
            schedule && p is null ? "이 일정이 없어졌습니다. 설정 창을 다시 열어 주세요." : "";
        if (schedule)
        {
            if (p is not null) MutedWrapped(UiText.Period(p,e!.IsPersonal));
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("###notificationMode",ref ruleMode,new[] { "분류 기본값 따름", "이 일정만 변경", "이 일정 알림 끄기" },3)) notificationError = "";
        }
        else if (ImGui.Checkbox("이 분류의 알림 사용",ref ruleCategoryEnabled)) notificationError = "";
        ImGui.BeginChild("###NotificationRuleBody",new(Math.Min(ImGui.GetContentRegionAvail().X,ImGui.GetFontSize() * 36),Math.Max(1,Math.Min(ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 1.6f,ImGui.GetFontSize() * (schedule && ruleMode != 1 ? 6 : TimingLabels(ruleCategory,p).End.Length == 0 || TimingLabels(ruleCategory,p).Start.Length == 0 ? 11 : 21)))));
        try
        {
            if (schedule && ruleMode == 0)
            {
                var defaults = NotificationPolicy.CategoryRule(model.State,ruleCategory);
                UiWidgets.Wrapped(CategoryName(ruleCategory) + " 기본값 · " + (defaults.Enabled ? "켜짐" : "꺼짐"));
                MutedWrapped(RuleSummary(ruleCategory,p,defaults.StartMinutes,defaults.EndMinutes));
            }
            else if (schedule && ruleMode == 2) UiWidgets.Wrapped("이 일정의 알림만 끕니다. 분류의 다른 일정에는 적용하지 않습니다.");
            else
            {
                var labels = TimingLabels(ruleCategory,p);
                ImGui.BeginDisabled(!schedule && !ruleCategoryEnabled);
                if (labels.Start.Length > 0) DrawLeadEditor(labels.Start + " 알림",ref ruleStarts);
                if (labels.End.Length > 0) { ImGui.Spacing(); DrawLeadEditor(labels.End + " 알림",ref ruleEnds); }
                ImGui.EndDisabled(); ImGui.Spacing();
                if (labels.End.Length > 0 && ruleCategory != nameof(CalendarGroup.Subscriptions)) MutedWrapped("종료 알림은 종료 시각이 있는 일정에만 적용합니다.");
                MutedWrapped("정각은 해당 시각에, ‘10분 전’은 해당 시각보다 10분 먼저 알립니다.");
                if (p is not null && e is not null)
                {
                    foreach (var edge in new[] { ("start",labels.Start,ruleStarts),("end",labels.End,ruleEnds) }.Where(v => v.Item2.Length > 0 && v.Item3.Length > 0))
                    {
                        var reason = NotificationPolicy.BlockReason(model.Feed,model.Workspace.ActiveHealth,e,p,edge.Item1,model.Now);
                        if (reason.Length > 0) MutedWrapped(edge.Item2 + " · " + reason + ". 확인될 때까지 시간 알림을 기다립니다.");
                    }
                }
                else MutedWrapped("날짜만 있는 일정은 시각이 정해진 뒤 시간 알림을 받습니다.");
            }
            if (notificationError.Length > 0) UiWidgets.Wrapped(notificationError);
            if (stale.Length > 0) MutedWrapped(stale);
        }
        finally { ImGui.EndChild(); }
        ImGui.BeginDisabled(stale.Length > 0);
        if (ImGui.Button("저장"))
        {
            var disabled = schedule ? ruleMode is 0 or 2 : !ruleCategoryEnabled;
            var validStart = ParseLeads(ruleStarts,out var starts); var validEnd = ParseLeads(ruleEnds,out var ends);
            var labels = TimingLabels(ruleCategory,p);
            if (labels.Start.Length == 0) { starts = []; validStart = true; }
            if (labels.End.Length == 0) { ends = []; validEnd = true; }
            if (!disabled && (!validStart || !validEnd)) notificationError = "0~525600분, 각 최대 6개로 입력하세요.";
            else if (!disabled && starts.Length + ends.Length == 0) notificationError = "알릴 시점을 하나 이상 선택하세요.";
            else
            {
                if (schedule) model.SetScheduleNotification(ruleEvent,rulePhase,ruleMode == 0 ? "inherit" : ruleMode == 2 ? "off" : "custom",starts,ends,ruleProfile);
                else model.SetCategoryNotification(new() { CategoryId = ruleCategory,Enabled = ruleCategoryEnabled,StartMinutes = starts,EndMinutes = ends },ruleProfile);
                ruleWindowOpen = false;
            }
        }
        NotificationRuleSaveTarget = ItemCenter();
        ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button("취소")) ruleWindowOpen = false;
        NotificationRuleCancelTarget = ItemCenter();
        if (!schedule && category is not null)
        { ContinueRow(ImGui.CalcTextSize("분류 편집").X + 20); if (ImGui.Button("분류 편집")) { ruleWindowOpen = false; OpenUserCategory(category); } }
    }
    private static void MutedWrapped(string value)
    {
        ImGui.PushStyleColor(ImGuiCol.Text,ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        try { UiWidgets.Wrapped(value); } finally { ImGui.PopStyleColor(); }
    }
    private static string LeadSummary(IReadOnlyList<int> minutes) => minutes.Count == 0 ? "사용 안 함" : string.Join(" · ", minutes.Select(NotificationPolicy.LeadLabel));
    private void DrawUserCategoryEditor()
    {
        UiWidgets.Heading(model.State.View.UserCategories.Any(c => c.Id == userCategoryId) ? "분류 편집" : "분류 추가",1.3f);
        ImGui.BeginChild("###UserCategoryBody",new(Math.Min(ImGui.GetContentRegionAvail().X,ImGui.GetFontSize() * 32),Math.Max(1,Math.Min(ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 1.6f,ImGui.GetFontSize() * 15))));
        try
        {
            ImGui.TextUnformatted("분류 이름"); ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X,ImGui.GetFontSize() * 27));
            if (ImGui.InputTextWithHint("###userCategoryName","예: 절바하 공대, 영식 고정팟",ref userCategoryName,40)) notificationError = "";
            ImGui.Spacing(); ImGui.TextUnformatted("아이콘"); ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X,ImGui.GetFontSize() * 20));
            DrawAppearanceIconSelector(new() { Icon = userCategoryIcon },userCategoryId == "user:default-raid" ? UiIcon.Raid : UiIcon.Person,Enum.Parse<UiIcon>(userCategoryIcon),new(userCategoryColor,1),value => userCategoryIcon = value.Length == 0 ? userCategoryId == "user:default-raid" ? "Raid" : "Person" : value);
            ImGui.Spacing(); ImGui.ColorEdit3("색상",ref userCategoryColor,ImGuiColorEditFlags.NoInputs);
            ImGui.Spacing(); MutedWrapped("개인 일정에서 이 분류를 선택할 수 있습니다. 분류별 기본 알림은 알림 설정 목록에서 관리합니다.");
            if (notificationError.Length > 0) UiWidgets.Wrapped(notificationError);
        }
        finally { ImGui.EndChild(); }
        if (ImGui.Button("저장"))
        {
            var name = userCategoryName.Trim();
            if (name.Length == 0) notificationError = "분류 이름을 입력하세요.";
            else if (model.State.View.UserCategories.Any(c => c.Id != userCategoryId && c.Name.Equals(name,StringComparison.OrdinalIgnoreCase)) || CalendarAppearanceStyle.Groups.Any(g => g.Label == name)) notificationError = "다른 분류 이름을 사용하세요.";
            else if (model.State.View.UserCategories.Count >= 64 && model.State.View.UserCategories.All(c => c.Id != userCategoryId)) notificationError = "분류는 최대 64개까지 만들 수 있습니다.";
            else
            {
                model.SaveUserCategory(new() { Id = userCategoryId,Name = name,Icon = userCategoryIcon,
                    Color = $"#{(int)MathF.Round(userCategoryColor.X * 255):X2}{(int)MathF.Round(userCategoryColor.Y * 255):X2}{(int)MathF.Round(userCategoryColor.Z * 255):X2}" });
                activeTab = categoryReturnTab; if (categoryReturnTab == 3) notificationScreen = 1;
            }
        }
        UserCategorySaveTarget = ItemCenter();
        ImGui.SameLine(); if (ImGui.Button("취소")) activeTab = categoryReturnTab;
    }
}
