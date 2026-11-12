using System.Text;
using System.Text.Json;
using DuckbillCalendar.Tools;

// Offline candidate extraction only. No HTTP, credentials, approval or publishing capability.
try
{
    if (args.Length != 3 || args[0] != "events")
    {
        Console.WriteLine("Usage: CalendarDataImport events <saved-events.html> <candidate.json>");
        Console.WriteLine("Reads local HTML only. Every extracted record requires review; no feed is published.");
        return args.Length == 0 ? 0 : 2;
    }
    var input = Path.GetFullPath(args[1]);
    if (new FileInfo(input).Length > 2 * 1024 * 1024) throw new InvalidDataException("HtmlSizeLimit");
    if (Path.GetFullPath(args[2]).Equals(input, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("InputOutputConflict");
    var html = Encoding.UTF8.GetString(await LocalInput.ReadAsync(input));
    var result = await EventPageParser.ParseAsync(html);
    var output = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    if (File.Exists(output)) throw new IOException("OutputAlreadyExists");
    var json = JsonSerializer.SerializeToUtf8Bytes(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("CandidateOutputSizeLimit");
    await using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await stream.WriteAsync(json);
    Console.WriteLine($"Candidates={result.Candidates.Count}; Warnings={result.Errors.Count}; ReviewRequired=true; Synthetic={result.IsSynthetic}");
    return result.Errors.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
{
    Console.Error.WriteLine($"ImportFailed:{ex.GetType().Name}:{ex.Message}");
    return 2;
}
