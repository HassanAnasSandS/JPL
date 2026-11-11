using System.Text.Json;

namespace JPL.Api.Models;

internal sealed class MatchSquadPlayerSnap
{
    public Guid Id { get; set; }
    public bool IsSubstitute { get; set; }
}

/// <summary>
/// Frozen match roster. MAUI stores MatchRosterPayload (TeamAPlayers objects);
/// older payloads may only have TeamAPlayerIds.
/// </summary>
internal sealed class MatchSquadSnapshot
{
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public List<Guid> TeamAPlayerIds { get; set; } = [];
    public List<Guid> TeamBPlayerIds { get; set; } = [];
    public List<MatchSquadPlayerSnap> TeamAPlayers { get; set; } = [];
    public List<MatchSquadPlayerSnap> TeamBPlayers { get; set; } = [];
    public List<MatchSquadPlayerSnap> TeamASubstitutes { get; set; } = [];
    public List<MatchSquadPlayerSnap> TeamBSubstitutes { get; set; } = [];

    public IReadOnlyList<Guid> PlayersOfTeamA() => Combine(TeamAPlayerIds, TeamAPlayers);

    public IReadOnlyList<Guid> PlayersOfTeamB() => Combine(TeamBPlayerIds, TeamBPlayers);

    public IReadOnlyList<Guid> PlayersOf(Guid teamId)
    {
        if (teamId != Guid.Empty && teamId == TeamAId)
            return PlayersOfTeamA();
        if (teamId != Guid.Empty && teamId == TeamBId)
            return PlayersOfTeamB();
        return [];
    }

    public IReadOnlySet<Guid> SubstituteIds()
    {
        var ids = new HashSet<Guid>();
        void Add(IEnumerable<MatchSquadPlayerSnap>? list, bool requireFlag)
        {
            if (list is null)
                return;
            foreach (var p in list)
            {
                if (p.Id == Guid.Empty)
                    continue;
                if (!requireFlag || p.IsSubstitute)
                    ids.Add(p.Id);
            }
        }

        Add(TeamASubstitutes, requireFlag: false);
        Add(TeamBSubstitutes, requireFlag: false);
        Add(TeamAPlayers, requireFlag: true);
        Add(TeamBPlayers, requireFlag: true);
        return ids;
    }

    public int CountOf(Guid teamId, bool isTeamA)
    {
        var ids = teamId != Guid.Empty ? PlayersOf(teamId) : (isTeamA ? PlayersOfTeamA() : PlayersOfTeamB());
        return ids.Count;
    }

    public static int SizeFor(string? json, Guid teamId, bool isTeamA, int fallback)
    {
        var snap = TryParse(json);
        var n = snap?.CountOf(teamId, isTeamA) ?? 0;
        return n > 0 ? n : fallback;
    }

    public static MatchSquadSnapshot? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<MatchSquadSnapshot>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<Guid> Combine(List<Guid>? ids, List<MatchSquadPlayerSnap>? players)
    {
        if (ids is { Count: > 0 })
            return ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (players is { Count: > 0 })
            return players.Select(p => p.Id).Where(id => id != Guid.Empty).Distinct().ToList();
        return [];
    }
}
