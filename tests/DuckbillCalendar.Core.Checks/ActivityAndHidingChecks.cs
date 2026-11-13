using System.Text.Json;
using DuckbillCalendar.Core;

internal static class ActivityAndHidingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-11-13T12:00:00Z"); var settings = new ViewSettings();
        var idle = new GameActivity(true, true, false, false, false, false, false, 100);
        var policy = new GameActivityPolicy();
        policy.Update(now, idle, settings); check(policy.AllowAutomaticUpdates, "Idle startup permits automatic updates without adding an artificial delay");
        var combat = idle with { InCombat = true };
        policy.Update(now, combat, settings); check(policy.Hold == AutomaticUpdateHold.Combat, "Combat pauses automatic work without owning any window state");
        policy.Update(now, idle with { InDuty = true }, settings); check(policy.Hold == AutomaticUpdateHold.Duty, "Duty deferral applies outside combat");
        policy.Update(now, idle with { InPvP = true }, settings); check(policy.Hold == AutomaticUpdateHold.Duty, "PvP deferral applies independently of the duty flags");
        policy.Update(now, idle with { Cutscene = true }, settings); check(policy.Hold == AutomaticUpdateHold.Cutscene, "Cutscenes defer network work");
        policy.Update(now, idle, settings); policy.Update(now.AddSeconds(14), idle, settings);
        check(policy.Hold == AutomaticUpdateHold.Settling, "Leaving combat/duty waits for a quiet period");
        policy.Update(now.AddSeconds(15), idle, settings); check(policy.AllowAutomaticUpdates, "Quiet period resumes once rather than scheduling accumulated requests");
        policy.Update(now.AddSeconds(20), idle with { Loading = true }, settings);
        check(policy.Hold == AutomaticUpdateHold.Loading, "Loading transitions defer automatic requests");
        policy.Update(now.AddSeconds(21), idle, settings); policy.Update(now.AddSeconds(25), combat, settings); policy.Update(now.AddSeconds(26), idle, settings);
        policy.Update(now.AddSeconds(40), idle, settings); check(!policy.AllowAutomaticUpdates, "A brief combat interruption restarts the quiet period");
        policy.Update(now.AddSeconds(41), idle, settings); check(policy.AllowAutomaticUpdates, "Resuming waits from the final interruption");
        policy.Update(now, idle with { LoggedIn = false }, settings); check(policy.Hold == AutomaticUpdateHold.LoggedOut, "Logged-out and unloaded sessions do not auto-fetch");
        var disabledPolicy = new GameActivityPolicy(); disabledPolicy.Update(now, combat, settings with { PauseAutomaticUpdatesWhenBusy = false });
        check(disabledPolicy.AllowAutomaticUpdates, "Explicitly disabling combat deferral takes effect");
        policy.Update(now, combat, settings); policy.Update(now, combat, settings with { PauseAutomaticUpdatesWhenBusy = false });
        check(policy.AllowAutomaticUpdates, "Turning deferral off during combat does not retain an obsolete quiet-period hold");
        disabledPolicy.Update(now, idle with { InPvP = true }, settings);
        check(!disabledPolicy.AllowAutomaticUpdates, "Restoring the setting defers automatic updates during PvP");
        var rollback = new GameActivityPolicy(); rollback.Update(now, combat, settings); rollback.Update(now.AddSeconds(5), idle, settings); rollback.Update(now.AddSeconds(-5), idle, settings);
        check(!rollback.AllowAutomaticUpdates, "Clock rollback cannot bypass the quiet period");

        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "DuckbillCalendar.slnx"))) root = Directory.GetParent(root)!.FullName;
        var feed = CalendarJson.Parse<FeedDocument>(File.ReadAllBytes(Path.Combine(root, "data/live/kr-calendar.snapshot.json")));
        var ccrc = feed.Events.Single(e => e.Id == "kr:curated:ccrc-2027-korea");
        var other = ccrc with { Id = "kr:curated:ccrc-2028-korea" };
        check(CalendarPresentation.RelatedEventIds(feed.Events.Append(other), [ccrc.Id]).SequenceEqual(new[] { ccrc.Id }), "Another tournament is not related even with the same title, category and evidence");
        var updateIds = CalendarPresentation.RelatedEventIds(feed.Events, ["kr:patch:v7-58"]);
        check(updateIds.Count == 2 && updateIds.Contains("kr:maintenance:game:v7-58:2026-11-10"), "Only the matching guarded downtime joins its patch");
        check(!updateIds.Contains("kr:pvp:cc-season-22"), "A season launched on the same patch day is not automatically related to the patch");
        var planned = feed.Events.Select(e => e.Category is "patch" or "maintenance" ? e with { Phases = e.Phases.Select(p => p with
            { Start = p.Start with { Certainty = "tentative" }, End = p.End.Kind is "instant" or "date" ? p.End with { Certainty = "tentative" } : p.End }).ToArray() } : e).ToArray();
        check(CalendarPresentation.RelatedEventIds(planned, ["kr:patch:v7-58"]).Count == 2, "Planned dates do not sever an explicitly proven update relationship");
        var unrelatedProof = feed.Events.Select(e => e.Id == "kr:maintenance:game:v7-58:2026-11-10" ? e with { Phases = e.Phases.Select(p => p with
            { Start = p.Start with { EvidenceIds = ["other-proof"] }, End = p.End with { EvidenceIds = ["other-proof"] } }).ToArray() } : e).ToArray();
        check(CalendarPresentation.RelatedEventIds(unrelatedProof, ["kr:patch:v7-58"]).Count == 1, "Matching version/date without shared proof does not form a hide relationship");
        var campaign = CalendarPresentation.RelatedEventIds(feed.Events, ["kr:curated:v8-ce-discount"]);
        check(campaign.Count == 3 && !campaign.Contains("kr:curated:v8-early-access") && !campaign.Contains("kr:curated:v8-release"), "Reviewed purchase family excludes early access and formal release");
        var duplicateMaintenance = feed.Events.Single(e => e.Id == "kr:maintenance:game:v7-58:2026-11-10") with { Id = "kr:maintenance:game:v7-58:other" };
        check(CalendarPresentation.RelatedEventIds(feed.Events.Append(duplicateMaintenance), ["kr:patch:v7-58"]).Count == 1, "Ambiguous same-version maintenance cannot broaden a related hide");
        var state = new LocalState { View = settings with { HiddenPhases = [new() { EventId = ccrc.Id, PhaseId = "selection" }] } };
        var copy = CalendarJson.Parse<LocalState>(JsonSerializer.SerializeToUtf8Bytes(state, CalendarJson.Options));
        check(FeedValidator.ValidateLocal(copy).Count == 0 && copy.View.HiddenPhases.Single().PhaseId == "selection" && copy.View.PauseAutomaticUpdatesWhenBusy,
            "Phase hide and activity preferences survive serialization");
        check(FeedValidator.ValidateLocal(state with { View = settings with { HiddenPhases = [state.View.HiddenPhases[0], state.View.HiddenPhases[0]] } }).Contains("DuplicateHiddenPhase"), "Duplicate hidden phase keys are rejected");
    }
}
