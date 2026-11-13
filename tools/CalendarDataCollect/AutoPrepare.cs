using System.Text.Json;
using DuckbillCalendar.Core;
using DuckbillCalendar.Tools;

namespace DuckbillCalendar.Collect;

internal static class AutoPrepare
{
    public const int MaximumRetainedEvents = 500;
    private const string MissingMarker = "[이번 첫 페이지에서 미확인: 삭제·취소·종료 판정이 아니며 이전 근거를 유지합니다.]";
    public static async Task<(CollectionManifest Manifest, CandidateDocument Candidates)> ReadCollectionAsync(string directory)
    {
        var manifest = CalendarJson.Parse<CollectionManifest>(await LocalInput.ReadAsync(Path.Combine(directory, "collection.json"), 65536));
        var bytes = await LocalInput.ReadAsync(Path.Combine(directory, "source.html"));
        if (manifest.SchemaVersion != 1 || manifest.Source != EventPageParser.PageAddress(manifest.Page) || manifest.Status != 200 || manifest.CompleteCoverage ||
            manifest.FetchedAt == default || manifest.FetchedAt > DateTimeOffset.UtcNow.AddMinutes(5) || manifest.Bytes != bytes.Length || manifest.SnapshotSha256 != Collector.Hash(bytes))
            throw new InvalidDataException("CollectionProvenanceMismatch");
        var candidates = await EventPageParser.ParseAsync(Collector.Utf8.GetString(bytes), manifest.FetchedAt, strictAuto: true);
        if (candidates.IsSynthetic || candidates.Errors.Count != 0 || candidates.Candidates.Count is < 1 or > 100 ||
            candidates.Candidates.Any(c => c.Phases.Count == 0 || c.Phases.Any(p => p.StartsAt is null)))
            throw new InvalidDataException("AutomaticCollectionRejected:" + string.Join(",", candidates.Errors.Take(5)));
        return (manifest, candidates);
    }

    public static async Task<FeedDocument> PrepareAsync(string directory, FeedDocument? previous)
    {
        return PrepareMany([await ReadCollectionAsync(directory)], previous);
    }

    public static FeedDocument PrepareMany(IReadOnlyList<(CollectionManifest Manifest, CandidateDocument Candidates)> pages, FeedDocument? previous)
    {
        if (pages.Count is < 1 or > 8) throw new InvalidDataException("EventPageLimit");
        var groups = pages.SelectMany(p => p.Candidates.Candidates.Select(c => (p.Manifest, Candidate: c))).GroupBy(p => p.Candidate.Id, StringComparer.Ordinal).ToArray();
        if (groups.Any(g => g.Select(p => p.Candidate.NormalizedRecordHash).Distinct(StringComparer.Ordinal).Count() != 1)) throw new InvalidDataException("ConflictingEventPages");
        var observed = groups.Select(g => g.First()).ToArray();
        if (previous is not null && (previous.ContentRevision < 1 || FeedValidator.Validate(previous).Count != 0)) throw new InvalidDataException("InvalidPreviousFeed");
        var oldEvents = (previous?.Events ?? []).ToDictionary(e => e.Id, StringComparer.Ordinal);
        var listedIds = observed.Select(c => c.Candidate.Id).ToHashSet(StringComparer.Ordinal);
        var retained = (previous?.Events ?? []).Where(e => !listedIds.Contains(e.Id)).Select(e => e.Id.StartsWith("kr:event:", StringComparison.Ordinal) && !(e.Note ?? "").Contains(MissingMarker, StringComparison.Ordinal)
            ? e with { Note = (e.Note is { Length: > 0 } ? e.Note + "\n" : "") + MissingMarker } : e).ToArray();
        if (retained.Length + observed.Length > MaximumRetainedEvents) throw new InvalidDataException("RetainedEventLimitRequiresMaintenance");
        var events = new List<CalendarEvent>();
        var evidence = new List<EvidenceRecord>();
        foreach (var (manifest, candidate) in observed)
        {
            oldEvents.TryGetValue(candidate.Id, out var old);
            var phaseIds = StablePhaseIds(candidate.Phases, old?.Phases ?? []);
            var evidenceId = candidate.Id + ":list:" + manifest.SnapshotSha256;
            evidence.Add(new()
            {
                Id = evidenceId, SourceId = "kr-events", Url = manifest.Source, ObservedAt = manifest.FetchedAt, SourceSnapshotSha256 = manifest.SnapshotSha256,
                Note = $"기계 검증: 공식 목록 {manifest.Page}페이지의 구조화된 time 필드와 표시 시각 일치. 사람/LLM 검수 아님. 개별 안내: {candidate.SourceUrl}"
            });
            events.Add(new()
            {
                Id = candidate.Id, Revision = old is null ? 1 : checked(old.Revision + 1), Title = candidate.Title,
                Category = old?.Category ?? "web-event", Lifecycle = old?.Lifecycle ?? "published",
                Note = "공식 목록의 구조화된 기간을 자동 반영. 본문 조건은 원문 확인. 기간 원문: " + string.Join(" / ", candidate.Phases.Select(p => p.RawText)),
                Phases = candidate.Phases.Select((p, index) => new EventPhase
                {
                    Id = phaseIds[index],
                    Label = p.Label,
                    Start = CalendarBoundary.Instant(p.StartsAt!.Value) with { EvidenceIds = [evidenceId] },
                    End = p.EndsAt is { } end ? CalendarBoundary.Instant(end) with { EvidenceIds = [evidenceId] } : new() { Reason = "not-announced" },
                    EndInclusion = "inclusive-unit"
                }).ToArray()
            });
        }
        events.AddRange(retained);
        var retainedEvidenceIds = retained.SelectMany(e => e.Phases).SelectMany(p => p.Start.EvidenceIds.Concat(p.End.EvidenceIds))
            .Concat((previous?.Targets ?? []).Where(t => t.ResolvedBoundary is not null).SelectMany(t => t.ResolvedBoundary!.EvidenceIds)).ToHashSet(StringComparer.Ordinal);
        evidence.AddRange((previous?.Evidence ?? []).Where(e => retainedEvidenceIds.Contains(e.Id) && evidence.All(current => current.Id != e.Id)));
        if (evidence.Count > 4000) throw new InvalidDataException("RetainedEvidenceLimitRequiresMaintenance");
        var feed = new FeedDocument
        {
            ContentRevision = previous?.ContentRevision ?? 1, GeneratedAt = pages.Max(p => p.Manifest.FetchedAt), Events = events, Evidence = evidence,
            Targets = previous?.Targets ?? [], Withdrawals = previous?.Withdrawals ?? [], FrontlineRules = previous?.FrontlineRules ?? []
        };
        var errors = FeedValidator.Validate(feed);
        if (errors.Count != 0) throw new InvalidDataException("AutomaticFeedRejected:" + string.Join(",", errors.Take(8)));
        if (JsonSerializer.SerializeToUtf8Bytes(feed, CalendarJson.Options).Length > Collector.MaximumBytes) throw new InvalidDataException("AutomaticFeedSizeLimit");
        return feed;
    }
    internal static IReadOnlyList<string> StablePhaseIds(IReadOnlyList<PhaseCandidate> current, IReadOnlyList<EventPhase> previous)
    {
        static string Label(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        static string Role(string text) => text.Contains("수령", StringComparison.Ordinal) || text.Contains("보상", StringComparison.Ordinal) ? "reward" : "participation";
        if (current.GroupBy(p => Label(p.Label), StringComparer.Ordinal).Any(g => g.Count() != 1)) throw new InvalidDataException("AmbiguousEventPhaseIdentity");
        var result = new string[current.Count]; var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < current.Count; i++)
        {
            var matches = previous.Where(p => Label(p.Label) == Label(current[i].Label)).ToArray();
            if (matches.Length == 1 && used.Add(matches[0].Id)) result[i] = matches[0].Id;
        }
        for (var i = 0; i < current.Count; i++)
        {
            if (result[i] is not null) continue;
            var role = Role(current[i].Label);
            // A single unmatched old/new phase may be a replacement, not a rename.
            // Do not transfer a user's hidden/alert key on role or dates alone.
            var id = role + "-" + Collector.Hash(System.Text.Encoding.UTF8.GetBytes(Label(current[i].Label)))[..12];
            if (previous.Any(p => p.Id == id) || !used.Add(id)) throw new InvalidDataException("AmbiguousEventPhaseIdentity");
            result[i] = id;
        }
        return result;
    }

