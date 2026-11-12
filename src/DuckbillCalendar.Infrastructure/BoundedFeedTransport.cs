using System.Net;
using System.Net.Http.Headers;

namespace DuckbillCalendar.Infrastructure;

public sealed record DownloadResult(byte[]? Bytes, bool NotModified, string? ETag);

/// <summary>HTTPS public data transport: one origin/path prefix, no cookies, no redirects.</summary>
public sealed class BoundedFeedTransport : IDisposable
{
    public const string CurrentAddress = "https://raw.githubusercontent.com/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/current.json";
    private const string AllowedPrefix = "/PlatyDuckbill/DuckbillCalendar/calendar-data/kr/";
    private readonly HttpClient client;
    private readonly bool ownsClient;

    public BoundedFeedTransport(HttpClient? httpClient = null)
    {
        ownsClient = httpClient is null;
        client = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static Uri GetCurrentUri(string? configuredAddress)
    {
        if (string.IsNullOrWhiteSpace(configuredAddress)) return new Uri(CurrentAddress);
        if (!Uri.TryCreate(configuredAddress, UriKind.Absolute, out var uri) || uri.AbsoluteUri != CurrentAddress)
            throw new InvalidDataException("UntrustedCurrentAddress");
        return uri;
    }

    public static Uri GetPayloadUri(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 160 || relative.Contains('%') || relative.Contains('\\') || relative.Contains('?') || relative.Contains('#') || relative.Contains(':') || relative.StartsWith('/') || relative.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("UntrustedPayloadPath");
        if (!relative.StartsWith("revisions/", StringComparison.Ordinal) || !relative.EndsWith(".json", StringComparison.Ordinal))
            throw new InvalidDataException("UntrustedPayloadPath");
        var uri = new Uri(new Uri(CurrentAddress), relative);
        ValidateUri(uri);
        return uri;
    }

    private static void ValidateUri(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Host != "raw.githubusercontent.com" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith(AllowedPrefix, StringComparison.Ordinal))
            throw new InvalidDataException("UntrustedAddress");
    }

    public async Task<DownloadResult> GetAsync(Uri uri, int maximumBytes, string? etag, CancellationToken cancellationToken)
    {
        if (maximumBytes is < 1 or > AtomicJsonStore.MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ValidateUri(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("DuckbillCalendar/0.1 (+public-calendar)");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var parsed)) request.Headers.IfNoneMatch.Add(parsed);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.RequestMessage?.RequestUri is { } final && final != uri) throw new InvalidDataException("RedirectNotAllowed");
        if ((int)response.StatusCode is >= 300 and < 400 && response.StatusCode != HttpStatusCode.NotModified)
            throw new InvalidDataException("RedirectNotAllowed");
        if (response.StatusCode == HttpStatusCode.NotModified) return new(null, true, response.Headers.ETag?.ToString());
        if (!response.IsSuccessStatusCode)
        {
            var retry = response.Headers.RetryAfter?.Delta;
            if (retry is null && response.Headers.RetryAfter?.Date is { } retryDate) retry = retryDate - DateTimeOffset.UtcNow;
            throw new FeedHttpException((int)response.StatusCode, retry);
        }
        if (response.Content.Headers.ContentLength is { } length && length > maximumBytes) throw new InvalidDataException("ResponseTooLarge");
        if (response.Content.Headers.ContentEncoding.Any(v => !v.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("UnsupportedContentEncoding");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("ResponseTooLarge");
            output.Write(buffer, 0, count);
        }
        return new(output.ToArray(), false, response.Headers.ETag?.ToString());
    }

    public void Dispose() { if (ownsClient) client.Dispose(); }
}

public sealed class FeedHttpException(int status, TimeSpan? retryAfter) : HttpRequestException("Http" + status)
{
    public int Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
