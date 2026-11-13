using System.Globalization;
using System.Text;
using System.Text.Json;
using DuckbillCalendar.Core;

var checks = new List<string>();
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    checks.Add(description);
}
CalendarBoundary Unknown() => new() { Kind = "unknown", Reason = "not-announced" };
CalendarBoundary Exact(string value) => new() { Kind = "instant", Value = value, Precision = "minute", Certainty = "confirmed", EvidenceIds = ["ev:1"] };
CalendarBoundary Date(string value) => new() { Kind = "date", Value = value, Timezone = "Asia/Seoul", Certainty = "confirmed", EvidenceIds = ["ev:1"] };
CalendarEvent Event(string id, string start, string end, string phase = "participation", string inclusion = "exclusive") => new()
{
    Id = "kr:" + id, Title = id, Category = "web-event",
    Phases = [new() { Id = phase, Label = phase, Start = Exact(start), End = Exact(end), EndInclusion = inclusion }]
};
FeedDocument Feed(params CalendarEvent[] events) => new()
{
    ContentRevision = 1, GeneratedAt = DateTimeOffset.Parse("2026-11-11T00:00:00Z"), Events = events,
    Evidence = [new() { Id = "ev:1", SourceId = "kr-events", Url = "https://www.ff14.co.kr/news/event", ObservedAt = DateTimeOffset.Parse("2026-11-11T00:00:00Z"), SourceSnapshotSha256 = new string('a', 64) }]
};
FeedHealth Fresh(long revision = 1, IReadOnlyList<PendingChange>? pending = null) => new()
{
    Sources = [new() { Id = "kr-events", LastAttemptAt = DateTimeOffset.Parse("2026-11-11T10:00:00Z"), LastSuccessAt = DateTimeOffset.Parse("2026-11-11T10:00:00Z"), LastResult = "success", CompleteCoverage = true, ComparedContentRevision = revision, PendingChanges = pending ?? [] }]
};
var now = DateTimeOffset.Parse("2026-11-11T11:00:00Z");
var spanning = Event("long", "2026-10-01T00:00:00+09:00", "2027-01-01T00:00:00+09:00");
Check(CalendarEngine.Today(DateTimeOffset.Parse("2026-11-10T15:00:00Z")) == new DateOnly(2026, 11, 11), "UTC to Korean date at 0:00");
Check(CalendarEngine.Today(DateTimeOffset.Parse("2026-11-10T14:59:59Z")) == new DateOnly(2026, 11, 10), "UTC before Korean date boundary");
Check(CalendarEngine.BoundaryDate(Date("2028-02-29")) == new DateOnly(2028, 2, 29), "Leap date preserved");
Check(CalendarEngine.BoundaryDate(Date("2027-02-29")) is null, "Invalid leap date not normalized");
Check(CalendarEngine.StartInstant(Date("2026-11-11")) is null, "Date-only never becomes instant");
Check(CalendarEngine.StartInstant(Exact("2026-11-11T20:00:00")) is null, "Missing offset rejected");
Check(CalendarEngine.StartInstant(Unknown()) is null, "Unknown stays unknown");
var month = CalendarEngine.BuildMonth([spanning], 2026, 11);
Check(month.Weeks.Count == 5, "November 2026 has five calendar weeks");
Check(month.Weeks.SelectMany(w => w.Bars).Count() == 5, "Whole-month interval splits across all weeks");
Check(month.Weeks[0].Bars[0].ContinuesBefore && month.Weeks[^1].Bars[0].ContinuesAfter, "Clipped interval has both continuation edges");
Check(CalendarEngine.MonthEvents([spanning], 2026, 11).Count == 1, "Month agenda includes interval without start/end inside month");
Check(CalendarEngine.DayEvents([spanning], new(2026, 11, 17)).Single().Ongoing, "Middle day includes ongoing phase");
Check(CalendarEngine.BuildMonth([spanning], 2026, 11, false).Weeks.All(w => w.Bars.Count == 0), "Endpoint mode removes all bars");
var midnight = Event("midnight", "2026-11-10T10:00:00+09:00", "2026-11-12T00:00:00+09:00");
var midBars = CalendarEngine.BuildMonth([midnight], 2026, 11).Weeks.SelectMany(w => w.Bars).Single();
Check(midBars.StartColumn == 2 && midBars.EndColumn == 3, "Exclusive midnight interval excludes end date from bar");
Check(CalendarEngine.DayEvents([midnight], new(2026, 11, 12)).Single().Edge == "end", "Exclusive midnight remains an explicit end-date marker");
var minuteEnd = Event("minute", "2026-11-11T00:00:00+09:00", "2026-11-11T23:59:00+09:00", inclusion: "inclusive-unit");
Check(CalendarEngine.EndExclusive(minuteEnd.Phases[0]) == DateTimeOffset.Parse("2026-11-12T00:00:00+09:00"), "Inclusive minute ends at next minute");
var overlapping = Enumerable.Range(0, 5).Select(i => Event("overlap" + i, "2026-11-09T00:00:00+09:00", "2026-11-15T00:00:00+09:00")).ToArray();
var overWeek = CalendarEngine.BuildMonth(overlapping, 2026, 11).Weeks[1];
Check(overWeek.LaneCount == 3 && overWeek.Bars.Count == 3, "Period lanes limited to three");
Check(overWeek.Days.Single(d => d.Date == new DateOnly(2026, 11, 11)).HiddenCount == 2, "Overflow periods counted on intermediate day");
Check(CalendarEngine.DayEvents(overlapping, new(2026, 11, 11)).Count == 5, "Full day details preserve hidden periods");
var nonOverlap = new[] { Event("first", "2026-11-09T00:00:00+09:00", "2026-11-11T00:00:00+09:00"), Event("second", "2026-11-11T00:00:00+09:00", "2026-11-13T00:00:00+09:00") };
Check(CalendarEngine.BuildMonth(nonOverlap, 2026, 11).Weeks[1].Bars.All(b => b.Lane == 0), "Non-overlapping bars reuse lane");
var reward = Event("reward", "2026-11-01T00:00:00+09:00", "2026-12-01T00:00:00+09:00", "reward-claim");
Check(CalendarEngine.BuildMonth([reward], 2026, 11).Weeks.All(w => w.Bars.Count == 0), "Reward bars hidden by default");
Check(CalendarEngine.BuildMonth([reward], 2026, 11, showRewardBars: true).Weeks.Any(w => w.Bars.Count != 0), "Reward bars enabled explicitly");
Check(CalendarEngine.BuildMonth([], 2028, 2).Weeks.SelectMany(w => w.Days).Any(d => d.Date == new DateOnly(2028, 2, 29)), "Leap day present in month grid");
var rule = new FrontlineRule { Id = "r1", Status = "verified", ValidFrom = "2026-11-01", ValidUntil = "2026-11-12", AnchorDate = "2026-11-11", AnchorIndex = 0, Sequence = ["A", "B", "C"] };
var ruled = Feed() with { FrontlineRules = [rule] };
Check(CalendarEngine.FrontlineFor(ruled, new(2026, 11, 10)).Name == "C", "Negative modulo before frontline anchor");
Check(!CalendarEngine.FrontlineFor(ruled, new(2026, 11, 12)).IsKnown, "Tomorrow checks its own rule validity");
Check(!CalendarEngine.FrontlineFor(ruled with { FrontlineRules = [rule with { AnchorDate = null }] }, new(2026, 11, 11)).IsKnown, "Missing anchor does not invent map");
Check(FeedValidator.Validate(ruled with { FrontlineRules = [rule, rule with { Id = "r2" }] }).Any(x => x.Contains("OverlappingRules")), "Overlapping frontline validity rejected");
Check(FeedValidator.Validate(new FeedDocument()).Count == 0, "Revision-zero empty offline feed valid");
var feed = Feed(minuteEnd);
Check(FeedValidator.Validate(feed).Count == 0, "Verified production-shaped fixture passes validation");
Check(FeedValidator.Validate(feed with { IsExample = true }).Contains("ExampleFeedForbidden"), "Production rejects example feed");
Check(FeedValidator.Validate(feed with { IsExample = true }, true).Count == 0, "Explicit example inspection allowed");
Check(FeedValidator.Validate(feed with { Events = [minuteEnd with { IsPersonal = true }] }).Any(x => x.StartsWith("PublicFeedContainsPersonalData")), "Personal flag cannot bypass public provenance");
Check(FeedValidator.Validate(feed with { Events = [minuteEnd, minuteEnd] }).Any(x => x.StartsWith("DuplicateId")), "Duplicate event IDs rejected");
Check(FeedValidator.Validate(feed with { Events = [minuteEnd with { Id = null! }] }).Count > 0, "Typed null ID fails without exception");
Check(FeedValidator.Validate(feed with { Events = null! }).Any(x => x.StartsWith("NullCollection")), "Typed null collection fails");
Check(FeedValidator.Validate(feed with { Events = [null!] }).Any(x => x.StartsWith("NullElement")), "Typed null collection element fails");
Check(FeedValidator.Validate(feed with { Events = [minuteEnd with { Phases = [minuteEnd.Phases[0] with { Start = null! }] }] }).Any(x => x.StartsWith("NullBoundary")), "Typed null boundary fails");
Check(FeedValidator.Validate(feed with { Events = [minuteEnd with { Phases = [minuteEnd.Phases[0] with { Start = Exact("2026-11-11T00:00:00") }] }] }).Any(x => x.StartsWith("InvalidInstant")), "Validator rejects missing UTC offset");
Check(FeedValidator.Validate(feed with { Evidence = [feed.Evidence[0] with { SourceSnapshotSha256 = null }] }).Any(x => x.StartsWith("Evidence.UnverifiedProvenance")), "Production requires provenance hash");
Check(FeedValidator.Validate(feed with { Events = [Event("reverse", "2026-11-12T00:00:00+09:00", "2026-11-11T00:00:00+09:00")] }).Any(x => x.StartsWith("Reversed")), "Reversed interval rejected");
var conditional = minuteEnd with { Phases = [minuteEnd.Phases[0] with { End = new() { Kind = "condition", Condition = new() { Type = "patch-maintenance", TargetId = "kr:patch:8", Relation = "before" }, DisplayText = "패치 전", Certainty = "confirmed", EvidenceIds = ["ev:1"] } }] };
Check(FeedValidator.Validate(Feed(conditional)).Any(x => x.StartsWith("UnknownOrMismatchedTarget")), "Unregistered conditional target rejected");
Check(FeedValidator.Validate(Feed(conditional) with { Targets = [new() { Id = "kr:patch:8", Kind = "patch-maintenance" }] }).Count == 0, "Known future unresolved target is valid");
Check(CalendarEngine.EndExclusive(conditional.Phases[0]) is null, "Condition does not invent timestamp");
Check(CalendarEngine.MonthEvents([conditional], 2026, 12).Count == 1, "Earlier-start conditional period stays in month agenda pending an end date");
Check(CalendarEngine.MonthEvents([conditional], 2026, 10).Count == 0, "Conditional period does not appear before its known start");
Check(CalendarEngine.BuildMonth([conditional], 2026, 12).Weeks.All(w => w.Bars.Count == 0), "Conditional end does not invent a continuous bar");
Check(CalendarEngine.DayEvents([conditional], new(2026, 12, 1)).Count == 0, "Unknown end does not assert confirmed ongoing day");
var unknownEnd = conditional with { Phases = [conditional.Phases[0] with { End = Unknown() }] };
Check(CalendarEngine.MonthEvents([unknownEnd], 2026, 12).Count == 1, "Earlier-start unannounced end remains discoverable in month agenda");
Check(CalendarEngine.MonthEvents([conditional with { Lifecycle = "cancelled" }], 2026, 12).Count == 0, "Cancelled unresolved period is not carried into later agendas");
var singlePoint = conditional with { Phases = [conditional.Phases[0] with { End = Unknown(), Point = true }] };
Check(CalendarEngine.MonthEvents([singlePoint], 2026, 12).Count == 0, "A past personal point does not become an open-ended period");
var entry = new PersonalEntry { Id = "local-1", Title = "정액제", Kind = "subscription", When = Exact("2026-11-11T21:00:00+09:00") };
var local = new LocalState { Entries = [entry], Alerts = [new() { EventId = "local-1", PhaseId = "local", Edge = "end", LeadMinutes = 60 }] };
Check(FeedValidator.ValidateLocal(local).Count == 0, "Local expiry validates without public evidence");
var merged = CalendarEngine.Merge(new(), local);
Check(merged.Single().Id == entry.Id && merged.Single().IsPersonal && merged.Single().Phases[0].Id == "local", "Local IDs and phase identity preserved");
Check(CalendarEngine.Merge(new(), local with { ActiveProfileId = "other" }).Count == 0, "Other profile entries excluded");
Check(CalendarEngine.Merge(feed with { Withdrawals = [minuteEnd.Id] }, new()).Count == 0, "Withdrawn public event excluded");
Check(CalendarEngine.Merge(feed with { Events = [minuteEnd with { Lifecycle = "cancelled" }] }, new()).Count == 0, "Cancelled event excluded");
var alerts = AlertPlanner.Plan(new(), local, now);
Check(alerts.Count == 1, "Offline personal expiry alert planned");
Check(AlertPlanner.Plan(new(), local with { SentAlertKeys = [alerts[0].Key] }, now).Count == 0, "Persisted alert key prevents duplicate");
Check(AlertPlanner.Plan(new(), local, now.AddHours(2)).Count == 0, "Old missed alerts not replayed");
Check(AlertPlanner.Plan(new(), local with { Entries = [entry with { When = Date("2026-11-11") }] }, now).Count == 0, "Date-only creates no timed alert");
Check(AlertPlanner.Plan(new(), local with { Entries = [entry with { When = entry.When with { Certainty = "inferred" } }] }, now).Count == 0, "Inferred instant creates no confirmed alert");
var publicEvent = Event("public", "2026-11-10T00:00:00+09:00", "2026-11-11T21:00:00+09:00");
var publicFeed = Feed(publicEvent);
var publicLocal = new LocalState { Alerts = [new() { EventId = publicEvent.Id, PhaseId = "participation", Edge = "end", LeadMinutes = 60 }] };
Check(AlertPlanner.Plan(publicFeed, publicLocal, now).Count == 0, "Public alert held without health observation");
Check(AlertPlanner.Plan(publicFeed, publicLocal, now, Fresh()).Count == 1, "Public exact alert allowed with matching coverage");
Check(AlertPlanner.Plan(publicFeed with { IsExample = true }, publicLocal, now, Fresh()).Count == 0, "Example public feed cannot emit alerts even with fresh-looking health");
Check(AlertPlanner.Plan(new() { IsExample = true }, local, now).Count == 1, "Example mode does not suppress independent personal expiry");
var twoProfiles = publicLocal with { Profiles = [new(), new() { Id = "second", Alias = "다른 계정" }], ActiveProfileId = "second" };
Check(AlertPlanner.Plan(publicFeed, twoProfiles, now, Fresh()).Count == 0, "Switching profile does not inherit another profile's public alert choice");
Check(AlertPlanner.Plan(publicFeed, twoProfiles with { Alerts = [publicLocal.Alerts[0] with { ProfileId = "second" }] }, now, Fresh()).Count == 1, "Active profile's explicit alert choice is used");
Check(FeedValidator.ValidateLocal(twoProfiles with { Alerts = [publicLocal.Alerts[0] with { ProfileId = "missing" }] }).Contains("UnknownAlertProfile"), "Alert choices require a known profile");
Check(FeedValidator.ValidateLocal(twoProfiles with { Alerts = [publicLocal.Alerts[0], publicLocal.Alerts[0] with { ProfileId = "second" }] }).Count == 0, "Different profiles can independently select the same public boundary");
Check(!HealthFreshness.ForEvent(publicFeed, Fresh(2), publicEvent, now).IsFresh, "New-content coverage cannot freshen old active content");
Check(!HealthFreshness.ForEvent(publicFeed, new() { Sources = [new() { Id = "kr-events" }] }, publicEvent, now).IsFresh, "Null hashes and not-run do not prove freshness");
var noImpact = new PendingChange { Kind = "new", FirstObservedAt = now, AffectedEventIds = [] };
Check(HealthFreshness.ForEvent(publicFeed, Fresh(pending: [noImpact]), publicEvent, now).IsFresh, "New candidate does not stale existing event");
Check(!HealthFreshness.ForEvent(publicFeed, Fresh(pending: [noImpact with { AffectedEventIds = null }]), publicEvent, now).IsFresh, "Unknown impact holds entire source");
Check(!HealthFreshness.ForEvent(publicFeed, Fresh(pending: [noImpact with { AffectedEventIds = [publicEvent.Id] }]), publicEvent, now).IsFresh, "Known pending change holds affected event");
Check(HealthFreshness.ForEvent(publicFeed, Fresh(pending: [noImpact with { AffectedEventIds = ["kr:other"] }]), publicEvent, now).IsFresh, "Pending change leaves other event fresh");
Check(HealthFreshness.ForEvent(publicFeed, Fresh() with { Sources = [Fresh().Sources[0] with { LastReviewedAt = now.AddDays(-50), NormalizedSourceHash = new string('a', 64), ApprovedSourceHash = new string('b', 64) }] }, publicEvent, now).IsFresh, "Old review and different aggregate hashes alone do not stale event");
var reference = new ContentReference { Revision = 1, Path = "revisions/000001.json", Sha256 = new string('a', 64), Bytes = 100 };
var pointer = new CurrentPointer { PublicationSequence = 1, GeneratedAt = now, Content = reference, Health = Fresh() };
Check(FeedValidator.Validate(pointer).Count == 0, "Current pointer validates");
Check(FeedValidator.Validate(pointer with { Content = reference with { Path = "revisions/../secret.json" } }).Contains("Pointer.InvalidPath"), "Payload traversal rejected");
Check(FeedValidator.Validate(pointer with { Health = new() { Sources = [Fresh().Sources[0] with { CompleteCoverage = true, LastSuccessAt = null }] } }).Any(x => x.StartsWith("Health.UnprovenCoverage")), "Unproven full coverage rejected");
foreach (var missing in new[] { "{}", "null" })
{
    try { var parsed = CalendarJson.Parse<FeedDocument>(Encoding.UTF8.GetBytes(missing)); Check(FeedValidator.Validate(parsed).Count != 0, "Incomplete JSON rejected by validation"); }
    catch (JsonException) { Check(true, "Incomplete JSON rejected by parser"); }
}
Check(!HealthFreshness.ForEvent(publicFeed, Fresh() with { Sources = [Fresh().Sources[0] with { LastAttemptAt = null }] }, publicEvent, now).IsFresh,
    "Successful coverage without an attempt timestamp remains unverified");
