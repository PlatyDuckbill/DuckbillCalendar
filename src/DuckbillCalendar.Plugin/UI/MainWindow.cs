using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow : Window
{
    private readonly CalendarModel model;
    private readonly Func<Vector2,float,float,bool>? duckbillIcon;
    private int activeTab;
    private string search = "";
    private int category;
    private bool selectSettings;
    private bool appearanceSettingsRequest;
    private bool frontlineInline;
    private bool editorOpen, editorRequest;
    private string editId = "", editName = "", editDate = "", editTime = "", editNote = "", editorError = "";
    private int editKind;
    private bool editDateOnly;
    private string importPath = "";
    private int jumpYear, jumpMonth;
    private float monthWheelRemainder;
    private bool dayPopupRequest;
    private bool resetOngoingSection;
    private int pickerYear, pickerMonth;
    internal Vector2 DatePickerButtonCenter { get; private set; }
    internal Vector2 PickerNextMonthTarget { get; private set; }
    internal Dictionary<DateOnly, Vector2> DatePickerTargets { get; } = [];
    internal Vector2 OngoingToggleCenter { get; private set; }
    internal bool OngoingSectionExpanded { get; private set; }
    internal Dictionary<string, Vector2> CardMenuTargets { get; } = [];
    internal Dictionary<string, Vector2> HideItemTargets { get; } = [];
    internal Dictionary<string, Vector2> RelatedHideTargets { get; } = [];
    private float gridRowHeight;
    private readonly FrontlineScheduleClient? frontline;
    private bool subscriptionEditor;
    private int chargeDays = 90;
    private int chargeHours;
    private int chargeMinutes;
    private bool chargeMode;
    private string chargeBasisTime = "";
    private int themeIndex;
    internal Vector2 GridOrigin { get; private set; }
    internal float GridCellWidth { get; private set; }
    internal float GridRowHeight => gridRowHeight;
    internal bool HeaderFits { get; private set; }
    internal bool CalendarActionsSingleRow { get; private set; }
    internal bool FrontlineSummaryFits { get; private set; }
    internal int FrontlineCellLabelCount { get; private set; }
    internal Vector2 CalendarFilterTarget { get; private set; }
    internal Vector2 FrontlineVisibilityTarget { get; private set; }
    internal bool AppearanceRowsFit { get; private set; }
    internal bool NewsFiltersFit { get; private set; }
    internal bool EditorSaveVisible { get; private set; }
    internal Vector2 EditorSaveMax { get; private set; }
    internal Dictionary<string, Vector2> SubscriptionResetTargets { get; } = [];
    internal Vector2 SubscriptionResetConfirmTarget { get; private set; }
    internal Vector2 SubscriptionResetCancelTarget { get; private set; }
    internal Dictionary<CalendarGroup, Vector2> AppearanceIconTargets { get; } = [];
    internal Dictionary<UiIcon, Vector2> AppearanceIconChoiceTargets { get; } = [];
    internal Dictionary<string, Vector2> AppearanceLabelTargets { get; } = [];
    internal Dictionary<string, Vector2> AppearanceAlarmTargets { get; } = [];

    public MainWindow(CalendarModel model, FrontlineScheduleClient? frontline = null, Func<Vector2,float,float,bool>? duckbillIcon = null) : base("오리부리 달력###DuckbillCalendar.Main")
    {
        this.model = model; this.frontline = frontline; this.duckbillIcon = duckbillIcon;
        Size = new(1060, 720); SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(620, 480), MaximumSize = new(float.MaxValue) };
    }
    private bool Light => ThemeScope.IsLight(model.State.View.Theme);
    private bool Compact => ImGui.GetContentRegionAvail().X < ImGui.GetFontSize() * 28 || ImGui.GetWindowHeight() < ImGui.GetFontSize() * 18;
    private Vector4 Accent => ThemeScope.Accent(model.State.View.Theme);
    public void OpenSettings() { IsOpen = true; selectSettings = true; }
    public override void Draw()
    {
        if (selectSettings) { activeTab = 2; selectSettings = false; }
        RouteNotificationRequests();
        DrawNavigation();
        if (model.State.View.ExampleMode)
        {
            UiWidgets.Wrapped("예시 일정입니다."); ImGui.SameLine();
            if (ImGui.SmallButton("실제 일정 보기")) model.Post(() => model.Workspace.LoadExample(false));
        }
        if (activeTab == 0) DrawCalendar();
        else if (activeTab == 1) DrawNews();
        else if (activeTab == 2) DrawSettings();
        else if (activeTab == 3) DrawNotifications();
        else if (activeTab == 4) DrawUserCategoryEditor();
        else if (activeTab == 5) DrawWeeklyRaidPlanner();
        else DrawReleaseNotes();
        if (editorRequest) { editorOpen = true; editorRequest = false; }
        if (activeTab == 0) DrawDayPopup();
        DrawNotificationWindows();
    }
    private void DrawNavigation()
    {
        var f = ImGui.GetFontSize();
        var compact = Compact;
        foreach (var tab in new[] { ("달력", UiIcon.Calendar, 0), ("새소식", UiIcon.News, 1) })
        {
            if (tab.Item3 > 0) ImGui.SameLine();
            if (UiWidgets.IconButton("nav." + tab.Item3, tab.Item2, tab.Item1, activeTab == tab.Item3, Accent, compact)) activeTab = tab.Item3;
        }
        var settingsWidth = UiWidgets.IconButtonSize("설정", compact).X;
        var bellWidth = UiWidgets.IconButtonSize("", compact).X;
        var settingsLeft = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - settingsWidth - bellWidth - ImGui.GetStyle().ItemSpacing.X;
        frontlineInline = activeTab == 0 && model.State.View.FrontlineUpdatesEnabled && !compact &&
            ImGui.GetItemRectMax().X + f + FrontlineSummaryWidth() + f < settingsLeft;
        if (frontlineInline)
        {
            ImGui.SameLine(0, f);
            DrawFrontline(inline: true);
        }
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - settingsWidth - bellWidth - ImGui.GetStyle().ItemSpacing.X));
        if (UiWidgets.IconButton("nav.notifications", UiIcon.Bell, "", activeTab == 3, Accent, compact)) { inboxRequest = true; inboxPage = 0; }
        DrawUnreadBadge();
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("알림 내역 · 미확인 " + model.UnreadNotifications + "건");
        ImGui.SameLine();
        if (UiWidgets.IconButton("nav.settings", UiIcon.Settings, "설정", activeTab == 2, Accent, compact)) activeTab = 2;
        if (model.Workspace.HasUnsavedChanges)
        {
            var pos = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddCircleFilled(new(pos.X - 4, ImGui.GetItemRectMin().Y + 4), 3, ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(0xe5bb6b)));
            if (ImGui.IsItemHovered()) UiWidgets.Tooltip("아직 저장되지 않은 변경사항이 있습니다.");
        }
    }
    private void DrawCalendar()
    {
        DrawCalendarActions();
        if (!frontlineInline) DrawFrontline();
        var height = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("###CalendarCanvas", new(0, Math.Max(1, height)));
        try { DrawMonth(); }
        finally { ImGui.EndChild(); }
    }
    private void DrawCalendarActions()
    {
        var f = ImGui.GetFontSize();
        var compact = Compact;
        var monthText = model.Year + "년 " + model.MonthNumber + "월";
        var controlHeight = UiWidgets.IconButtonSize("", compact).Y;
        if (UiWidgets.HeadingButton("calendar.month", monthText, compact ? 1.25f : 1.5f, controlHeight))
        { jumpYear = model.Year; jumpMonth = model.MonthNumber; ImGui.OpenPopup("###MonthJump"); }
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("클릭해서 다른 달로 이동");
        DrawMonthJump();
        ImGui.SameLine();
        if (UiWidgets.IconButton("prev", UiIcon.Left, "", false, Accent, compact)) model.ChangeMonth(-1);
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("이전 달 · 달력 위 휠로도 이동할 수 있습니다");
        ImGui.SameLine(); if (UiWidgets.IconButton("next", UiIcon.Right, "", false, Accent, compact)) model.ChangeMonth(1);
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("다음 달");
        ImGui.SameLine(); if (ImGui.Button("오늘", new(ImGui.CalcTextSize("오늘").X + f * 1.6f, controlHeight))) model.GoToday();
        var toolsWidth = UiWidgets.IconButtonSize(compact ? "" : "표시", compact).X + ImGui.GetStyle().ItemSpacing.X + UiWidgets.IconButtonSize(compact ? "" : "일정 추가", compact).X;
        var toolsLeft = ImGui.GetWindowContentRegionMax().X - toolsWidth;
        // Keep the two actions together when the month controls fill a narrow row.
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X <= ImGui.GetWindowPos().X + toolsLeft + .5f)
            ImGui.SameLine(toolsLeft);
        else ImGui.SetCursorPosX(toolsLeft);
        if (UiWidgets.IconButton("filters", UiIcon.Filter, compact ? "" : "표시", false, Accent, compact)) ImGui.OpenPopup("###CalendarVisibilityPopup");
        CalendarFilterTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        var filterY = ImGui.GetItemRectMin().Y;
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("표시할 일정과 기간 막대 설정");
        ImGui.SameLine();
        if (UiWidgets.IconButton("add", UiIcon.Plus, compact ? "" : "일정 추가", false, Accent, compact)) ImGui.OpenPopup("###AddScheduleMenu");
        CalendarActionsSingleRow = Math.Abs(ImGui.GetItemRectMin().Y - filterY) < 1;
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("일정 추가");
        if (ImGui.BeginPopup("###AddScheduleMenu"))
        {
            if (ImGui.MenuItem("한 번 등록")) OpenEditor();
            if (ImGui.MenuItem("공격대 반복 등록")) OpenWeeklyRaidPlanner();
            ImGui.EndPopup();
        }
        HeaderFits = ImGui.GetItemRectMax().X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
        ImGui.SetNextWindowSizeConstraints(new(f * 15, 0), new(f * 22, ImGui.GetMainViewport().WorkSize.Y - f * 3));
        if (ImGui.BeginPopup("###CalendarVisibilityPopup"))
        {
            try { DrawCalendarVisibility(); }
            finally { ImGui.EndPopup(); }
        }
    }
    private static void ContinueRow(float width)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right + .5f) ImGui.SameLine();
    }
    private void DrawCalendarVisibility()
    {
        ImGui.TextUnformatted("달력에 표시할 일정"); ImGui.Spacing();
        foreach (var (group, label, category, _) in CalendarAppearanceStyle.Groups)
        {
            var enabled = model.IsCalendarVisible(group);
            if (ImGui.Checkbox("###visible." + group, ref enabled)) model.SetCalendarVisible(group, enabled);
            ImGui.SameLine(); DrawAppearanceLabel(category, label, ImGui.GetFrameHeight());
            if (ImGui.IsItemClicked()) model.SetCalendarVisible(group, !enabled);
        }
        foreach (var custom in model.State.View.UserCategories)
        {
            var enabled = !model.State.View.HiddenUserCategoryIds.Contains(custom.Id);
            if (ImGui.Checkbox("###visible." + custom.Id, ref enabled)) model.SetUserCategoryVisible(custom.Id, enabled);
            ImGui.SameLine(); DrawAppearanceLabel(custom.Id, custom.Name, ImGui.GetFrameHeight());
            if (ImGui.IsItemClicked()) model.SetUserCategoryVisible(custom.Id, !enabled);
        }
        ImGui.Separator();
        var fields = model.State.View.ShowFrontlineInCalendar;
        if (ImGui.Checkbox("날짜 칸에 전장 표시", ref fields)) model.UpdateView(v => v with { ShowFrontlineInCalendar = fields });
        FrontlineVisibilityTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("상단의 오늘·내일 전장 요약은 그대로 표시합니다.");
        var bars = model.State.View.PeriodBars;
        if (ImGui.Checkbox("기간을 막대로 표시", ref bars)) model.SetPeriodBars(bars);
        ImGui.Separator();
        if (ImGui.Selectable("색상·아이콘 설정 ↗")) { activeTab = 2; appearanceSettingsRequest = true; }
    }
    private void DrawMonthJump()
    {
        var f = ImGui.GetFontSize();
        ImGui.SetNextWindowSizeConstraints(new(f * 16, 0), new(f * 20, ImGui.GetMainViewport().WorkSize.Y));
        if (!ImGui.BeginPopup("###MonthJump")) return;
        try
        {
            ImGui.TextUnformatted("다른 달로 이동");
            ImGui.SetNextItemWidth(f * 8); ImGui.InputInt("년", ref jumpYear);
            ImGui.SetNextItemWidth(f * 8); ImGui.InputInt("월", ref jumpMonth);
            ImGui.BeginDisabled(jumpYear is < 2020 or > 2100 || jumpMonth is < 1 or > 12);
            if (ImGui.Button("이동")) { model.GoToMonth(jumpYear, jumpMonth); ImGui.CloseCurrentPopup(); }
            ImGui.EndDisabled();
        }
        finally { ImGui.EndPopup(); }
    }
    private string FrontlineMap(DateOnly date)
    {
        return frontline?.Get(date, model.Now)?.Map ?? (frontline?.IsBusy == true ? "불러오는 중…" : "정보 없음");
    }
    private float FrontlineSummaryWidth() => ImGui.GetFontSize() * 4.6f +
        ImGui.CalcTextSize("오늘" + FrontlineMap(model.Today) + "내일" + FrontlineMap(model.Today.AddDays(1))).X;
    private void DrawFrontline(bool inline = false)
    {
        if (!model.State.View.FrontlineUpdatesEnabled) return;
        var f = ImGui.GetFontSize();
        var available = ImGui.GetContentRegionAvail().X;
        var stacked = FrontlineSummaryWidth() > available;
        var size = new Vector2(Math.Min(FrontlineSummaryWidth(), available), stacked ? f * 3.25f : UiWidgets.IconButtonSize("", Compact).Y);
        var pos = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("###frontlineSummary", size)) model.Post(() => Util.OpenLink(FrontlineScheduleClient.SourceUrl));
        var draw = ImGui.GetWindowDrawList(); var muted = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        draw.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(Accent with { W = ImGui.IsItemHovered() ? .12f : .055f }), 6);
        var centerY = stacked ? f * .48f : (size.Y - f) / 2;
        UiWidgets.DrawIcon(UiIcon.Swords, pos + new Vector2(f * .5f, centerY + f * .05f), f * .9f, muted);
        var x = pos.X + f * 1.85f; var y = pos.Y + centerY;
        draw.PushClipRect(pos, pos + size, true);
        foreach (var (label, date) in new[] { ("오늘", model.Today), ("내일", model.Today.AddDays(1)) })
        {
            if (label == "내일")
            {
                if (stacked) { x = pos.X + f * 1.85f; y += f * 1.3f; }
                else { x += f * .65f; draw.AddLine(new(x, y), new(x, y + f), muted); x += f * .65f; }
            }
            draw.AddText(new(x, y), muted, label); x += ImGui.CalcTextSize(label).X + f * .5f;
            var map = FrontlineMap(date); draw.AddText(new(x, y), ImGui.GetColorU32(ImGuiCol.Text), map); x += ImGui.CalcTextSize(map).X;
        }
        draw.PopClipRect();
        FrontlineSummaryFits = x <= pos.X + size.X + .5f;
        if (ImGui.IsItemHovered())
        {
            var today = frontline?.Get(model.Today, model.Now); var tomorrow = frontline?.Get(model.Today.AddDays(1), model.Now);
            UiWidgets.Tooltip("오늘 · " + (today?.Name ?? frontline?.Error ?? "정보 없음") + "\n내일 · " + (tomorrow?.Name ?? "정보 없음") +
                (frontline?.IsCached(model.Today, model.Now) == true ? "\n저장된 정보를 표시 중입니다." : "") + "\n에테르웨이 제공 · 한국 서버 · 매일 0시 변경\n클릭하면 제공 사이트를 엽니다.");
        }
    }
    private void DrawMonth()
    {
        if (model.Calendar is null) return;
        var f = ImGui.GetFontSize();
        var minRow = f * 4.8f;
        var needed = f * 2 + model.Calendar.Weeks.Count * minRow;
        var height = Math.Max(needed, ImGui.GetContentRegionAvail().Y - 1);
        gridRowHeight = (height - f * 2) / model.Calendar.Weeks.Count;
        ImGui.BeginChild("###MonthGrid", new(Math.Max(1, ImGui.GetContentRegionAvail().X), height), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        try { DrawMonthGrid(); HandleMonthWheel(); }
        finally { ImGui.EndChild(); }
    }
    private void DrawMonthGrid()
    {
        if (model.Calendar is null) return;
        FrontlineCellLabelCount = 0;
        OguDayTargets.Clear();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var cellWidth = width / 7;
        GridOrigin = origin; GridCellWidth = cellWidth;
        var f = ImGui.GetFontSize();
        var draw = ImGui.GetWindowDrawList();
        var line = ImGui.ColorConvertFloat4ToU32(ThemeScope.BorderColor(model.State.View.Theme));
        var rowHeight = gridRowHeight;
        var weekdayHeight = f * 1.8f;
        var markerHeight = f * 1.5f;
        var weekdays = new[] { "일", "월", "화", "수", "목", "금", "토" };
        for (var c = 0; c < 7; c++)
        {
            var color = c == 0 ? ThemeScope.Hex(Light ? 0xb34e64 : 0xf29aaa) : c == 6 ? ThemeScope.Hex(Light ? 0x436cba : 0x9ab9ff) : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            draw.AddText(origin + new Vector2(c * cellWidth + 10, 2), ImGui.ColorConvertFloat4ToU32(color), weekdays[c]);
        }
        for (var w = 0; w < model.Calendar.Weeks.Count; w++)
        {
            var week = model.Calendar.Weeks[w];
            var row = origin + new Vector2(0, weekdayHeight + w * rowHeight);
            for (var c = 0; c < 7; c++)
            {
                var day = week.Days[c];
                var pos = row + new Vector2(c * cellWidth, 0);
                var end = pos + new Vector2(cellWidth, rowHeight);
                draw.AddRectFilled(pos, end, ImGui.ColorConvertFloat4ToU32(ThemeScope.CalendarSurface(model.State.View.Theme, day.InMonth)));
                var selected = model.HasDateFocus && day.Date == model.SelectedDate;
                if (selected) draw.AddRectFilled(pos + Vector2.One, end - Vector2.One, ImGui.ColorConvertFloat4ToU32(Accent with { W = .08f }));
                draw.AddLine(pos, new(end.X, pos.Y), line);
                if (c > 0) draw.AddLine(pos, new(pos.X, end.Y), line);
                ImGui.SetCursorScreenPos(pos + new Vector2(2));
                if (ImGui.InvisibleButton("###day." + UiText.Date(day.Date), new(cellWidth - 4, rowHeight - 4))) OpenDay(day.Date);
                ImGui.SetItemAllowOverlap();
                if (ImGui.IsItemHovered()) draw.AddRectFilled(pos + Vector2.One, end - Vector2.One, ImGui.ColorConvertFloat4ToU32(Accent with { W = .04f }));
                var number = day.Date.Day.ToString(CultureInfo.InvariantCulture);
                var datePos = pos + new Vector2(f * .55f,f * .4f);
                var color = day.InMonth ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
                if (day.Date == model.Today)
                {
                    color = Light ? Vector4.One : ThemeScope.Hex(0x0e1727);
                    UiWidgets.NumberBadge(datePos + new Vector2(f * .5f),number,f * 1.65f,Accent,color,.9f);
                }
                else draw.AddText(datePos, ImGui.ColorConvertFloat4ToU32(color), number);
                var oguDay = SpecialCalendarDays.IsOguDay(day.Date);
                if (oguDay) DrawOguDayDecoration(day.Date,pos,end);
                var field = model.State.View.FrontlineUpdatesEnabled && model.State.View.ShowFrontlineInCalendar ? frontline?.Get(day.Date, model.Now) : null;
                if (!oguDay && field is not null && cellWidth > f * 6)
                {
                    var shortMap = field.Map switch { "봉인된 바위섬" => "봉바", "영광의 평원" => "쇄빙", "온살 하카이르" => "온살", "외곽 유적지대" => "제압", _ => "워코" };
                    draw.AddText(new(end.X - ImGui.CalcTextSize(shortMap).X - 9, datePos.Y), ImGui.GetColorU32(ImGuiCol.TextDisabled), shortMap);
                    FrontlineCellLabelCount++;
                }
                var laneHeight = week.LaneCount * markerHeight;
                var capacity = Math.Max(0, (int)((rowHeight - f * 1.9f - laneHeight - 3) / markerHeight));
                var entries = CalendarPresentation.Build(day.Items, day.Date);
                var overflow = day.HiddenCount > 0 || entries.Count > capacity;
                var headerOverflow = cellWidth >= f * 7.5f;
                var shown = Math.Max(0, Math.Min(entries.Count, capacity - (overflow && !headerOverflow ? 1 : 0)));
                for (var i = 0; i < shown; i++)
                {
                    var entry = entries[i]; var item = entry.Primary;
                    var point = pos + new Vector2(5, f * 1.9f + laneHeight + markerHeight * i);
                    DrawCalendarButton(point, new(cellWidth - 10, markerHeight - 4), item.Event, item.Phase,
                        "point." + UiText.Date(day.Date) + "." + i, CalendarMarkerLabel(entry), () => OpenDay(day.Date), entry.Group == CalendarDisplayGroup.Update ? UiIcon.Server : ScheduleIcon(entry.Category), entry.Category,
                        !entry.Ongoing && entry.Edge == "end" ? SubscriptionTime.IsSubscription(entry.Category) ? "만료" : entry.Category == "maintenance" ? "종료" : "마감" : null);
                }
                var more = day.HiddenCount + Math.Max(0, entries.Count - shown);
                if (more > 0)
                {
                    var morePos = headerOverflow ? pos + new Vector2(f * 2.1f, 6) : pos + new Vector2(9, f * 1.9f + laneHeight + markerHeight * shown);
                    var label = "+" + more + (headerOverflow ? "" : "개");
                    var size = headerOverflow ? new Vector2(ImGui.CalcTextSize(label).X + 10, f + 2) : new Vector2(cellWidth - 18, markerHeight - 2);
                    ImGui.SetCursorScreenPos(morePos);
                    if (ImGui.InvisibleButton("###more." + UiText.Date(day.Date), size)) OpenDay(day.Date);
                    if (headerOverflow) draw.AddRectFilled(morePos, morePos + size, ImGui.ColorConvertFloat4ToU32(Accent with { W = .12f }), 5);
                    draw.AddText(morePos + new Vector2(headerOverflow ? 5 : 0, 0), ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip(more + "개 더 보기 · 클릭하면 날짜의 전체 일정을 엽니다.");
                }
            }
            foreach (var bar in week.Bars)
            {
                var pos = row + new Vector2(bar.StartColumn * cellWidth + 5, f * 1.9f + bar.Lane * markerHeight);
                var size = new Vector2((bar.EndColumn - bar.StartColumn + 1) * cellWidth - 10, markerHeight - 4);
                DrawCalendarButton(pos, size, bar.Event, bar.Phase, "bar." + w + "." + bar.Event.Id + "." + bar.Phase.Id,
                    (bar.ContinuesBefore ? "‹ " : "") + bar.Event.Title + (bar.ContinuesAfter ? " ›" : ""),
                    () => OpenDay(week.StartsOn.AddDays(Math.Clamp((int)((ImGui.GetIO().MousePos.X - row.X) / cellWidth), bar.StartColumn, bar.EndColumn))));
            }
        }
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new(width, weekdayHeight + model.Calendar.Weeks.Count * rowHeight));
    }
    private void OpenDay(DateOnly date)
    {
        if (date.Year is < 2020 or > 2100) return;
        model.SelectDate(date); dayPopupRequest = true; resetOngoingSection = true;
    }
    private void DrawDayPopup()
    {
        var f = ImGui.GetFontSize();
        OngoingSectionExpanded = false;
        if (dayPopupRequest) { model.Tick(model.Now); ImGui.OpenPopup("###DaySchedule"); dayPopupRequest = false; }
        var work = ImGui.GetMainViewport().WorkSize;
        ImGui.SetNextWindowSizeConstraints(new(Math.Min(f * 29, work.X - 30), 0), new(Math.Min(f * 35, work.X - 30), work.Y - f * 3));
        if (!ImGui.BeginPopup("###DaySchedule")) return;
        try
        {
            CardMenuTargets.Clear(); HideItemTargets.Clear(); RelatedHideTargets.Clear(); CardAlertTargets.Clear(); CardEditTargets.Clear(); CardDeleteTargets.Clear();
            UiWidgets.Heading(model.SelectedDate.ToString("M월 d일 (ddd)", CultureInfo.GetCultureInfo("ko")), 1.25f);
            if (SpecialCalendarDays.IsOguDay(model.SelectedDate)) DrawOguDayHeading();
            var field = model.State.View.FrontlineUpdatesEnabled ? frontline?.Get(model.SelectedDate, model.Now) : null;
            if (field is not null) UiWidgets.Secondary("전장 · " + field.Map);
            ImGui.Spacing();
            var direct = model.DayDisplayEntries.Where(e => !e.Ongoing).ToArray();
            var ongoing = model.DayDisplayEntries.Where(e => e.Ongoing).ToArray();
            if (direct.Length == 0)
            {
                UiWidgets.Wrapped("이날 시작·마감하거나 직접 등록한 일정은 없습니다.");
                if (UiWidgets.IconButton("emptyDay.add", UiIcon.Plus, "개인 일정 추가", false, Accent)) { OpenEditor(); ImGui.CloseCurrentPopup(); }
            }
            foreach (var entry in direct) DrawScheduleCard(entry, compact: false);
            if (ongoing.Length > 0)
            {
                ImGui.Spacing(); ImGui.Separator();
                if (resetOngoingSection) ImGui.SetNextItemOpen(false, ImGuiCond.Always);
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                var expanded = ImGui.TreeNode("기간 중인 이벤트 " + ongoing.Length + "건###ongoingEvents");
                OngoingToggleCenter = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                OngoingSectionExpanded = expanded;
                ImGui.PopStyleColor();
                if (expanded)
                {
                    try
                    {
                        UiWidgets.Secondary("선택한 날짜가 기간에 포함된 일정입니다.");
                        foreach (var entry in ongoing) DrawScheduleCard(entry, compact: true);
                    }
                    finally { ImGui.TreePop(); }
                }
            }
            resetOngoingSection = false;
            if (ruleRequest || editorRequest || entryDeleteRequest) ImGui.CloseCurrentPopup();
        }
        finally { ImGui.EndPopup(); }
    }
    private UiIcon ScheduleIcon(string category) => CalendarAppearanceStyle.Resolve(model.State.View, category).Icon;
    private string ScheduleLabel(string category) => model.State.View.UserCategories.FirstOrDefault(c => c.Id == category)?.Name ?? (category switch
    {
        "personal" => "미분류", "subscription" or "retainer" or "bag" => "이용권",
        "maintenance" => "서버 점검", "patch" => "업데이트·출시", "pvp-season" => "PvP 시즌", "pvp-series" or "reward" => "PvP 보상",
        "competition" => "대회", "sale" or "discount" => "예약·할인", _ => "이벤트"
    });
    private string CardPeriod(CalendarItem item)
    {
        if (item.Phase.Point && item.Phase.Start.Kind == "date") return item.Event.IsPersonal ? "날짜만 지정" : item.Phase.Start.Certainty switch
        { "tentative" => "예정 · 시각 미발표", "inferred" => "추정 날짜 · 시각 미발표", _ => "시각 미발표" };
        var period = UiText.Period(item.Phase, item.Event.IsPersonal);
        var prefix = UiText.Date(model.SelectedDate) + " ";
        if ((item.Phase.Point || CalendarEngine.BoundaryDate(item.Phase.Start) == model.SelectedDate && CalendarEngine.BoundaryDate(item.Phase.End) == model.SelectedDate) && period.StartsWith(prefix, StringComparison.Ordinal))
            return period[prefix.Length..].TrimStart();
        return period;
    }
    private void DrawScheduleCard(CalendarDisplayEntry entry, bool compact)
    {
        var previousScale = ImGui.GetCurrentContext().CurrentWindow.FontWindowScale;
        if (compact) ImGui.SetWindowFontScale(previousScale * .9f);
        try
        {
            var f = ImGui.GetFontSize(); var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var item = entry.Primary; var title = UiText.DisplayTitle(entry);
            var color = CalendarAppearanceStyle.Resolve(model.State.View, entry.Category).Color; var tint = ImGui.ColorConvertFloat4ToU32(color);
            var bodyWidth = Math.Max(f, width - f * 1.5f);
            var lines = new List<string>();
            if (entry.Group == CalendarDisplayGroup.PurchaseCampaign)
            {
                foreach (var member in entry.Members.DistinctBy(i => (i.Event.Id, i.Phase.Id)))
                    lines.Add(UiText.PurchaseRole(member.Event) + " · " + CardPeriod(member));
            }
            else
            {
                if ((model.Events.FirstOrDefault(e => e.Id == item.Event.Id)?.Phases.Count ?? item.Event.Phases.Count) > 1) lines.Add(UiText.Phase(item.Event, item.Phase));
                lines.Add((entry.Group == CalendarDisplayGroup.Update ? "서버 점검 · " : "") + CardPeriod(item));
                if (item.Event.IsPersonal && !string.IsNullOrWhiteSpace(item.Event.Note)) lines.Add(item.Event.Note);
            }
            var titleHeight = ImGui.CalcTextSize(title, false, bodyWidth).Y;
            var spacing = ImGui.GetStyle().ItemSpacing.Y;
            var linesHeight = lines.Sum(line => ImGui.CalcTextSize(line, false, bodyWidth).Y + spacing);
            var review = entry.Members.Any(i => NeedsReview(i.Event));
            var height = f * 2.45f + titleHeight + spacing + linesHeight + (review ? f + spacing : 0);
            var pos = ImGui.GetCursorScreenPos();
            var clicked = false;
            if (item.Event.IsPersonal) ImGui.Dummy(new(width,height)); else clicked = ImGui.InvisibleButton("###day.card." + entry.Key,new(width,height));
            var hovered = ImGui.IsItemHovered(); ImGui.SetItemAllowOverlap(); var after = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(color with { W = compact ? .045f : hovered && !item.Event.IsPersonal ? .15f : .09f }), 7);
            draw.AddLine(pos + new Vector2(1, 9), pos + new Vector2(1, height - 9), tint, compact ? 1.5f : 3);
            UiWidgets.DrawIcon(ScheduleIcon(entry.Category), pos + new Vector2(f * .7f, f * .55f), f, tint);
            var action = entry.Ongoing ? "기간 중" : entry.Edge == "end" ? SubscriptionTime.IsSubscription(entry.Category) ? "만료" : entry.Category == "maintenance" ? "종료" : "마감" : entry.Edge == "start" && !item.Event.IsPersonal ? "시작" : "";
            var status = !entry.Ongoing && entry.Edge == "end" ? action : null;
            var headerLabel = ScheduleLabel(entry.Category) + (status is null && action.Length > 0 ? " · " + action : "");
            var headerRoom = Math.Max(f,width - CardPrimaryActionsWidth(entry) - f * (item.Event.IsPersonal ? 3 : 4.5f) - (status is null ? 0 : UiWidgets.StatusBadgeWidth(status)));
            var fittedHeader = FitLabel(headerLabel,headerRoom);
            draw.AddText(pos + new Vector2(f * 2,f * .55f),tint,fittedHeader);
            if (status is not null)
            {
                var (background, foreground) = ThemeScope.StatusBadgeColors(status, Light);
                UiWidgets.StatusBadge(pos + new Vector2(f * 2.3f + ImGui.CalcTextSize(fittedHeader).X,f * .5f), f * 1.15f, status, background, foreground);
            }
            var sources = entry.Members.Select(i => SourceFor(i.Event, i.Phase)).Where(url => url is not null).Select(url => url!).Distinct(StringComparer.Ordinal)
                .OrderBy(url => url.EndsWith("/news/event", StringComparison.Ordinal)).ToArray();
            ImGui.SetCursorScreenPos(pos + new Vector2(f * .7f, f * 1.95f));
            var bodyLeft = ImGui.GetCursorPosX();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + bodyWidth);
            try
            {
                ImGui.TextUnformatted(title);
                foreach (var line in lines) { ImGui.SetCursorPosX(bodyLeft); ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]); try { ImGui.TextUnformatted(line); } finally { ImGui.PopStyleColor(); } }
                if (review) { ImGui.SetCursorPosX(bodyLeft); ImGui.TextUnformatted("내용 재확인 필요"); }
            }
            finally { ImGui.PopTextWrapPos(); }
            var members = entry.Members.DistinctBy(i => (i.Event.Id,i.Phase.Id)).ToArray();
            var actionWidth = CardPrimaryActionsWidth(entry);
            var actionPos = pos + new Vector2(Math.Max(f * 2,width - actionWidth - (item.Event.IsPersonal ? f * .5f : f * 2)),f * .3f);
            ImGui.SetCursorScreenPos(actionPos); DrawCardPrimaryActions(entry,members);
            var menuPos = pos + new Vector2(width - f * 1.8f, f * .25f); var menuSize = new Vector2(f * 1.5f, f * 1.5f);
            var related = model.RelatedEventIds(members.Select(i => i.Event.Id));
            var relatedEvents = model.Events.Where(e => related.Contains(e.Id)).ToArray();
            var phaseCount = relatedEvents.Sum(e => e.Phases.Count(p => CalendarContentPolicy.Includes(e,p)));
            var showMore = !item.Event.IsPersonal && (sources.Length > 0 || phaseCount > members.Length);
            var menuHovered = false;
            if (showMore)
            {
            ImGui.SetCursorScreenPos(menuPos);
            if (ImGui.InvisibleButton("###cardMenu." + entry.Key, menuSize)) ImGui.OpenPopup("###cardActions." + entry.Key);
            CardMenuTargets[entry.Key] = menuPos + menuSize / 2;
            menuHovered = ImGui.IsItemHovered();
            UiWidgets.DrawIcon(UiIcon.More, menuPos + new Vector2(f * .3f), f * .85f, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            var work = ImGui.GetMainViewport().WorkSize;
            ImGui.SetNextWindowSizeConstraints(new(0, 0), new(work.X - 24, work.Y - 24));
            if (ImGui.BeginPopup("###cardActions." + entry.Key))
            {
                try
                {
                    if (!item.Event.IsPersonal && phaseCount > members.Length)
                    {
                    if (ImGui.MenuItem("관련 일정 모두 숨기기 (" + phaseCount + "개)"))
                        foreach (var id in related) model.SetEventHidden(id, true);
                    RelatedHideTargets[entry.Key] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip(string.Join("\n", relatedEvents.Select(e => e.Title + "\n  " + string.Join(" · ", e.Phases.Where(p => CalendarContentPolicy.Includes(e, p)).Select(p => p.Label).Take(8)))) + "\n같은 행사 또는 관계를 확인한 일정만 숨깁니다. 분류 표시 설정은 바꾸지 않습니다.");
                    }
                    if (sources.Length > 0)
                    {
                        ImGui.Separator();
                        for (var index = 0; index < sources.Length; index++)
                        {
                            var url = sources[index];
                            if (ImGui.MenuItem(sources.Length == 1 ? "공식 안내" : "공식 안내 " + (index + 1))) model.Post(() => Util.OpenLink(url));
                        }
                    }
                }
                finally { ImGui.EndPopup(); }
            }
            }
            ImGui.SetCursorScreenPos(after);
            var mouse = ImGui.GetIO().MousePos;
            var inMenu = mouse.X >= menuPos.X && mouse.X <= menuPos.X + menuSize.X && mouse.Y >= menuPos.Y && mouse.Y <= menuPos.Y + menuSize.Y;
            if (clicked && !inMenu && mouse.Y > pos.Y + f * 1.7f)
            {
                if (!item.Event.IsPersonal && sources.FirstOrDefault() is { } url) model.Post(() => Util.OpenLink(url));
            }

            ImGui.Spacing();
        }
        finally { ImGui.SetWindowFontScale(previousScale); }
    }
    private string? SourceFor(CalendarEvent item, EventPhase? phase = null)
    {
        return OfficialEventLinks.Resolve(model.Feed,item,phase);
    }
    private void HandleMonthWheel()
    {
        var io = ImGui.GetIO();
        if (!ImGui.IsWindowHovered() || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel) || ImGui.IsAnyItemActive() || io.KeyCtrl || io.KeyShift || io.KeyAlt)
        { monthWheelRemainder = 0; return; }
        var context = ImGui.GetCurrentContext();
        if (context.HoveredId == 0) context.HoveredId = ImGui.GetID("###MonthWheel");
        context.HoveredIdUsingMouseWheel = true;
        if (io.MouseWheel == 0) return;
        monthWheelRemainder = Math.Clamp(monthWheelRemainder + io.MouseWheel, -12f, 12f); io.MouseWheel = 0;
        var notches = (int)MathF.Truncate(monthWheelRemainder); monthWheelRemainder -= notches;
        var current = model.Year * 12 + model.MonthNumber - 1;
        var target = Math.Clamp(current - notches, 2020 * 12, 2100 * 12 + 11);
        if (target != current) model.ChangeMonth(target - current);
    }
    private void DrawCalendarButton(Vector2 pos, Vector2 size, CalendarEvent e, EventPhase phase, string id, string label, Action clicked, UiIcon? icon = null, string? category = null, string? status = null)
    {
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton("###" + id, size)) clicked();
        var hovered = ImGui.IsItemHovered();
        var style = CalendarAppearanceStyle.Resolve(model.State.View, category ?? e.Category);
        var color = style.Color;
        if (icon.HasValue) icon = style.Icon;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(color with { W = hovered ? .3f : status is not null ? .2f : .15f }), 4);
        draw.PushClipRect(pos, pos + size, true);
        var inset = icon.HasValue ? ImGui.GetFontSize() * 1.2f : 7;
        if (status is not null)
        {
            var (background, foreground) = ThemeScope.StatusBadgeColors(status, Light);
            var badgeLabel = size.X >= ImGui.GetFontSize() * 5.5f ? status : status == "종료" ? "끝" : "!";
            UiWidgets.StatusBadge(pos + new Vector2(2, 1), size.Y - 2, badgeLabel, background, foreground);
            inset = UiWidgets.StatusBadgeWidth(badgeLabel) + ImGui.GetFontSize() * .4f;
        }
        else if (icon.HasValue) UiWidgets.DrawIcon(icon.Value, pos + new Vector2(5, Math.Max(0, (size.Y - ImGui.GetFontSize() * .75f) / 2)), ImGui.GetFontSize() * .75f, ImGui.ColorConvertFloat4ToU32(color));
        draw.AddText(pos + new Vector2(inset, Math.Max(0, (size.Y - ImGui.GetFontSize()) / 2)), ImGui.ColorConvertFloat4ToU32(color), FitLabel(label, Math.Max(1, size.X - inset - 5)));
        draw.PopClipRect();
        if (hovered) UiWidgets.Tooltip((status is not null ? status + " · " : "") + e.Title + "\n" + UiText.Period(phase));
    }
    internal static string CalendarMarkerLabel(CalendarDisplayEntry entry)
    {
        var label = UiText.DisplayMarker(entry);
        if (entry.Ongoing) return label;
        var item = entry.Primary;
        var boundary = entry.Edge == "end" ? item.Phase.End : item.Phase.Start;
        if (CalendarEngine.StartInstant(boundary) is not { } instant) return label;
        var clock = instant.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm",CultureInfo.InvariantCulture);
        return label.StartsWith(clock,StringComparison.Ordinal) ? label : clock + " · " + label;
    }
    private static string FitLabel(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var positions = StringInfo.ParseCombiningCharacters(text);
        for (var count = positions.Length - 1; count > 0; count--)
        { var candidate = text[..positions[count]] + "…"; if (ImGui.CalcTextSize(candidate).X <= width) return candidate; }
        return "";
    }
    private static bool NeedsReview(CalendarEvent item) => item.Note?.Contains("공식 원문 재검토 대기", StringComparison.Ordinal) == true;
    private void DrawFilters()
    {
        var f = ImGui.GetFontSize();
        ImGui.SetNextItemWidth(Math.Min(f * 18, ImGui.GetContentRegionAvail().X));
        if (ImGui.InputTextWithHint("###newsSearch", "새소식 검색", ref search, 120)) model.SetNewsQuery(search, category);
        ContinueRow(f * 10);
        ImGui.SetNextItemWidth(f * 10);
        var names = new[] { "전체", "이벤트", "업데이트·점검", "PvP", "할인·예약", "공지" };
        if (ImGui.Combo("###newsCategory", ref category, names, names.Length)) model.SetNewsQuery(search, category);
        NewsFiltersFit = ImGui.GetItemRectMax().X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
        ImGui.Spacing();
    }
    private void DrawNews()
    {
        UiWidgets.Heading("새소식", 1.5f); DrawFilters();
        var f = ImGui.GetFontSize();
        var narrow = ImGui.GetContentRegionAvail().X < f * 25;
        var dateWidth = f * (narrow ? 3.2f : 6.3f);
        var titleWidth = Math.Max(f, ImGui.GetContentRegionAvail().X - dateWidth - ImGui.GetStyle().ScrollbarSize - ImGui.GetStyle().CellPadding.X * 4);
        if (!ImGui.BeginTable("###NewsRows", 2, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit, new(0, Math.Max(1, ImGui.GetContentRegionAvail().Y)))) return;
        try
        {
            ImGui.TableSetupColumn("날짜", ImGuiTableColumnFlags.WidthFixed, dateWidth);
            ImGui.TableSetupColumn("공식 안내  ↗", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
            foreach (var item in model.NewsEvents)
            {
                var date = CalendarModel.NewsDate(item);
                var titleHeight = ImGui.CalcTextSize(item.Title, false, titleWidth).Y;
                ImGui.TableNextRow(ImGuiTableRowFlags.None, Math.Max(f * 2.2f, titleHeight + f * .6f)); ImGui.TableNextColumn();
                UiWidgets.Secondary(date?.ToString(narrow ? "MM.dd" : "yyyy.MM.dd", CultureInfo.InvariantCulture) ?? "—");
                ImGui.TableNextColumn();
                var url = SourceFor(item);
                var pos = ImGui.GetCursorScreenPos();
                var clicked = ImGui.Selectable("###news." + item.Id, false, ImGuiSelectableFlags.None, new(-1, titleHeight));
                var hovered = ImGui.IsItemHovered(); var after = ImGui.GetCursorScreenPos();
                ImGui.SetCursorScreenPos(pos); ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + titleWidth);
                try { ImGui.TextUnformatted(item.Title); }
                finally { ImGui.PopTextWrapPos(); }
                ImGui.SetCursorScreenPos(after);
                if (clicked && url is not null) model.Post(() => Util.OpenLink(url));
                if (hovered) UiWidgets.Tooltip(item.Title + (url is null ? "\n연결된 공식 안내가 없습니다." : "\n클릭하면 공식 안내를 엽니다."));
            }
            if (model.NewsEvents.Count == 0) { ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted("검색 결과가 없습니다."); }
        }
        finally { ImGui.EndTable(); }
    }
    private void DrawSubscriptions()
    {
        SubscriptionResetTargets.Clear();
        var entries = model.State.Entries.Where(e => e.ProfileId == model.State.ActiveProfileId && SubscriptionTime.IsSubscription(e.Kind)).ToArray();
        UiWidgets.Heading("이용권", 1.15f);
        UiWidgets.Wrapped("만료일을 달력에 표시합니다.");
        foreach (var entry in entries)
        {
            ImGui.PushID(entry.Id);
            try
            {
                ImGui.Spacing(); ImGui.TextUnformatted(SubscriptionTime.Name(entry.Kind));
                var soon = entry.When.Kind == "instant" && CalendarEngine.StartInstant(entry.When) is { } instant ? instant - model.Now <= TimeSpan.FromDays(1)
                    : CalendarEngine.BoundaryDate(entry.When) <= model.Today;
                ImGui.PushStyleColor(ImGuiCol.Text, soon ? CalendarAppearanceStyle.Resolve(model.State.View, entry.Kind).Color : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                try { UiWidgets.Wrapped(UiText.LocalBoundary(entry.When) + " · " + UiText.SubscriptionRemaining(entry.When, model.Now)); }
                finally { ImGui.PopStyleColor(); }
                if (ImGui.SmallButton("충전")) { OpenEditor(entry, true); chargeMode = true; }
                ImGui.SameLine(); if (ImGui.SmallButton("만료일 수정")) OpenEditor(entry, true);
                ContinueRow(ImGui.CalcTextSize("초기화").X + ImGui.GetStyle().FramePadding.X * 2);
                if (ImGui.SmallButton("초기화")) ImGui.OpenPopup("###ResetSubscription");
                SubscriptionResetTargets[entry.Id] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                var f = ImGui.GetFontSize(); var work = ImGui.GetMainViewport().WorkSize;
                var resetWidth = Math.Min(f * 22, work.X - 24);
                ImGui.SetNextWindowSizeConstraints(new(resetWidth, 0), new(resetWidth, work.Y - 24));
                if (ImGui.BeginPopup("###ResetSubscription"))
                {
                    try
                    {
                        UiWidgets.Wrapped(SubscriptionTime.Name(entry.Kind) + " 등록 내용을 초기화할까요?");
                        UiWidgets.Wrapped("달력에 등록한 만료일과 메모를 지우고 미등록 상태로 되돌립니다.");
                        if (ImGui.Button("초기화하기")) { model.ResetSubscription(entry.Id); ImGui.CloseCurrentPopup(); }
                        SubscriptionResetConfirmTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                        ContinueRow(ImGui.CalcTextSize("취소").X + ImGui.GetStyle().FramePadding.X * 2);
                        if (ImGui.Button("취소")) ImGui.CloseCurrentPopup();
                        SubscriptionResetCancelTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                    }
                    finally { ImGui.EndPopup(); }
                }
            }
            finally { ImGui.PopID(); }
        }
        ImGui.Spacing();
        if (UiWidgets.IconButton("addSubscription", UiIcon.Plus, "이용권 등록", false, Accent)) OpenEditor(null, true);
    }
    private void DrawSettings()
    {
        var f = ImGui.GetFontSize();
        UiWidgets.Heading("설정", 1.5f);
        ContinueRow(ImGui.CalcTextSize("업데이트 내역").X + ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.SmallButton("업데이트 내역###releaseNotesSettings")) OpenReleaseNotes();
        ReleaseNotesSettingsTarget = ItemCenter();
        ImGui.BeginChild("###SettingsScroll", new(0, Math.Max(1, ImGui.GetContentRegionAvail().Y)));
        try
        {
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("테마"); ImGui.SameLine();
            var themes = new[] { "미드나이트", "차콜", "라이트" };
            themeIndex = model.State.View.Theme switch { "graphite" => 1, "paper" or "cloud" => 2, _ => 0 };
            ImGui.SetNextItemWidth(Math.Min(f * 13, ImGui.GetContentRegionAvail().X));
            if (ImGui.Combo("###theme", ref themeIndex, themes, themes.Length)) model.UpdateView(v => v with { Theme = themeIndex switch { 1 => "graphite", 2 => "cloud", _ => "midnight" }, Language = "ko" });
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            var wide = ImGui.GetContentRegionAvail().X >= f * 44;
            if (ImGui.BeginTable("###SettingsColumns", wide ? 2 : 1, ImGuiTableFlags.SizingStretchSame))
            {
                try
                {
                    ImGui.TableNextColumn();
                    UiWidgets.Heading("일정 업데이트", 1.15f);
                    var remote = model.State.View.RemoteUpdatesEnabled;
                    if (ImGui.Checkbox("일정 최신 정보 받기", ref remote)) model.UpdateView(v => v with { RemoteUpdatesEnabled = remote, CurrentUrl = BoundedFeedTransport.CurrentAddress });
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip("새로 등록되거나 기간이 변경된 일정을 주기적으로 가져옵니다. 개인 일정은 전송하지 않습니다.");
                    if (!remote)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, ThemeScope.Hex(Light ? 0x946015 : 0xe5bb6b));
                        try { UiWidgets.Wrapped("새 일정과 변경 사항이 반영되지 않습니다. 저장된 일정만 표시합니다."); }
                        finally { ImGui.PopStyleColor(); }
                    }
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, (UiWidgets.IconButtonSize("").Y - f) / 2));
                    if (remote)
                    {
                        var minutes = new[] { 30, 60, 180, 360, 720 };
                        var labels = new[] { "30분마다", "1시간마다", "3시간마다", "6시간마다", "12시간마다" };
                        var selected = Array.IndexOf(minutes, model.State.View.RemoteRefreshMinutes);
                        ImGui.SetNextItemWidth(Math.Min(f * 12, ImGui.GetContentRegionAvail().X));
                        if (ImGui.Combo("###remoteCadence", ref selected, labels, labels.Length)) model.UpdateView(v => v with { RemoteRefreshMinutes = minutes[selected] });
                        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("플러그인이 실행 중일 때 새 일정을 확인하는 주기입니다. 이미 공개된 새 일정이 있을 때 반영합니다.");
                        ContinueRow(UiWidgets.IconButtonSize("지금 확인").X);
                    }
                    var cooldown = model.Workspace.ManualRefreshAvailableAt;
                    ImGui.BeginDisabled(!remote || model.Workspace.IsBusy || model.Now < cooldown);
                    if (UiWidgets.IconButton("refresh", UiIcon.Refresh, "지금 확인", false, Accent)) model.Post(() => model.Workspace.Refresh(force: true));
                    ImGui.EndDisabled();
                    ImGui.PopStyleVar();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiWidgets.Tooltip(!remote ? "‘일정 최신 정보 받기’를 켜면 사용할 수 있습니다." : model.Now < cooldown ? "다시 확인할 수 있는 시각 · " + UiText.Time(cooldown) : "새 일정과 변경 사항을 지금 확인합니다.");
                    if (model.Workspace.IsRefreshing) UiWidgets.Secondary("새 일정을 확인하는 중…");
                    else if (remote && model.AutomaticUpdateHoldMessage is { } hold) UiWidgets.Wrapped(hold + " 수동 갱신은 가능합니다.");
                    var checks = new List<string>();
                    if (model.Workspace.LastClientCheckedAt is { } checkedAt) checks.Add("마지막 " + SettingsTime(checkedAt));
                    if (remote && model.Workspace.NextAutomaticRefreshAt is { } next)
                        checks.Add((model.Workspace.RemoteRefreshFailed ? "재시도 " : "다음 ") + (next <= model.Now ? "잠시 후" : SettingsTime(next)));
                    if (checks.Count > 0)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                        try { UiWidgets.Wrapped(string.Join("  ·  ", checks)); }
                        finally { ImGui.PopStyleColor(); }
                    }
                    if (model.Workspace.RemotePointer?.Health.Sources.Any(s => s.LastResult is "failed" or "review-needed") == true)
                        UiWidgets.Wrapped("일부 출처의 갱신이 지연되어 기존 일정을 유지합니다.");
                    ImGui.Spacing();
                    var fields = model.State.View.FrontlineUpdatesEnabled;
                    if (ImGui.Checkbox("전장 정보 받기", ref fields)) model.UpdateView(v => v with { FrontlineUpdatesEnabled = fields });
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip("오늘·내일 전장 정보를 6시간마다 확인합니다. 에테르웨이 제공 · 개인 일정은 전송하지 않습니다.");
                    var pause = model.State.View.PauseAutomaticUpdatesWhenBusy;
                    if (UiWidgets.WrappedCheckbox("pauseBusy", "전투·임무 중 자동 갱신 미루기", ref pause)) model.UpdateView(v => v with { PauseAutomaticUpdatesWhenBusy = pause });
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip("공개 일정과 전장의 자동 요청을 미룹니다. 종료 후 15초간 상태가 안정되면 재개하며, 수동 갱신은 사용할 수 있습니다.");
                    ImGui.TableNextColumn(); DrawSubscriptions();
                }
                finally { ImGui.EndTable(); }
            }
            ImGui.Spacing(); ImGui.Separator();
            DrawAppearanceSettings();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            DrawLocalNotificationSettings();
            ImGui.Spacing();
            if (model.Workspace.HasUnsavedChanges) UiWidgets.Wrapped("아직 저장되지 않은 변경사항이 있습니다.");
            DrawHiddenEvents();
            if (ImGui.TreeNode("고급 데이터 관리"))
            {
                try
                {
                    var example = model.State.View.ExampleMode;
                    if (ImGui.Checkbox("예시 데이터 사용", ref example)) model.Post(() => model.Workspace.LoadExample(example));
                    ImGui.SetNextItemWidth(Math.Min(f * 28, ImGui.GetContentRegionAvail().X));
                    ImGui.InputTextWithHint("###importPath", "가져올 JSON 파일 경로", ref importPath, 1024);
                    ImGui.BeginDisabled(string.IsNullOrWhiteSpace(importPath) || model.Workspace.IsBusy);
                    if (ImGui.Button("가져오기")) { var path = importPath; model.Post(() => model.Workspace.ImportFile(path, allowExample: false)); }
                    ImGui.EndDisabled();
                    UiWidgets.Wrapped("상태 · " + model.Workspace.StatusCode);
                    UiWidgets.Wrapped("자료 확인 기준 · " + UiText.Time(model.OfficialEventsObservedAt));
                    if (model.Workspace.RemotePointer is { } remotePointer && ImGui.TreeNode("출처별 확인 상태"))
                    {
                        try
                        {
                            foreach (var source in remotePointer.Health.Sources)
                                UiWidgets.Wrapped(source.Id + " · " + (source.LastResult switch { "partial" => "제한된 범위 확인", "success" => "확인", "failed" => "수집 실패", "review-needed" => "재검토 필요", _ => "미확인" }) +
                                    (source.LastAttemptAt is { } attempt ? " · " + UiText.Time(attempt) : ""));
                        }
                        finally { ImGui.TreePop(); }
                    }
                }
                finally { ImGui.TreePop(); }
            }
        }
        finally { ImGui.EndChild(); }
    }
    private string SettingsTime(DateTimeOffset time) => time.ToOffset(TimeSpan.FromHours(9)).ToString(
        CalendarEngine.Today(time) == model.Today ? "HH:mm" : "M/d HH:mm", CultureInfo.InvariantCulture);
    private void DrawLocalNotificationSettings() => DrawNotificationSettings();
    private void DrawAppearanceLabel(string category, string label, float height = 0)
    {
        var f = ImGui.GetFontSize(); var pos = ImGui.GetCursorScreenPos();
        var style = CalendarAppearanceStyle.Resolve(model.State.View, category); var color = ImGui.ColorConvertFloat4ToU32(style.Color);
        var width = Math.Max(f * 2, Math.Min(ImGui.GetContentRegionAvail().X, ImGui.CalcTextSize(label).X + f * 2.5f));
        height = Math.Max(f, height); var inset = (height - f) / 2;
        ImGui.Dummy(new(width, height));
        var draw = ImGui.GetWindowDrawList(); draw.PushClipRect(pos, pos + new Vector2(width, height + 2), true);
        UiWidgets.DrawIcon(style.Icon, pos + new Vector2(0, inset + 1), f * .9f, color);
        // Keep labels readable even when a custom swatch has low text contrast.
        var fitted = FitLabel(label, width - f * 2);
        draw.AddText(pos + new Vector2(f * 1.3f, inset), ImGui.GetColorU32(ImGuiCol.Text), fitted);
        draw.AddCircleFilled(new(pos.X + width - f * .3f, pos.Y + inset + f * .5f), f * .18f, color);
        draw.PopClipRect();
        if (fitted != label && ImGui.IsItemHovered()) UiWidgets.Tooltip(label);
    }
    private void SetCalendarAppearance(CalendarAppearance appearance) => model.UpdateView(v => v with
    {
        CalendarAppearances = v.CalendarAppearances.Where(a => a.Group != appearance.Group).Append(appearance).ToArray()
    });
    private void DrawAppearanceSettings()
    {
        var focus = appearanceSettingsRequest;
        appearanceSettingsRequest = false;
        var f = ImGui.GetFontSize();
        UiWidgets.Heading("일정 색상 및 아이콘", 1.15f);
        if (focus) ImGui.SetScrollHereY(0);
        AppearanceRowsFit = true; AppearanceIconTargets.Clear(); AppearanceLabelTargets.Clear(); AppearanceAlarmTargets.Clear();
        ContinueRow(ImGui.CalcTextSize("모두 기본값으로").X + ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.SmallButton("모두 기본값으로")) model.UpdateView(v => v with { CalendarAppearances = [], UserCategories = v.UserCategories.Select(c => c with { Icon = c.Id == "user:default-raid" ? "Raid" : "Person", Color = c.Id == "user:default-raid" ? "#C2A1EB" : c.Id == "user:default-personal" ? "#D8BB79" : "#91AEFF" }).ToArray() });
        ContinueRow(ImGui.CalcTextSize("분류 추가").X + 20);
        if (ImGui.SmallButton("분류 추가")) OpenUserCategory();
        if (!model.State.Notifications.Enabled)
        {
            UiWidgets.Secondary("전체 알림 꺼짐 · 개별 시점은 보존됩니다.");
            ContinueRow(ImGui.CalcTextSize("전체 알림 켜기").X + 20);
            if (ImGui.SmallButton("전체 알림 켜기")) model.UpdateNotifications(n => n with { Enabled = true });
        }
        var rows = CalendarAppearanceStyle.Groups.Select(g => (Group: (CalendarGroup?)g.Group, Id: g.Group.ToString(), g.Label, g.Category, Icon: g.Icon, Custom: (UserCalendarCategory?)null))
            .Concat(model.State.View.UserCategories.Select(c => (Group: (CalendarGroup?)null, c.Id, Label: c.Name, Category: c.Id, Icon: c.Id == "user:default-raid" ? UiIcon.Raid : UiIcon.Person, Custom: (UserCalendarCategory?)c))).ToArray();
        var columns = ImGui.GetContentRegionAvail().X >= f * 66 ? 2 : 1;
        var tight = ImGui.GetContentRegionAvail().X < f * 28;
        CategoryNotificationTargets.Clear(); CategoryDeleteTargets.Clear();
        if (!ImGui.BeginTable("###AppearanceRows", columns * 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg, new(ImGui.GetContentRegionAvail().X, 0))) return;
        try
        {
            for (var column = 0; column < columns; column++)
            {
                ImGui.TableSetupColumn("분류###appearanceGroup" + column, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("아이콘###appearanceIcon" + column, ImGuiTableColumnFlags.WidthFixed, f * (tight ? 4 : 8));
                ImGui.TableSetupColumn("색상###appearanceColor" + column, ImGuiTableColumnFlags.WidthFixed, f * (tight ? 1.7f : 2.5f));
                ImGui.TableSetupColumn("알림###appearanceNotify" + column, ImGuiTableColumnFlags.WidthFixed, f * (tight ? 4.2f : 5.5f));
                ImGui.TableSetupColumn("###appearanceDelete" + column, ImGuiTableColumnFlags.WidthFixed, f * (tight ? 1.3f : 2));
            }
            ImGui.TableHeadersRow();
            for (var index = 0; index < rows.Length; index++)
            {
                var (group, id, label, category, defaultIcon, custom) = rows[index];
                ImGui.PushID("appearance." + id);
                try
                {
                    var appearance = custom is not null ? new() { Group = CalendarGroup.Personal, Icon = custom.Icon, Color = custom.Color } : model.State.View.CalendarAppearances.FirstOrDefault(a => a.Group == group) ?? new CalendarAppearance { Group = group!.Value };
                    var style = CalendarAppearanceStyle.Resolve(model.State.View, category);
                    if (index % columns == 0) ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    DrawAppearanceLabel(category, label, ImGui.GetFrameHeight());
                    AppearanceLabelTargets[id] = ItemCenter();
                    ImGui.TableNextColumn(); DrawAppearanceIconSelector(appearance, defaultIcon, style.Icon, style.Color,
                        custom is null ? null : value => model.SaveUserCategory(custom with { Icon = value.Length == 0 ? custom.Id == "user:default-raid" ? "Raid" : "Person" : value }));
                    ImGui.TableNextColumn();
                    var rgb = new Vector3(style.Color.X, style.Color.Y, style.Color.Z);
                    if (ImGui.ColorEdit3("###color", ref rgb, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                    {
                        var color = $"#{(int)MathF.Round(rgb.X * 255):X2}{(int)MathF.Round(rgb.Y * 255):X2}{(int)MathF.Round(rgb.Z * 255):X2}";
                        if (custom is not null) model.SaveUserCategory(custom with { Color = color }); else SetCalendarAppearance(appearance with { Color = color });
                    }
                    AppearanceRowsFit &= ImGui.GetItemRectMax().X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip("색상 변경 · 오른쪽 클릭하면 이 분류를 기본값으로 복원합니다.");
                    if (ImGui.BeginPopupContextItem("###appearanceReset"))
                    {
                        if (ImGui.MenuItem("이 분류를 기본값으로"))
                        {
                            if (custom is null) model.UpdateView(v => v with { CalendarAppearances = v.CalendarAppearances.Where(a => a.Group != group).ToArray() });
                            else model.SaveUserCategory(custom with { Icon = custom.Id == "user:default-raid" ? "Raid" : "Person", Color = custom.Id == "user:default-raid" ? "#C2A1EB" : custom.Id == "user:default-personal" ? "#D8BB79" : "#91AEFF" });
                        }
                        ImGui.EndPopup();
                    }
                    ImGui.TableNextColumn();
                    var enabled = NotificationPolicy.CategoryRule(model.State, id).Enabled;
                    if (ImGui.Checkbox("###appearanceAlarmEnabled",ref enabled)) model.SetCategoryNotificationEnabled(id,enabled);
                    AppearanceAlarmTargets[id] = ItemCenter();
                    ImGui.SameLine();
                    if (enabled) ImGui.PushStyleColor(ImGuiCol.Button, Accent with { W = .25f });
                    if (ImGui.Button("설정###configureNotification", new(-1, ImGui.GetFrameHeight()))) OpenCategoryNotification(id);
                    if (enabled) ImGui.PopStyleColor();
                    CategoryNotificationTargets[id] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                    AppearanceRowsFit &= ImGui.GetItemRectMax().X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
                    ImGui.TableNextColumn();
                    if (custom is not null)
                    {
                        if (ImGui.Button("-###deleteCategory",new(-1,ImGui.GetFrameHeight()))) { deleteCategoryId = id; deleteDestinationId = ""; categoryDeleteRequest = true; }
                        CategoryDeleteTargets[id] = ItemCenter();
                        AppearanceRowsFit &= ImGui.GetItemRectMax().X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
                        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("분류 삭제 · 일정은 보존합니다.");
                    }
                }
                finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndTable(); }
    }
    private void DrawAppearanceIconSelector(CalendarAppearance appearance, UiIcon defaultIcon, UiIcon selectedIcon, Vector4 color, Action<string>? changed = null)
    {
        var f = ImGui.GetFontSize(); var pos = ImGui.GetCursorScreenPos();
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - 1); var height = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("###icon", ""))
        {
            try
            {
                AppearanceIconChoiceTargets.Clear();
                for (var index = 0; index <= CalendarAppearanceStyle.Icons.Length; index++)
                {
                    var icon = index == 0 ? defaultIcon : CalendarAppearanceStyle.Icons[index - 1].Icon;
                    var label = index == 0 ? "기본값" : CalendarAppearanceStyle.Icons[index - 1].Label;
                    var selected = index == 0 ? appearance.Icon.Length == 0 : appearance.Icon == icon.ToString();
                    var itemPos = ImGui.GetCursorScreenPos();
                    if (ImGui.Selectable("###iconChoice." + index, selected, ImGuiSelectableFlags.None, new(0, f * 1.65f)))
                    {
                        var value = index == 0 ? "" : icon.ToString();
                        if (changed is null) SetCalendarAppearance(appearance with { Icon = value }); else changed(value);
                    }
                    if (index > 0) AppearanceIconChoiceTargets[icon] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                    UiWidgets.DrawIcon(icon, itemPos + new Vector2(f * .25f, f * .35f), f * .9f, ImGui.ColorConvertFloat4ToU32(color));
                    ImGui.GetWindowDrawList().AddText(itemPos + new Vector2(f * 1.5f, f * .3f), ImGui.GetColorU32(ImGuiCol.Text), label);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
            }
            finally { ImGui.EndCombo(); }
        }
        if (changed is null) AppearanceIconTargets[appearance.Group] = pos + new Vector2(width / 2, height / 2);
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(pos, pos + new Vector2(Math.Max(1, width - height), height), true);
        UiWidgets.DrawIcon(selectedIcon, pos + new Vector2(f * .35f, (height - f * .9f) / 2), f * .9f, ImGui.ColorConvertFloat4ToU32(color));
        var selectedLabel = appearance.Icon.Length == 0 ? "기본값" : CalendarAppearanceStyle.Icons.FirstOrDefault(i => i.Icon == selectedIcon).Label ?? "기본값";
        draw.AddText(pos + new Vector2(f * 1.5f, (height - f) / 2), ImGui.GetColorU32(ImGuiCol.Text), selectedLabel);
        draw.PopClipRect();
    }
    private void DrawHiddenEvents()
    {
        var ids = model.State.View.HiddenEventIds;
        var phases = model.State.View.HiddenPhases;
        if (ids.Count + phases.Count == 0 || !ImGui.TreeNode("숨긴 일정 " + (ids.Count + phases.Count) + "건###hiddenEvents")) return;
        try
        {
            if (ImGui.SmallButton("모두 복원")) model.RestoreHiddenEvents();
            if (ImGui.BeginTable("###hiddenRows", 2, ImGuiTableFlags.SizingStretchProp))
            {
                try
                {
                    ImGui.TableSetupColumn("###hiddenTitle", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("###restore", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4);
                    foreach (var id in ids)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn();
                        UiWidgets.Wrapped((model.Events.FirstOrDefault(e => e.Id == id)?.Title ?? "현재 자료에 없는 일정") + " · 행사 전체");
                        ImGui.TableNextColumn(); if (ImGui.SmallButton("복원###restore." + id)) model.SetEventHidden(id, false);
                    }
                    foreach (var phase in phases)
                    {
                        var item = model.Events.FirstOrDefault(e => e.Id == phase.EventId);
                        var label = item?.Phases.FirstOrDefault(p => p.Id == phase.PhaseId)?.Label ?? "현재 자료에 없는 단계";
                        ImGui.TableNextRow(); ImGui.TableNextColumn(); UiWidgets.Wrapped((item?.Title ?? "현재 자료에 없는 일정") + " · " + label + " · 이 일정만");
                        ImGui.TableNextColumn(); if (ImGui.SmallButton("복원###restorePhase." + phase.EventId + "." + phase.PhaseId)) model.SetPhaseHidden(phase.EventId, phase.PhaseId, false);
                    }
                }
                finally { ImGui.EndTable(); }
            }
        }
        finally { ImGui.TreePop(); }
    }
    private void DrawDatePicker()
    {
        var f = ImGui.GetFontSize();
        var date = DateOnly.TryParseExact(editDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : model.Today;
        if (UiWidgets.IconButton("entryDate", UiIcon.Calendar, date.ToString("yyyy.MM.dd (ddd)", CultureInfo.GetCultureInfo("ko")), false, Accent))
        { pickerYear = date.Year; pickerMonth = date.Month; ImGui.OpenPopup("###EntryDatePicker"); }
        DatePickerButtonCenter = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
        var work = ImGui.GetMainViewport().WorkSize;
        var width = Math.Min(f * 22, work.X - 24);
        ImGui.SetNextWindowSizeConstraints(new(width, 0), new(width, work.Y - 24));
        if (!ImGui.BeginPopup("###EntryDatePicker")) return;
        try
        {
            void Move(int delta)
            {
                var next = new DateOnly(pickerYear, pickerMonth, 1).AddMonths(delta);
                if (next.Year is >= 2020 and <= 2100) { pickerYear = next.Year; pickerMonth = next.Month; }
            }
            ImGui.BeginDisabled(pickerYear <= 2020); if (ImGui.SmallButton("«###pickerPrevYear")) Move(-12); ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.BeginDisabled(pickerYear == 2020 && pickerMonth == 1); if (ImGui.SmallButton("‹###pickerPrevMonth")) Move(-1); ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.TextUnformatted(pickerYear + "년 " + pickerMonth + "월");
            ImGui.SameLine(); ImGui.BeginDisabled(pickerYear == 2100 && pickerMonth == 12); if (ImGui.SmallButton("›###pickerNextMonth")) Move(1);
            PickerNextMonthTarget = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2; ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.BeginDisabled(pickerYear >= 2100); if (ImGui.SmallButton("»###pickerNextYear")) Move(12); ImGui.EndDisabled();
            ImGui.Spacing(); DatePickerTargets.Clear();
            if (ImGui.BeginTable("###DatePickerGrid", 7, ImGuiTableFlags.SizingStretchSame))
            {
                try
                {
                    foreach (var weekday in new[] { "일", "월", "화", "수", "목", "금", "토" }) { ImGui.TableNextColumn(); UiWidgets.Secondary(weekday); }
                    var first = new DateOnly(pickerYear, pickerMonth, 1);
                    var start = first.AddDays(-(int)first.DayOfWeek);
                    var count = ((DateTime.DaysInMonth(pickerYear, pickerMonth) + (int)first.DayOfWeek + 6) / 7) * 7;
                    for (var index = 0; index < count; index++)
                    {
                        var day = start.AddDays(index); ImGui.TableNextColumn();
                        ImGui.PushID(day.DayNumber);
                        var selected = day == date;
                        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Accent with { W = .27f });
                        if (day.Month != pickerMonth) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                        ImGui.BeginDisabled(day.Year is < 2020 or > 2100);
                        if (ImGui.Button(day.Day.ToString(CultureInfo.InvariantCulture), new(-1, f * 1.65f))) { editDate = UiText.Date(day); ImGui.CloseCurrentPopup(); }
                        DatePickerTargets[day] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                        ImGui.EndDisabled();
                        if (day.Month != pickerMonth) ImGui.PopStyleColor();
                        if (selected) ImGui.PopStyleColor();
                        ImGui.PopID();
                    }
                }
                finally { ImGui.EndTable(); }
            }
            if (ImGui.SmallButton("오늘 선택")) { editDate = UiText.Date(model.Today); ImGui.CloseCurrentPopup(); }
        }
        finally { ImGui.EndPopup(); }
    }
    private void OpenEditor(PersonalEntry? entry = null, bool subscription = false)
    {
        subscriptionEditor = subscription || entry is not null && SubscriptionTime.IsSubscription(entry.Kind);
        editId = entry?.Id ?? ""; editName = entry?.Title ?? ""; editNote = entry?.Note ?? "";
        SetupEditNotifications(entry);
        editCategoryId = entry?.CategoryId ?? model.State.View.UserCategories.FirstOrDefault(c => c.Id == "user:default-personal")?.Id ?? "";
        editKind = entry?.Kind switch { "subscription" => 1, "retainer" => 2, "bag" => 3, _ => subscriptionEditor ? 1 : 0 };
        editDateOnly = entry?.When.Kind != "instant";
        var defaultDate = model.HasDateFocus ? model.SelectedDate : model.Year == model.Today.Year && model.MonthNumber == model.Today.Month ? model.Today : model.SelectedDate;
        var date = entry is null ? subscriptionEditor ? model.Today : defaultDate : CalendarEngine.BoundaryDate(entry.When) ?? model.Today;
        editDate = UiText.Date(date);
        editTime = entry?.When.Kind == "instant" && DateTimeOffset.TryParse(entry.When.Value, out var time) ? time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm", CultureInfo.InvariantCulture) : "18:00";
        chargeDays = 90; chargeHours = 0; chargeMinutes = 0; chargeMode = subscriptionEditor && entry is null;
        chargeBasisTime = "";
        editorError = ""; editorOpen = true; editorRequest = true;
    }
    private PersonalEntry? ExistingSubscription() => model.State.Entries.FirstOrDefault(e => e.ProfileId == model.State.ActiveProfileId &&
        (editId.Length > 0 ? e.Id == editId : e.Kind == (editKind == 2 ? "retainer" : editKind == 3 ? "bag" : "subscription")));
    private void DrawEditor()
    {
        if (!editorOpen) return;
        var f = ImGui.GetFontSize(); var work = ImGui.GetMainViewport().WorkSize;
        var width = Math.Min(f * (subscriptionEditor ? 27 : 32), Math.Max(1, work.X - 24));
        var constrained = true;
        ImGui.SetNextWindowSizeConstraints(new(width, 0), new(width, Math.Max(1, work.Y - 24)));
        ImGui.SetNextWindowSize(new(width,Math.Min(work.Y - 24,f * (subscriptionEditor ? 27 : 33))),ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().WorkPos + work / 2, ImGuiCond.Appearing, new(.5f));
        var entryLabel = editCategoryId.Length == 0 ? "미분류 일정" : CategoryName(editCategoryId);
        if (!entryLabel.EndsWith("일정",StringComparison.Ordinal)) entryLabel += " 일정";
        var title = subscriptionEditor ? (chargeMode ? "이용권 충전" : "이용권 만료일") : entryLabel + (editId.Length == 0 ? " 추가" : " 수정");
        if (!ImGui.Begin(title + "###DuckbillCalendar.EntryEditor",ref editorOpen,ImGuiWindowFlags.NoSavedSettings)) { ImGui.End(); return; }
        try
        {
            if (constrained) ImGui.BeginChild("###EditorBody", new(0, Math.Max(f * 2, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - f * .8f)));
            try
            {
            if (subscriptionEditor)
            {
                var kinds = new[] { "정액제", "집사 이용권", "초코보 가방 이용권" };
                var kindIndex = editKind - 1;
                ImGui.BeginDisabled(editId.Length > 0);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("###subscriptionKind", ref kindIndex, kinds, kinds.Length))
                {
                    editKind = kindIndex + 1; chargeBasisTime = ""; editorError = "";
                }
                ImGui.EndDisabled();
                if (ImGui.RadioButton("기간 추가", chargeMode)) chargeMode = true;
                ImGui.SameLine(); if (ImGui.RadioButton("만료일 입력", !chargeMode)) chargeMode = false;
            }
            else
            {
                ImGui.TextUnformatted("일정 이름");
                ImGui.SetNextItemWidth(-1); ImGui.InputTextWithHint("###entryTitle", "예: 공대 출발", ref editName, 80);
                var categories = model.State.View.UserCategories;
                var selectedCategory = categories.ToList().FindIndex(c => c.Id == editCategoryId) + 1;
                var categoryNames = new[] { "미분류 (분류 없음)" }.Concat(categories.Select(c => c.Name)).ToArray();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("###entryCategory", ref selectedCategory, categoryNames, categoryNames.Length)) editCategoryId = selectedCategory == 0 ? "" : categories[selectedCategory - 1].Id;
            }
            if (subscriptionEditor && chargeMode)
            {
                ImGui.Spacing();
                foreach (var days in new[] { 30, 90, 180 })
                {
                    if (days != 30) ImGui.SameLine();
                    if (ImGui.RadioButton(days + "일###charge." + days, chargeDays == days && chargeHours == 0 && chargeMinutes == 0)) { chargeDays = days; chargeHours = 0; chargeMinutes = 0; editorError = ""; }
                }
                ImGui.Spacing(); ImGui.TextUnformatted("추가할 기간");
                ImGui.SetNextItemWidth(f * 6); if (ImGui.InputInt("일###chargeDays", ref chargeDays)) editorError = "";
                ContinueRow(f * 8.5f);
                ImGui.SetNextItemWidth(f * 6); if (ImGui.InputInt("시간###chargeHours", ref chargeHours)) editorError = "";
                ContinueRow(f * 7.5f);
                ImGui.SetNextItemWidth(f * 6); if (ImGui.InputInt("분###chargeMinutes", ref chargeMinutes)) editorError = "";
                var previous = ExistingSubscription();
                if (SubscriptionTime.NeedsExistingExpiryTime(previous?.When, chargeHours, model.Now, chargeMinutes))
                {
                    ImGui.Spacing(); UiWidgets.Wrapped("기존 만료 시각이 필요합니다. (한국 시간)");
                    ImGui.SetNextItemWidth(f * 8); if (ImGui.InputTextWithHint("###chargeBasisTime", "HH:mm", ref chargeBasisTime, 6)) editorError = "";
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip("등록된 만료일에 시각이 없어 시간 충전의 기준을 정해야 합니다. 기존 이용권의 실제 만료 시각을 입력하세요.");
                }
                if (TryChargeBoundary(out var expiry, out var chargeError))
                {
                    if (previous is not null) UiWidgets.Wrapped("기존 만료일  " + UiText.LocalBoundary(previous.When));
                    ImGui.Spacing(); UiWidgets.Wrapped("충전 후 만료일  " + UiText.LocalBoundary(expiry!));
                    UiWidgets.Wrapped(chargeHours > 0 || chargeMinutes > 0 ? "남은 기간에 일·시간·분을 더합니다. 처음 등록하거나 만료됐다면 현재 시각부터 계산합니다." :
                        previous is null ? "오늘부터 계산합니다." : "남은 기간에 이어서 더합니다. 만료된 이용권은 오늘부터 계산합니다.");
                }
                else UiWidgets.Wrapped(chargeError);
            }
            else
            {
                ImGui.Spacing(); ImGui.TextUnformatted(subscriptionEditor ? "만료일" : "날짜");
                DrawDatePicker();
                var timed = !editDateOnly;
                if (ImGui.Checkbox("시간 지정", ref timed)) editDateOnly = !timed;
                if (timed)
                {
                    ImGui.SetNextItemWidth(f * 8); ImGui.InputTextWithHint("###entryTime", "HH:mm", ref editTime, 6);
                    if (ImGui.IsItemHovered()) UiWidgets.Tooltip("한국 시간 · 24시간 형식");
                }
            }
            if (!subscriptionEditor && ImGui.TreeNode("메모 추가"))
            {
                ImGui.InputTextMultiline("###entryNote", ref editNote, 400, new(-1, f * 3)); ImGui.TreePop();
            }
            if (!subscriptionEditor) DrawEditNotifications();
            if (editorError.Length > 0) UiWidgets.Wrapped(editorError);
            }
            finally { if (constrained) ImGui.EndChild(); }
            ImGui.Spacing();
            if (ImGui.Button(subscriptionEditor && chargeMode ? "충전하기" : "저장"))
            {
                if (!subscriptionEditor && editNotificationMode == 1 && (!ParseLeads(editNotificationMinutes,out var checkedLeads) || checkedLeads.Length == 0)) editorError = "알림 시점을 0~525600분, 최대 6개로 입력하세요.";
                else if (TryCreateEntry(out var entry))
                {
                    if (subscriptionEditor) model.SaveSubscription(entry!);
                    else { ParseLeads(editNotificationMinutes,out var leads); pendingEntrySaveRevision = model.EntrySaveRevision + 1; model.SaveEntryWithNotification(entry!,editNotificationMode == 0 ? "inherit" : editNotificationMode == 2 ? "off" : "custom",leads,editGlobalNotificationChanged ? editGlobalNotification : null,editId.Length > 0); }
                    editorOpen = false;
                }
            }
            EditorSaveVisible = ImGui.IsItemVisible(); EditorSaveMax = ImGui.GetItemRectMax();
            ImGui.SameLine(); if (ImGui.Button("취소")) editorOpen = false;
            if (editId.Length > 0)
            { ImGui.SameLine(); if (ImGui.Button("삭제")) { RequestEntryDelete(editId); editorOpen = false; } }
        }
        finally { ImGui.End(); }
    }
    private bool TryCreateEntry(out PersonalEntry? entry)
    {
        entry = null;
        if (editId.Length > 0 && !model.State.Entries.Any(e => e.Id == editId && e.ProfileId == model.State.ActiveProfileId))
        { editorError = "이 일정이 삭제되었거나 사용 중인 계정이 바뀌었습니다."; return false; }
        if (!subscriptionEditor && string.IsNullOrWhiteSpace(editName)) { editorError = "일정 이름을 입력하세요."; return false; }
        CalendarBoundary boundary;
        if (subscriptionEditor && chargeMode)
        {
            if (!TryChargeBoundary(out var expiry, out var error)) { editorError = error; return false; }
            boundary = expiry!;
        }
        else
        {
            if (!DateOnly.TryParseExact(editDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 2020 or > 2100)
            { editorError = "날짜를 YYYY-MM-DD 형식으로 입력하세요."; return false; }
            if (editDateOnly) boundary = CalendarBoundary.Date(date);
            else
            {
                if (!TimeOnly.TryParseExact(editTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) { editorError = "시각을 HH:mm 형식으로 입력하세요."; return false; }
                boundary = CalendarBoundary.Instant(new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(9)));
            }
        }
        var kind = subscriptionEditor ? editKind switch { 2 => "retainer", 3 => "bag", _ => "subscription" } : "personal";
        if (CalendarEngine.BoundaryDate(boundary)?.Year > 2100) { editorError = "만료일은 2100년 이내로 입력하세요."; return false; }
        entry = new()
        {
            Id = editId.Length > 0 ? editId : subscriptionEditor && ExistingSubscription() is { } existing ? existing.Id : Guid.NewGuid().ToString("N"),
            ProfileId = model.State.ActiveProfileId, Title = subscriptionEditor ? SubscriptionTime.Name(kind) : editName.Trim(),
            Kind = kind, When = boundary, Note = editNote.Trim(), CategoryId = kind == "personal" && model.State.View.UserCategories.Any(c => c.Id == editCategoryId) ? editCategoryId : "",
            SeriesId = kind == "personal" ? model.State.Entries.FirstOrDefault(e => e.Id == editId && e.ProfileId == model.State.ActiveProfileId)?.SeriesId ?? "" : "",
            SeriesIndex = kind == "personal" ? model.State.Entries.FirstOrDefault(e => e.Id == editId && e.ProfileId == model.State.ActiveProfileId)?.SeriesIndex : null
        };
        editorError = ""; return true;
    }
    private bool TryChargeBoundary(out CalendarBoundary? expiry, out string error)
    {
        expiry = null; error = "";
        var totalMinutes = chargeDays * 1440L + chargeHours * 60L + chargeMinutes;
        if (chargeDays < 0 || chargeHours < 0 || chargeMinutes < 0 || totalMinutes is < 1 or > 3650 * 1440L) { error = "1분 이상, 총 3650일 이내로 입력하세요. 일·시간·분은 0 이상이어야 합니다."; return false; }
        TimeOnly? basisTime = null;
        var previous = ExistingSubscription();
        if (SubscriptionTime.NeedsExistingExpiryTime(previous?.When, chargeHours, model.Now, chargeMinutes))
        {
            if (!TimeOnly.TryParseExact(chargeBasisTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            { error = "기존 만료 시각을 HH:mm 형식으로 입력하세요."; return false; }
            basisTime = time;
        }
        expiry = SubscriptionTime.ExtendDuration(previous?.When, chargeDays, chargeHours, chargeMinutes, model.Now, basisTime);
        return true;
    }
}
