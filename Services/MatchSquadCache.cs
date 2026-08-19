using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

/// <summary>Detached player snapshot for scoring UI — never bound to EF change-tracking.</summary>
public sealed class SquadPlayer
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public bool IsCaptain { get; init; }
    public string DisplayName => IsCaptain ? $"{FullName} (C)" : FullName;
    public override string ToString() => DisplayName;
}

/// <summary>Persisted on Match.SquadRosterJson so scoring works offline without Players/Teams tables.</summary>
public sealed class MatchRosterPayload
{
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public Guid? TeamACaptainId { get; set; }
    public Guid? TeamBCaptainId { get; set; }
    public string? TeamACaptainName { get; set; }
    public string? TeamBCaptainName { get; set; }
    public List<SquadPlayer> TeamAPlayers { get; set; } = new();
    public List<SquadPlayer> TeamBPlayers { get; set; } = new();

    public static MatchRosterPayload From(
        ApiTeamDto teamA,
        ApiTeamDto teamB,
        IReadOnlyList<ApiPlayerDto> players)
    {
        var byId = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);

        static List<SquadPlayer> Side(IReadOnlyList<Guid>? ids, Dictionary<Guid, ApiPlayerDto> byId, Guid? captainId)
            => (ids ?? Array.Empty<Guid>())
                .Where(id => byId.ContainsKey(id))
                .Select(id => new SquadPlayer
                {
                    Id = id,
                    FullName = byId[id].FullName,
                    IsCaptain = captainId == id
                })
                .OrderBy(p => p.FullName)
                .ToList();

        string? CapName(Guid? id)
            => id is Guid c && byId.TryGetValue(c, out var p) ? p.FullName : null;

