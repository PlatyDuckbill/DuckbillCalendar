using System.Globalization;
using System.Net;
using System.Text.Json;
using DuckbillCalendar.Core;

namespace DuckbillCalendar.Infrastructure;

public sealed record FrontlineDay(DateOnly Date, string Name, string Map);
public sealed record FrontlineMonth(DateTimeOffset FetchedAt, IReadOnlyList<FrontlineDay> Days);
internal sealed record FrontlineFetchResult(string Key, FrontlineMonth? Month, DateTimeOffset? RetryAt = null);

/// <summary>Reads explicit KR dates from Aetherway. Never infers an epoch or marks community data as official.</summary>
public sealed class FrontlineScheduleClient : IDisposable
{
    public const string SourceUrl = "https://aether-way.com/frontline";
    public const string Endpoint = "https://api.aether-way.com/api/v1/frontline/schedule";
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, FrontlineMonth> months = [];
    private readonly Dictionary<string, DateTimeOffset> retryAfter = [];
    private readonly HashSet<string> cacheChecked = [];
    private readonly HashSet<string> manualMonths = [];
    private bool activeManual;
    private Task<FrontlineFetchResult>? pending;
    private CancellationTokenSource? activeRequest;
    private readonly AtomicJsonStore? store;
    private bool disposed;
    private DateOnly? retainedMonth;
    public bool IsBusy => pending is not null;
    public Task BackgroundTask => pending ?? Task.CompletedTask;
    public string? Error { get; private set; }
    public DateTimeOffset ManualRefreshAvailableAt { get; private set; } = DateTimeOffset.MinValue;
    public bool RequestManualRefresh(DateTimeOffset now)
    {
        if (disposed || IsBusy || now < ManualRefreshAvailableAt) return false;
        var today = CalendarEngine.Today(now);
        manualMonths.Add(today.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        manualMonths.Add(today.AddDays(1).ToString("yyyy-MM", CultureInfo.InvariantCulture));
        ManualRefreshAvailableAt = now.AddMinutes(5);
        return true;
    }

    public FrontlineScheduleClient(HttpClient? client = null, string? directory = null)
    {
        ownsHttp = client is null;
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 32 }) { Timeout = Timeout.InfiniteTimeSpan };
        if (directory is not null) store = new AtomicJsonStore(directory);
    }

    public bool Tick(DateTimeOffset now, bool enabled, int year, int month, bool allowAutomatic = true)
    {
        if (disposed) return false;
        var changed = false;
        if (!enabled) manualMonths.Clear();
        if (!enabled || !allowAutomatic && !activeManual) activeRequest?.Cancel();
        if (pending?.IsCompleted == true)
        {
            var result = pending.GetAwaiter().GetResult(); pending = null;
            var cancelled = activeRequest?.IsCancellationRequested == true;
            activeRequest?.Dispose(); activeRequest = null;
            activeManual = false;
            if (cancelled) cacheChecked.Remove(result.Key);
            if (enabled && !cancelled)
            {
                if (result.Month is not null) { months[result.Key] = result.Month; Error = null; retryAfter.Remove(result.Key); }
                else { Error = "전장 정보를 갱신하지 못했습니다"; retryAfter[result.Key] = result.RetryAt > now.AddMinutes(15) ? result.RetryAt.Value : now.AddMinutes(15); }
                changed = true;
            }
        }
        if (!enabled || pending is not null || !allowAutomatic && manualMonths.Count == 0) return changed;
        var today = CalendarEngine.Today(now);
        var current = new DateOnly(today.Year, today.Month, 1);
        if (retainedMonth != current)
        {
            retainedMonth = current;
            var oldest = current.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var newest = current.AddMonths(2).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var expired = months.Keys.Concat(retryAfter.Keys).Concat(cacheChecked).Concat(manualMonths).Distinct()
                .Where(key => string.CompareOrdinal(key, oldest) < 0 || string.CompareOrdinal(key, newest) > 0).ToArray();
            foreach (var key in expired) { months.Remove(key); retryAfter.Remove(key); cacheChecked.Remove(key); manualMonths.Remove(key); }
        }
        // The provider publishes a bounded month range. Ask for dates; never extrapolate beyond it.
        var wanted = new[] { current, new DateOnly(today.AddDays(1).Year, today.AddDays(1).Month, 1), new DateOnly(year, month, 1) }
            .Where(d => d >= current.AddMonths(-1) && d <= current.AddMonths(2)).Distinct();
        foreach (var date in wanted)
        {
            var key = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var manual = manualMonths.Contains(key);
            if (!allowAutomatic && !manual) continue;
            if (!manual && months.TryGetValue(key, out var value) && now >= value.FetchedAt && now - value.FetchedAt < TimeSpan.FromHours(6)) continue;
            if (retryAfter.TryGetValue(key, out var retry) && now < retry) continue;
            var loadCache = cacheChecked.Add(key) && !months.ContainsKey(key) && !manual;
            activeRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            activeManual = manual;
            manualMonths.Remove(key);
            var token = activeRequest.Token;
            // Disk reads, JSON parsing and HTTP never run synchronously on the game's frame thread.
            pending = Task.Run(() => LoadOrFetch(key, date, now, loadCache, token));
            break;
        }
        return changed;
    }

    public FrontlineDay? Get(DateOnly day, DateTimeOffset now)
    {
        var key = day.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return months.TryGetValue(key, out var value) && now >= value.FetchedAt && now - value.FetchedAt <= TimeSpan.FromHours(24)
            ? value.Days.FirstOrDefault(d => d.Date == day) : null;
    }
    public bool IsCached(DateOnly day, DateTimeOffset now) => months.TryGetValue(day.ToString("yyyy-MM", CultureInfo.InvariantCulture), out var m) && now - m.FetchedAt >= TimeSpan.FromHours(6);
    private async Task<FrontlineFetchResult> LoadOrFetch(string key, DateOnly date, DateTimeOffset now, bool loadCache, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (loadCache && store is not null)
            {
                var cached = store.Load<FrontlineMonth>("frontline-" + key + ".json", m => ValidCache(m, date, now), out _);
                token.ThrowIfCancellationRequested();
                if (cached is not null) return new(key, cached);
            }
            return await Fetch(key, now, token);
        }
        catch (OperationCanceledException) { return new(key, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { return new(key, null); }
    }
    private async Task<FrontlineFetchResult> Fetch(string key, DateTimeOffset now, CancellationToken token)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var uri = new Uri(Endpoint + "?month=" + key);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json"); request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.UserAgent.ParseAdd("DuckbillCalendar/0.2 (+public-frontline-schedule)");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is 429 or 503)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - now);
                throw new FeedHttpException((int)response.StatusCode, delay);
            }
            if (response.StatusCode != HttpStatusCode.OK || response.RequestMessage?.RequestUri != uri ||
                response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > 65536 ||
                response.Content.Headers.ContentEncoding.Any(v => !v.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("InvalidFrontlineResponse");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream(); var buffer = new byte[4096]; int count;
            while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (bytes.Length + count > 65536) throw new InvalidDataException("FrontlineTooLarge");
                bytes.Write(buffer, 0, count);
            }
            var result = Parse(bytes.ToArray(), key, now);
            if (store is not null)
            {
                try { await store.SaveAsync("frontline-" + key + ".json", result, deadline.Token); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A valid response can still be shown if caching fails. */ }
            }
            return new(key, result);
        }
        catch (FeedHttpException ex) { return new(key, null, ex.RetryAfter is { } delay && delay > TimeSpan.Zero ? now.Add(delay > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : delay) : null); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException or OperationCanceledException or KeyNotFoundException or FormatException or OverflowException)
        { return new(key, null); }
    }

    public static FrontlineMonth Parse(byte[] bytes, string month, DateTimeOffset now)
    {
        if (bytes.Length > 65536 || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            throw new InvalidDataException("InvalidFrontlineMonth");
        using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
        var days = new List<FrontlineDay>();
        foreach (var value in json.RootElement.GetProperty("data").GetProperty("days").EnumerateArray())
        {
            var date = DateOnly.ParseExact(value.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var start = value.GetProperty("startsAt").GetDateTimeOffset(); var end = value.GetProperty("endsAt").GetDateTimeOffset();
            var midnight = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(9));
            if (start != midnight || end != midnight.AddDays(1)) throw new InvalidDataException("InvalidFrontlineReset");
            days.Add(new(date, value.GetProperty("name").GetString()!, value.GetProperty("subtitle").GetString()!));
        }
        var result = new FrontlineMonth(now, days);
        if (!ValidCache(result, first, now)) throw new InvalidDataException("InvalidFrontlineDays");
        return result;
    }
    private static bool ValidCache(FrontlineMonth month, DateOnly first, DateTimeOffset now)
    {
        if (month.FetchedAt > now || now - month.FetchedAt > TimeSpan.FromHours(24) || month.Days is null || month.Days.Count != DateTime.DaysInMonth(first.Year, first.Month)) return false;
        var expected = new Dictionary<string, string> { ["쟁탈전"] = "봉인된 바위섬", ["쇄빙전"] = "영광의 평원", ["계절끝 합전"] = "온살 하카이르", ["제압전"] = "외곽 유적지대", ["연습전"] = "워코 치테" };
        return month.Days.All(d => d is not null) && month.Days.Select(d => d.Date).Distinct().Count() == month.Days.Count && month.Days.All(d => d.Date.Year == first.Year && d.Date.Month == first.Month &&
            expected.TryGetValue(d.Name ?? "", out var map) && map == d.Map);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel();
        var request = activeRequest; activeRequest = null;
        _ = (pending ?? Task.CompletedTask).ContinueWith(_ =>
        {
            request?.Dispose(); lifetime.Dispose(); if (ownsHttp) http.Dispose();
        }, TaskScheduler.Default);
    }
}
