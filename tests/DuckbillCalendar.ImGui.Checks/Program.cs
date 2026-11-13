using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

if (args.Length == 2 && args[0] == "--inspect-plugin")
{
    var assembly = Assembly.LoadFile(Path.GetFullPath(args[1]));
    using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(args[1],".json")));
    var expectedVersion = Version.Parse(manifest.RootElement.GetProperty("AssemblyVersion").GetString()!);
    if (assembly.GetName().Version != expectedVersion) throw new InvalidDataException("Plugin version does not match its manifest");
    var releaseNotesVersion = assembly.GetType("DuckbillCalendar.Plugin.PluginReleaseNotes",true)!.GetField("CurrentVersion",BindingFlags.Static | BindingFlags.Public)!.GetRawConstantValue() as string;
    if (releaseNotesVersion != expectedVersion.ToString() || !manifest.RootElement.GetProperty("Changelog").GetString()!.StartsWith("최초 배포",StringComparison.Ordinal)) throw new InvalidDataException("Release notes and manifest do not match the release");
    using var icon = assembly.GetManifestResourceStream("DuckbillCalendar.BittyDuckbill") ?? throw new InvalidDataException("Missing minion resource");
    if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(icon)) != "7a0e1aa9403900d2def16ca56be68c4106040c822ef42b1efb12db003be0795a") throw new InvalidDataException("Minion resource hash mismatch");
    foreach (var name in new[] { "DuckbillCalendar.ExampleFeed", "DuckbillCalendar.OfficialSnapshot" })
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing feed resource: " + name);
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new InvalidDataException("Invalid feed resource: " + name);
    }
    Console.WriteLine($"Plugin artifact: manifest/assembly/release-notes version {expectedVersion}, first-release Changelog, minion SHA256 and both embedded feeds verified.");
    return;
}
if (args.Contains("--preview"))
{
    NativePreview.Run(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1060,
        args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 720,
        args.Length > 3 ? args[3] : "midnight", args.Length > 4 ? float.Parse(args[4], CultureInfo.InvariantCulture) : 1);
    return;
}
var checks = new List<string>();
void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("FAIL: " + label); checks.Add(label); }
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
var artifacts = Path.Combine(root, ".preview", "imgui-checks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(artifacts);
var example = File.ReadAllBytes(Path.Combine(root, "data/contracts/v2/revisions/000001.json"));
var bundle = File.ReadAllBytes(Path.Combine(root, "data/live/kr-calendar.snapshot.json"));
var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(9));
var rows = new List<string> { "scale,theme,width,height,canvasHeight,gridHeight,hostScroll" };
var flags = BindingFlags.Instance | BindingFlags.NonPublic;
Check(PluginReleaseNotes.Entries.Count == 1 && PluginReleaseNotes.Entries[0].Version == PluginReleaseNotes.CurrentVersion && PluginReleaseNotes.Entries[0].Title == "최초 배포","Public history starts with one initial release rather than internal development iterations");
foreach (var scale in new[] { 1f, 1.5f, 2f })
{
    unsafe
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO(); io.DisplaySize = new(1920, 1440); io.DeltaTime = 1f / 60; io.IniFilename = null; io.LogFilename = null;
            var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
            ushort[] ranges = [0x20, 0xff, 0x1100, 0x11ff, 0x2000, 0x27ff, 0x3130, 0x318f, 0xac00, 0xd7af, 0];
            fixed (ushort* glyphs = ranges)
            { io.Fonts.AddFontFromFileTTF(fontPath, 17 * scale, default, glyphs); byte* pixels; int w, h; io.Fonts.GetTexDataAsRGBA32(0, &pixels, &w, &h); }
            using var http = new HttpClient(new NoNetwork());
            using var workspace = new CalendarWorkspace(Path.Combine(artifacts, "state-" + scale), example, http, bundledFeed: bundle);
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false, null); model.Tick(now);
            var main = new MainWindow(model);
            void Field(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(main, value);
            T Read<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(main)!;
            void Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(main, values);
            ImGuiWindowPtr Find(string token)
            {
                for (var i = 0; i < context.Windows.Size; i++)
                { var window = context.Windows[i]; if (window.Active && ((Marshal.PtrToStringUTF8((nint)window.Name) ?? "").Contains(token, StringComparison.Ordinal) ||
                    token == "DaySchedule" && (window.Flags & ImGuiWindowFlags.Popup) != 0 && (window.Flags & ImGuiWindowFlags.Modal) == 0)) return window; }
                return default;
            }
            void Frame(float width = 1060, float height = 720, Action? draw = null)
            {
                model.Tick(now); ImGui.NewFrame();
                using (new ThemeScope(workspace.State.View.Theme))
                {
                    ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always); ImGui.SetNextWindowSize(new(width, height), ImGuiCond.Always);
                    ImGui.Begin("Redesign check host", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
                    if (draw is null) main.Draw(); else draw();
                    ImGui.End();
                    main.DrawFloatingWindows();
                }
                ImGui.Render(); model.Tick(now);
            }
            void ClosePopups() { context.OpenPopupStack.Clear(); Field("editorOpen", false); Field("editorRequest", false); Field("dayPopupRequest", false); Field("ruleWindowOpen",false); }
            var f = 17 * scale;
            foreach (var theme in new[] { "midnight", "cloud", "graphite" })
            {
                workspace.UpdateState(workspace.State with { View = workspace.State.View with { Theme = theme } }); workspace.FlushAsync().GetAwaiter().GetResult(); model.Tick(now);
                foreach (var size in new[] { new Vector2(620, 480), new Vector2(1060, 720), new Vector2(1320, 1050) })
                {
                    model.GoToMonth(2026, 11); model.Tick(now); Field("activeTab", 0);
                    for (var i = 0; i < 4; i++) Frame(size.X, size.Y);
                    var canvas = Find("CalendarCanvas"); var grid = Find("MonthGrid"); var host = Find("Redesign check host");
                    Check(canvas.Handle != null && grid.Handle != null && canvas.Size.X > size.X * .85f, $"Calendar uses full content width at {theme}/{scale}/{size}");
                    Check(canvas.Size.Y > 0 && canvas.Pos.Y + canvas.Size.Y <= host.Pos.Y + host.Size.Y + 1, $"Calendar canvas stays within host height at {theme}/{scale}/{size}");
                    Check(Find("DetailPane").Handle == null && Find("ContextAgenda").Handle == null, $"No empty permanent detail panel at {theme}/{scale}/{size}");
                    Check(main.GridRowHeight >= f * 4.7f && main.GridCellWidth > 0, $"Date targets remain usable at {theme}/{scale}/{size}");
                    Check(main.HeaderFits, $"Month navigation and add/filter actions stay inside the host at {theme}/{scale}/{size}");
                    if (size.X - 32 > f * 43 && size.Y >= f * 18)
                        Check(main.CalendarActionsSingleRow, $"Filter and add stay on one row when there is space at {theme}/{scale}/{size}");
                    if (scale == 1 && size.Y >= 720)
                        Check(Math.Abs(grid.Pos.Y + grid.Size.Y - (canvas.Pos.Y + canvas.Size.Y)) < f * 2, $"Month fills vertical canvas without a large lower void at {theme}/{size}");
                    rows.Add(FormattableString.Invariant($"{scale},{theme},{size.X},{size.Y},{canvas.Size.Y},{grid.Size.Y},{host.ScrollMax.Y}"));
                    Field("activeTab", 1); for (var i = 0; i < 3; i++) Frame(size.X, size.Y);
                    var news = Find("NewsRows");
                    Check(news.Handle != null && news.Size.X > size.X * .85f, $"News uses full-width direct-link table at {theme}/{scale}/{size}");
                    Check(main.NewsFiltersFit, $"News search/category controls wrap inside the host at {theme}/{scale}/{size}");
                    Field("activeTab", 2); for (var i = 0; i < 3; i++) Frame(size.X, size.Y);
                    Check(Find("SettingsScroll").Handle != null && Find("EmptySubscriptionCard").Handle == null && main.AppearanceIconTargets.Count == 8, $"Settings expose all eight category styles without expanding a section or an empty subscription card at {theme}/{scale}/{size}");
                    Field("appearanceSettingsRequest", true); for (var i = 0; i < 3; i++) Frame(size.X, size.Y);
                    Check(main.AppearanceRowsFit, $"Category icon and color controls stay inside settings at {theme}/{scale}/{size}");
                }
            }
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { Theme = "midnight" } }); workspace.FlushAsync().GetAwaiter().GetResult(); model.Tick(now);
            Field("activeTab", 0); model.GoToMonth(2027, 2); model.Tick(now);
            Frame(1320, 1050); Frame(1320, 1050);
            var first = model.Calendar!.Weeks[0].StartsOn;
            var date = new DateOnly(2027, 2, 19); var dayIndex = date.DayNumber - first.DayNumber;
            var point = main.GridOrigin + new Vector2((dayIndex % 7 + .5f) * main.GridCellWidth, f * 1.8f + (dayIndex / 7 + .6f) * main.GridRowHeight);
            void Click(Vector2 p, bool pressed, float width = 1320, float height = 1050)
            { io.AddMousePosEvent(p.X, p.Y); io.AddMouseButtonEvent(0, pressed); Frame(width, height); }
            Click(point, false); Click(point, true); Click(point, false);
            Check(model.SelectedDate == date && model.DayBoundaryItems.Count == 3 && model.SelectedEventId is null, $"Date click shows all three February 19 schedules without selecting an arbitrary detail at {scale}x");
            Check(Find("DaySchedule").Handle != null, $"Date click opens a bounded day popup at {scale}x");
            var popup = Find("DaySchedule");
            Check(popup.Size.X <= io.DisplaySize.X && popup.Size.Y <= io.DisplaySize.Y, $"Day popup fits viewport at {scale}x");
            ClosePopups();
            var markerPoint = main.GridOrigin + new Vector2(dayIndex % 7 * main.GridCellWidth + 25, f * 1.8f + dayIndex / 7 * main.GridRowHeight + f * 2.3f);
            Click(markerPoint, false); Click(markerPoint, true); Click(markerPoint, false);
            Check(model.PanelMode == CalendarPanelMode.Day && model.SelectedDate == date && model.SelectedEventId is null, $"Marker opens the same complete date popup at {scale}x");
            ClosePopups();
            Frame(1320, 1400); Frame(1320, 1400);
            var adjacent = new DateOnly(2027, 3, 1); var adjacentIndex = adjacent.DayNumber - first.DayNumber;
            var adjacentPoint = main.GridOrigin + new Vector2((adjacentIndex % 7 + .5f) * main.GridCellWidth, f * 1.8f + (adjacentIndex / 7 + .6f) * main.GridRowHeight);
            Click(adjacentPoint, false, 1320, 1400); Click(adjacentPoint, true, 1320, 1400); Click(adjacentPoint, false, 1320, 1400);
            Check(model.MonthNumber == 3 && model.SelectedDate == adjacent, $"Clicking an adjacent month date synchronizes the month heading at {scale}x");
            ClosePopups();
            model.GoToMonth(2027, 2); model.Tick(now); Frame(1320, 1050);
            var hover = main.GridOrigin + new Vector2(30, f * 3);
            void Wheel(float delta, bool control = false)
            { io.AddMousePosEvent(hover.X, hover.Y); io.AddMouseWheelEvent(0, delta); io.KeyCtrl = control; Frame(1320, 1050); io.KeyCtrl = false; }
            Wheel(0); Wheel(1);
            Check(model.MonthNumber == 1, $"Wheel up moves to previous month at {scale}x");
            Wheel(-1); Check(model.MonthNumber == 2, $"Wheel down moves to next month at {scale}x");
            Wheel(1, true); Check(model.MonthNumber == 2, $"Modifier wheel does not change month at {scale}x");
            Call("OpenDay", date); Frame(1320, 1050); Wheel(1);
            Check(model.MonthNumber == 2, $"Open day popup blocks wheel navigation behind it at {scale}x"); ClosePopups();
            Wheel(0); Wheel(0);
            Wheel(.4f); Wheel(.4f); Check(model.MonthNumber == 2, $"Fractional wheel does not jump before a full notch at {scale}x");
            Wheel(.3f); Check(model.MonthNumber == 1, $"Fractional wheel accumulates one month at {scale}x");
            io.AddMousePosEvent(1800, 1400); io.AddMouseWheelEvent(0, 1); Frame(1320, 1050);
            Check(model.MonthNumber == 1, $"Wheel outside calendar does not navigate at {scale}x");
            model.GoToMonth(2020, 1); model.Tick(now); Frame(1320, 1050); Wheel(0); Wheel(12);
            Check(model.Year == 2020 && model.MonthNumber == 1, $"Wheel clamps at lower supported year at {scale}x");
            model.GoToMonth(2100, 12); model.Tick(now); Frame(1320, 1050); Wheel(-12);
            Check(model.Year == 2100 && model.MonthNumber == 12, $"Wheel clamps at upper supported year at {scale}x");
            model.GoToMonth(2026, 8); model.Tick(now); Frame(1320, 1050);
            Check(model.Calendar!.Weeks.Count == 6 && main.GridRowHeight >= f * 4.7f, $"Six-week month retains all dates and usable row targets at {scale}x");
            // Date is selected with the actual mini-calendar; time retains native keyboard input.
            Field("activeTab", 0); Call("OpenEditor", null, false); Field("editDateOnly", false); Field("editDate", "2026-11-01");
            for (var i = 0; i < 4; i++) Frame(1320, 1050);
            var editor = Find("DuckbillCalendar.EntryEditor"); var widths = new List<float>();
            for (var i = 0; i < 30; i++) { Frame(1320, 1050); widths.Add(Find("DuckbillCalendar.EntryEditor").Size.X); }
            Check(widths.Max() - widths.Min() < .5f && widths[0] <= io.DisplaySize.X, $"Editor width is stable and bounded over 30 frames at {scale}x");
            var dateButton = main.DatePickerButtonCenter;
            Click(dateButton, false); Click(dateButton, true); Click(dateButton, false); Frame(1320, 1050);
            Check(main.DatePickerTargets.ContainsKey(new(2026, 11, 12)), $"Date picker exposes actual month dates at {scale}x");
            var dateChoice = main.DatePickerTargets[new(2026, 11, 12)];
            Click(dateChoice, false); Click(dateChoice, true); Click(dateChoice, false);
            Check(Read<string>("editDate") == "2026-11-12", $"Native calendar selection sets the complete date without typing at {scale}x");
            foreach (var entry in new[] { ("editTime", "###entryTime", "23:59") })
            {
                Field(entry.Item1, ""); editor = Find("EditorBody");
                Frame(1320, 1050, () =>
                {
                    context.NavWindow = editor; context.NavActivateId = editor.GetID(entry.Item2); context.NavActivateInputId = context.NavActivateId; context.NavActivateFlags = ImGuiActivateFlags.PreferInput;
                    main.Draw();
                });
                foreach (var c in entry.Item3) io.AddInputCharacter(c); Frame(1320, 1050); Frame(1320, 1050);
                Check(Read<string>(entry.Item1) == entry.Item3, $"Native editor accepts complete {entry.Item1} at {scale}x (actual={Read<string>(entry.Item1)})");
            }
            Field("editName", "공대 출발");
            object?[] created = [null]; var saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry { Title: "공대 출발", Kind: "personal", When.Kind: "instant" }, $"Personal editor preserves Korean title and exact date/time at {scale}x");
            ClosePopups();
            var party = new PersonalEntry { Id = "native-party", Title = "공대 출발", Kind = "personal", When = CalendarBoundary.Instant(new(2026, 11, 15, 18, 0, 0, TimeSpan.FromHours(9))) };
            model.SaveEntry(party); model.Tick(now); Call("OpenDay", new DateOnly(2026, 11, 15));
            for (var i = 0; i < 4; i++) Frame(1320, 1050);
            Check(!main.OngoingSectionExpanded && model.DayDisplayEntries.Count(e => !e.Ongoing) == 1 && model.DayDisplayEntries.Count(e => e.Ongoing) == 3, $"Personal day defaults to its own plan with three collapsed ongoing groups at {scale}x");
            var toggle = main.OngoingToggleCenter;
            Click(toggle, false); Click(toggle, true); Click(toggle, false);
            Check(main.OngoingSectionExpanded, $"Ongoing events expand in their secondary section at {scale}x");
            ClosePopups(); Call("OpenDay", new DateOnly(2026, 11, 10));
            for (var i = 0; i < 4; i++) Frame(1320, 1050);
            Check(!main.OngoingSectionExpanded, $"Another date restores the collapsed ongoing default at {scale}x");
            var seasonEntry = model.DayDisplayEntries.Single(e => e.Category == "pvp-season");
            var menuTarget = Vector2.Zero;
            Check(main.HideItemTargets.ContainsKey(seasonEntry.Primary.Event.Id), $"Each card exposes its own direct hide action at {scale}x");
            var hideTarget = main.HideItemTargets[seasonEntry.Primary.Event.Id];
            Click(hideTarget, false); Click(hideTarget, true); Click(hideTarget, false);
            Check(model.State.View.HiddenPhases.Any(p => p.EventId == seasonEntry.Primary.Event.Id && p.PhaseId == seasonEntry.Primary.Phase.Id) &&
                !model.State.View.HiddenEventIds.Contains(seasonEntry.Primary.Event.Id) && model.DayDisplayEntries.All(e => e.Members.All(i => i.Event.Id != seasonEntry.Primary.Event.Id)), $"Native single hide records the selected phase instead of the whole event at {scale}x");
            model.RestoreHiddenEvents(); model.Tick(now); ClosePopups();
            const string tournamentId = "kr:curated:ccrc-2027-korea";
            Call("OpenDay", new DateOnly(2026, 11, 10)); for (var i = 0; i < 4; i++) Frame(1320, 1050);
            var tournamentEntry = model.DayDisplayEntries.Single(e => e.Primary.Event.Id == tournamentId);
            hideTarget = main.HideItemTargets[tournamentId]; Click(hideTarget, false); Click(hideTarget, true); Click(hideTarget, false);
            Check(model.CalendarEvents.Single(e => e.Id == tournamentId).Phases.Count == 3 && model.State.View.Calendars.Competitions,
                $"Native single hide keeps other tournament stages and competition visibility at {scale}x");
            model.SetPhaseHidden(tournamentId, "selection", false); model.Tick(now); ClosePopups();
            Call("OpenDay", new DateOnly(2026, 11, 10)); for (var i = 0; i < 4; i++) Frame(1320, 1050);
            tournamentEntry = model.DayDisplayEntries.Single(e => e.Primary.Event.Id == tournamentId);
            menuTarget = main.CardMenuTargets[tournamentEntry.Key]; Click(menuTarget, false); Click(menuTarget, true); Click(menuTarget, false); Frame(1320, 1050);
            var relatedHide = main.RelatedHideTargets[tournamentEntry.Key]; Click(relatedHide, false); Click(relatedHide, true); Click(relatedHide, false);
            Check(model.State.View.HiddenEventIds.Contains(tournamentId) && model.CalendarEvents.All(e => e.Id != tournamentId) && model.State.View.Calendars.Competitions && model.CalendarEvents.Any(e => e.Category == "patch"),
                $"Native whole-event hide keeps classification and unrelated schedules at {scale}x");
            model.RestoreHiddenEvents(); model.Tick(now); ClosePopups();
            ClosePopups(); Call("OpenEditor", null, true); Field("editName", ""); Field("chargeDays", 90);
            for (var i = 0; i < 4; i++) Frame(1320, 1050);
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry { Title: "정액제", Kind: "subscription", When.Value: "2027-02-11" }, $"Subscription form needs no name and calculates first 90-day expiry at {scale}x");
            Field("chargeDays", 0); created = [null];
            Check(!(bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!, $"Subscription form rejects zero-day charge at {scale}x");
            Field("chargeDays", 0); Field("chargeHours", 0);
            Frame(1320, 1050); Frame(1320, 1050);
            var chargeEditor = Find("EditorBody");
            Frame(1320, 1050, () =>
            {
                context.NavWindow = chargeEditor;
                context.NavActivateId = chargeEditor.GetID("###chargeHours"); context.NavActivateInputId = context.NavActivateId; context.NavActivateFlags = ImGuiActivateFlags.PreferInput;
                main.Draw();
            });
            io.AddInputCharacter('6'); Frame(1320, 1050); Frame(1320, 1050);
            io.AddKeyEvent(ImGuiKey.Enter, true); Frame(1320, 1050); io.AddKeyEvent(ImGuiKey.Enter, false); Frame(1320, 1050);
            Check(Read<int>("chargeHours") == 6, $"Renewal duration accepts hours through native keyboard input at {scale}x");
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry timedRenewal && CalendarEngine.StartInstant(timedRenewal.When) ==
                now.AddHours(6), $"Renewal form supports zero days plus six hours at {scale}x");
            Field("chargeDays", 30); created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry mixedRenewal && CalendarEngine.StartInstant(mixedRenewal.When) == now.AddDays(30).AddHours(6), $"Renewal form adds day and hour quantities together at {scale}x");
            Field("chargeDays", 0); Field("chargeHours", 0); Field("chargeMinutes", 0);
            Frame(1320, 1050); Frame(1320, 1050); chargeEditor = Find("EditorBody");
            Frame(1320, 1050, () =>
            {
                context.NavWindow = chargeEditor; context.NavActivateId = chargeEditor.GetID("###chargeMinutes");
                context.NavActivateInputId = context.NavActivateId; context.NavActivateFlags = ImGuiActivateFlags.PreferInput; main.Draw();
            });
            foreach (var c in "30") io.AddInputCharacter(c); Frame(1320, 1050); Frame(1320, 1050);
            io.AddKeyEvent(ImGuiKey.Enter, true); Frame(1320, 1050); io.AddKeyEvent(ImGuiKey.Enter, false); Frame(1320, 1050);
            Check(Read<int>("chargeMinutes") == 30, $"Minute quantity accepts native input at {scale}x");
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry minuteRenewal && CalendarEngine.StartInstant(minuteRenewal.When) == now.AddMinutes(30), $"Renewal form supports thirty minutes with zero days and hours at {scale}x");
            Field("chargeDays", 30); Field("chargeHours", 6); Field("chargeMinutes", 15); created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry fullRenewal && CalendarEngine.StartInstant(fullRenewal.When) == now.AddDays(30).AddHours(6).AddMinutes(15), $"Renewal form sums all three duration fields at {scale}x");
            Field("chargeMinutes", 0);
            Field("chargeHours", -1); created = [null];
            Check(!(bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!, $"Renewal form rejects negative hour quantities at {scale}x");
            var dateOnlyService = new PersonalEntry { Id = "hourly-service", Kind = "subscription", Title = "정액제", When = CalendarBoundary.Date(new(2026, 11, 20)) };
            model.SaveSubscription(dateOnlyService); model.Tick(now); Field("chargeDays", 0); Field("chargeHours", 6); Field("chargeBasisTime", ""); created = [null];
            Check(!(bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!, $"Hourly renewal asks for the missing existing expiry clock instead of guessing it at {scale}x");
            Frame(1320, 1050); Frame(1320, 1050); chargeEditor = Find("EditorBody");
            Frame(1320, 1050, () =>
            {
                context.NavWindow = chargeEditor; context.NavActivateId = chargeEditor.GetID("###chargeBasisTime");
                context.NavActivateInputId = context.NavActivateId; context.NavActivateFlags = ImGuiActivateFlags.PreferInput; main.Draw();
            });
            foreach (var c in "21:30") io.AddInputCharacter(c); Frame(1320, 1050); Frame(1320, 1050);
            Check(Read<string>("chargeBasisTime") == "21:30", $"The existing expiry clock accepts native text input at {scale}x");
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry hourlyRenewal && CalendarEngine.StartInstant(hourlyRenewal.When) ==
                new DateTimeOffset(2026, 11, 21, 3, 30, 0, TimeSpan.FromHours(9)), $"Six-hour renewal carries an existing 21:30 expiry into the next date at {scale}x");
            model.DeleteEntry(dateOnlyService.Id); model.Tick(now);
            Field("chargeHours", 0);
            Field("chargeMode", false); Field("editDate", "2026-11-13");
            Frame(1320, 1050); Frame(1320, 1050); dateButton = main.DatePickerButtonCenter;
            Click(dateButton, false); Click(dateButton, true); Click(dateButton, false); Frame(1320, 1050);
            var nextMonth = main.PickerNextMonthTarget; Click(nextMonth, false); Click(nextMonth, true); Click(nextMonth, false); Frame(1320, 1050);
            Check(main.DatePickerTargets.ContainsKey(new(2026, 12, 31)), $"Subscription date picker navigates to the next month at {scale}x");
            dateChoice = main.DatePickerTargets[new(2026, 12, 31)]; Click(dateChoice, false); Click(dateChoice, true); Click(dateChoice, false);
            Check(Read<string>("editDate") == "2026-12-31", $"Subscription expiry uses calendar selection at {scale}x");
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry", flags)!.Invoke(main, created)!;
            Check(saved && created[0] is PersonalEntry { Title: "정액제", When.Value: "2026-12-31" }, $"Selected expiry saves without a manually typed date at {scale}x");
            ClosePopups();
            // The summary and grid setting are independent, using an explicit dated provider fixture.
            using var fieldHttp = new HttpClient(new FieldFixture(File.ReadAllBytes(Path.Combine(root, "tests/DuckbillCalendar.Infrastructure.Checks/Fixtures/frontline-2026-11.json"))));
            using var fields = new FrontlineScheduleClient(fieldHttp);
            fields.Tick(now, true, 2026, 11); fields.BackgroundTask.GetAwaiter().GetResult(); fields.Tick(now, true, 2026, 11);
            var fieldMain = new MainWindow(model, fields);
            model.GoToMonth(2026, 11); model.UpdateView(v => v with { ShowFrontlineInCalendar = false }); model.Tick(now);
            foreach (var size in new[] { new Vector2(620, 480), new Vector2(1320, 1050) })
            {
                for (var i = 0; i < 3; i++) Frame(size.X, size.Y, fieldMain.Draw);
                Check(fieldMain.FrontlineSummaryFits && fieldMain.FrontlineCellLabelCount == 0,
                    $"Today/tomorrow summary fits while date cells stay clean at {scale}x/{size}");
            }
            model.UpdateView(v => v with { ShowFrontlineInCalendar = true }); model.Tick(now);
            for (var i = 0; i < 3; i++) Frame(1800, 1050, fieldMain.Draw);
            Check(fieldMain.FrontlineCellLabelCount > 20 && fieldMain.FrontlineSummaryFits, $"Opt-in date labels coexist with the summary at {scale}x");
            model.UpdateView(v => v with { ShowFrontlineInCalendar = false }); model.Tick(now);
            var standardEvent = CalendarAppearanceStyle.Resolve(model.State.View, "web-event");
            Check(standardEvent == CalendarAppearanceStyle.Resolve(model.State.View, "in-game-event"), $"The event legend matches web and game event styling at {scale}x");
            var customStyle = new CalendarAppearance { Group = CalendarGroup.Pvp, Icon = "Trophy", Color = "#33AA99" };
            Call("SetCalendarAppearance", customStyle); model.Tick(now);
            var styled = CalendarAppearanceStyle.Resolve(model.State.View, "pvp-season");
            Check(styled.Icon == UiIcon.Trophy && styled.Color == ThemeScope.Hex(0x33aa99), $"Saved category customization resolves to the same icon and color used by markers, cards and legend at {scale}x");
            model.UpdateView(v => v with { CalendarAppearances = [] }); model.Tick(now);
            Check(CalendarAppearanceStyle.Resolve(model.State.View, "pvp-season").Icon == UiIcon.Flag, $"Restoring defaults resets the category icon at {scale}x");
            ClosePopups(); Field("activeTab", 2); Field("appearanceSettingsRequest", true);
            workspace.FlushAsync().GetAwaiter().GetResult(); model.Tick(now);
            for (var i = 0; i < 4; i++) Frame(1320, 1050);
            var eventColorBefore = CalendarAppearanceStyle.Resolve(model.State.View, "web-event").Color;
            var iconTarget = main.AppearanceIconTargets[CalendarGroup.Events]; Click(iconTarget, false); Click(iconTarget, true); Click(iconTarget, false); Frame(1320, 1050);
            // ImGui auto-fits/repositions a newly opened combo over its first frames.
            // Read the choice target after layout settles, rather than clicking stale geometry.
            for (var settle = 0; settle < 3; settle++) Frame(1320,1050);
            if (!main.AppearanceIconChoiceTargets.TryGetValue(UiIcon.Update,out var updateIconChoice)) throw new InvalidOperationException($"Icon combo did not open at {scale}x target={iconTarget}, current={main.AppearanceIconTargets[CalendarGroup.Events]}, mouse={io.MousePos}, activeId={context.ActiveId}");
            Click(updateIconChoice, false); Click(updateIconChoice, true); Click(updateIconChoice, false); Frame(1320, 1050);
            Check(CalendarAppearanceStyle.Resolve(model.State.View, "web-event").Icon == UiIcon.Update, $"Selecting an illustrated icon option updates the actual category appearance at {scale}x");
            Check(CalendarAppearanceStyle.Resolve(model.State.View, "web-event").Color == eventColorBefore, $"Changing an icon preserves its category color at {scale}x");
            model.UpdateView(v => v with { CalendarAppearances = [] }); model.Tick(now); ClosePopups();
            UiText.SetLanguage("en"); Check(UiText.Get("Calendar") == "달력", $"Legacy English setting still renders Korean at {scale}x");
            Check(UiText.SubscriptionRemaining(CalendarBoundary.Instant(now.AddHours(6)), now) == "6시간 남음", $"An hourly subscription shows its actual remaining hours at {scale}x");
            Check(UiText.SubscriptionRemaining(CalendarBoundary.Instant(now.AddMinutes(-1)), now) == "만료됨", $"An expiry earlier today is expired rather than still described as upcoming at {scale}x");
            Check(UiText.SubscriptionRemaining(CalendarBoundary.Date(model.Today), now) == "오늘 만료 · 시각 미등록", $"A date-only expiry does not pretend its remaining hours are known at {scale}x");
            var beforePadding = ImGui.GetStyle().FramePadding;
            using (new ThemeScope("cloud")) { Check(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].X > .8f, $"Light theme owns its background at {scale}x"); }
            Check(ImGui.GetStyle().FramePadding == beforePadding, $"Theme scope restores caller style at {scale}x");
            ClosePopups(); io.DisplaySize = new(620, 480); Call("OpenEditor", null, true);
            for (var i = 0; i < 4; i++) Frame(620, 480);
            Check(main.EditorSaveVisible && main.EditorSaveMax.Y <= 480, $"Small viewport keeps charge/save action visible outside the scrolling form at {scale}x (visible={main.EditorSaveVisible}, max={main.EditorSaveMax}, modalPos={Find("DuckbillCalendar.EntryEditor").Pos}, modalSize={Find("DuckbillCalendar.EntryEditor").Size})");
            Check(Find("DuckbillCalendar.EntryEditor").Size.X <= 620, $"Small viewport bounds the full renewal modal at {scale}x");
            ClosePopups(); io.DisplaySize = new(1920, 1440);
            var resetService = new PersonalEntry { Id = "native-reset-service", Kind = "subscription", Title = "정액제", When = CalendarBoundary.Date(new(2026, 12, 13)) };
            var keepService = resetService with { Id = "native-keep-service", Kind = "retainer", Title = "집사 이용권" };
            model.SaveSubscription(resetService); model.SaveSubscription(keepService); model.Tick(now);
            void SubscriptionFrame() => Frame(1320, 1050, () => Call("DrawSubscriptions"));
            void SubscriptionClick(Vector2 target)
            {
                io.AddMousePosEvent(target.X, target.Y); io.AddMouseButtonEvent(0, false); SubscriptionFrame();
                io.AddMouseButtonEvent(0, true); SubscriptionFrame(); io.AddMouseButtonEvent(0, false); SubscriptionFrame();
            }
            for (var i = 0; i < 4; i++) SubscriptionFrame();
            SubscriptionClick(main.SubscriptionResetTargets[resetService.Id]);
            Check(Find("DaySchedule").Handle != null && model.State.Entries.Any(e => e.Id == resetService.Id), $"Reset opens confirmation before changing data at {scale}x");
            SubscriptionClick(main.SubscriptionResetCancelTarget);
            Check(model.State.Entries.Any(e => e.Id == resetService.Id), $"Cancelling reset preserves subscription registration at {scale}x");
            SubscriptionClick(main.SubscriptionResetTargets[resetService.Id]);
            for (var i = 0; i < 3; i++) SubscriptionFrame();
            SubscriptionClick(main.SubscriptionResetConfirmTarget);
            Check(model.State.Entries.All(e => e.Id != resetService.Id) && model.State.Entries.Any(e => e.Id == keepService.Id), $"Confirming reset removes only the chosen service at {scale}x (confirm={main.SubscriptionResetConfirmTarget}, popup={Find("DaySchedule").Pos}/{Find("DaySchedule").Size}, remaining={string.Join(",", model.State.Entries.Select(e => e.Id))})");
            ClosePopups(); Call("OpenUserCategory", (object?)null); Field("userCategoryName", "절바하 공대"); Field("userCategoryIcon", "Trophy");
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            var saveCategory = main.UserCategorySaveTarget; Click(saveCategory,false); Click(saveCategory,true); Click(saveCategory,false);
            Check(model.State.View.UserCategories.Any(c => c.Name == "절바하 공대"), $"Native category editor creates a named raid category at {scale}x");
            var customCategory = model.State.View.UserCategories.Single(c => c.Name == "절바하 공대");
            Call("OpenEditor",null,false); Field("editName","공대 출발"); Field("editCategoryId",customCategory.Id);
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry",flags)!.Invoke(main,created)!;
            Check(saved && created[0] is PersonalEntry { CategoryId: var categoryId } && categoryId == customCategory.Id, $"Personal editor assigns the chosen custom category at {scale}x");
            ClosePopups(); Call("OpenCategoryNotification", customCategory.Id); Field("ruleCategoryEnabled",true); Field("ruleStarts","60, 10");
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            Check(Read<bool>("ruleWindowOpen") && context.OpenPopupStack.Size == 0, $"Category notification settings use a separate undimmed window at {scale}x");
            var saveRule = main.NotificationRuleSaveTarget; Click(saveRule,false); Click(saveRule,true); Click(saveRule,false);
            Check(model.State.Notifications.Categories.Single(r => r.CategoryId == customCategory.Id).StartMinutes.SequenceEqual(new[]{10,60}), $"Category notification editor saves multiple lead times at {scale}x");
            Call("OpenCategoryNotification",customCategory.Id); Field("ruleStarts","99");
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            var cancelRule = main.NotificationRuleCancelTarget; Click(cancelRule,false); Click(cancelRule,true); Click(cancelRule,false);
            Check(!Read<bool>("ruleWindowOpen") && model.State.Notifications.Categories.Single(r => r.CategoryId == customCategory.Id).StartMinutes.SequenceEqual(new[]{10,60}), $"Cancel closes the independent rule window without saving its draft at {scale}x");
            var removedCategory = new UserCalendarCategory { Id = "user:removed-open",Name = "삭제 중인 분류" };
            model.SaveUserCategory(removedCategory); model.Tick(now); Call("OpenCategoryNotification",removedCategory.Id);
            model.DeleteUserCategory(removedCategory.Id); model.Tick(now);
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            saveRule = main.NotificationRuleSaveTarget; Click(saveRule,false); Click(saveRule,true); Click(saveRule,false);
            Check(Read<bool>("ruleWindowOpen") && model.State.Notifications.Categories.All(r => r.CategoryId != removedCategory.Id), $"Deleting a category while its rule window is open safely prevents stale saves at {scale}x");
            ClosePopups();
            Field("activeTab",3); Field("notificationScreen",1);
            for (var i = 0; i < 3; i++) Frame(1320,1050);
            var categoryToggle = main.NotificationToggleTargets["Events"]; Click(categoryToggle,false); Click(categoryToggle,true); Click(categoryToggle,false);
            Check(model.State.Notifications.Categories.Single(r => r.CategoryId == "Events").Enabled && main.NotificationSummaryLabels["Events"].Contains("켜짐"), $"Category list turns alarms on and shows timing without entering details at {scale}x");
            categoryToggle = main.NotificationToggleTargets["Events"]; Click(categoryToggle,false); Click(categoryToggle,true); Click(categoryToggle,false);
            Check(!model.State.Notifications.Categories.Single(r => r.CategoryId == "Events").Enabled && main.NotificationSummaryLabels["Events"].Contains("1시간 전"), $"Category list turns alarms off while retaining its timing summary at {scale}x");
            var raidEntry = new PersonalEntry { Id = "native-raid", Title = "공대 일정", CategoryId = customCategory.Id, When = CalendarBoundary.Instant(now.AddHours(1)) };
            model.SaveEntry(raidEntry); model.Tick(now);
            var raid = model.Events.Single(e => e.Id == raidEntry.Id);
            var raidMarker = CalendarPresentation.Build(CalendarEngine.DayEvents(model.Events,CalendarEngine.Today(now.AddHours(1))),CalendarEngine.Today(now.AddHours(1))).Single(e => e.Primary.Event.Id == raid.Id);
            Check(MainWindow.CalendarMarkerLabel(raidMarker).StartsWith("13:00 · "), $"Timed raid schedule shows its clock in the calendar label at {scale}x");
            var datePhase = raid.Phases[0] with { Start = CalendarBoundary.Date(CalendarEngine.Today(now)) };
            var dateMarker = new CalendarDisplayEntry("date",CalendarDisplayGroup.Single,"start",[new(raid,datePhase,"start",CalendarEngine.Today(now))]);
            Check(!MainWindow.CalendarMarkerLabel(dateMarker).StartsWith("00:00"), $"Date-only schedules do not acquire an invented midnight clock at {scale}x");
            ClosePopups(); Field("activeTab",0); Call("OpenDay",CalendarEngine.Today(now.AddHours(1)));
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            var raidCard = model.DayDisplayEntries.Single(e => e.Primary.Event.Id == raid.Id);
            Check(main.HideItemTargets.ContainsKey(raid.Id) && main.CardEditTargets.ContainsKey(raidCard.Key) && main.CardDeleteTargets.ContainsKey(raidCard.Key) && !main.RelatedHideTargets.ContainsKey(raidCard.Key), $"Raid schedules offer only their own hide action without related-event grouping at {scale}x");
            ClosePopups();
            Call("OpenScheduleNotification",raid,raid.Phases.Single(),false); Field("ruleMode",1); Field("ruleStarts","5");
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            saveRule = main.NotificationRuleSaveTarget; Click(saveRule,false); Click(saveRule,true); Click(saveRule,false);
            Check(model.State.Notifications.Schedules.Single(r => r.EventId == raid.Id).StartMinutes.SequenceEqual(new[]{5}), $"A single raid schedule can override its category through the actual editor at {scale}x");
            Field("activeTab",3); Field("notificationScreen",3); Field("choicesMode",0); Field("choicesPage",0);
            for (var i = 0; i < 3; i++) Frame(1320,1050);
            var toggleKey = raid.Id + "/local"; var toggleTarget = main.NotificationToggleTargets[toggleKey];
            Click(toggleTarget,false); Click(toggleTarget,true); Click(toggleTarget,false);
            Check(model.State.Notifications.Schedules.Single(r => r.EventId == raid.Id) is { Mode: "off", StartMinutes: var kept } && kept.SequenceEqual(new[]{5}), $"List toggle disables an alarm without losing its custom lead at {scale}x");
            Check(main.NotificationSummaryLabels[toggleKey].Contains("꺼짐") && main.NotificationSummaryLabels[toggleKey].Contains("5분 전"), $"Disabled list row still shows its configured time at {scale}x");
            toggleTarget = main.NotificationToggleTargets[toggleKey]; Click(toggleTarget,false); Click(toggleTarget,true); Click(toggleTarget,false);
            Check(NotificationPolicy.EffectiveAlerts(model.Feed,model.State).Any(r => r.EventId == raid.Id && r.LeadMinutes == 5), $"List toggle restores the original alarm directly at {scale}x");
            workspace.UpdateState(workspace.State with { Entries = workspace.State.Entries.Concat(Enumerable.Range(0,250).Select(i => new PersonalEntry { Id = "many-"+i, Title = "많은 일정 "+i, When = CalendarBoundary.Instant(now.AddDays(i+1)) })).ToArray() }); model.Tick(now);
            Field("choicesMode",1); Field("choicesPage",0); Field("choicesRequest",true);
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            Check(main.NotificationChoiceRenderedRows == 12, $"A large schedule collection renders only twelve search results per page at {scale}x");
            var nextChoices = main.NotificationPageNextTarget; Click(nextChoices,false); Click(nextChoices,true); Click(nextChoices,false);
            Check(Read<int>("choicesPage") == 1 && main.NotificationChoiceRenderedRows <= 12, $"Notification selection paginates rather than extending the settings page at {scale}x");
            ClosePopups(); io.DisplaySize = new(620,480); Call("OpenUserCategory",customCategory);
            for (var i = 0; i < 4; i++) Frame(620,480);
            Check(main.UserCategorySaveTarget.Y > 0 && main.UserCategorySaveTarget.Y < 480, $"Custom category save remains accessible in a small viewport at {scale}x");
            ClosePopups(); io.DisplaySize = new(1920,1440); Field("inboxRequest",true);
            for (var i = 0; i < 3; i++) Frame(1320,1050);
            Check(Read<int>("activeTab") == 3 && Read<int>("notificationScreen") == 0 && context.OpenPopupStack.Size == 0, $"Notification history opens as an ordinary main view at {scale}x");
            Check(MainWindow.TimingLabels("Subscriptions") == ("","이용권 만료") && MainWindow.TimingLabels(customCategory.Id) == ("약속 시각",""), $"Expiry and raid alarms use contextual time labels at {scale}x");
            Check(MainWindow.TimingLabels("Events",new() { Point = true }).End.Length == 0 && MainWindow.TimingLabels("Events").End == "참여 종료", $"Point events do not offer a fictitious deadline at {scale}x");
            Field("activeTab",2);
            void AppearanceFrame() => Frame(1320,1050,() => { Call("DrawAppearanceSettings"); Call("DrawNotificationWindows"); });
            void AppearanceClick(Vector2 target)
            {
                io.AddMousePosEvent(target.X,target.Y); io.AddMouseButtonEvent(0,false); AppearanceFrame();
                io.AddMouseButtonEvent(0,true); AppearanceFrame(); io.AddMouseButtonEvent(0,false); AppearanceFrame();
            }
            for (var i = 0; i < 3; i++) AppearanceFrame();
            AppearanceClick(main.AppearanceLabelTargets[customCategory.Id]);
            Check(!Read<bool>("userCategoryRequest") && context.OpenPopupStack.Size == 0, $"Custom category labels are non-clickable like system labels at {scale}x");
            AppearanceClick(main.CategoryDeleteTargets[customCategory.Id]);
            Check(model.State.View.UserCategories.Any(c => c.Id == customCategory.Id) && context.OpenPopupStack.Size > 0, $"Minus button asks before deleting the category at {scale}x");
            AppearanceClick(main.CategoryDeleteCancelTarget);
            Check(model.State.View.UserCategories.Any(c => c.Id == customCategory.Id), $"Cancelling category deletion keeps the category at {scale}x");
            AppearanceClick(main.CategoryDeleteTargets[customCategory.Id]);
            var confirmBox = Find("DaySchedule");
            var confirmDescription = confirmBox.Handle == null ? "none" : confirmBox.Pos + "/" + confirmBox.Size;
            AppearanceClick(main.CategoryDeleteConfirmTarget);
            Check(model.State.View.UserCategories.All(c => c.Id != customCategory.Id) && model.State.Entries.Any(e => e.Id == raid.Id && e.CategoryId.Length == 0), $"Confirmed category removal preserves its schedule at {scale}x; target={main.CategoryDeleteConfirmTarget}; mouse={io.MousePos}; hovered={main.CategoryDeleteConfirmHovered}; popup={confirmDescription}; dest={Read<string>("deleteDestinationId")}; stack={context.OpenPopupStack.Size}");
            AppearanceClick(main.CategoryDeleteTargets["user:default-personal"]);
            var deleteBox = Find("DaySchedule");
            var deleteBoxDescription = deleteBox.Handle == null ? "none" : deleteBox.Pos + "/" + deleteBox.Size;
            AppearanceClick(main.CategoryDeleteConfirmTarget);
            Check(model.State.View.PersonalCategoryRemoved && model.State.Entries.Any(e => e.Id == raid.Id), $"Default personal category can be deleted while preserving schedules at {scale}x; target={main.CategoryDeleteConfirmTarget}; popup={deleteBoxDescription}; stack={context.OpenPopupStack.Size}");
            ClosePopups(); Call("OpenWeeklyRaidPlanner",(object?)null); Field("weeklyStartDate",new DateOnly(2026,11,16)); Field("weeklyWeeks",4); Field("weeklyInterval",1);
            Field("weeklyTitle","주간 등록 검사"); Field("weeklyTimes",new[] {"20:00","20:00","20:00","20:00","20:00","20:00","18:00"});
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            if (!main.WeeklyTwoColumns) { Field("weeklyPane",1); for (var i = 0; i < 3; i++) Frame(1320,1050); }
            var monday = main.WeeklyDayTargets[DayOfWeek.Monday]; Click(monday,false); Click(monday,true); Click(monday,false);
            var weeklyBody = Find(main.WeeklyTwoColumns ? "WeeklyForm" : "WeeklyBody");
            weeklyBody.Scroll.Y = weeklyBody.ScrollMax.Y;
            for (var i = 0; i < 3; i++) Frame(1320,1050);
            var sunday = main.WeeklyDayTargets[DayOfWeek.Sunday]; Click(sunday,false); Click(sunday,true); Click(sunday,false);
            Check(main.WeeklyPreviewCount == 8, $"Native weekly form previews four Mondays and Sundays with independent times at {scale}x");
            var entryCountBeforeWeekly = model.State.Entries.Count;
            var register = main.WeeklyRegisterTarget; Click(register,false); Click(register,true); Click(register,false);
            Check(model.State.Entries.Count == entryCountBeforeWeekly + 8 && model.LastWeeklyRegistration is { Registered:8 }, $"Native bulk register commits all eight previewed dates at {scale}x");
            var registeredSeries = model.State.Entries.Where(e => e.Title == "주간 등록 검사").OrderBy(e => e.SeriesIndex).ToArray();
            Check(registeredSeries.Where(e => CalendarEngine.StartInstant(e.When)!.Value.DayOfWeek == DayOfWeek.Sunday).All(e => CalendarEngine.StartInstant(e.When)!.Value.Hour == 18), $"Bulk registration preserves the distinct Sunday clock at {scale}x");
            Check(main.WeeklyRegisterTarget.Y < 1050, $"Weekly registration action remains in the viewport at {scale}x");
            ClosePopups(); Field("activeTab",0); Call("OpenDay",CalendarEngine.BoundaryDate(registeredSeries[2].When)!.Value);
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            var registeredCard = model.DayDisplayEntries.Single(e => e.Primary.Event.Id == registeredSeries[2].Id);
            Check(main.CardEditTargets.ContainsKey(registeredCard.Key) && main.CardDeleteTargets.ContainsKey(registeredCard.Key) && !main.CardAlertTargets.ContainsKey(registeredCard.Key), $"Local raid cards expose edit and delete while notification editing lives inside edit at {scale}x");
            var editRegistered = main.CardEditTargets[registeredCard.Key]; Click(editRegistered,false); Click(editRegistered,true); Click(editRegistered,false);
            Check(Read<bool>("editorOpen") && context.OpenPopupStack.Size == 0, $"Edit opens an independent window without dimming at {scale}x");
            Frame(1320,1050,() => {});
            Check(Find("DuckbillCalendar.EntryEditor").Handle != null, $"The independent edit window remains visible when the main view is not drawn at {scale}x");
            created = [null]; saved = (bool)typeof(MainWindow).GetMethod("TryCreateEntry",flags)!.Invoke(main,created)!;
            Check(saved && created[0] is PersonalEntry { SeriesId: var keptSeries, SeriesIndex: 2 } && keptSeries == registeredSeries[2].SeriesId, $"Editing an occurrence retains its batch identity and position at {scale}x");
            ClosePopups(); Call("OpenDay",CalendarEngine.BoundaryDate(registeredSeries[2].When)!.Value);
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            var deleteRegistered = main.CardDeleteTargets[registeredCard.Key]; Click(deleteRegistered,false); Click(deleteRegistered,true); Click(deleteRegistered,false);
            Check(main.EntryDeleteScopeTargets.Count == 3, $"Bulk deletion offers only, following and whole-series choices at {scale}x");
            var followingTarget = main.EntryDeleteScopeTargets["following"]; Click(followingTarget,false); Click(followingTarget,true); Click(followingTarget,false);
            var confirmSeriesDelete = main.EntryDeleteConfirmTarget; Click(confirmSeriesDelete,false); Click(confirmSeriesDelete,true); Click(confirmSeriesDelete,false);
            var hoverName = context.HoveredWindow.Handle == null ? "none" : Marshal.PtrToStringUTF8((nint)context.HoveredWindow.Name);
            if (model.State.Entries.Count(e => e.SeriesId == registeredSeries[0].SeriesId) != 2)
                for (var wi = 0; wi < context.Windows.Size; wi++) { var dw = context.Windows[wi]; if (dw.Active) Console.WriteLine($"ACTIVE {Marshal.PtrToStringUTF8((nint)dw.Name)} {dw.Pos} {dw.Size} {dw.Flags}"); }
            Check(model.State.Entries.Count(e => e.SeriesId == registeredSeries[0].SeriesId) == 2 && model.State.Entries.Any(e => e.Id == raid.Id), $"Native following deletion removes only the selected batch's later dates at {scale}x; scope={Read<string>("entryDeleteScope")}; remaining={model.State.Entries.Count(e => e.SeriesId == registeredSeries[0].SeriesId)}; target={main.EntryDeleteConfirmTarget}; mouse={io.MousePos}; editor={Read<bool>("editorOpen")}; rule={Read<bool>("ruleWindowOpen")}; hovered={hoverName}; popups={context.OpenPopupStack.Size}");
            ClosePopups(); Field("activeTab",0); model.GoToMonth(2027,5);
            for (var i = 0; i < 4; i++) Frame(1320,1050);
            Check(main.OguDayTargets.Count == 1 && main.OguDayTargets.ContainsKey(new(2027,5,9)), $"May shows exactly one Ogu Day decoration at {scale}x");
            var beforeEasterState = model.State;
            var ogu = main.OguDayTargets[new(2027,5,9)]; Click(ogu,false); Click(ogu,true); Click(ogu,false);
            Check(model.SelectedDate == new DateOnly(2027,5,9) && model.CalendarEvents.All(e => e.Title != "오구데이"), $"Ogu Day opens the ordinary date view without adding a synthetic public event at {scale}x");
            ClosePopups(); model.GoToMonth(2028,5); for (var i = 0; i < 3; i++) Frame(1320,1050);
            Check(main.OguDayTargets.ContainsKey(new(2028,5,9)) && ReferenceEquals(beforeEasterState,model.State), $"The annual Easter egg needs neither network data nor a stored personal schedule at {scale}x");
            model.GoToMonth(2028,6); for (var i = 0; i < 3; i++) Frame(1320,1050);
            Check(main.OguDayTargets.Count == 0, $"Ogu Day does not leak into unrelated months at {scale}x");
            ClosePopups(); io.DisplaySize = new(620,480); main.OpenSettings();
            workspace.FlushAsync().GetAwaiter().GetResult(); model.Tick(now);
            for (var i = 0; i < 4; i++) Frame(620,480);
            var stateBeforeNotes = model.State;
            var notesButton = main.ReleaseNotesSettingsTarget;
            Click(notesButton,false,620,480); Click(notesButton,true,620,480); Click(notesButton,false,620,480);
            for (var i = 0; i < 2; i++) Frame(620,480);
            var notesScroll = Find("ReleaseNotesScroll");
            Check(Read<int>("activeTab") == 6 && main.ReleaseNotesRenderedEntries == 1 && context.OpenPopupStack.Size == 0,
                $"Settings opens the offline initial-release history as an inline page at {scale}x");
            Check(notesScroll.Handle != null && notesScroll.Size.Y > 0 && notesScroll.Pos.Y + notesScroll.Size.Y <= 490 &&
                main.ReleaseNotesBackTarget.X < 620 && main.ReleaseNotesBackTarget.Y < 480,
                $"History text scrolls inside a small viewport while the return action remains visible at {scale}x");
            var backNotes = main.ReleaseNotesBackTarget;
            Click(backNotes,false,620,480); Click(backNotes,true,620,480); Click(backNotes,false,620,480); Frame(620,480);
            Check(Read<int>("activeTab") == 2 && ReferenceEquals(stateBeforeNotes,model.State),
                $"Returning from release history restores settings without modifying user data at {scale}x");
            ClosePopups(); io.DisplaySize = new(1920,1440);
        }
        finally { ImGui.DestroyContext(context); }
    }
}
File.WriteAllLines(Path.Combine(artifacts, "geometry.csv"), rows);
Console.WriteLine($"PASS: {checks.Count} native UI checks; 3 themes, 3 sizes, 1/1.5/2x; date/marker clicks, popup, wheel, native text input and renewal form. No GPU/game runtime. Artifacts: {artifacts}");
sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Native checks never access HTTP");
}
sealed class FieldFixture(byte[] bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(
        new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } });
}
