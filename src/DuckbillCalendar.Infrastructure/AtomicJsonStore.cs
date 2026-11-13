using System.Security.Cryptography;
using System.Text.Json;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Infrastructure;

/// <summary>Single-writer storage. A failed write leaves the previously active file intact.</summary>
public sealed class AtomicJsonStore(string directory)
{
    public const int MaximumFileBytes = 2 * 1024 * 1024;
    private readonly HashSet<string> corruptPaths = new(StringComparer.OrdinalIgnoreCase);
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    public string Resolve(string name)
    {
        var target = Path.GetFullPath(Path.Combine(DirectoryPath, name));
        if (!target.StartsWith(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("StoragePathOutsideRoot");
        return target;
    }

    public T? Load<T>(string name, Func<T, bool> valid, out string status) where T : class
    {
        var path = Resolve(name);
        status = "Missing";
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var bytes = ReadBounded(candidate);
                var parsed = CalendarJson.Parse<T>(bytes);
                if (!valid(parsed)) throw new InvalidDataException("InvalidStoredDocument");
                status = candidate == path ? "Loaded" : "BackupRecovered";
                return parsed;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                status = "CorruptPreserved";
                corruptPaths.Add(candidate);
                // Preserve the bad file for diagnosis before a future successful write replaces it.
                try
                {
                    var info = new FileInfo(candidate);
                    if (info.Length <= MaximumFileBytes)
                    {
                        var preserved = candidate + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff");
                        File.Copy(candidate, preserved, false);
                    }
                }
                catch (Exception preserveError) when (preserveError is IOException or UnauthorizedAccessException) { }
            }
        }
        return null;
    }

    public static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("FileTooLarge");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            if (output.Length + count > MaximumFileBytes) throw new InvalidDataException("FileTooLarge");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public Task SaveAsync<T>(string name, T document, CancellationToken cancellationToken = default) =>
        WriteAsync(name, JsonSerializer.SerializeToUtf8Bytes(document, CalendarJson.Options), true, cancellationToken);

    public async Task WriteAsync(string name, byte[] bytes, bool backup, CancellationToken cancellationToken = default)
    {
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("FileTooLarge");
        var target = Resolve(name);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(bytes, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Do not replace a recovered good backup with the known-corrupt primary.
            if (File.Exists(target)) File.Replace(temporary, target, backup && !corruptPaths.Contains(target) ? target + ".bak" : null, true);
            else File.Move(temporary, target);
            corruptPaths.Remove(target);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task<string> PutContentAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var name = Path.Combine("cache", "content", sha + ".json");
        var path = Resolve(name);
        if (File.Exists(path))
        {
            bool matches;
            try
            {
                var existing = ReadBounded(path);
                matches = CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(bytes));
            }
            catch (InvalidDataException) { matches = false; }
            if (!matches)
            {
                // The filename is the SHA of the verified replacement bytes. Different existing
                // bytes are local corruption, not permission to alter an official revision.
                File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                await WriteAsync(name, bytes, false, cancellationToken);
            }
        }
        else await WriteAsync(name, bytes, false, cancellationToken);
        return sha;
    }
    public int PruneContent(IEnumerable<string?> retainedHashes)
    {
        var retained = retainedHashes.Where(h => h is { Length: 64 }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contentRoot = Resolve(Path.Combine("cache", "content"));
        if (!Directory.Exists(contentRoot)) return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(contentRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            var hash = Path.GetFileNameWithoutExtension(file);
            if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)) || retained.Contains(hash)) continue;
            var resolved = Resolve(Path.Combine("cache", "content", Path.GetFileName(file)));
            try { File.Delete(resolved); removed++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }
}
