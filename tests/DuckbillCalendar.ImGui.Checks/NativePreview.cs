using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Reflection;
using System.Net;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

/// <summary>Software preview of the actual production ImGui draw commands. No renderer/game hooks.</summary>
internal static class NativePreview
{
    private sealed record Texture(byte[] Pixels, int Width, int Height);
    public static unsafe string Run(int width = 1060, int height = 720, string theme = "midnight", float scale = 1)
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Workspace not found");
        var directory = Path.Combine(root, ".preview", "native-visual", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(directory);
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new(width, height); io.DeltaTime = 1f / 60;
            io.IniFilename = null; io.LogFilename = null;
            var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
            if (!File.Exists(font)) throw new FileNotFoundException("Korean preview requires installed Malgun Gothic", font);
            ushort[] ranges = [0x20, 0xff, 0x1100, 0x11ff, 0x2000, 0x27ff, 0x3130, 0x318f, 0xac00, 0xd7af, 0];
            var textures = new Dictionary<ulong, Texture>();
            fixed (ushort* glyphs = ranges)
            {
                io.Fonts.AddFontFromFileTTF(font, 17 * scale, default, glyphs);
                byte* pixels; int nativeWidth, nativeHeight;
                io.Fonts.GetTexDataAsRGBA32(0, &pixels, &nativeWidth, &nativeHeight);
                for (var texture = 0; texture < io.Fonts.Textures.Size; texture++)
                {
                    io.Fonts.GetTexDataAsRGBA32(texture, &pixels, &nativeWidth, &nativeHeight);
                    var id = (ulong)texture + 1;
                    io.Fonts.SetTexID(texture, new ImTextureID(id));
                    textures.Add(id, new(new ReadOnlySpan<byte>(pixels, checked(nativeWidth * nativeHeight * 4)).ToArray(), nativeWidth, nativeHeight));
                }
            }
            using var http = new HttpClient(new NoNetwork());
            using var workspace = new CalendarWorkspace(Path.Combine(directory, "state"),
                File.ReadAllBytes(Path.Combine(root, "data/contracts/v2/revisions/000001.json")), http,
                bundledFeed: File.ReadAllBytes(Path.Combine(root, "data/live/kr-calendar.snapshot.json")));
            var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(9));
            var model = new CalendarModel(workspace); model.SetAutomaticUpdateState(false, null); model.Tick(now);
            workspace.UpdateState(workspace.State with { View = workspace.State.View with { Theme = theme } }); model.Tick(now);
            using var fieldHttp = new HttpClient(new FrontlineFixture(File.ReadAllBytes(Path.Combine(root, "tests/DuckbillCalendar.Infrastructure.Checks/Fixtures/frontline-2026-11.json"))));
            using var fields = new FrontlineScheduleClient(fieldHttp);
            fields.Tick(now, true, 2026, 11); fields.BackgroundTask.GetAwaiter().GetResult(); fields.Tick(now, true, 2026, 11);
            var iconPng = File.ReadAllBytes(Path.Combine(root,"src/DuckbillCalendar.Plugin/Assets/bitty-duckbill-cutout.png"));
            var iconPixels = File.ReadAllBytes(Path.Combine(root,"tests/DuckbillCalendar.ImGui.Checks/Fixtures/bitty-duckbill.rgba"));
            using var iconMeta = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"tests/DuckbillCalendar.ImGui.Checks/Fixtures/bitty-duckbill.json")));
            var iconData = iconMeta.RootElement;
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(iconPng)) != iconData.GetProperty("pngSha256").GetString() ||
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(iconPixels)) != iconData.GetProperty("rgbaSha256").GetString())
                throw new InvalidDataException("Duckbill texture fixture does not match the production PNG");
            textures.Add(0xd09,new(iconPixels,iconData.GetProperty("width").GetInt32(),iconData.GetProperty("height").GetInt32()));
            var main = new MainWindow(model,fields,(pos,size,alpha) => {
                ImGui.GetWindowDrawList().AddImage(new ImTextureID(0xd09),pos,pos + new Vector2(size),Vector2.Zero,Vector2.One,ImGui.ColorConvertFloat4ToU32(new(1,1,1,alpha))); return true;
            });
            void Field(string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, value);
            void Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, values);
            void Frame(string name, Action select)
            {
                ImGui.GetCurrentContext().OpenPopupStack.Clear();
                Field("editorOpen", false); Field("editorRequest", false); Field("ruleWindowOpen",false);
                select(); model.Tick(now);
                io.AddMousePosEvent(-100, -100);
                for (var frame = 0; frame < 6; frame++)
                {
                    model.Tick(now); workspace.FlushAsync().GetAwaiter().GetResult(); model.Tick(now);
                    if (frame == 4) io.AddMousePosEvent(-100, -100);
                    if (name == "date-picker" && frame is 2 or 3)
                    { io.AddMousePosEvent(main.DatePickerButtonCenter.X, main.DatePickerButtonCenter.Y); io.AddMouseButtonEvent(0, frame == 2); }
                    if (name == "ongoing-expanded" && frame is 2 or 3)
                    { io.AddMousePosEvent(main.OngoingToggleCenter.X, main.OngoingToggleCenter.Y); io.AddMouseButtonEvent(0, frame == 2); }
                    if (name == "filters" && frame is 2 or 3)
                    { io.AddMousePosEvent(main.CalendarFilterTarget.X, main.CalendarFilterTarget.Y); io.AddMouseButtonEvent(0, frame == 2); }
                    if (name == "reset-subscription" && frame is 2 or 3)
                    { var point = main.SubscriptionResetTargets["preview-sub"]; io.AddMousePosEvent(point.X, point.Y); io.AddMouseButtonEvent(0, frame == 2); }
                    if (name == "appearance-icons" && frame is 2 or 3)
                    { var point = main.AppearanceIconTargets[CalendarGroup.Events]; io.AddMousePosEvent(point.X, point.Y); io.AddMouseButtonEvent(0, frame == 2); }
                    if (name == "hide-menu" && frame is 2 or 3)
                    {
                        var entry = model.DayDisplayEntries.Single(e => e.Primary.Event.Id == "kr:curated:ccrc-2027-korea");
                        var point = main.CardMenuTargets[entry.Key]; io.AddMousePosEvent(point.X, point.Y); io.AddMouseButtonEvent(0, frame == 2);
                    }
                    ImGui.NewFrame();
                    using (new ThemeScope(workspace.State.View.Theme))
                    {
                        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                        ImGui.SetNextWindowSize(new(width, height), ImGuiCond.Always);
                        ImGui.Begin("오리부리 달력###NativePreview", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
                        try { if (name == "reset-subscription") Call("DrawSubscriptions"); else main.Draw(); }
                        finally { ImGui.End(); }
                        main.DrawFloatingWindows();
                    }
                    ImGui.Render();
                }
                var path = Path.Combine(directory, name + "-" + width + "x" + height + ".png");
                Capture(ImGui.GetDrawData(), textures, path);
                Console.WriteLine("Native preview: " + path);
            }
            Frame("month", () => model.GoToMonth(2026, 11));
            Frame("filters", () => model.GoToMonth(2026, 11));
            Frame("maintenance", () => Call("OpenDay", new DateOnly(2026, 11, 10)));
            Frame("hide-menu", () => Call("OpenDay", new DateOnly(2026, 11, 10)));
            var party = new PersonalEntry { Id = "preview-party", Kind = "personal", Title = "공대 출발", When = CalendarBoundary.Instant(new(2026, 11, 15, 18, 0, 0, TimeSpan.FromHours(9))) };
            Frame("personal-day", () => { model.SaveEntry(party); Call("OpenDay", new DateOnly(2026, 11, 15)); });
            Frame("ongoing-expanded", () => Call("OpenDay", new DateOnly(2026, 11, 15)));
            Frame("release", () => model.GoToMonth(2027, 2));
            Frame("day", () => Call("OpenDay", new DateOnly(2027, 2, 19)));
            Frame("news", () => { Field("dayPopupRequest", false); Field("activeTab", 1); });
            Frame("settings", () => { model.SaveSubscription(new() { Id = "preview-sub", Title = "정액제", Kind = "subscription", When = CalendarBoundary.Instant(new(2026, 12, 13, 21, 30, 0, TimeSpan.FromHours(9))) }); main.OpenSettings(); });
            Frame("changelog", main.OpenReleaseNotes);
            main.OpenSettings();
            Frame("settings-off", () => { model.UpdateView(v => v with { RemoteUpdatesEnabled = false }); });
            Frame("online-settings", () => { model.UpdateView(v => v with { RemoteUpdatesEnabled = true, CurrentUrl = "", RemoteRefreshMinutes = 180 }); });
            model.UpdateView(v => v with { RemoteUpdatesEnabled = false }); model.Tick(now);
            Frame("appearance", () => { main.OpenSettings(); Field("appearanceSettingsRequest", true); });
            Frame("appearance-icons", () => { main.OpenSettings(); Field("appearanceSettingsRequest", true); });
            var subscription = new PersonalEntry { Id = "preview-sub", Title = "정액제", Kind = "subscription", When = CalendarBoundary.Date(new(2026, 12, 13)) };
            Frame("settings-registered", () => model.SaveSubscription(subscription));
            Frame("reset-subscription", () => { });
            Frame("subscription", () => { Call("OpenEditor", subscription, true); Field("chargeMode", true); });
            Frame("renewal-hours", () => { Call("OpenEditor", subscription, true); Field("chargeMode", true); Field("chargeDays", 0); Field("chargeHours", 6); Field("chargeBasisTime", "21:30"); });
            Frame("renewal-minutes", () => { Call("OpenEditor", subscription, true); Field("chargeMode", true); Field("chargeDays", 0); Field("chargeHours", 2); Field("chargeMinutes", 30); Field("chargeBasisTime", "21:30"); });
            Frame("expiry", () => { Field("activeTab", 0); model.GoToMonth(2026, 12); });
            // Separate popup names share the same stable ID. Closing is performed by the normal cancel action in interactive checks.
            Frame("personal", () =>
            {
                Call("OpenEditor", party, false);
            });
            Frame("date-picker", () => Call("OpenEditor", party, false));
            var raidCategory = new UserCalendarCategory { Id = "user:preview-raid", Name = "절바하 공대", Icon = "Trophy", Color = "#E7BE74" };
            model.SaveUserCategory(raidCategory);
            model.SaveEntry(party with { CategoryId = raidCategory.Id, When = CalendarBoundary.Instant(now.AddHours(2)) });
            model.SetCategoryNotification(new() { CategoryId = raidCategory.Id, Enabled = true, StartMinutes = [60,10] });
            model.SetScheduleNotification(party.Id,"local","custom",[30],[]); model.Tick(now);
            var raidEvent = model.Events.Single(e => e.Id == party.Id);
            Frame("month-personal", () => { Field("activeTab",0); model.GoToMonth(2026,11); });
            Frame("personal-menu", () => { Field("activeTab",0); Call("OpenDay",CalendarEngine.Today(CalendarEngine.StartInstant(raidEvent.Phases[0])!.Value)); });
            Frame("notification-settings", () => { main.OpenSettings(); });
            Frame("notification-categories", () => { Field("activeTab",3); Field("notificationScreen",1); });
            Frame("category-notification", () => Call("OpenCategoryNotification",raidCategory.Id));
            Frame("schedule-notification", () => Call("OpenScheduleNotification",raidEvent,raidEvent.Phases.Single(),false));
            Frame("notification-choices", () => { Field("choicesMode",0); Field("choicesPage",0); Field("choicesRequest",true); });
            Frame("user-category", () => Call("OpenUserCategory",raidCategory));
            Frame("category-delete", () => { main.OpenSettings(); Field("deleteCategoryId",raidCategory.Id); Field("categoryDeleteRequest",true); });
            Frame("notification-inbox-empty", () => { Field("inboxRequest",true); });
            workspace.UpdateState(workspace.State with { NotificationState = workspace.State.NotificationState with { Inbox = [
                new() { Id = NotificationPolicy.Id("preview-notice"), Kind = "new-notice", Title = "새 공지 안내", Message = "새 공지가 확인되었습니다.", CreatedAt = now.AddMinutes(-10), Url = "https://www.ff14.co.kr/news/notice" },
                new() { Id = NotificationPolicy.Id("preview-raid"), Kind = "schedule", ProfileId = "default", EventId = party.Id, PhaseId = "local", Edge = "start", LeadMinutes = 30, Title = party.Title, Message = "시작 30분 전", CreatedAt = now, BoundaryAt = now.AddMinutes(30) }
            ] } }); model.Tick(now);
            Frame("notification-inbox", () => { Field("inboxRequest",true); });
            Frame("weekly-raid", () => { Call("OpenWeeklyRaidPlanner",(object?)null); Field("weeklyStartDate",new DateOnly(2026,11,16)); Field("weeklySelected",new[] {true,false,false,false,false,false,true}); Field("weeklyTimes",new[] {"20:00","20:00","20:00","20:00","20:00","20:00","18:00"}); Field("weeklyPane",1); });
            Frame("weekly-raid-preview", () => { Field("weeklyPane",2); });
            Frame("ogu-day", () => { Field("activeTab",0); model.GoToMonth(2027,5); });
            Frame("ogu-day-detail", () => { Call("OpenDay",new DateOnly(2027,5,9)); });
            return directory;
        }
        finally { ImGui.DestroyContext(context); }
    }

    private static unsafe void Capture(ImDrawDataPtr data, IReadOnlyDictionary<ulong, Texture> textures, string path)
    {
        if (!data.Valid || data.DisplaySize.X <= 0 || data.DisplaySize.Y <= 0) throw new InvalidDataException("No native draw data");
        var width = checked((int)MathF.Ceiling(data.DisplaySize.X * data.FramebufferScale.X));
        var height = checked((int)MathF.Ceiling(data.DisplaySize.Y * data.FramebufferScale.Y));
        if (width is < 1 or > 8192 || height is < 1 or > 8192) throw new InvalidDataException("Preview dimensions exceed limit");
        var output = new byte[checked(width * height * 4)];
        for (var i = 0; i < output.Length; i += 4) { output[i] = 17; output[i + 1] = 20; output[i + 2] = 22; output[i + 3] = 255; }
        var triangles = 0;
        for (var li = 0; li < data.CmdListsCount; li++)
        {
            var list = new ImDrawListPtr(data.CmdLists[li]);
            for (var ci = 0; ci < list.CmdBuffer.Size; ci++)
            {
                var command = list.CmdBuffer[ci];
                if (command.UserCallback != null) throw new NotSupportedException("Preview does not execute native draw callbacks");
                if (!textures.TryGetValue(command.TextureId.Handle, out var texture)) throw new NotSupportedException("Preview supports registered native textures only");
                var clip = command.ClipRect;
                var minX = Math.Clamp((int)MathF.Floor((clip.X - data.DisplayPos.X) * data.FramebufferScale.X), 0, width);
                var minY = Math.Clamp((int)MathF.Floor((clip.Y - data.DisplayPos.Y) * data.FramebufferScale.Y), 0, height);
                var maxX = Math.Clamp((int)MathF.Ceiling((clip.Z - data.DisplayPos.X) * data.FramebufferScale.X), 0, width);
                var maxY = Math.Clamp((int)MathF.Ceiling((clip.W - data.DisplayPos.Y) * data.FramebufferScale.Y), 0, height);
                for (uint index = command.IdxOffset; index + 2 < command.IdxOffset + command.ElemCount; index += 3)
                {
                    var a = list.VtxBuffer[checked((int)(command.VtxOffset + list.IdxBuffer[(int)index]))];
                    var b = list.VtxBuffer[checked((int)(command.VtxOffset + list.IdxBuffer[(int)index + 1]))];
                    var c = list.VtxBuffer[checked((int)(command.VtxOffset + list.IdxBuffer[(int)index + 2]))];
                    Rasterize(a, b, c, data.DisplayPos, data.FramebufferScale, minX, minY, maxX, maxY, texture.Pixels, texture.Width, texture.Height, output, width);
                    triangles++;
                }
            }
        }
        WritePng(path, width, height, output);
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), $"Actual ImGui draw data: lists={data.CmdListsCount}, vertices={data.TotalVtxCount}, triangles={triangles}, registeredTextures={textures.Count}. CPU nearest-neighbor texture rasterization; no GPU, no game process.\n");
    }

    private static void Rasterize(ImDrawVert a, ImDrawVert b, ImDrawVert c, Vector2 offset, Vector2 scale,
        int clipX0, int clipY0, int clipX1, int clipY1, byte[] texture, int textureWidth, int textureHeight, byte[] output, int width)
    {
        var p0 = (a.Pos - offset) * scale; var p1 = (b.Pos - offset) * scale; var p2 = (c.Pos - offset) * scale;
        var area = Cross(p1 - p0, p2 - p0);
        if (Math.Abs(area) < 0.0001f) return;
        if (area < 0) { (p1, p2) = (p2, p1); (b, c) = (c, b); area = -area; }
        var x0 = Math.Max(clipX0, (int)MathF.Floor(Math.Min(p0.X, Math.Min(p1.X, p2.X))));
        var y0 = Math.Max(clipY0, (int)MathF.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y))));
        var x1 = Math.Min(clipX1, (int)MathF.Ceiling(Math.Max(p0.X, Math.Max(p1.X, p2.X))));
        var y1 = Math.Min(clipY1, (int)MathF.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y))));
        var ca = Color(a.Col); var cb = Color(b.Col); var cc = Color(c.Col);
        var edgeA = p2 - p1; var edgeB = p0 - p2; var edgeC = p1 - p0;
        static bool TopLeft(Vector2 edge) => edge.Y < 0 || edge.Y == 0 && edge.X > 0;
        static bool Inside(float weight, Vector2 edge) => weight > 0 || weight == 0 && TopLeft(edge);
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            var p = new Vector2(x + .5f, y + .5f);
            var ea = Cross(edgeA, p - p1); var eb = Cross(edgeB, p - p2); var ec = Cross(edgeC, p - p0);
            if (!Inside(ea, edgeA) || !Inside(eb, edgeB) || !Inside(ec, edgeC)) continue;
            var wa = ea / area; var wb = eb / area; var wc = ec / area;
            var uv = a.Uv * wa + b.Uv * wb + c.Uv * wc;
            var vertex = ca * wa + cb * wb + cc * wc;
            var tx = Math.Clamp((int)(uv.X * textureWidth), 0, textureWidth - 1);
            var ty = Math.Clamp((int)(uv.Y * textureHeight), 0, textureHeight - 1);
            var tex = (ty * textureWidth + tx) * 4;
            var alpha = vertex.W * texture[tex + 3] / 255f;
            if (alpha <= 0) continue;
            var dest = (y * width + x) * 4;
            output[dest] = Blend(output[dest], vertex.X * texture[tex], alpha);
            output[dest + 1] = Blend(output[dest + 1], vertex.Y * texture[tex + 1], alpha);
            output[dest + 2] = Blend(output[dest + 2], vertex.Z * texture[tex + 2], alpha);
        }
    }
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static Vector4 Color(uint packed) => new((packed & 255) / 255f, ((packed >> 8) & 255) / 255f, ((packed >> 16) & 255) / 255f, ((packed >> 24) & 255) / 255f);
    private static byte Blend(byte old, float value, float alpha) => (byte)Math.Clamp((int)MathF.Round(value * alpha + old * (1 - alpha)), 0, 255);
    private static void WritePng(string path, int width, int height, byte[] rgba)
    {
        using var output = File.Create(path); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width); BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(output, "IHDR", ihdr);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var y = 0; y < height; y++) { zlib.WriteByte(0); zlib.Write(rgba, y * width * 4, width * 4); }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
    }
    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); stream.Write(number);
        var name = Encoding.ASCII.GetBytes(type); stream.Write(name); stream.Write(data);
        uint crc = 0xffffffff;
        foreach (var value in name.Concat(data)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("Native preview never accesses the network");
    }
    private sealed class FrontlineFixture(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } });
    }
}