Check(FeedValidator.Validate(pointer with { Health = Fresh() with { Sources = [Fresh().Sources[0] with { LastAttemptAt = null }] } }).Any(x => x.StartsWith("Health.UnprovenCoverage")),
    "Pointer rejects coverage without last attempt");
Check(!HealthFreshness.ForEvent(publicFeed, Fresh() with { Sources = [Fresh().Sources[0] with { LastSuccessAt = now, LastAttemptAt = now.AddMinutes(-1) }] }, publicEvent, now).IsFresh,
    "Success after recorded attempt is not accepted as an observation");
Check(!HealthFreshness.ForEvent(publicFeed, Fresh(), publicEvent, DateTimeOffset.MaxValue).IsFresh,
    "Extreme injected clock yields expired observation without overflow");
Check(!HealthFreshness.ForEvent(publicFeed, Fresh() with { Sources = [Fresh().Sources[0] with { LastSuccessAt = now.AddMinutes(6), LastAttemptAt = now.AddMinutes(6) }] }, publicEvent, now).IsFresh,
    "Future source timestamp beyond clock tolerance is not fresh");
foreach (var invalidJson in new[]
{
    "{\"kind\":\"unknown\",\"reason\":\"not-announced\",\"reason\":\"unsupported\"}",
    "{\"kind\":\"unknown\",\"reason\":\"not-announced\",\"value\":null}",
    "{\"kind\":\"instant\",\"value\":\"2026-11-11T00:00:00Z\",\"precision\":\"minute\",\"certainty\":\"confirmed\"}"
})
{
    bool rejected = false;
    try { CalendarJson.Parse<CalendarBoundary>(Encoding.UTF8.GetBytes(invalidJson)); }
    catch (JsonException) { rejected = true; }
    Check(rejected, "Wire boundary rejects duplicate/mixed/missing fields");
}
foreach (var boundary in new[] { Exact("2026-11-11T00:00:00Z"), Date("2026-11-11"), Unknown(), conditional.Phases[0].End })
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(boundary, CalendarJson.Options);
    var restored = CalendarJson.Parse<CalendarBoundary>(bytes);
    Check(restored.Kind == boundary.Kind && restored.Value == boundary.Value && restored.Condition?.TargetId == boundary.Condition?.TargetId,
        "Boundary wire round trip preserves variant and unresolved condition");
}
var originalCulture = CultureInfo.CurrentCulture;
var originalUiCulture = CultureInfo.CurrentUICulture;
try
{
    var selectedDate = new DateOnly(2026, 11, 12);
    var selectedInstant = new DateTimeOffset(2026, 11, 12, 14, 25, 0, TimeSpan.FromHours(9));
    foreach (var name in new[] { "ar-SA", "ko-KR", "en-US" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        var dateBoundary = CalendarBoundary.Date(selectedDate);
        var instantBoundary = CalendarBoundary.Instant(selectedInstant);
        var dateRestored = CalendarJson.Parse<CalendarBoundary>(JsonSerializer.SerializeToUtf8Bytes(dateBoundary, CalendarJson.Options));
        var instantRestored = CalendarJson.Parse<CalendarBoundary>(JsonSerializer.SerializeToUtf8Bytes(instantBoundary, CalendarJson.Options));
        Check(dateBoundary.Value == "2026-11-12" && CalendarEngine.BoundaryDate(dateRestored) == selectedDate &&
            CalendarEngine.StartInstant(instantRestored) == selectedInstant,
            "Factory and JSON round trip preserve selected Gregorian date/instant under " + name);
    }
}
finally
{
    CultureInfo.CurrentCulture = originalCulture;
    CultureInfo.CurrentUICulture = originalUiCulture;
}
WireChecks.Run(Check);
PublicationChecks.Run(Check);
ActivityAndHidingChecks.Run(Check);
NotificationChecks.Run(Check);
WeeklyScheduleChecks.Run(Check);
OfficialLinkChecks.Run(Check);
var oldStateNode = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(new LocalState(), CalendarJson.Options))!;
oldStateNode["view"]!.AsObject().Remove("remoteRefreshMinutes");
oldStateNode["view"]!.AsObject().Remove("showFrontlineInCalendar");
oldStateNode["view"]!.AsObject().Remove("calendarAppearances");
oldStateNode["view"]!.AsObject().Remove("hiddenPhases");
oldStateNode["view"]!.AsObject().Remove("pauseAutomaticUpdatesWhenBusy");
oldStateNode["view"]!.AsObject().Remove("hideWindowInCombat");
oldStateNode["view"]!.AsObject().Remove("hideWindowInDuty");
var oldView = CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(oldStateNode, CalendarJson.Options));
Check(oldView.View.RemoteRefreshMinutes == 60 && !oldView.View.ShowFrontlineInCalendar && oldView.View.CalendarAppearances.Count == 0, "Older state defaults to hourly refresh, clean date cells and standard appearance");
Check(oldView.View.HiddenPhases.Count == 0 && oldView.View.PauseAutomaticUpdatesWhenBusy && !oldView.View.HideWindowInCombat && !oldView.View.HideWindowInDuty, "Older settings retain entries and defer updates without automatically hiding the window");
oldStateNode["view"]!["hideWindowInCombat"] = true; oldStateNode["view"]!["hideWindowInDuty"] = true;
var removedHideSettings = CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(oldStateNode, CalendarJson.Options));
Check(!removedHideSettings.View.HideWindowInCombat && !removedHideSettings.View.HideWindowInDuty && FeedValidator.ValidateLocal(removedHideSettings).Count == 0,
    "Removed hide settings are accepted and discarded without rejecting existing local state");