    public static async Task<(CurrentPointer Pointer, FeedDocument Feed, byte[] Payload)> VerifyCurrentAsync(string directory)
    {
        var pointer = CalendarJson.Parse<CurrentPointer>(await LocalInput.ReadAsync(Path.Combine(directory, "current.json"), 65536));
        if (FeedValidator.Validate(pointer).Count != 0) throw new InvalidDataException("InvalidPreviousPointer");
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var payloadPath = Path.GetFullPath(Path.Combine(directory, pointer.Content.Path));
        if (!payloadPath.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("PreviousPayloadEscapesRoot");
        var payload = await LocalInput.ReadAsync(payloadPath);
        var feed = FeedPublication.ReadPayload(pointer, payload);
        return (pointer, feed, payload);
    }

    public static async Task MarkPartialAsync(string collectionDirectory, string publishedDirectory)
    {
        await using var publicationLock = new FileStream(Path.Combine(publishedDirectory, ".prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var (manifest, candidates) = await ReadCollectionAsync(collectionDirectory);
        var (pointer, feed, _) = await VerifyCurrentAsync(publishedDirectory);
        foreach (var candidate in candidates.Candidates)
        {
            var item = feed.Events.SingleOrDefault(e => e.Id == candidate.Id) ?? throw new InvalidDataException("PartialHealthFeedMismatch");
            var ids = item.Phases.SelectMany(p => p.Start.EvidenceIds).ToHashSet(StringComparer.Ordinal);
            if (!feed.Evidence.Any(e => ids.Contains(e.Id) && e.SourceId == "kr-events" && e.SourceSnapshotSha256 == manifest.SnapshotSha256 && e.ObservedAt == manifest.FetchedAt))
                throw new InvalidDataException("PartialHealthEvidenceMismatch");
        }
        var updated = pointer with { Health = new() { Sources = pointer.Health.Sources.Select(s => s.Id == "kr-events" ? s with
        {
            LastAttemptAt = manifest.FetchedAt, LastSuccessAt = manifest.FetchedAt, LastResult = "partial", CompleteCoverage = false,
            ComparedContentRevision = pointer.Content.Revision
        } : s).ToArray() } };
        if (FeedValidator.Validate(updated).Count != 0) throw new InvalidDataException("InvalidPartialHealth");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(updated, CalendarJson.Options);
        if (bytes.Length > 65536) throw new InvalidDataException("PartialPointerSizeLimit");
        var path = Path.Combine(publishedDirectory, "current.json");
        var temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
        try { await Collector.WriteNewAsync(temporary, bytes); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
