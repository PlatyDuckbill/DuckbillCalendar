using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using DuckbillCalendar.Core;
using DuckbillCalendar.Infrastructure;
using DuckbillCalendar.Plugin;

/// <summary>Software preview of the actual production ImGui draw commands. No renderer/game hooks.</summary>
internal static class NativePreview
{
    private sealed record Texture(byte[] Pixels, int Width, int Height);
    public static unsafe string Run(int width = 980, int height = 660)
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
                io.Fonts.AddFontFromFileTTF(font, 17, default, glyphs);
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
            var model = new CalendarModel(workspace); model.Tick(now);
            var main = new MainWindow(model);
            void Frame(string name, Action select)
            {
                select(); model.Tick(now);
                for (var frame = 0; frame < 6; frame++)
                {
                    ImGui.NewFrame();
                    using (new ThemeScope(workspace.State.View.Theme))
                    {
                        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                        ImGui.SetNextWindowSize(new(width, height), ImGuiCond.Always);
                        ImGui.Begin("DuckbillCalendar###NativePreview", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
                        try { main.Draw(); }
                        finally { ImGui.End(); }
                    }
                    ImGui.Render();
                }
                var path = Path.Combine(directory, name + "-" + width + "x" + height + ".png");
                Capture(ImGui.GetDrawData(), textures, path);
                Console.WriteLine("Native preview: " + path);
            }
            Frame("month", () => model.GoToMonth(2027, 2));
            Frame("day", () => model.SelectDate(new(2027, 2, 19)));
            Frame("event", () =>
            {
                var item = model.Events.Single(e => e.Id == "kr:curated:v8-early-access");
                model.Select(item, item.Phases.Single(p => p.Id == "early-access"), new(2027, 2, 19));
            });
            Frame("undated", model.ShowUndatedAgenda);
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
                if (!textures.TryGetValue(command.TextureId.Handle, out var texture)) throw new NotSupportedException("Preview supports registered native font atlases only");
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
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), $"Actual ImGui draw data: lists={data.CmdListsCount}, vertices={data.TotalVtxCount}, triangles={triangles}, fontAtlases={textures.Count}. CPU nearest-neighbor texture rasterization; no GPU, no game process.\n");
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
}
