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
    private bool remoteEnabled;
    private bool remoteDraftLoaded;
    public Action? ToggleCompact { get; set; }

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
        if (model.Workspace.FeedSource == "bundled") UiWidgets.Wrapped(UiText.Get("BundledSnapshotInfo", UiText.Time(model.Feed.GeneratedAt)));
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
        UiWidgets.Secondary(UiText.Get("StatusLine", model.Workspace.StatusCode, model.State.Entries.Count, model.Feed.ContentRevision));
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
        if (Button("PreviousMonth", "prev")) model.ChangeMonth(-1);
        ImGui.SameLine();
        ImGui.TextUnformatted(new DateOnly(model.Year, model.MonthNumber, 1).ToString("yyyy.MM", CultureInfo.InvariantCulture));
        ImGui.SameLine(); if (Button("NextMonth", "next")) model.ChangeMonth(1);
        ImGui.SameLine(); if (Button("Today", "today")) model.GoToday();
        ImGui.SameLine(); var list = model.ListView;
        if (ImGui.Checkbox(UiText.Get("ListView") + "###ListView", ref list)) model.SetListView(list);
        ImGui.SameLine(); var bars = model.State.View.PeriodBars;
        if (ImGui.Checkbox(UiText.Get("PeriodBars") + "###PeriodBars", ref bars)) model.UpdateView(v => v with { PeriodBars = bars });
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("PeriodBarsHelp"));
        ImGui.SameLine(); if (Button("AddEvent", "add")) OpenEditor();
        DrawFilters();
        var width = ImGui.GetContentRegionAvail().X;
        var split = width >= ImGui.GetFontSize() * 53;
        if (split && ImGui.BeginTable("###CalendarSplit", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
        {
            try
            {
                ImGui.TableSetupColumn("###CalendarLeft", ImGuiTableColumnFlags.WidthStretch, 0.72f);
                ImGui.TableSetupColumn("###CalendarDetail", ImGuiTableColumnFlags.WidthStretch, 0.28f);
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                DrawCalendarContent();
                ImGui.TableNextColumn(); DrawDetail();
            }
            finally { ImGui.EndTable(); }
        }
        else
        {
            DrawCalendarContent();
            ImGui.Separator(); DrawDetail();
        }
    }
    private void DrawFilters()
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
        if (ImGui.InputText(UiText.Get("Search") + "###Search", ref search, 120)) model.SetQuery(search, category, onlyAlerts);
        ImGui.SameLine(); ImGui.SetNextItemWidth(ImGui.GetFontSize() * 11);
        var names = new[] { "All", "CategoryEvent", "CategoryPatch", "CategoryPvp", "CategorySale", "CategoryPersonal" }.Select(k => UiText.Get(k)).ToArray();
        if (ImGui.Combo("###CategoryFilter", ref category, names, names.Length)) model.SetQuery(search, category, onlyAlerts);
        ImGui.SameLine();
        if (ImGui.Checkbox(UiText.Get("OnlyAlertEvents") + "###OnlyAlerts", ref onlyAlerts)) model.SetQuery(search, category, onlyAlerts);
    }
    private void DrawCalendarContent()
    {
        if (model.ListView) DrawEventList(model.MonthEvents, "month");
        else DrawMonth();
        ImGui.Separator();
        ImGui.TextUnformatted(UiText.Get("FrontlineStrip", model.TodayFrontline?.IsKnown == true ? model.TodayFrontline.Name ?? UiText.Get("UnknownField") : UiText.Get("UnknownField"), model.TomorrowFrontline?.IsKnown == true ? model.TomorrowFrontline.Name ?? UiText.Get("UnknownField") : UiText.Get("UnknownField")));
        if (ImGui.IsItemHovered()) UiWidgets.Tooltip(UiText.Get("FrontlineHelp"));
        foreach (var e in model.Events.Where(e => e.Category is "pvp-season" or "pvp-series"))
        {
            var phase = e.Phases.FirstOrDefault(p => CalendarEngine.BoundaryDate(p.Start).HasValue);
            if (phase == null) continue;
            if (ImGui.SmallButton(UiText.Get("StartJump", e.Title, UiText.Boundary(phase.Start)) + "###start." + e.Id)) model.JumpToStart(e);
        }
        if (Button("Compact", "compact")) ToggleCompact?.Invoke();
        var undated = model.FilteredEvents.Where(e => e.Phases.Any(p => !p.Point && p.End.Kind is "condition" or "unknown")).ToArray();
        if (undated.Length > 0 && ImGui.CollapsingHeader(UiText.Get("Undated", undated.Length) + "###Undated")) DrawEventList(undated, "undated");
    }
    private void DrawMonth()
    {
        if (model.Calendar == null) return;
        var available = ImGui.GetContentRegionAvail().X;
        var cellWidth = available / 7f;
        var font = ImGui.GetFontSize();
        var dateHeight = font + 7;
        var markerHeight = font + 5;
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
            var rowHeight = dateHeight + laneHeight + markerHeight * 2 + font + 4;
            for (var col = 0; col < 7; col++)
            {
                var day = week.Days[col];
                var pos = origin + new Vector2(col * cellWidth, 0);
                var end = pos + new Vector2(cellWidth, rowHeight);
                var bg = day.InMonth ? (light ? 0xfaf9f4 : 0x202426) : (light ? 0xe5e5dd : 0x171a1c);
                draw.AddRectFilled(pos, end, ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(bg)));
                draw.AddRect(pos, end, ImGui.GetColorU32(ImGuiCol.Border));
                if (day.Date == model.SelectedDate) draw.AddRect(pos + Vector2.One, end - Vector2.One, ImGui.ColorConvertFloat4ToU32(ThemeScope.Hex(light ? 0x805d25 : 0xd8bb79)), 0, ImDrawFlags.None, 2);
                ImGui.SetCursorScreenPos(pos + new Vector2(3, 2));
                if (ImGui.InvisibleButton("###day." + UiText.Date(day.Date), new(cellWidth - 6, dateHeight - 3))) model.SelectDate(day.Date);
                var number = day.Date.Day.ToString(CultureInfo.InvariantCulture);
                var numberColor = day.Date == model.Today ? ThemeScope.Hex(light ? 0x805d25 : 0xd8bb79) : day.InMonth ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
                draw.AddText(pos + new Vector2(6, 3), ImGui.ColorConvertFloat4ToU32(numberColor), day.Date == model.Today ? number + " *" : number);
                var visibleItems = day.Items.Take(2).ToArray();
                for (var index = 0; index < visibleItems.Length; index++)
                {
                    var item = visibleItems[index];
                    var point = pos + new Vector2(3, dateHeight + laneHeight + markerHeight * index);
                    DrawCalendarButton(point, new(cellWidth - 6, markerHeight - 2), item.Event, item.Phase,
                        "point." + UiText.Date(day.Date) + "." + index, MarkerLabel(item), () => model.Select(item.Event, item.Phase, day.Date));
                }
                var more = day.HiddenCount + Math.Max(0, day.Items.Count - 2);
                if (more > 0)
                {
                    ImGui.SetCursorScreenPos(pos + new Vector2(3, rowHeight - font - 2));
                    if (ImGui.SmallButton(UiText.Get("MoreCount", more) + "###more." + UiText.Date(day.Date))) model.SelectDate(day.Date);
                }
            }
            foreach (var bar in week.Bars)
            {
                var pos = origin + new Vector2(bar.StartColumn * cellWidth + 2, dateHeight + bar.Lane * markerHeight);
                var size = new Vector2((bar.EndColumn - bar.StartColumn + 1) * cellWidth - 4, markerHeight - 2);
                var title = (bar.ContinuesBefore ? "< " : "") + bar.Event.Title + " · " + UiText.Phase(bar.Event, bar.Phase) + (bar.ContinuesAfter ? " >" : "");
                DrawCalendarButton(pos, size, bar.Event, bar.Phase, "bar." + UiText.Date(week.StartsOn) + "." + bar.Event.Id + "." + bar.Phase.Id, title,
                    () => model.Select(bar.Event, bar.Phase, week.StartsOn.AddDays(bar.StartColumn)));
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
        var color = ThemeScope.EventColor(e.Category, model.State.View.Theme == "paper");
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(color with { W = hovered ? .28f : .14f }), 2);
        draw.AddLine(pos, pos + new Vector2(0, size.Y), ImGui.ColorConvertFloat4ToU32(color), 2);
        draw.PushClipRect(pos, pos + size, true);
        draw.AddText(pos + new Vector2(4, 0), ImGui.ColorConvertFloat4ToU32(color), label);
        draw.PopClipRect();
        if (hovered) UiWidgets.Tooltip(e.Title + "\n" + UiText.Phase(e, phase) + "\n" + UiText.Boundary(phase.Start) + " — " + UiText.Boundary(phase.End));
    }
    private static string MarkerLabel(CalendarItem item) => item.Event.Title + " · " + UiText.Get(item.Phase.Point ? "PointShort" : item.Edge == "start" ? "StartShort" : "EndShort");
    private void DrawEventList(IReadOnlyList<CalendarEvent> list, string scope)
    {
        if (list.Count == 0) { UiWidgets.Secondary(UiText.Get("NoResults")); return; }
        foreach (var e in list)
        {
            if (ImGui.Selectable(e.Title + "###" + scope + "." + e.Id, model.SelectedEventId == e.Id)) model.Select(e);
            UiWidgets.Secondary(UiText.Category(e.Category));
        }
    }
    private void DrawNews()
    {
        DrawFilters();
        if (ImGui.BeginTable("###NewsSplit", 2, ImGuiTableFlags.Resizable))
        {
            try
            {
                ImGui.TableNextColumn(); DrawEventList(model.FilteredEvents.Where(e => !e.IsPersonal).ToArray(), "news");
                ImGui.TableNextColumn(); DrawDetail();
            }
            finally { ImGui.EndTable(); }
        }
    }
    private void DrawDetail()
    {
        ImGui.TextUnformatted(UiText.Get("SelectedDate", UiText.Date(model.SelectedDate)));
        ImGui.Separator();
        foreach (var item in model.DayItems)
            if (ImGui.Selectable(item.Event.Title + " · " + UiText.Phase(item.Event, item.Phase) + "###dayitem." + item.Event.Id + "." + item.Phase.Id + "." + item.Edge,
                item.Event.Id == model.SelectedEventId && item.Phase.Id == model.SelectedPhaseId)) model.Select(item.Event, item.Phase);
        var e = model.SelectedEvent;
        if (e == null) { UiWidgets.Wrapped(UiText.Get("SelectEvent")); return; }
        ImGui.Separator();
        UiWidgets.Wrapped(e.Title);
        if (model.SelectionFiltered) UiWidgets.Wrapped(UiText.Get("SelectionFiltered"));
        UiWidgets.Secondary(e.IsPersonal ? UiText.Get("ManualLocal") : UiText.Get(model.State.View.ExampleMode ? "ExampleRecord" : "PublicRecord"));
        if (!string.IsNullOrWhiteSpace(e.Note)) UiWidgets.Wrapped(e.Note);
        foreach (var p in e.Phases)
        {
            ImGui.PushID(e.Id + "." + p.Id);
            ImGui.Separator();
            if (p.Id == model.SelectedPhaseId) UiWidgets.Colored(ThemeScope.EventColor(e.Category, model.State.View.Theme == "paper"), UiText.Get("SelectedPhase", UiText.Phase(e, p)));
            else ImGui.TextUnformatted(UiText.Phase(e, p));
            if (p.Point) UiWidgets.Wrapped(UiText.Get("AtValue", UiText.Boundary(p.Start)));
            else
            {
                if (p.Start.Kind != "unknown") UiWidgets.Wrapped(UiText.Get("StartValue", UiText.Boundary(p.Start)));
                UiWidgets.Wrapped(UiText.Get("EndValue", UiText.Boundary(p.End)));
            }
            if (p.Start.Kind != "unknown") DrawAlert(e, p, "start", p.Point ? "PointAlert" : "StartAlert");
            if (!p.Point) DrawAlert(e, p, "end", p.End.Kind is "condition" or "unknown" ? "PendingEndAlert" : "EndAlert");
            ImGui.PopID();
        }
        UiWidgets.Wrapped(UiText.Get("AlertsUnavailable"));
        if (e.IsPersonal)
        {
            if (Button("Edit", "edit." + e.Id)) OpenEditor(model.State.Entries.FirstOrDefault(x => x.Id == e.Id));
        }
        else
        {
            var evidenceIds = e.Phases.SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds)).Distinct().ToHashSet(StringComparer.Ordinal);
            foreach (var evidence in model.Feed.Evidence.Where(x => evidenceIds.Contains(x.Id)))
            {
                UiWidgets.Wrapped(UiText.Get("SourceValue", evidence.Url ?? evidence.SourceId));
                if (evidence.Url is { } url && IsOfficialPage(url) && Button("OpenSource", "source." + evidence.Id))
                    model.Post(() => Util.OpenLink(url));
                if (evidence.ObservedAt is { } observed) UiWidgets.Secondary(UiText.Get("ObservedAt", UiText.Time(observed)));
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
        UiWidgets.Wrapped(UiText.Get("SubscriptionHelp"));
        if (Button("AddSubscription", "addSub")) OpenEditor(null, subscription: true);
        var profile = model.State.Profiles.FirstOrDefault(p => p.Id == model.State.ActiveProfileId);
        UiWidgets.Secondary(UiText.Get("ActiveProfile", profile?.Alias ?? UiText.Get("ProfileUnbound")));
        var entries = model.State.Entries.Where(e => e.ProfileId == model.State.ActiveProfileId && e.Kind is "subscription" or "retainer" or "bag").ToArray();
        if (entries.Length == 0) UiWidgets.Secondary(UiText.Get("NoSubscriptions"));
        foreach (var entry in entries)
        {
            ImGui.PushID(entry.Id); ImGui.Separator();
            ImGui.TextUnformatted(entry.Title);
            UiWidgets.Wrapped(UiText.Get("ExpiresValue", UiText.Boundary(entry.When)));
            if (Button("Edit", "edit")) OpenEditor(entry);
            ImGui.PopID();
        }
    }
    private void DrawSettings()
    {
        var view = model.State.View;
        ImGui.TextUnformatted(UiText.Get("DisplaySettings"));
        var korean = view.Language == "ko";
        if (ImGui.RadioButton(UiText.Get("LanguageKorean") + "###language.ko", korean)) model.UpdateView(v => v with { Language = "ko" });
        ImGui.SameLine(); if (ImGui.RadioButton(UiText.Get("LanguageEnglish") + "###language.en", !korean)) model.UpdateView(v => v with { Language = "en" });
        var light = view.Theme == "paper";
        if (ImGui.Checkbox(UiText.Get("PaperTheme") + "###theme", ref light)) model.UpdateView(v => v with { Theme = light ? "paper" : "graphite" });
        var rewards = view.ShowRewardBars;
        if (ImGui.Checkbox(UiText.Get("ShowRewardBars") + "###rewardBars", ref rewards)) model.UpdateView(v => v with { ShowRewardBars = rewards });
        UiWidgets.Wrapped(UiText.Get("RewardBarsHelp"));
        var localAlerts = view.LocalChatAlerts;
        if (ImGui.Checkbox(UiText.Get("EnableLocalAlerts") + "###localAlerts", ref localAlerts))
            model.UpdateView(v => v with { LocalChatAlerts = localAlerts });
        UiWidgets.Wrapped(UiText.Get("AlertDeliveryHelp"));
        ImGui.Separator();
        var example = view.ExampleMode;
        if (ImGui.Checkbox(UiText.Get("ExampleMode") + "###example", ref example)) model.Post(() => model.Workspace.LoadExample(example));
        UiWidgets.Wrapped(UiText.Get("ExampleHelp"));
        ImGui.Separator();
        ImGui.TextUnformatted(UiText.Get("DataSettings"));
        UiWidgets.Wrapped(UiText.Get("DataNotPublished"));
        if (!remoteDraftLoaded)
        {
            remoteUrl = string.IsNullOrWhiteSpace(view.CurrentUrl) ? BoundedFeedTransport.CurrentAddress : view.CurrentUrl;
            remoteEnabled = view.RemoteUpdatesEnabled; remoteDraftLoaded = true;
        }
        ImGui.Checkbox(UiText.Get("RemoteEnabled") + "###remoteEnabled", ref remoteEnabled);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(UiText.Get("FeedUrl") + "###feedUrl", ref remoteUrl, 512);
        UiWidgets.Wrapped(UiText.Get("RemoteHelp"));
        if (Button("SaveDataSettings", "saveRemote"))
        {
            if (!remoteEnabled || CalendarModel.IsPublishedEndpoint(remoteUrl.Trim()))
            {
                var url = remoteUrl.Trim(); var enabled = remoteEnabled;
                model.UpdateView(v => v with { CurrentUrl = url, RemoteUpdatesEnabled = enabled });
                editorError = "";
            }
            else editorError = "InvalidFeedUrl";
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!view.RemoteUpdatesEnabled || !CalendarModel.IsPublishedEndpoint(view.CurrentUrl) || model.Workspace.IsBusy);
        if (Button("Refresh", "refresh")) model.Post(() => model.Workspace.Refresh(force: true));
        ImGui.EndDisabled();
        if (editorError == "InvalidFeedUrl") UiWidgets.Wrapped(UiText.Get(editorError));
        ImGui.Separator();
        ImGui.TextUnformatted(UiText.Get("ImportLocalFile"));
        ImGui.SetNextItemWidth(-1); ImGui.InputText(UiText.Get("FilePath") + "###filePath", ref importPath, 1024);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(importPath) || model.Workspace.IsBusy);
        if (Button("Import", "import")) { var path = importPath; model.Post(() => model.Workspace.ImportFile(path, allowExample: false)); }
        ImGui.EndDisabled();
        UiWidgets.Wrapped(UiText.Get("ImportHelp"));
        ImGui.Separator();
        UiWidgets.Wrapped(UiText.Get("DataStatusCode", model.Workspace.StatusCode));
        UiWidgets.Wrapped(UiText.Get("FeedRevision", model.Feed.ContentRevision));
        UiWidgets.Secondary(UiText.Get("LastClientCheck", model.Workspace.LastClientCheckedAt is { } clientCheck ? UiText.Time(clientCheck) : UiText.Get("Never")));
        if (model.Workspace.RemotePointer is { } pointer)
        {
            UiWidgets.Wrapped(UiText.Get("PublishedRevision", pointer.PublicationSequence, pointer.Content.Revision));
            foreach (var source in pointer.Health.Sources)
            {
                UiWidgets.Wrapped(UiText.Get("SourceHealth", source.Id, source.LastResult));
                UiWidgets.Secondary(UiText.Get("LastSourceSuccess", source.LastSuccessAt is { } success ? UiText.Time(success) : UiText.Get("Never")));
                UiWidgets.Secondary(UiText.Get("LastSourceReview", source.LastReviewedAt is { } reviewed ? UiText.Time(reviewed) : UiText.Get("Never")));
                UiWidgets.Secondary(UiText.Get("SourceCoverage", source.CompleteCoverage ? UiText.Get("CoverageComplete") : UiText.Get("CoverageUnknown"), source.ComparedContentRevision?.ToString(CultureInfo.InvariantCulture) ?? "—", source.PendingChanges.Count));
            }
        }
        ImGui.Separator();
        UiWidgets.Wrapped(UiText.Get("AlertsUnavailable"));
        UiWidgets.Wrapped(UiText.Get("PrivacyHelp"));
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
