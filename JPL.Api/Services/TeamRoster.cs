namespace JPL.Api.Services;

/// <summary>
/// Squad = full team roster. Playing list = who appears in the next match.
/// Empty/missing playing list falls back to the full squad (legacy teams).
/// </summary>
internal static class TeamRoster
{
    public static List<Guid> Distinct(IReadOnlyList<Guid>? ids)
        => (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();

    public static bool TryResolvePlaying(
        IReadOnlyList<Guid> squadIds,
        IReadOnlyList<Guid>? playingPlayerIds,
        out HashSet<Guid> playingSet,
        out string? error)
    {
        playingSet = new HashSet<Guid>();
        error = null;
        var allowed = squadIds.ToHashSet();

        if (playingPlayerIds is null)
        {
            playingSet = allowed;
            return true;
        }

        foreach (var id in Distinct(playingPlayerIds))
        {
            if (!allowed.Contains(id))
            {
                error = "Playing players must be selected from the squad.";
                return false;
            }

            playingSet.Add(id);
        }

        if (playingSet.Count == 0)
        {
            error = "Select at least one playing player from the squad.";
            return false;
        }

        return true;
    }
}
