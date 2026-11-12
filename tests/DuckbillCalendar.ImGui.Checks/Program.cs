using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

// Executes the production MainWindow/UiWidgets sources against the installed native
// ImGui library. There is no renderer, game process, window, or network access.
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks.Add(name);
}
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
var artifacts = Path.Combine(root, ".preview", "imgui-checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(artifacts);
var example = File.ReadAllBytes(Path.Combine(root, "data", "contracts", "v2", "revisions", "000001.json"));
var bundledFeed = File.ReadAllBytes(Path.Combine(root, "data", "live", "kr-calendar.snapshot.json"));
var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
if (!File.Exists(fontPath)) throw new FileNotFoundException("Korean geometry checks require installed Windows Malgun Gothic", fontPath);
var openEditor = typeof(MainWindow).GetMethod("OpenEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawEditor = typeof(MainWindow).GetMethod("DrawEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
var editDate = typeof(MainWindow).GetField("editDate", BindingFlags.Instance | BindingFlags.NonPublic)!;
var editTime = typeof(MainWindow).GetField("editTime", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawMonth = typeof(MainWindow).GetMethod("DrawMonth", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawNews = typeof(MainWindow).GetMethod("DrawNews", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawSettings = typeof(MainWindow).GetMethod("DrawSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawSubscriptions = typeof(MainWindow).GetMethod("DrawSubscriptions", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawFilters = typeof(MainWindow).GetMethod("DrawFilters", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawVisibility = typeof(MainWindow).GetMethod("DrawCalendarVisibility", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawActions = typeof(MainWindow).GetMethod("DrawCalendarActions", BindingFlags.Instance | BindingFlags.NonPublic)!;
var report = new List<string> { "scale,modalWidth,modalHeight,tooltipWidth,tooltipHeight" };
const string TooltipText = "모험가 맞춤 헬스케어 프로그램 참여\n참여 기간\n2026-10-13 00:00 KST — 2026-11-11 23:59 KST";
foreach (var scale in new[] { 1f, 1.5f, 2f })
{
    unsafe
    {
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new(1920, 1080);
            io.DeltaTime = 1f / 60;
            io.IniFilename = null;
            io.LogFilename = null;
            ushort[] ranges = [0x20, 0xff, 0x1100, 0x11ff, 0x3130, 0x318f, 0xac00, 0xd7af, 0];
            fixed (ushort* glyphs = ranges)
            {
                io.Fonts.AddFontFromFileTTF(fontPath, 17 * scale, default, glyphs);
                byte* pixels; int pixelWidth, pixelHeight;
                io.Fonts.GetTexDataAsRGBA32(0, &pixels, &pixelWidth, &pixelHeight);
            }
            ImGui.GetStyle().ScaleAllSizes(scale);
            // Negative controls reproduce the two previous patterns using the same
            // native library and font. Passing fixed geometry alone would miss a
            // test harness which cannot expose these failures in the first place.
            var legacyWidths = new List<float>();
            Vector2 legacyTooltip = default;
            string legacyTitle = "", legacyNote = "";
            for (var frame = 0; frame < 30; frame++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowSize(new(460, 0), ImGuiCond.Appearing);
                ImGui.Begin("Legacy auto-size pattern", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("제목###legacyTitle", ref legacyTitle, 80);
                ImGui.InputTextMultiline("메모###legacyNote", ref legacyNote, 400, new(-1, 17 * scale * 4));
                legacyWidths.Add(ImGui.GetWindowSize().X);
                ImGui.End();
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(0);
                ImGui.TextUnformatted(TooltipText);
                ImGui.PopTextWrapPos();
                legacyTooltip = ImGui.GetWindowSize();
                ImGui.EndTooltip();
                ImGui.Render();
            }
            Check(legacyWidths.Max() - legacyWidths.Min() > 17 * scale * 3,
                $"Negative control reproduces legacy auto-sized full-width input growth at {scale}x");
            Check(legacyTooltip.X < 17 * scale * 8 && legacyTooltip.Y > 17 * scale * 10,
                $"Negative control reproduces legacy zero-width-wrap Korean tooltip at {scale}x");
            using var http = new HttpClient(new NoNetwork());
            using var workspace = new CalendarWorkspace(Path.Combine(artifacts, "scale-" + scale), example, http, bundledFeed: bundledFeed);
            var model = new CalendarModel(workspace);
            model.Tick(DateTimeOffset.UtcNow);
            var main = new MainWindow(model);
            openEditor.Invoke(main, [null, false]);
            var widths = new List<float>();
            Vector2 modal = default, tooltip = default;
            ImGuiWindowPtr modalWindow = default;
            for (var frame = 0; frame < 107; frame++)
            {
                if (frame == 101) foreach (var character in "2026-11-12") io.AddInputCharacter(character);
                if (frame == 104) foreach (var character in "23:59") io.AddInputCharacter(character);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(20, 20), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new(1000, 700), ImGuiCond.Always);
                ImGui.Begin("Headless host", ImGuiWindowFlags.NoSavedSettings);
                if (frame == 0) ImGui.OpenPopup("###DuckbillCalendar.EntryEditor");
                if (frame is 100 or 103)
                {
                    var date = frame == 100;
                    (date ? editDate : editTime).SetValue(main, "");
                    context.NavWindow = modalWindow;
                    context.NavActivateId = modalWindow.GetID(date ? "###entryDate" : "###entryTime");
                    context.NavActivateInputId = context.NavActivateId;
                    context.NavActivateFlags = ImGuiActivateFlags.PreferInput;
                }
                drawEditor.Invoke(main, null);
                UiWidgets.Tooltip(TooltipText);
                ImGui.End();
                ImGui.Render();
                for (var index = 0; index < context.Windows.Size; index++)
                {
                    var window = context.Windows[index];
                    if (!window.Active) continue;
                    var name = Marshal.PtrToStringUTF8((nint)window.Name) ?? "";
                    if (name.EndsWith("###DuckbillCalendar.EntryEditor", StringComparison.Ordinal)) { modal = window.Size; modalWindow = window; }
                    if ((window.Flags & ImGuiWindowFlags.Tooltip) != 0) tooltip = window.Size;
                }
                if (frame >= 5 && frame < 100) widths.Add(modal.X);
            }
            var font = 17 * scale;
            Check(widths.Count == 95 && widths.Max() - widths.Min() < 0.5f, $"Actual modal width stays stable over 100 frames at {scale}x");
            Check(Math.Abs(modal.X - font * 30) < 2 && modal.X < io.DisplaySize.X, $"Actual modal width follows font scale and fits viewport at {scale}x");
            Check(modal.Y > font * 15 && modal.Y < io.DisplaySize.Y - font, $"Actual modal auto-height stays within viewport at {scale}x");
            Check(tooltip.X > font * 10 && tooltip.X <= font * 30, $"Actual Korean tooltip has readable bounded width at {scale}x");
            Check(tooltip.Y > font * 2 && tooltip.Y < font * 10, $"Actual Korean tooltip does not become a vertical glyph column at {scale}x");
            Check((string?)editDate.GetValue(main) == "2026-11-12", $"Actual editor accepts the complete ten-character date at {scale}x");
            Check((string?)editTime.GetValue(main) == "23:59", $"Actual editor accepts the complete five-character time at {scale}x");
            for (var frame = 0; frame < 10; frame++)
            {
                ImGui.NewFrame();
                ImGui.SetNextWindowSize(new(1180, 900), ImGuiCond.Always);
                ImGui.Begin("Actual calendar smoke", ImGuiWindowFlags.NoSavedSettings);
                main.Draw();
                ImGui.End();
                ImGui.Render();
            }
            Check(!model.Feed.IsExample && model.Feed.Events.Count > 0 && model.Calendar is not null,
                $"Actual full MainWindow draws real bundled event feed for ten frames at {scale}x");
            var early = model.UpcomingHighlights.First(h => h.Phase.Id == "early-access");
            var release = model.UpcomingHighlights.First(h => h.Phase.Id == "release");
            model.JumpToHighlight(release); model.Tick(DateTimeOffset.UtcNow);
            Check(model.Year == 2027 && model.MonthNumber == 2 && model.SelectedPhaseId == "release" && release.Boundary.Kind == "date" && early.Boundary.Kind == "instant",
                $"Real expansion milestone navigates to February while retaining date-only versus exact-time precision at {scale}x");
            Check(model.Calendar!.Weeks.SelectMany(w => w.Days).SelectMany(d => d.Items).Any(i => i.Phase.Id == "early-access" && i.Date == new DateOnly(2027, 2, 19)) &&
                model.Calendar.Weeks.SelectMany(w => w.Days).SelectMany(d => d.Items).Any(i => i.Phase.Id == "release" && i.Date == new DateOnly(2027, 2, 23)),
                $"Real February grid renders separate early-access and release markers at {scale}x");
            float WheelFrame(float wheel, bool outside = false, bool control = false, bool popup = false)
            {
                io.AddMousePosEvent(outside ? 5000 : 45, outside ? 5000 : 75);
                io.AddMouseWheelEvent(0, wheel);
                ImGui.NewFrame(); ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always); ImGui.SetNextWindowSize(new(1180, 1000), ImGuiCond.Always);
                ImGui.Begin("Month wheel navigation", ImGuiWindowFlags.NoSavedSettings);
                io.KeyCtrl = control;
                if (popup)
                {
                    ImGui.OpenPopup("###WheelBlock");
                    if (ImGui.BeginPopup("###WheelBlock")) { ImGui.TextUnformatted("Blocking popup"); ImGui.EndPopup(); }
                }
                drawMonth.Invoke(main, null);
                ImGui.Dummy(new(10, 2000)); // The parent really can scroll; the grid must not forward its wheel.
                var scroll = ImGui.GetScrollY();
                if (popup && ImGui.BeginPopup("###WheelBlock")) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); }
                io.KeyCtrl = false;
                ImGui.End(); ImGui.Render(); model.Tick(DateTimeOffset.UtcNow);
                return scroll;
            }
            WheelFrame(0); WheelFrame(0);
            WheelFrame(1);
            Check(model.Year == 2027 && model.MonthNumber == 1, $"Wheel up over real calendar navigates to previous month at {scale}x");
            WheelFrame(-1);
            Check(model.Year == 2027 && model.MonthNumber == 2, $"Wheel down over real calendar navigates to next month at {scale}x");
            WheelFrame(.4f); WheelFrame(.4f); var beforeFraction = model.MonthNumber; WheelFrame(.3f);
            Check(beforeFraction == 2 && model.MonthNumber == 1, $"Fractional wheel input accumulates without multiplying month changes at {scale}x");
            WheelFrame(1, outside: true);
            Check(model.MonthNumber == 1, $"Wheel outside calendar leaves current month unchanged at {scale}x");
            WheelFrame(0); WheelFrame(0); WheelFrame(1, control: true);
            Check(model.MonthNumber == 1, $"Ctrl-wheel does not trigger calendar navigation at {scale}x");
            WheelFrame(1, popup: true);
            Check(model.MonthNumber == 1, $"Popup blocks month wheel navigation at {scale}x");
            WheelFrame(0); var parentBefore = WheelFrame(0); var parentScroll = WheelFrame(-1);
            Check(Math.Abs(parentScroll - parentBefore) < .5f && model.MonthNumber == 2, $"Grid consumes navigation without scrolling its parent at {scale}x (before={parentBefore}, after={parentScroll}, month={model.MonthNumber})");
            model.GoToMonth(2020, 1); model.Tick(DateTimeOffset.UtcNow); WheelFrame(0); WheelFrame(12);
            Check(model.Year == 2020 && model.MonthNumber == 1, $"Wheel respects lower supported year boundary at {scale}x");
            model.GoToMonth(2100, 11); model.Tick(DateTimeOffset.UtcNow); WheelFrame(0); WheelFrame(-6);
            Check(model.Year == 2100 && model.MonthNumber == 12, $"Multi-notch wheel clamps at upper supported month at {scale}x");
            model.GoToMonth(2027, 2); model.Tick(DateTimeOffset.UtcNow);
            io.AddMousePosEvent(5000, 5000);
            var gridHeight = 0f;
            for (var frame = 0; frame < 3; frame++)
            {
                ImGui.NewFrame(); ImGui.SetNextWindowSize(new(1180, 1000), ImGuiCond.Always);
                ImGui.Begin("Compact calendar measurement", ImGuiWindowFlags.NoSavedSettings);
                var before = ImGui.GetCursorScreenPos().Y;
                drawMonth.Invoke(main, null); gridHeight = ImGui.GetCursorScreenPos().Y - before;
                ImGui.End(); ImGui.Render();
            }
            Check(gridHeight <= font * (model.Calendar.Weeks.Count * 6.4f + 2), $"Actual month grid keeps compact week height at {scale}x");
            foreach (var hostWidth in new[] { 760f, 1180f, 1600f })
            {
                Vector2 detailSize = default, emptySize = default;
                var settingHeight = 0f;
                var filtersFit = false;
                var visibilityFits = false;
                for (var frame = 0; frame < 3; frame++)
                {
                    ImGui.NewFrame(); ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always); ImGui.SetNextWindowSize(new(hostWidth, 1000), ImGuiCond.Always);
                    ImGui.Begin("Responsive toolbar measurement", ImGuiWindowFlags.NoSavedSettings);
                    var contentRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
                    drawActions.Invoke(main, null);
                    var actionRight = ImGui.GetItemRectMax().X;
                    drawFilters.Invoke(main, null);
                    filtersFit = actionRight <= contentRight + .5f && ImGui.GetItemRectMax().X <= contentRight + .5f;
                    drawVisibility.Invoke(main, null);
                    visibilityFits = ImGui.GetItemRectMax().X <= contentRight + .5f;
                    ImGui.End(); ImGui.Render();
                    ImGui.NewFrame(); ImGui.SetNextWindowPos(new(10, 10), ImGuiCond.Always); ImGui.SetNextWindowSize(new(hostWidth, 1000), ImGuiCond.Always);
                    ImGui.Begin("News table measurement", ImGuiWindowFlags.NoSavedSettings);
                    drawNews.Invoke(main, null); ImGui.End(); ImGui.Render();
                    for (var index = 0; index < context.Windows.Size; index++)
                    {
                        var window = context.Windows[index]; var name = Marshal.PtrToStringUTF8((nint)window.Name) ?? "";
                        if (window.Active && name.Contains("DetailPane", StringComparison.Ordinal)) detailSize = window.Size;
                    }
                    ImGui.NewFrame(); ImGui.SetNextWindowSize(new(hostWidth, 1000), ImGuiCond.Always);
                    ImGui.Begin("Settings measurement", ImGuiWindowFlags.NoSavedSettings);
                    var before = ImGui.GetCursorScreenPos().Y; drawSettings.Invoke(main, null); settingHeight = ImGui.GetCursorScreenPos().Y - before;
                    ImGui.End(); ImGui.Render();
                    ImGui.NewFrame(); ImGui.SetNextWindowSize(new(hostWidth, 1000), ImGuiCond.Always);
                    ImGui.Begin("Subscription measurement", ImGuiWindowFlags.NoSavedSettings);
                    drawSubscriptions.Invoke(main, null); ImGui.End(); ImGui.Render();
                    for (var index = 0; index < context.Windows.Size; index++)
                    {
                        var window = context.Windows[index]; var name = Marshal.PtrToStringUTF8((nint)window.Name) ?? "";
                        if (window.Active && name.Contains("EmptySubscriptionCard", StringComparison.Ordinal)) emptySize = window.Size;
                    }
                }
                Check(filtersFit, $"Actual action row and filter checkbox wrap inside content at {scale}x / {hostWidth}px");
                Check(visibilityFits, $"Calendar group checkboxes wrap inside content at {scale}x / {hostWidth}px");
                Check(detailSize.X > 0 && detailSize.X < hostWidth && detailSize.Y <= font * 31 + 2,
                    $"Actual news detail has bounded internal scrolling at {scale}x / {hostWidth}px");
                Check(settingHeight > font * 5 && settingHeight < font * 28,
                    $"Default settings stay compact with advanced diagnostics collapsed at {scale}x / {hostWidth}px (height={settingHeight}, font={font})");
                Check(emptySize.X > font * 10 && emptySize.Y <= font * 8 + 2,
                    $"Empty subscription card keeps intentional compact bounds at {scale}x / {hostWidth}px");
            }
            report.Add(FormattableString.Invariant($"{scale},{modal.X},{modal.Y},{tooltip.X},{tooltip.Y}"));
        }
        finally { ImGui.DestroyContext(context); }
    }
}
File.WriteAllLines(Path.Combine(artifacts, "geometry.csv"), report);
Console.WriteLine($"PASS: {checks.Count} native ImGui geometry checks; actual production UI sources; Korean font; 100 frames at 1/1.5/2x. No GPU or game runtime. Artifacts: {artifacts}");

sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Geometry checks must not access HTTP");
}