Check(!JsonSerializer.Serialize(removedHideSettings, CalendarJson.Options).Contains("hideWindowInCombat", StringComparison.Ordinal), "Saving removes obsolete auto-hide settings");
Check(new ViewSettings().RemoteUpdatesEnabled, "New installations receive schedule updates by default");
var savedOffline = new LocalState { View = new() { RemoteUpdatesEnabled = false } };
Check(!CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(savedOffline, CalendarJson.Options)).View.RemoteUpdatesEnabled, "A saved offline choice is retained instead of overwritten by new defaults");
foreach (var interval in new[] { -1, 0, 1, 59, 1440 })
    Check(FeedValidator.ValidateLocal(new() { View = new() { RemoteRefreshMinutes = interval } }).Contains("InvalidRemoteRefreshInterval"), "Unsupported refresh interval rejected: " + interval);
var customized = new LocalState { View = new() { RemoteRefreshMinutes = 180, ShowFrontlineInCalendar = true,
    CalendarAppearances = [new() { Group = CalendarGroup.Personal, Color = "#abcdef", Icon = "Flag" }] } };
var customizedCopy = CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(customized, CalendarJson.Options));
Check(FeedValidator.ValidateLocal(customizedCopy).Count == 0 && customizedCopy.View.CalendarAppearances.Single().Color == "#abcdef" && customizedCopy.View.ShowFrontlineInCalendar,
    "Color, icon, frontline visibility and cadence survive serialization");
