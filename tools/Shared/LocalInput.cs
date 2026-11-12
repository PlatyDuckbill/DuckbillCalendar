namespace DuckbillCalendar.Tools;

internal static class LocalInput
{
    public static async Task<byte[]> ReadAsync(string path, int limit = 2 * 1024 * 1024)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (input.Length > limit) throw new InvalidDataException("InputSizeLimit");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("InputSizeLimit");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
