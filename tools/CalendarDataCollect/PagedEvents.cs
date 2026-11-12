using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal sealed record EventPageManifest
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<int> Pages { get; init; } = [];
}

internal static class PagedEvents
{
    public static IReadOnlyList<int> LinkedPages(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var pages = new SortedSet<int> { 1 };
        foreach (var anchor in document.QuerySelectorAll("nav.page a[href]"))
        {
            if (!Uri.TryCreate(new Uri(EventPageParser.Source), anchor.GetAttribute("href"), out var url) ||
                url.Scheme != "https" || url.Host != "www.ff14.co.kr" || url.AbsolutePath != "/news/event") continue;
            var match = Regex.Match(url.Query, @"^\?category=1&page=(\d+)$", RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups[1].Value, out var page) || page is < 1 or > 8) throw new InvalidDataException("EventPaginationLimit");
            pages.Add(page);
        }
        return pages.ToArray();
    }

    public static async Task CollectAsync(string directory, HttpClient http, CancellationToken ct)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("EventPagesAlreadyExist");
        Directory.CreateDirectory(directory);
        var expected = new SortedSet<int> { 1 };
        var collected = new HashSet<int>();
        while (expected.Any(p => !collected.Contains(p)))
        {
            var page = expected.First(p => !collected.Contains(p));
            var part = Path.Combine(directory, "page-" + page.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
            if (await Collector.CollectAsync(part, http, ct, page) != 0) throw new InvalidDataException("EventPageWarnings:" + page);
            _ = await AutoPrepare.ReadCollectionAsync(part);
            var html = Collector.Utf8.GetString(await LocalInput.ReadAsync(Path.Combine(part, "source.html")));
            foreach (var linked in LinkedPages(html)) expected.Add(linked);
            collected.Add(page);
        }
        await Collector.WriteNewAsync(Path.Combine(directory, "pages.json"), JsonSerializer.SerializeToUtf8Bytes(new EventPageManifest { Pages = collected.Order().ToArray() }, CalendarJson.Options));
    }

    public static async Task<FeedDocument> PrepareAsync(string directory, FeedDocument? previous)
    {
        var index = CalendarJson.Parse<EventPageManifest>(await LocalInput.ReadAsync(Path.Combine(directory, "pages.json"), 65536));
        if (index.SchemaVersion != 1 || index.Pages.Count is < 1 or > 8 || !index.Pages.Contains(1) ||
            index.Pages.Distinct().Count() != index.Pages.Count || index.Pages.Any(p => p is < 1 or > 8)) throw new InvalidDataException("InvalidEventPages");
        var pages = new List<(CollectionManifest Manifest, CandidateDocument Candidates)>();
        foreach (var page in index.Pages.Order())
        {
            var part = Path.Combine(directory, "page-" + page.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
            var value = await AutoPrepare.ReadCollectionAsync(part);
            if (value.Manifest.Page != page) throw new InvalidDataException("EventPageNumberMismatch");
            var links = LinkedPages(Collector.Utf8.GetString(await LocalInput.ReadAsync(Path.Combine(part, "source.html"))));
            if (links.Except(index.Pages).Any()) throw new InvalidDataException("EventPagesIncomplete");
            pages.Add(value);
        }
        return AutoPrepare.PrepareMany(pages, previous);
    }
}