foreach (var appearance in new CalendarAppearance[] { new() { Icon = "Unknown" }, new() { Color = "abcdef" }, new() { Color = "#gggggg" }, new() { Color = null! }, new() { Group = (CalendarGroup)999 } })
    Check(FeedValidator.ValidateLocal(new() { View = new() { CalendarAppearances = [appearance] } }).Count > 0, "Malformed calendar appearance rejected");
Check(FeedValidator.ValidateLocal(new() { View = new() { CalendarAppearances = [new(), new()] } }).Contains("InvalidAppearanceGroup"), "Duplicate category appearance rejected");
PresentationChecks.Run(Check);
var chargeNow = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(9));
Check(SubscriptionTime.Extend(null, 90, chargeNow).Value == "2027-02-11", "First 90-day registration starts on today's KST date");
Check(SubscriptionTime.Extend(CalendarBoundary.Date(new(2026, 12, 13)), 90, chargeNow).Value == "2027-03-13", "90-day renewal keeps the existing 30 days");
Check(SubscriptionTime.Extend(CalendarBoundary.Date(new(2026, 10, 1)), 30, chargeNow).Value == "2026-12-13", "An expired service renews from today instead of a past date");
Check(SubscriptionTime.Extend(CalendarBoundary.Date(new(2028, 2, 28)), 2, chargeNow).Value == "2028-03-01", "Date-only renewal handles leap day without inventing a time");
var exactExpiry = CalendarBoundary.Instant(chargeNow.AddDays(3));
Check(CalendarEngine.StartInstant(SubscriptionTime.Extend(exactExpiry, 90, chargeNow)) == chargeNow.AddDays(93), "Timed renewal preserves the exact expiry time");
Check(CalendarEngine.StartInstant(SubscriptionTime.Extend(CalendarBoundary.Instant(chargeNow.AddDays(-3)), 30, chargeNow)) == chargeNow.AddDays(30), "Expired timed renewal starts at now");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(null, 0, 6, chargeNow)) == chargeNow.AddHours(6), "A new service can receive hours without requiring one day");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(exactExpiry, 30, 6, chargeNow)) == chargeNow.AddDays(33).AddHours(6), "Mixed day/hour renewal adds both durations to the active expiry instead of replacing its clock");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(CalendarBoundary.Instant(chargeNow.AddDays(-3)), 0, 36, chargeNow)) == chargeNow.AddHours(36), "Expired hourly renewal starts now and accepts more than 24 hours");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(CalendarBoundary.Date(new(2026, 12, 13)), 0, 6, chargeNow, new(21, 30))) ==
    new DateTimeOffset(2026, 12, 14, 3, 30, 0, TimeSpan.FromHours(9)), "Hourly renewal of a date-only expiry requires an explicit existing clock and carries into the next KST date");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(CalendarBoundary.Date(new(2026, 10, 1)), 0, 6, chargeNow)) == chargeNow.AddHours(6), "An expired date-only service does not require an irrelevant historical expiry clock");
