namespace DuckbillCalendar.Core;

public static class PersonalDeletionPolicy
{
    public static IReadOnlyList<PersonalEntry> Targets(LocalState state,string id,string scope)
    {
        var entry = state.Entries.FirstOrDefault(e => e.Id == id && e.ProfileId == state.ActiveProfileId);
        if (entry is null || scope is not ("one" or "following" or "series")) return [];
        if (scope == "one") return [entry];
        if (entry.SeriesId.Length == 0 || entry.SeriesIndex is null) return [];
        return state.Entries.Where(e => e.ProfileId == entry.ProfileId && e.SeriesId == entry.SeriesId &&
            (scope == "series" || e.SeriesIndex >= entry.SeriesIndex)).OrderBy(e => e.SeriesIndex).ToArray();
    }
}
