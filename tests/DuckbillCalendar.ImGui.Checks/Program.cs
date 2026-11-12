using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
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
var bundledFeed = File.ReadAllBytes(Path.Combine(root, "data", "live", "kr-events.snapshot.json"));
var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
if (!File.Exists(fontPath)) throw new FileNotFoundException("Korean geometry checks require installed Windows Malgun Gothic", fontPath);
var openEditor = typeof(MainWindow).GetMethod("OpenEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
var drawEditor = typeof(MainWindow).GetMethod("DrawEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
var editDate = typeof(MainWindow).GetField("editDate", BindingFlags.Instance | BindingFlags.NonPublic)!;
var editTime = typeof(MainWindow).GetField("editTime", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
