using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;

namespace DuckbillCalendar.Plugin;

internal sealed class MainWindow : Window
{
    private readonly CalendarModel model;
    private string search = "";
    private int category;
    private bool onlyAlerts;
    private string appliedSearch = "";
    private int appliedCategory;
    private bool appliedOnlyAlerts;
    private bool selectSettings;
    private bool editorOpen;
    private bool editorRequest;
    private string editId = "";
    private string editName = "";
    private string editDate = "";
    private string editTime = "";
    private string editNote = "";
    private int editKind;
    private bool editDateOnly;
    private string editorError = "";
    private string importPath = "";
    private string remoteUrl = "";
    private int jumpYear;
    private int jumpMonth;
    private float monthWheelRemainder;

    public MainWindow(CalendarModel model) : base("DuckbillCalendar###DuckbillCalendar.Main")
    {
        this.model = model;
        Size = new(980, 660);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new() { MinimumSize = new(760, 500), MaximumSize = new(float.MaxValue) };
    }
    public void OpenSettings() { IsOpen = true; selectSettings = true; }
    public override void Draw()
    {
        if (model.State.View.ExampleMode)
        {
            Banner("ExampleBanner");
            ImGui.SameLine();
            if (Button("UseActualFeed", "useActualFeed")) model.Post(() => model.Workspace.LoadExample(false));
        }
        if (model.Workspace.HasUnsavedChanges) UiWidgets.Colored(ThemeScope.Hex(0xd8bb79), UiText.Get("UnsavedChanges"));
        if (model.Feed.Events.Count == 0) UiWidgets.Wrapped(UiText.Get("EmptyPublicFeed"));
        if (ImGui.BeginTabBar("###DuckbillCalendar.Tabs"))
        {
            try
            {
                Tab("Calendar", "calendar", DrawCalendar);
                Tab("News", "news", DrawNews);
                Tab("Subscriptions", "subscriptions", DrawSubscriptions);
                Tab("Settings", "settings", DrawSettings, selectSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
                selectSettings = false;
            }
            finally { ImGui.EndTabBar(); }
        }
        ImGui.Separator();
        UiWidgets.Secondary(UiText.Get("WorkspaceFooter", model.Feed.Events.Count, UiText.Time(model.OfficialEventsObservedAt)));
        if (editorRequest) { ImGui.OpenPopup("###DuckbillCalendar.EntryEditor"); editorRequest = false; }
        DrawEditor();
    }
    private void Tab(string key, string id, Action draw, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
    {
        if (!ImGui.BeginTabItem(UiText.Get(key) + "###DuckbillCalendar.Tab." + id, flags)) return;
        try { draw(); }
        finally { ImGui.EndTabItem(); }
    }
    private void DrawCalendar()
    {
        DrawCalendarActions();
        DrawFiltersCore(false);
        DrawReleaseLandmark();
        var width = ImGui.GetContentRegionAvail().X;
        var font = ImGui.GetFontSize();
        var wide = width >= font * 51;
        if (!wide && model.PanelMode is CalendarPanelMode.Day or CalendarPanelMode.Event)
        {
            DrawContextAgenda();
            return;
        }
        if (model.ViewMode != CalendarViewMode.Grid && model.PanelMode != CalendarPanelMode.Event)
        {
            DrawCalendarContent();
            return;
        }
        if (wide && ImGui.BeginTable("###CalendarWorkspaceV2", 2, ImGuiTableFlags.SizingStretchProp))
        {
            try
            {
                ImGui.TableSetupColumn("###CalendarCanvas", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("###CalendarAgenda", ImGuiTableColumnFlags.WidthFixed, Math.Clamp(width * .31f, font * 15, font * 20));
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                DrawCalendarContent();
                ImGui.TableNextColumn(); DrawContextAgenda();
            }
            finally { ImGui.EndTable(); }
        }
        else
        {
            DrawCalendarContent();
        }
    }
    private void DrawCalendarActions()
    {
        if (Button("PreviousMonth", "prev")) model.ChangeMonth(-1);
        ContinueRow(ButtonWidth(new DateOnly(model.Year, model.MonthNumber, 1).ToString("yyyy.MM", CultureInfo.InvariantCulture)));
        if (ImGui.SmallButton(new DateOnly(model.Year, model.MonthNumber, 1).ToString("yyyy.MM", CultureInfo.InvariantCulture) + "###monthPicker"))
        {
            jumpYear = model.Year; jumpMonth = model.MonthNumber;
            ImGui.OpenPopup("###MonthJump");
        }
        DrawMonthJump();
        ContinueRow(ButtonWidth(UiText.Get("NextMonth"))); if (Button("NextMonth", "next")) model.ChangeMonth(1);
        ContinueRow(ButtonWidth(UiText.Get("Today"))); if (Button("Today", "today")) model.GoToday();
        ContinueRow(ButtonWidth(UiText.Get("MonthGridView")));
        if (ImGui.RadioButton(UiText.Get("MonthGridView") + "###gridView", model.ViewMode == CalendarViewMode.Grid)) model.SetListView(false);
        ContinueRow(ButtonWidth(UiText.Get("MonthAgendaView")));
        if (ImGui.RadioButton(UiText.Get("MonthAgendaView") + "###agendaView", model.ViewMode == CalendarViewMode.Agenda)) model.SetListView(true);
        ContinueRow(ButtonWidth(UiText.Get("UndatedView")));
        if (ImGui.RadioButton(UiText.Get("UndatedView") + "###undatedView", model.ViewMode == CalendarViewMode.Undated)) model.ShowUndatedAgenda();
        var count = Enum.GetValues<CalendarGroup>().Count(model.IsCalendarVisible);
        var calendars = UiText.Get("CalendarVisibilityButton", count);
        ContinueRow(ButtonWidth(calendars));
        if (ImGui.SmallButton(calendars + "###calendarMenu")) ImGui.OpenPopup("###CalendarVisibilityPopup");
        var menuWidth = Math.Min(ImGui.GetFontSize() * 20, ImGui.GetMainViewport().WorkSize.X - 32);
        ImGui.SetNextWindowSizeConstraints(new(menuWidth, 0), new(menuWidth, ImGui.GetMainViewport().WorkSize.Y - 32));
        if (ImGui.BeginPopup("###CalendarVisibilityPopup"))
        {
            try { DrawCalendarVisibility(); }
            finally { ImGui.EndPopup(); }
        }
        ContinueRow(ButtonWidth(UiText.Get("CalendarViewOptions")));
        if (ImGui.SmallButton(UiText.Get("CalendarViewOptions") + "###viewMenu")) ImGui.OpenPopup("###CalendarViewPopup");
        ImGui.SetNextWindowSizeConstraints(new(menuWidth, 0), new(menuWidth, ImGui.GetMainViewport().WorkSize.Y - 32));
        if (ImGui.BeginPopup("###CalendarViewPopup"))
        {
            try
            {
                var bars = model.State.View.PeriodBars;
                if (ImGui.Checkbox(UiText.Get("PeriodBars") + "###PeriodBars", ref bars)) model.SetPeriodBars(bars);
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("PeriodBarsHelp"));
                var rewards = model.State.View.ShowRewardBars;
                if (ImGui.Checkbox(UiText.Get("ShowRewardBars") + "###calendarRewardBars", ref rewards)) model.UpdateView(v => v with { ShowRewardBars = rewards });
                UiWidgets.Wrapped(UiText.Get("DefaultEndpointDisplay"));
            }
            finally { ImGui.EndPopup(); }
        }
        ContinueRow(ButtonWidth(UiText.Get("AddEvent"))); if (Button("AddEvent", "add")) OpenEditor();
    }
    private void DrawFilters() => DrawFiltersCore(true);
    private void DrawFiltersCore(bool showCategory)
    {
        if (appliedSearch != model.Search || appliedCategory != model.CategoryFilter || appliedOnlyAlerts != model.OnlyAlerts)
        {
            search = model.Search;
            category = model.CategoryFilter;
            onlyAlerts = model.OnlyAlerts;
        }
        appliedSearch = model.Search;
        appliedCategory = model.CategoryFilter;
        appliedOnlyAlerts = model.OnlyAlerts;
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12);
        if (ImGui.InputTextWithHint("###Search", UiText.Get("CalendarSearchHint"), ref search, 120)) model.SetQuery(search, category, onlyAlerts);
        if (showCategory)
        {
            ContinueRow(ImGui.GetFontSize() * 11); ImGui.SetNextItemWidth(ImGui.GetFontSize() * 11);
            var names = new[] { "All", "CategoryEvent", "CategoryPatch", "CategoryPvp", "CategorySale", "CategoryPersonal", "CategoryNotice" }.Select(k => UiText.Get(k)).ToArray();
            if (ImGui.Combo("###CategoryFilter", ref category, names, names.Length)) model.SetQuery(search, category, onlyAlerts);
        }
        ContinueRow(CheckboxWidth(UiText.Get("OnlyAlertEvents")));
        if (ImGui.Checkbox(UiText.Get("OnlyAlertEvents") + "###OnlyAlerts", ref onlyAlerts)) model.SetQuery(search, category, onlyAlerts);
    }
    private void DrawCalendarVisibility()
    {
        ImGui.TextUnformatted(UiText.Get("VisibleCalendars"));
        ImGui.Separator();
        if (ImGui.SmallButton(UiText.Get("SelectAllCalendars") + "###calendarsAll")) model.SetAllCalendars(true);
        ImGui.SameLine();
        if (ImGui.SmallButton(UiText.Get("HideAllCalendars") + "###calendarsNone")) model.SetAllCalendars(false);
        var groups = new (CalendarGroup Group, string Key, string Color)[]
        {
            (CalendarGroup.Events, "CategoryEvent", "web-event"), (CalendarGroup.Patches, "CategoryPatch", "patch"),
            (CalendarGroup.Maintenance, "CategoryMaintenance", "maintenance"), (CalendarGroup.Pvp, "CategoryPvp", "pvp-season"),
            (CalendarGroup.Competitions, "CategoryCompetitions", "notice"), (CalendarGroup.Sales, "CategorySale", "sale"),
            (CalendarGroup.Subscriptions, "CategorySubscription", "subscription"), (CalendarGroup.Personal, "CategoryPersonal", "personal"),
            (CalendarGroup.Notices, "ShowNoticePublications", "notice")
        };
        foreach (var item in groups)
        {
            var label = UiText.Get(item.Key);
            var visible = model.IsCalendarVisible(item.Group);
            ImGui.PushStyleColor(ImGuiCol.CheckMark, ThemeScope.EventColor(item.Color, model.State.View.Theme == "paper"));
            try
            {
                if (ImGui.Checkbox(label + "###calendar.visible." + item.Group, ref visible)) model.SetCalendarVisible(item.Group, visible);
            }
            finally { ImGui.PopStyleColor(); }
        }
    }
    private static float ButtonWidth(string label) => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
    private static float CheckboxWidth(string label) => ImGui.CalcTextSize(label).X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
    private static void ContinueRow(float nextWidth)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextWidth <= right) ImGui.SameLine();
    }
    private void DrawMonthJump()
    {
        var width = ImGui.GetFontSize() * 20;
        ImGui.SetNextWindowSizeConstraints(new(width, 0), new(width, ImGui.GetMainViewport().WorkSize.Y));
        if (!ImGui.BeginPopup("###MonthJump")) return;
        try
        {
            ImGui.TextUnformatted(UiText.Get("GoToMonth"));
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 7);
            ImGui.InputInt(UiText.Get("Year") + "###jumpYear", ref jumpYear);
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 7);
            ImGui.InputInt(UiText.Get("Month") + "###jumpMonth", ref jumpMonth);
            ImGui.BeginDisabled(jumpYear is < 2020 or > 2100 || jumpMonth is < 1 or > 12);
            if (Button("Go", "goMonth")) { model.GoToMonth(jumpYear, jumpMonth); ImGui.CloseCurrentPopup(); }
            ImGui.EndDisabled();
        }
        finally { ImGui.EndPopup(); }
    }
    private void DrawHighlights() => DrawReleaseLandmark();
    private void DrawReleaseLandmark()
    {
        var releases = model.UpcomingHighlights.Where(h => h.Event.Id.StartsWith("kr:curated:v8-", StringComparison.Ordinal) && h.Phase.Id is "early-access" or "release").Take(2).ToArray();
        if (releases.Length == 0 || releases.All(h => h.Date.Year == model.Year && h.Date.Month == model.MonthNumber)) return;
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        try { ImGui.TextUnformatted(UiText.Get("ReleaseLandmark")); }
        finally { ImGui.PopStyleColor(); }
        foreach (var release in releases)
        {
            ContinueRow(ButtonWidth(UiText.CompactHighlight(release, includeDate: true)));
            if (ImGui.SmallButton(UiText.CompactHighlight(release, includeDate: true) + "###releaseLandmark." + release.Phase.Id)) model.JumpToHighlight(release);
            if (ImGui.IsItemHovered()) UiWidgets.Tooltip(release.Event.Title + "\n" + UiText.Boundary(release.Boundary));
        }
        ImGui.Separator();
    }
    private void DrawCalendarContent()
    {
        if (model.ViewMode == CalendarViewMode.Undated) DrawUndatedCollection();
        else if (model.ViewMode == CalendarViewMode.Agenda) DrawMonthCollection();
        else DrawMonth();
        ImGui.Spacing();
        UiWidgets.Secondary(UiText.Get("FrontlineStrip", model.TodayFrontline?.IsKnown == true ? model.TodayFrontline.Name ?? UiText.Get("UnknownField") : UiText.Get("UnknownField"), model.TomorrowFrontline?.IsKnown == true ? model.TomorrowFrontline.Name ?? UiText.Get("UnknownField") : UiText.Get("UnknownField")));
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("FrontlineHelp"));
    }
    private void DrawMonthCollection()
    {
        ImGui.BeginChild("###MonthCollection", new(0, Math.Max(ImGui.GetFontSize() * 12, ImGui.GetContentRegionAvail().Y - ImGui.GetFontSize() * 3)));
        try { DrawMonthAgenda(); }
        finally { ImGui.EndChild(); }
    }
    private void DrawUndatedCollection()
    {
        ImGui.TextUnformatted(UiText.Get("UndatedCollectionTitle", model.UndatedEvents.Count));
        UiWidgets.Wrapped(UiText.Get("UndatedCollectionHelp"));
        ImGui.BeginChild("###UndatedCollection", new(0, Math.Max(ImGui.GetFontSize() * 10, ImGui.GetContentRegionAvail().Y - ImGui.GetFontSize() * 5)));
        try
        {
            foreach (var item in model.UndatedEvents)
            {
                foreach (var phase in item.Phases)
                    DrawAgendaEvent(item, phase, "undated", CalendarEngine.BoundaryDate(phase.Start) ?? CalendarEngine.BoundaryDate(phase.End) ?? model.Today, "undated");
            }
            if (model.UndatedEvents.Count == 0) UiWidgets.Wrapped(UiText.Get("NoUndatedCollection"));
        }
        finally { ImGui.EndChild(); }
    }
    private void DrawContextAgenda()
    {
        ImGui.BeginChild("###ContextAgenda", new(0, Math.Max(ImGui.GetFontSize() * 9, ImGui.GetContentRegionAvail().Y - ImGui.GetFontSize() * 2.5f)));
        try
        {
            if (model.PanelMode == CalendarPanelMode.Event)
            {
                if (ImGui.SmallButton(UiText.Get("BackToDateAgenda") + "###backToAgenda")) model.BackToAgenda();
                DrawDetail();
            }
            else if (model.PanelMode == CalendarPanelMode.Day) DrawDayAgenda();
            else if (model.PanelMode == CalendarPanelMode.Undated)
            {
                ImGui.TextUnformatted(UiText.Get("UndatedContextTitle"));
                UiWidgets.Wrapped(UiText.Get("UndatedContextHelp"));
            }
            else DrawMonthAgenda();
        }
        finally { ImGui.EndChild(); }
    }
    private void DrawMonthAgenda()
    {
        ImGui.TextUnformatted(UiText.Get("MonthAgendaTitle", model.Year, model.MonthNumber));
        UiWidgets.Secondary(UiText.Get("MonthAgendaScope"));
        ImGui.Separator();
        if (model.MonthAgenda.Count == 0)
        {
            UiWidgets.Wrapped(UiText.Get("NoMonthBoundaries"));
            if (ImGui.SmallButton(UiText.Get("BrowseUndated") + "###monthBrowseUndated")) model.ShowUndatedAgenda();
            return;
        }
        var currentMonth = model.Year == model.Today.Year && model.MonthNumber == model.Today.Month;
        var active = model.MonthAgenda.Where(h => !currentMonth || h.Date >= model.Today).ToArray();
        foreach (var group in active.GroupBy(h => h.Date))
        {
            ImGui.Spacing(); ImGui.TextUnformatted(UiText.DayLabel(group.Key));
            foreach (var item in group) DrawAgendaEvent(item.Event, item.Phase, item.Edge, item.Date, "monthAgenda");
        }
        var past = currentMonth ? model.MonthAgenda.Where(h => h.Date < model.Today).ToArray() : [];
        if (active.Length == 0 && currentMonth) UiWidgets.Wrapped(UiText.Get("NoRemainingMonthBoundaries"));
        if (past.Length > 0 && ImGui.TreeNode(UiText.Get("PastMonthItems", past.Length) + "###pastMonthAgenda"))
        {
            try
            {
                foreach (var group in past.GroupBy(h => h.Date))
                {
                    ImGui.TextUnformatted(UiText.DayLabel(group.Key));
                    foreach (var item in group) DrawAgendaEvent(item.Event, item.Phase, item.Edge, item.Date, "pastMonthAgenda");
                }
            }
            finally { ImGui.TreePop(); }
        }
    }
    private void DrawDayAgenda()
    {
        ImGui.TextUnformatted(UiText.DayLabel(model.SelectedDate));
        if (ImGui.SmallButton(UiText.Get("ShowMonthAgenda") + "###showMonthAgenda")) model.ShowMonthOverview();
        ImGui.Separator();
        if (model.DayBoundaryItems.Count == 0) UiWidgets.Wrapped(UiText.Get("NoDayBoundaries"));
        foreach (var item in model.DayBoundaryItems) DrawAgendaEvent(item.Event, item.Phase, item.Edge, item.Date, "dayAgenda");
        if (model.DayOngoingItems.Count > 0)
        {
            ImGui.Spacing(); ImGui.TextUnformatted(UiText.Get("OngoingOnDay", model.DayOngoingItems.Count));
            foreach (var item in model.DayOngoingItems) DrawAgendaEvent(item.Event, item.Phase, "ongoing", model.SelectedDate, "dayOngoing");
        }
    }
    private void DrawAgendaEvent(CalendarEvent item, EventPhase phase, string edge, DateOnly date, string scope)
    {
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var title = item.Title;
        var subtitle = UiText.AgendaRole(item, phase, edge);
        var boundary = edge == "end" ? phase.End : phase.Start;
        if (edge == "undated")
            subtitle = UiText.Phase(item, phase) + "\n" + UiText.Get("StartShort") + ": " + UiText.Boundary(phase.Start) +
                "\n" + UiText.Get("EndShort") + ": " + UiText.Boundary(phase.End);
        else if (edge != "ongoing") subtitle = UiText.ClockLabel(boundary) + " · " + subtitle;
        if (NeedsReview(item)) subtitle = UiText.Get("NeedsReviewShort") + " · " + subtitle;
        var font = ImGui.GetFontSize();
        var titleHeight = ImGui.CalcTextSize(title, false, Math.Max(1, width - 12)).Y;
        var subtitleHeight = ImGui.CalcTextSize(subtitle, false, Math.Max(1, width - 12)).Y;
        var rowHeight = Math.Max(font * 2.5f, titleHeight + subtitleHeight + 10);
        var pos = ImGui.GetCursorScreenPos();
        var selected = model.SelectedEventId == item.Id && model.SelectedPhaseId == phase.Id && model.SelectedEdge == edge;
        var clicked = ImGui.Selectable("###" + scope + "." + item.Id + "." + phase.Id + "." + edge, selected, ImGuiSelectableFlags.None, new(width, rowHeight));
        var hovered = ImGui.IsItemHovered();
        var after = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddLine(pos + new Vector2(2, 4), pos + new Vector2(2, rowHeight - 4), ImGui.ColorConvertFloat4ToU32(ThemeScope.EventColor(item.Category, model.State.View.Theme == "paper")), 2);
        ImGui.SetCursorScreenPos(pos + new Vector2(8, 4));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 12);
        try
        {
            ImGui.TextUnformatted(title);
            ImGui.SetCursorScreenPos(new(pos.X + 8, ImGui.GetCursorScreenPos().Y));
            UiWidgets.Secondary(subtitle);
        }
        finally { ImGui.PopTextWrapPos(); }
        ImGui.SetCursorScreenPos(after);
        if (clicked)
        {
            if (edge is "ongoing" or "undated") model.Select(item, phase, date, edge);
            else model.SelectAgenda(new(item, phase, edge, boundary, date));
        }
        if (hovered) UiWidgets.Tooltip(title + "\n" + subtitle + "\n" + UiText.Boundary(boundary));
    }
    private void DrawMonth()
    {
        if (model.Calendar == null) return;
        var font = ImGui.GetFontSize();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var height = font + 5 + spacing + model.Calendar.Weeks.Sum(w => MonthRowHeight(font, w.LaneCount) + spacing) + 2;
        // Keep the hover region local to the grid, independently of the detail pane.
        ImGui.BeginChild("###MonthGrid", new(Math.Max(1, ImGui.GetContentRegionAvail().X), height), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        try
        {
            DrawMonthGrid();
            HandleMonthWheel();
        }
        finally { ImGui.EndChild(); }
    }
    private void HandleMonthWheel()
    {
        var io = ImGui.GetIO();
        if (!ImGui.IsWindowHovered() || ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel) ||
            ImGui.IsAnyItemActive() || io.KeyCtrl || io.KeyShift || io.KeyAlt)
        {
            monthWheelRemainder = 0;
            return;
        }
        // API 15 ships ImGui 1.88: custom wheel users register the hovered ID
        // rather than the newer SetKeyOwner API. Register even on idle frames so
        // NewFrame won't also scroll the parent when the next wheel event arrives.
        var context = ImGui.GetCurrentContext();
        if (context.HoveredId == 0) context.HoveredId = ImGui.GetID("###MonthWheel");
        context.HoveredIdUsingMouseWheel = true;
        if (io.MouseWheel == 0) return;
        monthWheelRemainder = Math.Clamp(monthWheelRemainder + io.MouseWheel, -12f, 12f);
        io.MouseWheel = 0;
        var notches = (int)MathF.Truncate(monthWheelRemainder);
        monthWheelRemainder -= notches;
        if (notches == 0) return;
        var current = model.Year * 12 + model.MonthNumber - 1;
        var target = Math.Clamp(current - notches, 2020 * 12, 2100 * 12 + 11);
        if (target != current) model.ChangeMonth(target - current);
    }
    private static float MonthRowHeight(float font, int laneCount) => Math.Max(font * 4.8f, font + 2 + laneCount * font + font * 3 + 4);
    private void DrawMonthGrid()
    {
        if (model.Calendar == null) return;
        var available = ImGui.GetContentRegionAvail().X;
        var cellWidth = available / 7f;
        var font = ImGui.GetFontSize();
        var dateHeight = font + 2;
        var markerHeight = font;
        var light = model.State.View.Theme == "paper";
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var header = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        for (var col = 0; col < 7; col++) draw.AddText(header + new Vector2(col * cellWidth + 5, 0), ImGui.GetColorU32(ImGuiCol.TextDisabled), UiText.Get(days[col]));
        ImGui.Dummy(new(available, font + 5));
        foreach (var week in model.Calendar.Weeks)
        {
            var origin = ImGui.GetCursorScreenPos();
            var laneHeight = week.LaneCount * markerHeight;
            var rowHeight = MonthRowHeight(font, week.LaneCount);
            for (var col = 0; col < 7; col++)
            {
                var day = week.Days[col];
                var pos = origin + new Vector2(col * cellWidth, 0);
                var end = pos + new Vector2(cellWidth, rowHeight);
                var bg = day.InMonth ? (light ? 0xfaf9f4 : 0x202426) : (light ? 0xe5e5dd : 0x171a1c);
                draw.AddRectFilled(pos, end, ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(bg)));
                draw.AddRect(pos, end, ImGui.GetColorU32(ImGuiCol.Border));
                if (model.HasDateFocus && day.Date == model.SelectedDate) draw.AddRect(pos + Vector2.One, end - Vector2.One, ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(light ? 0x356b8b : 0x87b8d7)), 0, ImDrawFlags.None, 2);
                ImGui.SetCursorScreenPos(pos + new Vector2(3, 2));
                if (ImGui.InvisibleButton("###day." + UiText.Date(day.Date), new(cellWidth - 6, rowHeight - 4))) model.SelectDate(day.Date);
                // The whole date is a target; its markers remain independently selectable.
                ImGui.SetItemAllowOverlap();
                var number = day.Date.Day.ToString(CultureInfo.InvariantCulture);
                var numberColor = day.Date == model.Today ? ThemeScope.Hex(light ? 0x805d25 : 0xd8bb79) : day.InMonth ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
                draw.AddText(pos + new Vector2(6, 3), ImGui.ColorConvertFloat4ToU32(numberColor), number);
                var markerSpace = Math.Max(0, rowHeight - dateHeight - laneHeight - 4);
                var capacity = Math.Max(0, (int)(markerSpace / markerHeight));
                var reserveOverflow = day.HiddenCount > 0 || day.Items.Count > capacity;
                var visibleCount = Math.Max(0, Math.Min(day.Items.Count, capacity - (reserveOverflow ? 1 : 0)));
                var visibleItems = day.Items.Take(visibleCount).ToArray();
                for (var index = 0; index < visibleItems.Length; index++)
                {
                    var item = visibleItems[index];
                    var point = pos + new Vector2(3, dateHeight + laneHeight + markerHeight * index);
                    DrawCalendarButton(point, new(cellWidth - 6, markerHeight - 2), item.Event, item.Phase,
                        "point." + UiText.Date(day.Date) + "." + index, UiText.CalendarMarker(item), () => model.SelectAgenda(new(item.Event, item.Phase, item.Edge, item.Edge == "end" ? item.Phase.End : item.Phase.Start, day.Date)));
                }
                var more = day.HiddenCount + Math.Max(0, day.Items.Count - visibleCount);
                if (more > 0)
                {
                    var morePos = pos + new Vector2(3, rowHeight - font);
                    ImGui.SetCursorScreenPos(morePos);
                    if (ImGui.InvisibleButton("###more." + UiText.Date(day.Date), new(cellWidth - 6, font))) model.SelectDate(day.Date);
                    draw.AddText(morePos, ImGui.GetColorU32(ImGuiCol.TextDisabled), UiText.Get("MoreCount", more));
                }
            }
            foreach (var bar in week.Bars)
            {
                var pos = origin + new Vector2(bar.StartColumn * cellWidth + 2, dateHeight + bar.Lane * markerHeight);
                var size = new Vector2((bar.EndColumn - bar.StartColumn + 1) * cellWidth - 4, markerHeight - 2);
                var title = (bar.ContinuesBefore ? "< " : "") + UiText.Category(bar.Event.Category) + " · " + bar.Event.Title + (bar.ContinuesAfter ? " >" : "");
                DrawCalendarButton(pos, size, bar.Event, bar.Phase, "bar." + UiText.Date(week.StartsOn) + "." + bar.Event.Id + "." + bar.Phase.Id, title,
                    () => model.Select(bar.Event, bar.Phase, week.StartsOn.AddDays(Math.Clamp((int)((ImGui.GetIO().MousePos.X - origin.X) / cellWidth), bar.StartColumn, bar.EndColumn)), "ongoing"));
            }
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new(available, rowHeight));
        }
    }
    private void DrawCalendarButton(Vector2 pos, Vector2 size, CalendarEvent e, EventPhase phase, string id, string label, Action clicked)
    {
        ImGui.SetCursorScreenPos(pos);
        if (ImGui.InvisibleButton("###" + id, size)) clicked();
        var hovered = ImGui.IsItemHovered();
        var color = NeedsReview(e) ? ThemeScope.Hex(model.State.View.Theme == "paper" ? 0x946123 : 0xd8bb79) : ThemeScope.EventColor(e.Category, model.State.View.Theme == "paper");
        if (NeedsReview(e)) label = UiText.Get("NeedsReviewShort") + " · " + label;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(color with { W = hovered ? .28f : .14f }), 2);
        draw.AddLine(pos, pos + new Vector2(0, size.Y), ImGui.ColorConvertFloat4ToU32(color), 2);
        draw.PushClipRect(pos, pos + size, true);
        draw.AddText(pos + new Vector2(6, 0), ImGui.ColorConvertFloat4ToU32(color), FitLabel(label, Math.Max(1, size.X - 10)));
        draw.PopClipRect();
        if (hovered) UiWidgets.Tooltip(e.Title + "\n" + UiText.Phase(e, phase) + "\n" + UiText.Boundary(phase.Start) + " — " + UiText.Boundary(phase.End));
    }
    private static string FitLabel(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var positions = StringInfo.ParseCombiningCharacters(text);
        for (var count = positions.Length - 1; count > 0; count--)
        {
            var candidate = text[..positions[count]] + "…";
            if (ImGui.CalcTextSize(candidate).X <= width) return candidate;
        }
        return "";
    }
    private static bool NeedsReview(CalendarEvent item) => item.Note?.Contains("공식 원문 재검토 대기", StringComparison.Ordinal) == true;
    private void DrawEventList(IReadOnlyList<CalendarEvent> list, string scope)
    {
        if (list.Count == 0) { UiWidgets.Secondary(UiText.Get("NoResults")); return; }
        foreach (var e in list)
        {
            if (ImGui.Selectable(e.Title + "###" + scope + "." + e.Id, model.SelectedEventId == e.Id)) model.Select(e);
            UiWidgets.Secondary(e.Category == "notice" && e.Phases.FirstOrDefault(p => p.Id == "published") is { } publication
                ? UiText.Get("NoticePublishedAt", UiText.Boundary(publication.Start)) : UiText.Category(e.Category));
        }
    }
    private void DrawNews()
    {
        DrawFilters();
        if (!ImGui.BeginTable("###NewsSplit", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) return;
        try
        {
            ImGui.TableSetupColumn("###NewsList", ImGuiTableColumnFlags.WidthStretch, .6f);
            ImGui.TableSetupColumn("###NewsDetail", ImGuiTableColumnFlags.WidthStretch, .4f);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            var height = Math.Clamp(ImGui.GetContentRegionAvail().Y - ImGui.GetFontSize() * 2, ImGui.GetFontSize() * 8, ImGui.GetFontSize() * 29);
            if (ImGui.BeginTable("###NewsRows", 3, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit, new(0, height)))
            {
                try
                {
                    ImGui.TableSetupColumn(UiText.Get("DateColumn"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5.6f);
                    ImGui.TableSetupColumn(UiText.Get("CategoryColumn"), ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5.5f);
                    ImGui.TableSetupColumn(UiText.Get("TitleColumn"), ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
                    foreach (var item in model.NewsEvents)
                    {
                        ImGui.TableNextRow(); ImGui.TableNextColumn();
                        var date = item.Phases.Select(p => CalendarEngine.BoundaryDate(p.Start)).FirstOrDefault(d => d.HasValue);
                        ImGui.TextUnformatted(date is { } value ? value.ToString("yy.MM.dd", CultureInfo.InvariantCulture) : UiText.Get("UnknownDateShort"));
                        ImGui.TableNextColumn();
                        UiWidgets.Colored(ThemeScope.EventColor(item.Category, model.State.View.Theme == "paper"), UiText.Category(item.Category));
                        ImGui.TableNextColumn();
                        if (ImGui.Selectable(item.Title + "###news.row." + item.Id, item.Id == model.SelectedEventId)) model.Select(item);
                        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(item.Title + "\n" + UiText.Category(item.Category));
                    }
                }
                finally { ImGui.EndTable(); }
            }
            ImGui.TableNextColumn(); DrawDetailPanel();
        }
        finally { ImGui.EndTable(); }
    }
    private void DrawDetailPanel()
    {
        var font = ImGui.GetFontSize();
        var height = Math.Clamp(ImGui.GetContentRegionAvail().Y - font, font * 8, font * 31);
        ImGui.BeginChild("###DetailPane", new(0, height));
        try { DrawDetail(); }
        finally { ImGui.EndChild(); }
    }
    private void DrawDetail()
    {
        var e = model.SelectedEvent;
        if (e == null) { UiWidgets.Wrapped(UiText.Get("SelectEvent")); return; }
        var focused = e.Phases.FirstOrDefault(p => p.Id == model.SelectedPhaseId) ?? e.Phases.FirstOrDefault();
        if (focused == null) return;
        var selectedDate = model.SelectedEdge == "end" ? CalendarEngine.BoundaryDate(focused.End) : CalendarEngine.BoundaryDate(focused.Start);
        if (selectedDate is { } focusDate) UiWidgets.Secondary(UiText.DayLabel(focusDate));
        ImGui.Separator();
        UiWidgets.Wrapped(e.Title);
        if (NeedsReview(e)) UiWidgets.Colored(ThemeScope.Hex(model.State.View.Theme == "paper" ? 0x946123 : 0xd8bb79), UiText.Get("NeedsReviewShort"));
        UiWidgets.Colored(ThemeScope.EventColor(e.Category, model.State.View.Theme == "paper"), UiText.Category(e.Category));
        if (e.Category == "notice" && e.Phases.Any(CalendarModel.IsPublicationPhase) && ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("NoticePublicationHelp"));
        if (model.SelectionFiltered) UiWidgets.Wrapped(UiText.Get("SelectionFiltered"));
        var evidenceRows = model.SelectedEvidence;
        var primary = evidenceRows.FirstOrDefault(x => x.Url is { } url && IsOfficialPage(url));
        if (primary?.Url is { } primaryUrl && Button("OpenSource", "primarySource." + e.Id)) model.Post(() => Util.OpenLink(primaryUrl));
        if (primary?.ObservedAt is { } primaryObserved) UiWidgets.Wrapped(UiText.Get("ObservedAt", UiText.Time(primaryObserved)));
        foreach (var p in new[] { focused })
        {
            ImGui.PushID(e.Id + "." + p.Id);
            try
            {
                ImGui.Separator();
                if (p.Id == model.SelectedPhaseId) UiWidgets.Colored(ThemeScope.EventColor(e.Category, model.State.View.Theme == "paper"), UiText.Phase(e, p));
                else ImGui.TextUnformatted(UiText.Phase(e, p));
                if (ImGui.BeginTable("###BoundaryRows", 2, ImGuiTableFlags.SizingStretchProp))
                {
                    try
                    {
                        ImGui.TableSetupColumn("###BoundaryLabel", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3);
                        ImGui.TableSetupColumn("###BoundaryValue", ImGuiTableColumnFlags.WidthStretch);
                        void BoundaryRow(string label, CalendarBoundary boundary)
                        {
                            ImGui.TableNextRow(); ImGui.TableNextColumn(); UiWidgets.Secondary(UiText.Get(label));
                            ImGui.TableNextColumn(); UiWidgets.Wrapped(UiText.Boundary(boundary));
                        }
                        BoundaryRow(p.Point ? "PointShort" : "StartShort", p.Start);
                        if (!p.Point) BoundaryRow("EndShort", p.End);
                    }
                    finally { ImGui.EndTable(); }
                }
                if (ImGui.TreeNode(UiText.Get("ReminderOptions") + "###ReminderOptions"))
                {
                    try
                    {
                        if (p.Start.Kind != "unknown") DrawAlert(e, p, "start", p.Point ? "PointAlert" : "StartAlert");
                        if (!p.Point) DrawAlert(e, p, "end", p.End.Kind is "condition" or "unknown" ? "PendingEndAlert" : "EndAlert");
                        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("AlertsUnavailable"));
                    }
                    finally { ImGui.TreePop(); }
                }
            }
            finally { ImGui.PopID(); }
        }
        var related = e.Phases.Where(p => p.Id != focused.Id).ToArray();
        if (related.Length > 0 && ImGui.TreeNode(UiText.Get("RelatedPhases", related.Length) + "###relatedPhases"))
        {
            try
            {
                foreach (var other in related)
                {
                    if (ImGui.Selectable(UiText.Phase(e, other) + "###related." + other.Id))
                        model.Select(e, other, CalendarEngine.BoundaryDate(other.Start) ?? CalendarEngine.BoundaryDate(other.End), other.Point ? "start" : null);
                    UiWidgets.Secondary(UiText.Boundary(other.Start));
                }
            }
            finally { ImGui.TreePop(); }
        }
        if (e.IsPersonal)
        {
            if (Button("Edit", "edit." + e.Id)) OpenEditor(model.State.Entries.FirstOrDefault(x => x.Id == e.Id));
        }
        if ((!string.IsNullOrWhiteSpace(e.Note) || evidenceRows.Count > 0) && ImGui.CollapsingHeader(UiText.Get("SourceDetails") + "###SourceDetails"))
        {
            if (!string.IsNullOrWhiteSpace(e.Note)) UiWidgets.Wrapped(e.Note);
            foreach (var evidence in evidenceRows)
            {
                ImGui.Separator();
                if (evidence.Url is { } url && IsOfficialPage(url) && Button("OpenSource", "source." + evidence.Id)) model.Post(() => Util.OpenLink(url));
                if (evidence.ObservedAt is { } observed) UiWidgets.Wrapped(UiText.Get("ObservedAt", UiText.Time(observed)));
                if (!string.IsNullOrWhiteSpace(evidence.Note)) UiWidgets.Wrapped(evidence.Note);
            }
        }
    }
    private void DrawAlert(CalendarEvent e, EventPhase p, string edge, string label)
    {
        var enabled = model.HasAlert(e, p, edge);
        if (ImGui.Checkbox(UiText.Get(label) + "###alert." + edge, ref enabled)) model.ToggleAlert(e, p, edge);
    }
    private void DrawSubscriptions()
    {
        var profile = model.State.Profiles.FirstOrDefault(p => p.Id == model.State.ActiveProfileId);
        UiWidgets.Secondary(UiText.Get("ActiveProfile", profile?.Alias ?? UiText.Get("ProfileUnbound")));
        var entries = model.State.Entries.Where(e => e.ProfileId == model.State.ActiveProfileId && e.Kind is "subscription" or "retainer" or "bag")
            .OrderBy(e => CalendarEngine.BoundaryDate(e.When)).ToArray();
        if (entries.Length == 0)
        {
            ImGui.BeginChild("###EmptySubscriptionCard", new(Math.Min(ImGui.GetContentRegionAvail().X, ImGui.GetFontSize() * 40), ImGui.GetFontSize() * 8));
            try
            {
                ImGui.Spacing(); ImGui.TextUnformatted(UiText.Get("NoSubscriptions"));
                UiWidgets.Wrapped(UiText.Get("SubscriptionEmptyHelp"));
                ImGui.Spacing(); if (Button("AddSubscription", "firstSub")) OpenEditor(null, subscription: true);
            }
            finally { ImGui.EndChild(); }
            return;
        }
        if (Button("AddSubscription", "addSub")) OpenEditor(null, subscription: true);
        if (!ImGui.BeginTable("###SubscriptionRows", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;
        try
        {
            ImGui.TableSetupColumn(UiText.Get("TitleColumn"), ImGuiTableColumnFlags.WidthStretch, .32f);
            ImGui.TableSetupColumn(UiText.Get("ExpiryColumn"), ImGuiTableColumnFlags.WidthStretch, .38f);
            ImGui.TableSetupColumn(UiText.Get("RemainingColumn"), ImGuiTableColumnFlags.WidthStretch, .18f);
            ImGui.TableSetupColumn("###SubscriptionAction", ImGuiTableColumnFlags.WidthStretch, .12f);
            ImGui.TableHeadersRow();
            foreach (var entry in entries)
            {
                ImGui.PushID(entry.Id);
                try
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Title);
                    ImGui.TableNextColumn(); UiWidgets.Wrapped(UiText.Boundary(entry.When));
                    ImGui.TableNextColumn();
                    var date = CalendarEngine.BoundaryDate(entry.When);
                    var days = date?.DayNumber - model.Today.DayNumber;
                    var instant = CalendarEngine.StartInstant(entry.When);
                    var expired = instant.HasValue ? instant <= model.Now : days < 0;
                    var label = expired ? UiText.Get("Expired") : days is 0 ? UiText.Get("ExpiresToday") : days is { } remaining ? UiText.Get("DaysRemaining", remaining) : UiText.Get("UnknownDateShort");
                    UiWidgets.Colored(expired || days <= 3 ? ThemeScope.Hex(model.State.View.Theme == "paper" ? 0x946123 : 0xd8bb79) : ImGui.GetStyle().Colors[(int)ImGuiCol.Text], label);
                    ImGui.TableNextColumn(); if (Button("Edit", "edit")) OpenEditor(entry);
                }
                finally { ImGui.PopID(); }
            }
        }
        finally { ImGui.EndTable(); }
    }
    private void DrawSettings()
    {
        var view = model.State.View;
        var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 48 ? 2 : 1;
        if (ImGui.BeginTable("###GeneralSettings." + columns, columns, ImGuiTableFlags.SizingStretchProp))
        {
            try
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(UiText.Get("DisplaySettings")); ImGui.Separator();
                var korean = view.Language == "ko";
                if (ImGui.RadioButton(UiText.Get("LanguageKorean") + "###language.ko", korean)) model.UpdateView(v => v with { Language = "ko" });
                ImGui.SameLine(); if (ImGui.RadioButton(UiText.Get("LanguageEnglish") + "###language.en", !korean)) model.UpdateView(v => v with { Language = "en" });
                var light = view.Theme == "paper";
                if (ImGui.Checkbox(UiText.Get("PaperTheme") + "###theme", ref light)) model.UpdateView(v => v with { Theme = light ? "paper" : "graphite" });
                var bars = view.PeriodBars;
                if (ImGui.Checkbox(UiText.Get("PeriodBars") + "###settingsBars", ref bars)) model.SetPeriodBars(bars);
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("PeriodBarsHelp"));
                var rewards = view.ShowRewardBars;
                if (ImGui.Checkbox(UiText.Get("ShowRewardBars") + "###rewardBars", ref rewards)) model.UpdateView(v => v with { ShowRewardBars = rewards });
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("RewardBarsHelp"));
                var notices = view.ShowNoticePublications;
                if (ImGui.Checkbox(UiText.Get("ShowNoticePublications") + "###settingsNotices", ref notices)) model.UpdateView(v => v with { ShowNoticePublications = notices });
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("NoticePublicationHelp"));
                ImGui.Spacing(); ImGui.TextUnformatted(UiText.Get("NotificationSettings")); ImGui.Separator();
                var localAlerts = view.LocalChatAlerts;
                if (ImGui.Checkbox(UiText.Get("EnableLocalAlerts") + "###localAlerts", ref localAlerts)) model.UpdateView(v => v with { LocalChatAlerts = localAlerts });
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("AlertDeliveryHelp"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(UiText.Get("DataSettings")); ImGui.Separator();
                var enabled = view.RemoteUpdatesEnabled;
                if (ImGui.Checkbox(UiText.Get("RemoteEnabled") + "###remoteEnabled", ref enabled))
                    model.UpdateView(v => v with { RemoteUpdatesEnabled = enabled, CurrentUrl = BoundedFeedTransport.CurrentAddress });
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("RemoteHelp"));
                ImGui.BeginDisabled(!view.RemoteUpdatesEnabled || model.Workspace.IsBusy);
                if (Button("Refresh", "refresh")) model.Post(() => model.Workspace.Refresh(force: true));
                ImGui.EndDisabled();
                UiWidgets.Wrapped(UiText.Get("LastClientCheck", model.Workspace.LastClientCheckedAt is { } check ? UiText.Time(check) : UiText.Get("Never")));
                UiWidgets.Wrapped(UiText.Get("DataReceiveSummary", model.Feed.Events.Count));
                if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("DataNotPublished"));
                UiWidgets.Secondary(UiText.Get("SettingsAutoSave"));
            }
            finally { ImGui.EndTable(); }
        }
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader(UiText.Get("AdvancedData") + "###AdvancedData")) return;
        var example = view.ExampleMode;
        if (ImGui.Checkbox(UiText.Get("ExampleMode") + "###example", ref example)) model.Post(() => model.Workspace.LoadExample(example));
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("ExampleHelp"));
        ImGui.Separator(); ImGui.TextUnformatted(UiText.Get("FeedUrl"));
        remoteUrl = BoundedFeedTransport.CurrentAddress;
        ImGui.SetNextItemWidth(-1); ImGui.InputText("###feedUrl", ref remoteUrl, 512, ImGuiInputTextFlags.ReadOnly);
        ImGui.TextUnformatted(UiText.Get("ImportLocalFile"));
        ImGui.SetNextItemWidth(-1); ImGui.InputText("###filePath", ref importPath, 1024);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(importPath) || model.Workspace.IsBusy);
        if (Button("Import", "import")) { var path = importPath; model.Post(() => model.Workspace.ImportFile(path, allowExample: false)); }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("ImportHelp"));
        UiWidgets.Wrapped(UiText.Get("DataStatusCode", model.Workspace.StatusCode));
        UiWidgets.Wrapped(UiText.Get("FeedRevision", model.Feed.ContentRevision));
        if (model.Workspace.RemotePointer is { } pointer)
        {
            UiWidgets.Wrapped(UiText.Get("PublishedRevision", pointer.PublicationSequence, pointer.Content.Revision));
            foreach (var source in pointer.Health.Sources)
            {
                UiWidgets.Wrapped(UiText.Get("SourceHealth", source.Id, source.LastResult));
                UiWidgets.Wrapped(UiText.Get("LastSourceSuccess", source.LastSuccessAt is { } success ? UiText.Time(success) : UiText.Get("Never")));
                UiWidgets.Wrapped(UiText.Get("SourceCoverage", source.CompleteCoverage ? UiText.Get("CoverageComplete") : UiText.Get("CoverageUnknown"), source.ComparedContentRevision?.ToString(CultureInfo.InvariantCulture) ?? "—", source.PendingChanges.Count));
            }
        }
        if (ImGui.TreeNode(UiText.Get("PrivacyDetails") + "###PrivacyDetails"))
        {
            try { UiWidgets.Wrapped(UiText.Get("PrivacyHelp")); }
            finally { ImGui.TreePop(); }
        }
    }
    private void OpenEditor(PersonalEntry? entry = null, bool subscription = false)
    {
        editId = entry?.Id ?? "";
        editName = entry?.Title ?? "";
        editNote = entry?.Note ?? "";
        editKind = entry?.Kind switch { "subscription" => 1, "retainer" => 2, "bag" => 3, _ => subscription ? 1 : 0 };
        editDateOnly = entry?.When.Kind == "date";
        var date = entry == null ? model.SelectedDate : CalendarEngine.BoundaryDate(entry.When) ?? model.SelectedDate;
        editDate = UiText.Date(date);
        editTime = entry?.When.Kind == "instant" && DateTimeOffset.TryParse(entry.When.Value, out var time) ? time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm", CultureInfo.InvariantCulture) : "";
        editorError = ""; editorOpen = true; editorRequest = true;
    }
    private void DrawEditor()
    {
        var font = ImGui.GetFontSize();
        var work = ImGui.GetMainViewport().WorkSize;
        var width = Math.Clamp(font * 30, Math.Min(font * 18, work.X - 16), Math.Max(1, work.X - 16));
        ImGui.SetNextWindowSizeConstraints(new(width, 0), new(width, Math.Max(font * 12, work.Y - font * 2)));
        ImGui.SetNextWindowSize(new(width, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(UiText.Get(editId.Length == 0 ? "AddEvent" : "EditEvent") + "###DuckbillCalendar.EntryEditor", ref editorOpen,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings)) return;
        try
        {
        ImGui.TextUnformatted(UiText.Get("EntryTitle"));
        ImGui.SetNextItemWidth(-1); ImGui.InputText("###entryTitle", ref editName, 80);
        var names = new[] { "KindPersonal", "KindSubscription", "KindRetainer", "KindBag" }.Select(k => UiText.Get(k)).ToArray();
        ImGui.TextUnformatted(UiText.Get("EntryKind"));
        ImGui.SetNextItemWidth(-1); ImGui.Combo("###entryKind", ref editKind, names, names.Length);
        ImGui.TextUnformatted(UiText.Get("EntryDate"));
        ImGui.SetNextItemWidth(font * 12); ImGui.InputText("###entryDate", ref editDate, 11);
        ImGui.Checkbox(UiText.Get("DateOnly") + "###dateOnly", ref editDateOnly);
        if (!editDateOnly)
        {
            ImGui.TextUnformatted(UiText.Get("EntryTime"));
            ImGui.SetNextItemWidth(font * 8); ImGui.InputText("###entryTime", ref editTime, 6);
        }
        UiWidgets.Wrapped(UiText.Get("DateOnlyHelp"));
        ImGui.TextUnformatted(UiText.Get("EntryNote"));
        ImGui.InputTextMultiline("###entryNote", ref editNote, 400, new(-1, font * 4));
        UiWidgets.Secondary(UiText.Get("EntryScope"));
        if (editorError.Length > 0) UiWidgets.Colored(ThemeScope.Hex(0xe2a7a1), UiText.Get(editorError));
        if (Button("Save", "entrySave"))
        {
            if (TryCreateEntry(out var entry)) { model.SaveEntry(entry!); ImGui.CloseCurrentPopup(); editorOpen = false; }
        }
        ImGui.SameLine(); if (Button("Cancel", "entryCancel")) { ImGui.CloseCurrentPopup(); editorOpen = false; }
        if (editId.Length > 0)
        {
            ImGui.SameLine();
            if (Button("Delete", "entryDelete")) ImGui.OpenPopup("###DeleteEntryConfirm");
            if (ImGui.BeginPopup("###DeleteEntryConfirm"))
            {
                var confirmed = false;
                try
                {
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + font * 24);
                    try { ImGui.TextUnformatted(UiText.Get("DeleteEntryQuestion", editName)); }
                    finally { ImGui.PopTextWrapPos(); }
                    if (Button("ConfirmDelete", "confirmDelete")) { model.DeleteEntry(editId); confirmed = true; ImGui.CloseCurrentPopup(); }
                    ImGui.SameLine(); if (Button("Cancel", "keepEntry")) ImGui.CloseCurrentPopup();
                }
                finally { ImGui.EndPopup(); }
                if (confirmed) { ImGui.CloseCurrentPopup(); editorOpen = false; }
            }
        }
        }
        finally { ImGui.EndPopup(); }
    }
    private bool TryCreateEntry(out PersonalEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(editName)) { editorError = "TitleRequired"; return false; }
        if (!DateOnly.TryParseExact(editDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 2020 or > 2100)
        { editorError = "InvalidDate"; return false; }
        CalendarBoundary boundary;
        if (editDateOnly) boundary = CalendarBoundary.Date(date);
        else
        {
            if (!TimeOnly.TryParseExact(editTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) { editorError = "InvalidTime"; return false; }
            boundary = CalendarBoundary.Instant(new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(9)));
        }
        entry = new()
        {
            Id = editId.Length == 0 ? Guid.NewGuid().ToString("N") : editId,
            ProfileId = model.State.ActiveProfileId,
            Title = editName.Trim(), Kind = editKind switch { 1 => "subscription", 2 => "retainer", 3 => "bag", _ => "personal" },
            When = boundary, Note = editNote.Trim()
        };
        editorError = ""; return true;
    }
    private static bool Button(string key, string id) => ImGui.Button(UiText.Get(key) + "###" + id);
    private static bool IsOfficialPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Host is "www.ff14.co.kr" or "guide.ff14.co.kr";
    private static void Banner(string key) => UiWidgets.Colored(ThemeScope.Hex(0xd8bb79), UiText.Get(key));
}
