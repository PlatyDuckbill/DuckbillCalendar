using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Plugin;

internal sealed partial class MainWindow
{
    private string calendarSearch = "", lastCalendarSearch = "";
    private bool calendarSearchOpen, focusCalendarSearch, requestCalendarSearch;
    private IReadOnlyList<CalendarEvent>? lastSearchEvents;
    private DateOnly lastSearchToday;
    private IReadOnlyList<CalendarSearchResult> calendarSearchResults = [];
    private string searchFocusEventId = "", searchFocusPhaseId = "";
    internal Vector2 CalendarSearchInputTarget { get; private set; }
    internal Vector2 CalendarSearchClearTarget { get; private set; }
    internal Dictionary<string, Vector2> CalendarSearchResultTargets { get; } = [];
    internal Dictionary<string, Vector2> CalendarSearchSourceTargets { get; } = [];
    internal IReadOnlyList<CalendarSearchResult> CalendarSearchResults => calendarSearchResults;
    internal bool CalendarSearchFits { get; private set; }
    internal bool SearchHiddenPreviewShown { get; private set; }
    internal bool CalendarSearchOpen => calendarSearchOpen;
    internal Vector2 CalendarSearchButtonTarget { get; private set; }
    internal Vector2 CalendarSearchPopupPosition { get; private set; }
    internal Vector2 CalendarSearchPopupSize { get; private set; }

    private void OpenCalendarSearch() { calendarSearchOpen = focusCalendarSearch = requestCalendarSearch = true; }
    private void CloseCalendarSearch()
    {
        calendarSearchOpen = focusCalendarSearch = requestCalendarSearch = false;
        calendarSearch = lastCalendarSearch = ""; calendarSearchResults = [];
    }

