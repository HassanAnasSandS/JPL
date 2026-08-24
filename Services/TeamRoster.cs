namespace JPL.Services;

/// <summary>
/// Squad = full team roster. Playing list = who appears in the next match.
/// Empty/missing playing list falls back to the full squad (legacy teams).
/// </summary>
internal static class TeamRoster
{
    public static IReadOnlyList<Guid> PlayingPlayerIds(
        IReadOnlyList<Guid>? squad,
        IReadOnlyList<Guid>? playing)
    {
        var squadIds = Distinct(squad);
        if (playing is null || playing.Count == 0)
            return squadIds;

        var allowed = squadIds.ToHashSet();
        var chosen = Distinct(playing).Where(allowed.Contains).ToList();
        return chosen.Count > 0 ? chosen : squadIds;
    }

    public static IReadOnlyList<Guid> PlayingPlayerIds(ApiTeamDto team)
        => PlayingPlayerIds(team.PlayerIds, team.PlayingPlayerIds);

    public static List<Guid> Distinct(IReadOnlyList<Guid>? ids)
        => (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
}
