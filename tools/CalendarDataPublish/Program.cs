using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

// Writes a local preparation directory only. No git push, hosting, credentials or scheduler.
try
{
    var allowExample = args.Contains("--allow-example", StringComparer.Ordinal);
    var positional = args.Where(a => a != "--allow-example").ToArray();
    if (positional.Length is < 2 or > 3 || positional[0] is not ("validate" or "prepare") ||
        (positional[0] == "prepare" && positional.Length != 3))
    {
        Console.WriteLine("Usage: CalendarDataPublish validate <feed.json> [--allow-example]");
        Console.WriteLine("       CalendarDataPublish prepare <feed.json> <local-directory> [--allow-example]");
        Console.WriteLine("Local preparation only. Example input is rejected unless explicitly enabled.");
        return args.Length == 0 ? 0 : 2;
    }
    var sourcePath = Path.GetFullPath(positional[1]);
    if (new FileInfo(sourcePath).Length > 2 * 1024 * 1024) throw new InvalidDataException("FeedSizeLimit");
    var source = CalendarJson.Parse<FeedDocument>(await LocalInput.ReadAsync(sourcePath));
    var errors = FeedValidator.Validate(source, allowExample);
    if (errors.Count != 0)
    {
        foreach (var error in errors.Take(40)) Console.Error.WriteLine(error);
        return 1;
    }
    if (positional[0] == "validate") { Console.WriteLine($"Valid: events={source.Events.Count}; example={source.IsExample}"); return 0; }
    var directory = Path.GetFullPath(positional[2]);
    if (sourcePath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("OutputMustNotContainSource");
    Directory.CreateDirectory(directory);
    await using var publishLock = new FileStream(Path.Combine(directory, ".prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var currentPath = Path.Combine(directory, "current.json");
    var before = File.Exists(currentPath) ? await LocalInput.ReadAsync(currentPath, 65536) : null;
    if (before is { Length: > 65536 }) throw new InvalidDataException("CurrentSizeLimit");
    var previous = before is null ? null : CalendarJson.Parse<CurrentPointer>(before);
    if (previous is not null && FeedValidator.Validate(previous, allowExample).Count != 0)
        throw new InvalidDataException("ExistingCurrentUnsupported");
    var revisions = Path.Combine(directory, "revisions");
    Directory.CreateDirectory(revisions);
    var onDisk = Directory.EnumerateFiles(revisions, "*.json").Select(Path.GetFileNameWithoutExtension)
        .Select(s => long.TryParse(s, out var number) ? number : 0).DefaultIfEmpty(0).Max();
    var revision = checked(Math.Max(previous?.Content.Revision ?? 0, onDisk) + 1);
    var sequence = checked((previous?.PublicationSequence ?? 0) + 1);
    var now = DateTimeOffset.UtcNow;
    var feed = source with { ContentRevision = revision, GeneratedAt = now };
    var payload = JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options);
    if (payload.Length > 2 * 1024 * 1024) throw new InvalidDataException("GeneratedFeedSizeLimit");
    var relative = $"revisions/{revision:D6}.json";
    var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
    var pointer = new CurrentPointer
    {
        IsExample = source.IsExample, PublicationSequence = sequence, GeneratedAt = now,
        Content = new() { Revision = revision, Region = "KR", SchemaVersion = 2, Path = relative, Sha256 = hash, Bytes = payload.Length },
        Health = new() { Sources = feed.Evidence.Select(e => e.SourceId).Distinct().Select(id => new SourceHealth { Id = id }).ToArray() }
    };
    var pointerBytes = JsonSerializer.SerializeToUtf8Bytes(pointer, CalendarJson.Options);
    if (pointerBytes.Length > 65536) throw new InvalidDataException("GeneratedCurrentSizeLimit");
    var payloadPath = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    // Immutable file comes first. An interrupted run can leave an orphan, never a pointer to missing bytes.
    await WriteNew(payloadPath, payload);
    var latest = File.Exists(currentPath) ? await LocalInput.ReadAsync(currentPath, 65536) : null;
    if ((before is null) != (latest is null) || (before is not null && !before.AsSpan().SequenceEqual(latest)))
        throw new IOException("CurrentChangedDuringPreparation");
    var temporary = currentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        await WriteNew(temporary, pointerBytes);
        File.Move(temporary, currentPath, overwrite: true);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
    Console.WriteLine($"Prepared locally: content={revision}; publication={sequence}; events={feed.Events.Count}; example={feed.IsExample}");
    Console.WriteLine("Health remains not-run. Nothing uploaded. Remote CAS/approval/coverage workflow is not implemented.");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or OverflowException)
{
    Console.Error.WriteLine($"PreparationFailed:{ex.GetType().Name}:{ex.Message}");
    return 2;
}

static async Task WriteNew(string path, byte[] bytes)
{
    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough);
    await stream.WriteAsync(bytes);
    await stream.FlushAsync();
    stream.Flush(flushToDisk: true);
}