    private void DrawCalendarSearch()
    {
        CalendarSearchInputTarget = CalendarSearchClearTarget = Vector2.Zero;
        CalendarSearchResultTargets.Clear(); CalendarSearchSourceTargets.Clear();
        CalendarSearchFits = true;
        if (calendarSearch.Length > 0 && !calendarSearchOpen) OpenCalendarSearch();
        if (requestCalendarSearch) { ImGui.OpenPopup("###CalendarSearchPopup"); requestCalendarSearch = false; }
        if (!calendarSearchOpen) return;
        var viewport = ImGui.GetMainViewport(); var f = ImGui.GetFontSize();
        var width = Math.Min(f * 30, viewport.WorkSize.X - f);
        var height = Math.Min(f * (string.IsNullOrWhiteSpace(calendarSearch) ? 4.8f : 18), viewport.WorkSize.Y - f);
        var min = viewport.WorkPos + new Vector2(f * .5f);
        var max = viewport.WorkPos + viewport.WorkSize - new Vector2(width + f * .5f, height + f * .5f);
        var anchor = CalendarSearchButtonTarget + new Vector2(-width + f, f * 1.3f);
        ImGui.SetNextWindowPos(Vector2.Clamp(anchor, min, Vector2.Max(min, max)));
        ImGui.SetNextWindowSize(new(width, height));
        if (!ImGui.BeginPopup("###CalendarSearchPopup", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
        { CloseCalendarSearch(); return; }
        try
        {
            CalendarSearchPopupPosition = ImGui.GetWindowPos(); CalendarSearchPopupSize = ImGui.GetWindowSize();
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) CloseCalendarSearch();
            else DrawCalendarSearchContent();
            if (!calendarSearchOpen) ImGui.CloseCurrentPopup();
        }
        finally { ImGui.EndPopup(); }
    }

    private void DrawCalendarSearchContent()
    {
        var f = ImGui.GetFontSize(); var width = ImGui.GetContentRegionAvail().X;
        var clearWidth = ImGui.CalcTextSize("지우기").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetNextItemWidth(Math.Max(f * 3, width - clearWidth - ImGui.GetStyle().ItemSpacing.X));
        if (focusCalendarSearch) { ImGui.SetKeyboardFocusHere(); focusCalendarSearch = false; }
        var enter = ImGui.InputTextWithHint("###calendarSearch", "일정 찾기 · 예: 백은, 업데이트, 공격대", ref calendarSearch, 120, ImGuiInputTextFlags.EnterReturnsTrue);
        CalendarSearchInputTarget = ItemCenter();
        if ((ImGui.IsItemActive() || ImGui.IsItemFocused()) && ImGui.IsKeyPressed(ImGuiKey.Escape)) { CloseCalendarSearch(); return; }
        ImGui.SameLine(); ImGui.BeginDisabled(calendarSearch.Length == 0);
        if (GameThemeChrome.Button("지우기###clearCalendarSearch")) calendarSearch = "";
        ImGui.EndDisabled(); CalendarSearchClearTarget = ItemCenter();
        CalendarSearchFits = ImGui.GetItemRectMax().X <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X + .5f;
        CalendarSearchResultTargets.Clear();
        CalendarSearchSourceTargets.Clear();
        if (string.IsNullOrWhiteSpace(calendarSearch))
        {
            calendarSearchResults = []; lastCalendarSearch = "";
            MutedWrapped("전체 기간에서 찾습니다. 결과 선택으로 날짜 이동 · Esc 닫기");
            return;
        }
        if (calendarSearch != lastCalendarSearch || !ReferenceEquals(lastSearchEvents, model.Events) || lastSearchToday != model.Today)
        {
            calendarSearchResults = model.FindSchedules(calendarSearch);
            lastCalendarSearch = calendarSearch; lastSearchEvents = model.Events; lastSearchToday = model.Today;
        }
        if (enter && calendarSearchResults.FirstOrDefault(r => r.Date?.Year is >= 2020 and <= 2100) is { } first) { OpenSearchResult(first); return; }
        UiWidgets.Secondary("전체 기간 · 검색 결과 " + calendarSearchResults.Count + "개");
        if (calendarSearchResults.Count == 0)
        {
            MutedWrapped("일치하는 일정이 없습니다. 이름 일부나 버전 번호로 검색해 보세요.");
            return;
        }
        var height = Math.Max(1, ImGui.GetContentRegionAvail().Y);
        ImGui.BeginChild("###CalendarSearchResults", new(0, height), true);
        try
        {
            foreach (var result in calendarSearchResults)
            {
                ImGui.PushID(result.Key);
                try
                {
                    var hidden = !model.VisibleCalendarEvents.Any(e => e.Id == result.Event.Id && e.Phases.Any(p => p.Id == result.Phase.Id));
                    var url = SourceFor(result.Event, result.Phase);
                    var linkWidth = url is null ? 0 : UiWidgets.IconButtonSize("", true).X + ImGui.GetStyle().ItemSpacing.X;
                    var rowWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - linkWidth);
                    var pos = ImGui.GetCursorScreenPos();
                    var titleWidth = Math.Max(f, rowWidth - f);
                    var title = result.Event.Title + (hidden ? " · 숨김" : "");
                    var titleHeight = ImGui.CalcTextSize(title, false, titleWidth).Y;
                    var period = UiText.Period(result.Phase, result.Event.IsPersonal);
                    var caption = result.Phase.Label + " · " + period;
                    var captionHeight = ImGui.CalcTextSize(caption, false, titleWidth).Y * .85f;
                    var rowHeight = titleHeight + captionHeight + f * .8f;
                    var clicked = false;
                    if (result.Date?.Year is >= 2020 and <= 2100)
                    {
                        clicked = ImGui.InvisibleButton("###searchDate", new(rowWidth, rowHeight));
                        CalendarSearchResultTargets[result.Key] = ItemCenter();
                    }
                    else ImGui.Dummy(new(rowWidth, rowHeight));
                    var hovered = ImGui.IsItemHovered(); var after = ImGui.GetCursorScreenPos();
                    var draw = ImGui.GetWindowDrawList();
                    if (hovered) draw.AddRectFilled(pos, pos + new Vector2(rowWidth, rowHeight), ImGui.ColorConvertFloat4ToU32(Accent with { W = .09f }), 4);
                    draw.AddText(ImGui.GetFont(), f, pos + new Vector2(f * .4f, f * .2f), ImGui.GetColorU32(ImGuiCol.Text), title, titleWidth);
                    draw.AddText(ImGui.GetFont(), f * .85f, pos + new Vector2(f * .4f, titleHeight + f * .35f), ImGui.GetColorU32(ImGuiCol.TextDisabled), caption, titleWidth / .85f);
                    if (hovered && result.Date is not null) UiWidgets.Tooltip("클릭하면 " + UiText.Date(result.Date.Value) + " 달력으로 이동합니다." + (hidden ? "\n숨김 설정은 유지하고 선택한 일정을 열어 봅니다." : ""));
                    if (url is not null)
                    {
                        ImGui.SetCursorScreenPos(pos + new Vector2(rowWidth + ImGui.GetStyle().ItemSpacing.X, f * .2f));
                        if (UiWidgets.IconButton("searchSource", UiIcon.Link, "", false, Accent, true)) model.Post(() => openSearchLink(url));
                        CalendarSearchSourceTargets[result.Key] = ItemCenter();
                        if (ImGui.IsItemHovered()) UiWidgets.Tooltip("공식 안내 열기");
                    }
                    ImGui.SetCursorScreenPos(after); ImGui.Separator();
                    if (clicked) { OpenSearchResult(result); break; }
                }
                finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndChild(); }
    }

    private void OpenSearchResult(CalendarSearchResult result)
    {
        if (result.Date?.Year is not (>= 2020 and <= 2100)) return;
        model.JumpToSearchResult(result);
        revealDate = result.Date;
        searchFocusEventId = result.Event.Id; searchFocusPhaseId = result.Phase.Id;
        CloseCalendarSearch();
        dayPopupRequest = resetOngoingSection = true;
    }

    private IReadOnlyList<CalendarDisplayEntry> SearchHiddenPreview()
    {
        if (model.SelectedEventId != searchFocusEventId || model.SelectedPhaseId != searchFocusPhaseId ||
            model.SelectedEvent is not { } item || item.Phases.FirstOrDefault(p => p.Id == searchFocusPhaseId) is not { } phase ||
            model.DayDisplayEntries.Any(e => e.Members.Any(m => m.Event.Id == item.Id && m.Phase.Id == phase.Id))) return [];
        return CalendarPresentation.Build(CalendarEngine.DayEvents([item with { Phases = [phase] }], model.SelectedDate), model.SelectedDate);
    }
}