        return new MatchRosterPayload
        {
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            TeamAName = teamA.Name,
            TeamBName = teamB.Name,
            TeamACaptainId = teamA.CaptainPlayerId,
            TeamBCaptainId = teamB.CaptainPlayerId,
            TeamACaptainName = CapName(teamA.CaptainPlayerId),
            TeamBCaptainName = CapName(teamB.CaptainPlayerId),
            TeamAPlayers = Side(teamA.PlayerIds, byId, teamA.CaptainPlayerId),
            TeamBPlayers = Side(teamB.PlayerIds, byId, teamB.CaptainPlayerId)
        };
    }

    public static MatchRosterPayload FromLocal(
        Team teamA,
        Team teamB,
        IReadOnlyList<Player> players)
    {
        var byId = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);

        static List<SquadPlayer> Side(IEnumerable<Guid> ids, Dictionary<Guid, Player> byId, Guid? captainId)
            => ids
                .Where(id => byId.ContainsKey(id))
                .Select(id => new SquadPlayer
                {
                    Id = id,
                    FullName = byId[id].FullName,
                    IsCaptain = captainId == id
                })
                .OrderBy(p => p.FullName)
                .ToList();

        string? CapName(Guid? id)
            => id is Guid c && byId.TryGetValue(c, out var p) ? p.FullName : null;

        return new MatchRosterPayload
        {
            TeamAId = teamA.Id,
            TeamBId = teamB.Id,
            TeamAName = teamA.Name,
            TeamBName = teamB.Name,
            TeamACaptainId = teamA.CaptainPlayerId,
            TeamBCaptainId = teamB.CaptainPlayerId,
            TeamACaptainName = CapName(teamA.CaptainPlayerId),
            TeamBCaptainName = CapName(teamB.CaptainPlayerId),
            TeamAPlayers = Side(teamA.TeamPlayers.Select(tp => tp.PlayerId), byId, teamA.CaptainPlayerId),
            TeamBPlayers = Side(teamB.TeamPlayers.Select(tp => tp.PlayerId), byId, teamB.CaptainPlayerId)
        };
    }

    public static string Serialize(MatchRosterPayload payload)
        => JsonSerializer.Serialize(payload);

    public static MatchRosterPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<MatchRosterPayload>(json);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class MatchSquadSnapshot
{
    public Guid MatchId { get; init; }
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }
    public string TeamAName { get; init; } = string.Empty;
    public string TeamBName { get; init; } = string.Empty;
    public Guid? TeamACaptainId { get; init; }
    public Guid? TeamBCaptainId { get; init; }
    public string? TeamACaptainName { get; init; }
    public string? TeamBCaptainName { get; init; }
    public IReadOnlyList<SquadPlayer> TeamAPlayers { get; init; } = Array.Empty<SquadPlayer>();
    public IReadOnlyList<SquadPlayer> TeamBPlayers { get; init; } = Array.Empty<SquadPlayer>();
    /// <summary>
    /// Tournament matches: every player from every tournament team (fielder dropdown).
    /// Empty for normal matches — fielders fall back to bowling side.
    /// </summary>
    public IReadOnlyList<SquadPlayer> TournamentPlayers { get; set; } = Array.Empty<SquadPlayer>();
    public Guid? BattingTeamId { get; set; }
    public Guid? BowlingTeamId { get; set; }
    public DateTime LoadedAtUtc { get; init; } = DateTime.Now;

    public IReadOnlyList<SquadPlayer> Batters =>
        BattingTeamId == TeamAId ? TeamAPlayers
        : BattingTeamId == TeamBId ? TeamBPlayers
        : TeamAPlayers;

    public IReadOnlyList<SquadPlayer> Bowlers =>
        BowlingTeamId == TeamAId ? TeamAPlayers
        : BowlingTeamId == TeamBId ? TeamBPlayers
        : TeamBPlayers;

    /// <summary>Catch / run-out / stumped: tournament = all tournament teams; else bowling side.</summary>
    public IReadOnlyList<SquadPlayer> Fielders =>
        TournamentPlayers.Count > 0 ? TournamentPlayers : Bowlers;

    public IReadOnlyList<SquadPlayer> AllPlayers
    {
        get
        {
            var map = new Dictionary<Guid, SquadPlayer>();
            foreach (var p in TeamAPlayers) map[p.Id] = p;
            foreach (var p in TeamBPlayers) map[p.Id] = p;
            foreach (var p in TournamentPlayers) map[p.Id] = p;
            return map.Values.OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public string NameOf(Guid playerId)
        => AllPlayers.FirstOrDefault(p => p.Id == playerId)?.FullName
           ?? playerId.ToString("N")[..8];

    public string TeamNameOf(Guid teamId)
        => teamId == TeamAId ? TeamAName
            : teamId == TeamBId ? TeamBName
            : "Team";
}

public interface IMatchSquadCache
{
    MatchSquadSnapshot? TryGet(Guid matchId);
    Task<MatchSquadSnapshot> WarmAsync(Guid matchId, CancellationToken ct = default);
    void Put(MatchSquadSnapshot snapshot);
    void SetInningsSides(Guid matchId, Guid battingTeamId, Guid bowlingTeamId);
    void Clear(Guid matchId);
}

public sealed class MatchSquadCache : IMatchSquadCache
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IApiClient _api;
    private readonly Dictionary<Guid, MatchSquadSnapshot> _cache = new();
    private readonly object _gate = new();

    public MatchSquadCache(IDbContextFactory<JplDbContext> dbFactory, IApiClient api)
    {
        _dbFactory = dbFactory;
        _api = api;
    }

    public MatchSquadSnapshot? TryGet(Guid matchId)
    {
        lock (_gate)
            return _cache.TryGetValue(matchId, out var s) ? s : null;
    }

    public void Put(MatchSquadSnapshot snapshot)
    {
        lock (_gate)
            _cache[snapshot.MatchId] = snapshot;
    }

    public async Task<MatchSquadSnapshot> WarmAsync(Guid matchId, CancellationToken ct = default)
    {
        MatchSquadSnapshot? existing;
        lock (_gate)
            existing = _cache.TryGetValue(matchId, out var s) ? s : null;

        if (existing is not null)
        {
            if (existing.TournamentPlayers.Count == 0)
            {
                await using var dbRefresh = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                var matchRefresh = await dbRefresh.Matches.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == matchId, ct)
                    .ConfigureAwait(false);
                if (matchRefresh?.TournamentId is Guid tidRefresh)
                {
                    existing.TournamentPlayers = await LoadTournamentPlayersAsync(dbRefresh, tidRefresh, ct)
                        .ConfigureAwait(false);
                }
            }
            return existing;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found for squad cache.");

        var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);

        var innings = await db.Innings.AsNoTracking()
            .Where(i => i.MatchId == matchId && !i.IsCompleted)
            .OrderByDescending(i => i.InningsNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var tournamentPlayers = match.TournamentId is Guid tid
            ? await LoadTournamentPlayersAsync(db, tid, ct).ConfigureAwait(false)
            : Array.Empty<SquadPlayer>();

        var snap = roster is null
            ? new MatchSquadSnapshot
            {
                MatchId = matchId,
                TeamAId = match.TeamAId,
                TeamBId = match.TeamBId,
                TeamAName = "Team A",
                TeamBName = "Team B",
                BattingTeamId = innings?.BattingTeamId ?? match.TeamAId,
                BowlingTeamId = innings?.BowlingTeamId ?? match.TeamBId,
                TournamentPlayers = tournamentPlayers
            }
            : new MatchSquadSnapshot
            {
                MatchId = matchId,
                TeamAId = roster.TeamAId,
                TeamBId = roster.TeamBId,
                TeamAName = roster.TeamAName,
                TeamBName = roster.TeamBName,
                TeamACaptainId = roster.TeamACaptainId ?? match.TeamACaptainId,
                TeamBCaptainId = roster.TeamBCaptainId ?? match.TeamBCaptainId,
                TeamACaptainName = roster.TeamACaptainName,
                TeamBCaptainName = roster.TeamBCaptainName,
                TeamAPlayers = WithCaptainFlags(roster.TeamAPlayers, roster.TeamACaptainId ?? match.TeamACaptainId),
                TeamBPlayers = WithCaptainFlags(roster.TeamBPlayers, roster.TeamBCaptainId ?? match.TeamBCaptainId),
                BattingTeamId = innings?.BattingTeamId ?? match.TeamAId,
                BowlingTeamId = innings?.BowlingTeamId ?? match.TeamBId,
                TournamentPlayers = tournamentPlayers
            };

        lock (_gate)
            _cache[matchId] = snap;

        return snap;
    }

    private async Task<IReadOnlyList<SquadPlayer>> LoadTournamentPlayersAsync(
        JplDbContext db,
        Guid tournamentId,
        CancellationToken ct)
    {
        _ = db;
        var teamIds = new List<Guid>();
        try
        {
            var detail = await _api.GetTournamentAsync(tournamentId, ct).ConfigureAwait(false);
            if (detail?.Teams is { Count: > 0 })
                teamIds = detail.Teams.Select(t => t.TeamId).Distinct().ToList();
        }
        catch
        {
            /* offline — fielders fall back to bowling side */
        }

        if (teamIds.Count == 0)
            return Array.Empty<SquadPlayer>();

        IReadOnlyList<ApiTeamDto> teams;
        IReadOnlyList<ApiPlayerDto> players;
        try
        {
            teams = await _api.GetTeamsAsync(ct).ConfigureAwait(false);
            players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<SquadPlayer>();
        }

        var teamMap = teams.Where(t => !t.IsDeleted && teamIds.Contains(t.Id)).ToList();
        var playerMap = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);
        var teamNameByPlayer = new Dictionary<Guid, string>();
        foreach (var team in teamMap)
        {
            foreach (var pid in team.PlayerIds ?? Array.Empty<Guid>())
            {
                if (!teamNameByPlayer.ContainsKey(pid))
                    teamNameByPlayer[pid] = team.Name;
            }
        }

        return teamNameByPlayer.Keys
            .Where(id => playerMap.ContainsKey(id))
            .Select(id =>
            {
                var name = playerMap[id].FullName;
                var teamName = teamNameByPlayer.GetValueOrDefault(id);
                return new SquadPlayer
                {
                    Id = id,
                    FullName = string.IsNullOrWhiteSpace(teamName) ? name : $"{name} ({teamName})",
                    IsCaptain = false
                };
            })
            .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetInningsSides(Guid matchId, Guid battingTeamId, Guid bowlingTeamId)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(matchId, out var snap))
                return;
            snap.BattingTeamId = battingTeamId;
            snap.BowlingTeamId = bowlingTeamId;
        }
    }

    public void Clear(Guid matchId)
    {
        lock (_gate)
            _cache.Remove(matchId);
    }

    private static IReadOnlyList<SquadPlayer> WithCaptainFlags(
        IReadOnlyList<SquadPlayer> players, Guid? captainId)
    {
        if (players.Count == 0)
            return players;
        return players
            .Select(p => p.IsCaptain == (captainId == p.Id)
                ? p
                : new SquadPlayer
                {
                    Id = p.Id,
                    FullName = p.FullName,
                    IsCaptain = captainId == p.Id
                })
            .ToList();
    }
}