var missingClockRejected = false;
try { SubscriptionTime.ExtendDuration(CalendarBoundary.Date(new(2026, 12, 13)), 0, 6, chargeNow); } catch (InvalidOperationException) { missingClockRejected = true; }
Check(missingClockRejected, "An unknown active expiry time cannot silently become midnight during hourly renewal");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(null, 0, 0, 1, chargeNow)) == chargeNow.AddMinutes(1), "Minute-only renewal accepts a single minute without requiring an hour");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(exactExpiry, 1, 2, 30, chargeNow)) == chargeNow.AddDays(4).AddHours(2).AddMinutes(30), "Renewal adds days, hours and minutes together to the active expiry");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(CalendarBoundary.Date(new(2026, 12, 13)), 0, 0, 30, chargeNow, new(23, 50))) ==
    new DateTimeOffset(2026, 12, 14, 0, 20, 0, TimeSpan.FromHours(9)), "Minute-only renewal carries a user-supplied existing clock across midnight");
var missingMinuteClockRejected = false;
try { SubscriptionTime.ExtendDuration(CalendarBoundary.Date(new(2026, 12, 13)), 0, 0, 30, chargeNow); } catch (InvalidOperationException) { missingMinuteClockRejected = true; }
Check(missingMinuteClockRejected, "Minute-only renewal cannot guess an active date-only expiry's clock");
Check(CalendarEngine.StartInstant(SubscriptionTime.ExtendDuration(CalendarBoundary.Instant(chargeNow.AddDays(-1)), 0, 0, 90, chargeNow)) == chargeNow.AddMinutes(90), "Expired service can renew by more than sixty minutes from now");
foreach (var invalidMinutes in new[] { (0, -1), (3650, 1), (int.MaxValue, int.MaxValue) })
{
    var rejected = false;
    try { SubscriptionTime.ExtendDuration(null, invalidMinutes.Item1, 0, invalidMinutes.Item2, chargeNow); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid minute duration is rejected: " + invalidMinutes);
}
foreach (var invalidDuration in new[] { (0, 0), (-1, 6), (0, -1), (3650, 1), (int.MaxValue, int.MaxValue) })
{
    var rejected = false;
    try { SubscriptionTime.ExtendDuration(null, invalidDuration.Item1, invalidDuration.Item2, chargeNow); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid total renewal duration is rejected: " + invalidDuration);
}
foreach (var invalidDays in new[] { -1, 0, 3651 })
{
    var rejected = false;
    try { SubscriptionTime.Extend(null, invalidDays, chargeNow); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid renewal day count is rejected: " + invalidDays);
}
var policyPhase = new EventPhase { Id = "period", Label = "점검" };
foreach (var title in new[] { "웹젠 PC방 점검", "크리스탈샵 점검", "공식 홈페이지 점검", "공식 가이드 점검" })
    Check(!CalendarContentPolicy.Includes(new() { Id = "kr:maintenance:aux", Category = "maintenance", Title = title }, policyPhase), "Calendar excludes auxiliary maintenance: " + title);
Check(CalendarContentPolicy.Includes(new() { Id = "kr:maintenance:game:test", Category = "maintenance", Title = "V7.58 게임·런처 점검" }, policyPhase), "Calendar keeps actual game server downtime");
Check(CalendarContentPolicy.Includes(new() { Id = "kr:maintenance:game:mixed", Category = "maintenance", Title = "게임·런처 및 공식 가이드 점검" }, policyPhase), "A normalized game interval survives a source notice which also lists guide maintenance");
Check(!CalendarContentPolicy.Includes(new() { Category = "notice", Title = "점검 완료 안내" }, new() { Id = "published" }), "Publication time is never treated as a playable calendar date");
Console.WriteLine($"PASS: {checks.Count} Core checks (calendar, boundaries, lanes, validation, profiles, freshness, offline alerts).");
