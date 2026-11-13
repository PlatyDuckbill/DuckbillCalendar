using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    private static readonly DayOfWeek[] WeeklyDays = [DayOfWeek.Monday,DayOfWeek.Tuesday,DayOfWeek.Wednesday,DayOfWeek.Thursday,DayOfWeek.Friday,DayOfWeek.Saturday,DayOfWeek.Sunday];
    private static readonly string[] WeeklyDayNames = ["월요일","화요일","수요일","목요일","금요일","토요일","일요일"];
    private bool weeklyInitialized, weeklySubmitted;
    private string weeklyTitle = "공격대", weeklyCategoryId = "", weeklyNote = "", weeklyCommonTime = "20:00";
    private DateOnly weeklyStartDate;
    private int weeklyWeeks = 4, weeklyInterval = 1, weeklyPane, weeklyPreviewPage, weeklyPickerYear, weeklyPickerMonth;
    private bool[] weeklySelected = new bool[7];
    private string[] weeklyTimes = Enumerable.Repeat("20:00",7).ToArray();
    internal Dictionary<DayOfWeek,Vector2> WeeklyDayTargets { get; } = [];
    internal Vector2 WeeklyRegisterTarget { get; private set; }
    internal int WeeklyPreviewCount { get; private set; }
    internal bool WeeklyTwoColumns { get; private set; }
    private void OpenWeeklyRaidPlanner(DateOnly? start = null)
    {
        if (!weeklyInitialized)
        {
            weeklyInitialized = true; weeklyStartDate = start ?? model.Today;
            weeklyCategoryId = model.State.View.UserCategories.FirstOrDefault(c => c.Id == "user:default-raid")?.Id ?? "";
        }
        else if (start is { } date) weeklyStartDate = date;
        weeklySubmitted = false; activeTab = 5; editorOpen = editorRequest = false;
    }
    private WeeklyScheduleRequest WeeklyRequest() => new()
    {
        Title = weeklyTitle,CategoryId = weeklyCategoryId,StartDate = weeklyStartDate,Weeks = weeklyWeeks,IntervalWeeks = weeklyInterval,
        Note = weeklyNote,Days = WeeklyDays.Select((day,index) => (day,index)).Where(v => weeklySelected[v.index]).Select(v => new WeeklyScheduleDay(v.day,weeklyTimes[v.index])).ToArray()
    };
    private void WeeklyChanged() { weeklySubmitted = false; weeklyPreviewPage = 0; }
    private void DrawWeeklyRaidPlanner()
    {
        UiWidgets.Heading("공격대 반복 등록",Compact ? 1.15f : 1.3f);
        if (!Compact) MutedWrapped("요일마다 시각을 정하고, 등록할 날짜를 확인한 뒤 한 번에 추가하세요.");
        if (!weeklyInitialized) OpenWeeklyRaidPlanner();
        if (weeklyCategoryId.Length > 0 && !model.State.View.UserCategories.Any(c => c.Id == weeklyCategoryId)) weeklyCategoryId = "";
        var f = ImGui.GetFontSize();
        WeeklyTwoColumns = ImGui.GetContentRegionAvail().X >= f * 53;
        if (!WeeklyTwoColumns)
        {
            if (ImGui.SmallButton("기본")) weeklyPane = 0;
            ImGui.SameLine(); if (ImGui.SmallButton("요일·시각")) weeklyPane = 1;
            ImGui.SameLine(); if (ImGui.SmallButton("미리보기")) weeklyPane = 2;
        }
        var plan = WeeklySchedulePlanner.Plan(model.State,WeeklyRequest());
        var bodyHeight = Math.Max(1,ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * (weeklySubmitted ? Compact ? 2.3f : 3.4f : Compact ? 1.3f : 2.6f));
        WeeklyDayTargets.Clear();
        if (WeeklyTwoColumns && ImGui.BeginTable("###WeeklyLayout",2,ImGuiTableFlags.SizingStretchSame))
        {
            try
            {
                ImGui.TableNextColumn();
                ImGui.BeginChild("###WeeklyForm",new(0,bodyHeight));
                try { DrawWeeklyForm(); } finally { ImGui.EndChild(); }
                plan = WeeklySchedulePlanner.Plan(model.State,WeeklyRequest());
                ImGui.TableNextColumn();
                ImGui.BeginChild("###WeeklyPreview",new(0,bodyHeight));
                try { DrawWeeklyPreview(plan); } finally { ImGui.EndChild(); }
            }
            finally { ImGui.EndTable(); }
        }
        else
        {
            ImGui.BeginChild("###WeeklyBody",new(0,bodyHeight));
            try { if (weeklyPane == 0) DrawWeeklyBasics(); else if (weeklyPane == 1) DrawWeeklyDayPattern(); else DrawWeeklyPreview(plan); }
            finally { ImGui.EndChild(); }
            plan = WeeklySchedulePlanner.Plan(model.State,WeeklyRequest());
        }
        WeeklyPreviewCount = plan.Occurrences.Count;
        if (weeklySubmitted && model.LastWeeklyRegistration is { } result)
        {
            if (result.Errors.Count > 0) UiWidgets.Wrapped(WeeklyError(result.Errors[0]));
            else UiWidgets.Secondary(result.Registered + "건 등록 완료" + (result.Skipped > 0 ? " · 중복 " + result.Skipped + "건 제외" : ""));
        }
        var categoryAlerts = model.State.Notifications.Enabled && NotificationPolicy.CategoryRule(model.State,weeklyCategoryId.Length == 0 ? "Personal" : weeklyCategoryId).Enabled;
        if (!Compact) UiWidgets.Secondary(categoryAlerts ? "알림: 이 분류의 기본 설정 적용" : "알림 꺼짐 · 알림 목록에서 켤 수 있습니다.");
        ImGui.BeginDisabled(!plan.IsValid || plan.NewCount == 0);
        if (ImGui.Button(plan.NewCount > 0 ? plan.NewCount + "개 일정 등록" : "일정 등록")) { model.RegisterWeeklySchedules(WeeklyRequest()); weeklySubmitted = true; }
        ImGui.EndDisabled(); WeeklyRegisterTarget = ItemCenter();
        ContinueRow(ImGui.CalcTextSize("달력으로").X + 20);
        if (ImGui.Button("달력으로")) activeTab = 0;
        ContinueRow(ImGui.CalcTextSize("새 패턴").X + 20);
        if (ImGui.Button("새 패턴"))
        { weeklySelected = new bool[7]; weeklyTimes = Enumerable.Repeat("20:00",7).ToArray(); weeklyWeeks = 4; weeklyInterval = 1; weeklyStartDate = model.Today; WeeklyChanged(); }
    }
    private void DrawWeeklyForm() { DrawWeeklyBasics(); DrawWeeklyDayPattern(); }
    private void DrawWeeklyBasics()
    {
        var f = ImGui.GetFontSize();
        ImGui.TextUnformatted("일정 이름"); ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("###weeklyTitle","예: 절바하 공대",ref weeklyTitle,80)) WeeklyChanged();
        ImGui.TextUnformatted("분류");
        var categories = model.State.View.UserCategories.ToArray(); var index = Array.FindIndex(categories,c => c.Id == weeklyCategoryId) + 1;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("###weeklyCategory",ref index,new[] { "미분류 (분류 없음)" }.Concat(categories.Select(c => c.Name)).ToArray(),categories.Length + 1))
        { weeklyCategoryId = index == 0 ? "" : categories[index - 1].Id; WeeklyChanged(); }
        ImGui.Spacing(); ImGui.TextUnformatted("등록 시작일"); DrawWeeklyDatePicker();
        ContinueRow(ImGui.CalcTextSize("오늘").X + 20); if (ImGui.SmallButton("오늘")) { weeklyStartDate = model.Today; WeeklyChanged(); }
        ImGui.SetNextItemWidth(f * 5); if (ImGui.InputInt("주 등록###weeklyWeeks",ref weeklyWeeks)) WeeklyChanged();
        ContinueRow(f * 11);
        ImGui.SetNextItemWidth(f * 5); if (ImGui.InputInt("주마다###weeklyInterval",ref weeklyInterval)) WeeklyChanged();
    }
    private void DrawWeeklyDayPattern()
    {
        var f = ImGui.GetFontSize();
        if (!Compact)
        {
        ImGui.Spacing(); ImGui.TextUnformatted("요일별 시각");
        if (ImGui.SmallButton("평일")) { for (var i = 0; i < 7; i++) weeklySelected[i] = i < 5; WeeklyChanged(); }
        ImGui.SameLine(); if (ImGui.SmallButton("주말")) { for (var i = 0; i < 7; i++) weeklySelected[i] = i >= 5; WeeklyChanged(); }
        ImGui.SameLine(); if (ImGui.SmallButton("선택 해제")) { Array.Fill(weeklySelected,false); WeeklyChanged(); }
        }
        if (ImGui.BeginTable("###WeeklyDays",2,ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            try
            {
                ImGui.TableSetupColumn("요일",ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("시각 (한국 시간)",ImGuiTableColumnFlags.WidthFixed,f * 9);
                if (!Compact) ImGui.TableHeadersRow();
                for (var i = 0; i < 7; i++)
                {
                    ImGui.PushID(i); ImGui.TableNextRow(); ImGui.TableNextColumn();
                    if (ImGui.Checkbox(WeeklyDayNames[i],ref weeklySelected[i])) WeeklyChanged();
                    WeeklyDayTargets[WeeklyDays[i]] = ItemCenter();
                    ImGui.TableNextColumn(); ImGui.BeginDisabled(!weeklySelected[i]); ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputTextWithHint("###weeklyTime","20:00",ref weeklyTimes[i],5)) WeeklyChanged();
                    ImGui.EndDisabled(); ImGui.PopID();
                }
            }
            finally { ImGui.EndTable(); }
        }
        ImGui.SetNextItemWidth(f * 5); ImGui.InputText("###weeklyCommon",ref weeklyCommonTime,5);
        ContinueRow(ImGui.CalcTextSize("선택한 요일에 시각 적용").X + 20);
        if (ImGui.SmallButton("선택한 요일에 시각 적용")) { for (var i = 0; i < 7; i++) if (weeklySelected[i]) weeklyTimes[i] = weeklyCommonTime; WeeklyChanged(); }
        if (ImGui.TreeNode("메모 추가###weeklyNote"))
        { ImGui.SetNextItemWidth(-1); if (ImGui.InputTextMultiline("###weeklyMemo",ref weeklyNote,1000,new(-1,f * 4))) WeeklyChanged(); ImGui.TreePop(); }
    }
    private void DrawWeeklyPreview(WeeklySchedulePlan plan)
    {
        UiWidgets.Heading("등록할 날짜",1.1f);
        if (!plan.IsValid)
        { foreach (var error in plan.Errors) MutedWrapped(WeeklyError(error)); return; }
        UiWidgets.Wrapped(weeklyStartDate.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture) + "부터 " + weeklyWeeks + "주 · " + weeklyInterval + "주마다");
        UiWidgets.Colored(Accent,"새 일정 " + plan.NewCount + "건" + (plan.ExistingCount > 0 ? " · 이미 등록된 " + plan.ExistingCount + "건 제외" : ""));
        var past = plan.Occurrences.Count(o => !o.AlreadyExists && o.When < model.Now);
        if (past > 0) MutedWrapped("지난 시각의 일정 " + past + "건 포함");
        MutedWrapped("각 날짜는 개별 일정으로 등록됩니다. 이후 주차가 자동으로 추가되지는 않습니다.");
        ImGui.Separator();
        weeklyPreviewPage = Math.Clamp(weeklyPreviewPage,0,Math.Max(0,(plan.Occurrences.Count - 1) / 12));
        foreach (var occurrence in plan.Occurrences.Skip(weeklyPreviewPage * 12).Take(12))
        {
            var at = occurrence.When;
            var label = at.ToString("M월 d일 (ddd)  HH:mm",CultureInfo.GetCultureInfo("ko"));
            if (occurrence.AlreadyExists) UiWidgets.Secondary(label + " · 이미 등록됨"); else ImGui.TextUnformatted(label);
        }
        DrawNotificationPaging(ref weeklyPreviewPage,plan.Occurrences.Count,12);
    }
    private void DrawWeeklyDatePicker()
    {
        var f = ImGui.GetFontSize();
        if (UiWidgets.IconButton("weeklyStart",UiIcon.Calendar,weeklyStartDate.ToString("yyyy.MM.dd (ddd)",CultureInfo.GetCultureInfo("ko")),false,Accent))
        { weeklyPickerYear = weeklyStartDate.Year; weeklyPickerMonth = weeklyStartDate.Month; ImGui.OpenPopup("###WeeklyDatePicker"); }
        var width = Math.Min(f * 22,ImGui.GetMainViewport().WorkSize.X - 24);
        ImGui.SetNextWindowSizeConstraints(new(width,0),new(width,ImGui.GetMainViewport().WorkSize.Y - 24));
        if (!ImGui.BeginPopup("###WeeklyDatePicker")) return;
        try
        {
            var month = new DateOnly(weeklyPickerYear,weeklyPickerMonth,1);
            ImGui.BeginDisabled(month == new DateOnly(2020,1,1)); if (ImGui.SmallButton("이전")) { month = month.AddMonths(-1); weeklyPickerYear = month.Year; weeklyPickerMonth = month.Month; } ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.TextUnformatted(weeklyPickerYear + "년 " + weeklyPickerMonth + "월"); ImGui.SameLine();
            ImGui.BeginDisabled(month == new DateOnly(2100,12,1)); if (ImGui.SmallButton("다음")) { month = month.AddMonths(1); weeklyPickerYear = month.Year; weeklyPickerMonth = month.Month; } ImGui.EndDisabled();
            if (ImGui.BeginTable("###WeeklyPickerGrid",7,ImGuiTableFlags.SizingStretchSame))
            {
                try
                {
                    foreach (var day in new[] { "월","화","수","목","금","토","일" }) { ImGui.TableNextColumn(); UiWidgets.Secondary(day); }
                    var offset = ((int)month.DayOfWeek + 6) % 7; var first = month.AddDays(-offset);
                    var count = ((DateTime.DaysInMonth(month.Year,month.Month) + offset + 6) / 7) * 7;
                    for (var i = 0; i < count; i++)
                    {
                        var date = first.AddDays(i); ImGui.TableNextColumn(); ImGui.PushID(date.DayNumber);
                        ImGui.BeginDisabled(date.Year is < 2020 or > 2100);
                        if (date.Month != month.Month) ImGui.PushStyleColor(ImGuiCol.Text,ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                        if (ImGui.Selectable(date.Day.ToString(CultureInfo.InvariantCulture),date == weeklyStartDate)) { weeklyStartDate = date; WeeklyChanged(); ImGui.CloseCurrentPopup(); }
                        if (date.Month != month.Month) ImGui.PopStyleColor(); ImGui.EndDisabled(); ImGui.PopID();
                    }
                }
                finally { ImGui.EndTable(); }
            }
        }
        finally { ImGui.EndPopup(); }
    }
    private static string WeeklyError(string code) => code switch
    {
        "InvalidTitle" => "일정 이름을 1~80자로 입력하세요.", "InvalidNote" => "메모는 1000자까지 입력할 수 있습니다.",
        "InvalidCategory" => "일정을 넣을 분류를 다시 선택하세요.", "InvalidWeeks" => "등록할 기간을 1~52주로 입력하세요.",
        "InvalidInterval" => "반복 간격을 1~52주로 입력하세요.", "NoWeekdays" => "진행할 요일을 선택하세요. 요일마다 다른 시각을 지정할 수 있습니다.",
        "InvalidRange" or "InvalidStartDate" => "2020~2100년 안에서 등록할 날짜 범위를 선택하세요.",
        "CapacityExceeded" => "저장 가능한 일정 수를 넘습니다. 등록할 주 수를 줄이세요.",
        "ProfileChanged" => "사용 중인 계정이 바뀌었습니다. 분류와 일정을 확인하고 다시 등록하세요.",
        _ when code.StartsWith("InvalidTime:",StringComparison.Ordinal) && int.TryParse(code[12..],out var day) && day is >= 0 and <= 6 => CultureInfo.GetCultureInfo("ko").DateTimeFormat.GetDayName((DayOfWeek)day) + " 시각을 HH:mm 형식으로 입력하세요.",
        _ => "요일과 시각 설정을 다시 확인하세요."
    };
}
