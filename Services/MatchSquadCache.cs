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
    public bool IsSubstitute { get; init; }
    public string DisplayName
    {
        get
        {
            var n = FullName;
            if (IsCaptain)
                n += " (C)";
            if (IsSubstitute)
                n += " (Substitute)";
            return n;
        }
    }
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
    public List<SquadPlayer> TeamASubstitutes { get; set; } = new();
    public List<SquadPlayer> TeamBSubstitutes { get; set; } = new();

    public IEnumerable<SquadPlayer> AllNamedPlayers()
        => TeamAPlayers.Concat(TeamBPlayers).Concat(TeamASubstitutes).Concat(TeamBSubstitutes);

    public static MatchRosterPayload From(
        ApiTeamDto teamA,
        ApiTeamDto teamB,
        IReadOnlyList<ApiPlayerDto> players)
    {
        var byId = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);

        static List<SquadPlayer> Side(
            IReadOnlyList<Guid>? ids,
            Dictionary<Guid, ApiPlayerDto> byId,
            Guid? captainId,
            bool substitute)
            => (ids ?? Array.Empty<Guid>())
                .Where(id => byId.ContainsKey(id))
                .Select(id => new SquadPlayer
                {
                    Id = id,
                    FullName = byId[id].FullName,
                    IsCaptain = captainId == id,
                    IsSubstitute = substitute
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
            TeamAPlayers = Side(TeamRoster.PlayingPlayerIds(teamA), byId, teamA.CaptainPlayerId, substitute: false),
            TeamBPlayers = Side(TeamRoster.PlayingPlayerIds(teamB), byId, teamB.CaptainPlayerId, substitute: false),
            TeamASubstitutes = Side(TeamRoster.SubstitutePlayerIds(teamA), byId, teamA.CaptainPlayerId, substitute: true),
            TeamBSubstitutes = Side(TeamRoster.SubstitutePlayerIds(teamB), byId, teamB.CaptainPlayerId, substitute: true)
        };
    }

    public static MatchRosterPayload FromLocal(
        Team teamA,
        Team teamB,
        IReadOnlyList<Player> players)
    {
        var byId = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);

        static List<SquadPlayer> Side(
            IEnumerable<Guid> ids,
            Dictionary<Guid, Player> byId,
            Guid? captainId,
            bool substitute)
            => ids
                .Where(id => byId.ContainsKey(id))
                .Select(id => new SquadPlayer
                {
                    Id = id,
                    FullName = byId[id].FullName,
                    IsCaptain = captainId == id,
                    IsSubstitute = substitute
                })
                .OrderBy(p => p.FullName)
                .ToList();

        string? CapName(Guid? id)
            => id is Guid c && byId.TryGetValue(c, out var p) ? p.FullName : null;

        static (IEnumerable<Guid> Playing, IEnumerable<Guid> Bench) Split(ICollection<TeamPlayer> tps)
        {
            var playing = tps.Where(tp => tp.IsPlaying).Select(tp => tp.PlayerId).ToList();
            if (playing.Count == 0)
                return (tps.Select(tp => tp.PlayerId), Array.Empty<Guid>());
            var chosen = playing.ToHashSet();
            return (playing, tps.Select(tp => tp.PlayerId).Where(id => !chosen.Contains(id)));
        }

        var a = Split(teamA.TeamPlayers);
        var b = Split(teamB.TeamPlayers);
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
            TeamAPlayers = Side(a.Playing, byId, teamA.CaptainPlayerId, substitute: false),
            TeamBPlayers = Side(b.Playing, byId, teamB.CaptainPlayerId, substitute: false),
            TeamASubstitutes = Side(a.Bench, byId, teamA.CaptainPlayerId, substitute: true),
            TeamBSubstitutes = Side(b.Bench, byId, teamB.CaptainPlayerId, substitute: true)
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

    /// <summary>
    /// Roster JSON stores who played (IDs). Overlay current Players/Teams names.
    /// Snapshot names are fallback only when the master row is missing.
    /// </summary>
    public void ApplyLiveNames(
        IReadOnlyDictionary<Guid, string> playerNames,
        IReadOnlyDictionary<Guid, string> teamNames)
    {
        if (teamNames.TryGetValue(TeamAId, out var a) && !string.IsNullOrWhiteSpace(a))
            TeamAName = a;
        if (teamNames.TryGetValue(TeamBId, out var b) && !string.IsNullOrWhiteSpace(b))
            TeamBName = b;

        TeamAPlayers = Relabel(TeamAPlayers, playerNames);
        TeamBPlayers = Relabel(TeamBPlayers, playerNames);
        TeamASubstitutes = Relabel(TeamASubstitutes, playerNames);
        TeamBSubstitutes = Relabel(TeamBSubstitutes, playerNames);

        if (TeamACaptainId is Guid ca && playerNames.TryGetValue(ca, out var can) && !string.IsNullOrWhiteSpace(can))
            TeamACaptainName = can;
        if (TeamBCaptainId is Guid cb && playerNames.TryGetValue(cb, out var cbn) && !string.IsNullOrWhiteSpace(cbn))
            TeamBCaptainName = cbn;
    }

    public static List<SquadPlayer> Relabel(
        IEnumerable<SquadPlayer> players,
        IReadOnlyDictionary<Guid, string> playerNames)
        => players.Select(p =>
        {
            if (!playerNames.TryGetValue(p.Id, out var n) || string.IsNullOrWhiteSpace(n) || n == p.FullName)
                return p;
            return new SquadPlayer { Id = p.Id, FullName = n, IsCaptain = p.IsCaptain, IsSubstitute = p.IsSubstitute };
        }).ToList();
}

/// <summary>Current Players/Teams names by ID — API first, local SQLite if offline.</summary>
public static class LiveMasterNames
{
    public static async Task<(Dictionary<Guid, string> Players, Dictionary<Guid, string> Teams)> LoadAsync(
        IApiClient api,
        IDbContextFactory<JplDbContext> dbFactory,
        CancellationToken ct)
    {
        var players = new Dictionary<Guid, string>();
        var teams = new Dictionary<Guid, string>();

        try
        {
            var pTask = api.GetPlayersAsync(ct);
            var tTask = api.GetTeamsAsync(ct);
            await Task.WhenAll(pTask, tTask).ConfigureAwait(false);
            foreach (var p in await pTask.ConfigureAwait(false))
            {
                if (!p.IsDeleted && !string.IsNullOrWhiteSpace(p.FullName))
                    players[p.Id] = p.FullName;
            }
            foreach (var t in await tTask.ConfigureAwait(false))
            {
                if (!t.IsDeleted && !string.IsNullOrWhiteSpace(t.Name))
                    teams[t.Id] = t.Name;
            }
        }
        catch
        {
            /* offline — try SQLite copies */
        }

        if (players.Count > 0 && teams.Count > 0)
            return (players, teams);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            if (players.Count == 0)
            {
                var rows = await db.Players.AsNoTracking()
                    .Where(p => !p.IsDeleted)
                    .Select(p => new { p.Id, p.FullName })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                foreach (var p in rows)
                {
                    if (!string.IsNullOrWhiteSpace(p.FullName))
                        players[p.Id] = p.FullName;
                }
            }

            if (teams.Count == 0)
            {
                var rows = await db.Teams.AsNoTracking()
                    .Where(t => !t.IsDeleted)
                    .Select(t => new { t.Id, t.Name })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                foreach (var t in rows)
                {
                    if (!string.IsNullOrWhiteSpace(t.Name))
                        teams[t.Id] = t.Name;
                }
            }
        }
        catch
        {
            /* no local master rows */
        }

        return (players, teams);
    }

    public static string Lookup(IReadOnlyDictionary<Guid, string> map, Guid id, string fallback)
        => map.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : fallback;

    public static string RefreshVsTitle(string stored, string oldA, string oldB, string newA, string newB)
    {
        if (string.IsNullOrWhiteSpace(newA) || string.IsNullOrWhiteSpace(newB))
            return stored;
        if (string.Equals(stored.Trim(), $"{oldA} vs {oldB}", StringComparison.OrdinalIgnoreCase))
            return $"{newA} vs {newB}";
        return stored;
    }
}

public sealed class MatchSquadSnapshot
{
    private IReadOnlyList<SquadPlayer> _teamAPlayers = Array.Empty<SquadPlayer>();
    private IReadOnlyList<SquadPlayer> _teamBPlayers = Array.Empty<SquadPlayer>();
    private IReadOnlyList<SquadPlayer> _teamASubstitutes = Array.Empty<SquadPlayer>();
    private IReadOnlyList<SquadPlayer> _teamBSubstitutes = Array.Empty<SquadPlayer>();
    private IReadOnlyList<SquadPlayer> _tournamentPlayers = Array.Empty<SquadPlayer>();
    private Guid? _battingTeamId;
    private Guid? _bowlingTeamId;

    // Scoring reads Fielders / AllPlayers / NameOf per ball — rebuild only when a list changes.
    private IReadOnlyList<SquadPlayer>? _fielders;
    private IReadOnlyList<SquadPlayer>? _allPlayers;
    private HashSet<Guid>? _substituteIds;
    private Dictionary<Guid, string>? _displayNames;

    public Guid MatchId { get; init; }
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public Guid? TeamACaptainId { get; init; }
    public Guid? TeamBCaptainId { get; init; }
    public string? TeamACaptainName { get; set; }
    public string? TeamBCaptainName { get; set; }

    public IReadOnlyList<SquadPlayer> TeamAPlayers
    {
        get => _teamAPlayers;
        set { _teamAPlayers = value; Invalidate(); }
    }

    public IReadOnlyList<SquadPlayer> TeamBPlayers
    {
        get => _teamBPlayers;
        set { _teamBPlayers = value; Invalidate(); }
    }

    public IReadOnlyList<SquadPlayer> TeamASubstitutes
    {
        get => _teamASubstitutes;
        set { _teamASubstitutes = value; Invalidate(); }
    }

    public IReadOnlyList<SquadPlayer> TeamBSubstitutes
    {
        get => _teamBSubstitutes;
        set { _teamBSubstitutes = value; Invalidate(); }
    }

    /// <summary>
    /// Tournament matches: every player from every tournament team (fielder dropdown).
    /// Empty for normal matches — fielders fall back to bowling side.
    /// </summary>
    public IReadOnlyList<SquadPlayer> TournamentPlayers
    {
        get => _tournamentPlayers;
        set { _tournamentPlayers = value; Invalidate(); }
    }

    public Guid? BattingTeamId
    {
        get => _battingTeamId;
        set { _battingTeamId = value; Invalidate(); }
    }

    public Guid? BowlingTeamId
    {
        get => _bowlingTeamId;
        set { _bowlingTeamId = value; Invalidate(); }
    }

    public DateTime LoadedAtUtc { get; init; } = DateTime.Now;

    /// <summary>Last time names/substitutes were refreshed — scoring serves a fresh snapshot without I/O.</summary>
    public DateTime RefreshedAtUtc { get; set; } = DateTime.MinValue;

    /// <summary>Bench lookup already succeeded; skip the Teams/Players round trip on later warms.</summary>
    public bool SubstitutesResolved { get; set; }

    /// <summary>Tournament fielder pool already resolved (or match is not a tournament match).</summary>
    public bool TournamentPlayersResolved { get; set; }

    public IReadOnlyList<SquadPlayer> Batters =>
        BattingTeamId == TeamAId ? TeamAPlayers
        : BattingTeamId == TeamBId ? TeamBPlayers
        : TeamAPlayers;

    public IReadOnlyList<SquadPlayer> Bowlers =>
        BowlingTeamId == TeamAId ? TeamAPlayers
        : BowlingTeamId == TeamBId ? TeamBPlayers
        : TeamBPlayers;

    public IReadOnlyList<SquadPlayer> BowlingSubstitutes =>
        BowlingTeamId == TeamAId ? TeamASubstitutes
        : BowlingTeamId == TeamBId ? TeamBSubstitutes
        : Array.Empty<SquadPlayer>();

    /// <summary>
    /// Catch / run-out / stumped: bowling XI plus that team's squad substitutes.
    /// Tournament matches also include other tournament playing XIs.
    /// </summary>
    public IReadOnlyList<SquadPlayer> Fielders => _fielders ??= BuildFielders();

    public IReadOnlyList<SquadPlayer> AllPlayers => _allPlayers ??= BuildAllPlayers();

    public IReadOnlySet<Guid> SubstituteIds() => _substituteIds ??= BuildSubstituteIds();

    public string NameOf(Guid playerId)
    {
        _displayNames ??= AllPlayers
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().DisplayName);
        return _displayNames.TryGetValue(playerId, out var name)
            ? name
            : playerId.ToString("N")[..8];
    }

    /// <summary>Called after any roster list changes so the derived views are rebuilt once, lazily.</summary>
    public void Invalidate()
    {
        _fielders = null;
        _allPlayers = null;
        _substituteIds = null;
        _displayNames = null;
    }

    private IReadOnlyList<SquadPlayer> BuildFielders()
    {
        var map = new Dictionary<Guid, SquadPlayer>();
        void Add(IEnumerable<SquadPlayer> list)
        {
            foreach (var p in list)
                map[p.Id] = p;
        }

        if (TournamentPlayers.Count > 0)
            Add(TournamentPlayers);
        Add(Bowlers);
        Add(BowlingSubstitutes);
        return map.Values
            .OrderBy(p => p.IsSubstitute)
            .ThenBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<SquadPlayer> BuildAllPlayers()
    {
        var map = new Dictionary<Guid, SquadPlayer>();
        foreach (var p in TeamAPlayers) map[p.Id] = p;
        foreach (var p in TeamBPlayers) map[p.Id] = p;
        foreach (var p in TeamASubstitutes) map[p.Id] = p;
        foreach (var p in TeamBSubstitutes) map[p.Id] = p;
        foreach (var p in TournamentPlayers) map[p.Id] = p;
        return map.Values.OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private HashSet<Guid> BuildSubstituteIds()
    {
        var ids = new HashSet<Guid>();
        foreach (var p in TeamASubstitutes) ids.Add(p.Id);
        foreach (var p in TeamBSubstitutes) ids.Add(p.Id);
        return ids;
    }

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
    void ClearAll();
}

public sealed class MatchSquadCache : IMatchSquadCache
{
    /// <summary>How long a warmed snapshot is reused without any SQLite/API work.</summary>
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IApiClient _api;
    private readonly Dictionary<Guid, MatchSquadSnapshot> _cache = new();
    private readonly object _gate = new();
    private Dictionary<Guid, string> _playerNames = new();
    private Dictionary<Guid, string> _teamNames = new();
    private DateTime _namesAtUtc = DateTime.MinValue;

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

        // Ball-by-ball scoring warms the cache after every delivery — serve a recent
        // snapshot straight from memory instead of re-hitting SQLite and the API.
        if (existing is not null && DateTime.UtcNow - existing.RefreshedAtUtc < FreshFor)
            return existing;

        await EnsureMasterNamesAsync(ct).ConfigureAwait(false);

        if (existing is not null)
        {
            if (!existing.TournamentPlayersResolved)
            {
                await using var dbRefresh = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                var matchRefresh = await dbRefresh.Matches.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == matchId, ct)
                    .ConfigureAwait(false);
                if (matchRefresh?.TournamentId is Guid tidRefresh)
                {
                    var pool = await LoadTournamentPlayersAsync(dbRefresh, tidRefresh, ct).ConfigureAwait(false);
                    existing.TournamentPlayers = pool;
                    existing.TournamentPlayersResolved = pool.Count > 0;
                }
                else
                    existing.TournamentPlayersResolved = true;
            }

            await FillSubstitutesAsync(existing, ct).ConfigureAwait(false);
            OverlaySnapshot(existing);
            existing.RefreshedAtUtc = DateTime.UtcNow;
            return existing;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found for squad cache.");

        var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
        OverlayRoster(roster);

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
                TeamASubstitutes = MarkSubstitutes(roster.TeamASubstitutes),
                TeamBSubstitutes = MarkSubstitutes(roster.TeamBSubstitutes),
                BattingTeamId = innings?.BattingTeamId ?? match.TeamAId,
                BowlingTeamId = innings?.BowlingTeamId ?? match.TeamBId,
                TournamentPlayers = tournamentPlayers
            };

        snap.TournamentPlayersResolved = match.TournamentId is null || tournamentPlayers.Count > 0;
        await FillSubstitutesAsync(snap, ct).ConfigureAwait(false);
        OverlaySnapshot(snap);
        snap.RefreshedAtUtc = DateTime.UtcNow;

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
            foreach (var pid in TeamRoster.PlayingPlayerIds(team))
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

    public void ClearAll()
    {
        lock (_gate)
        {
            _cache.Clear();
            _playerNames = new();
            _teamNames = new();
            _namesAtUtc = DateTime.MinValue;
        }
    }

    private async Task EnsureMasterNamesAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if ((DateTime.UtcNow - _namesAtUtc).TotalSeconds < 20
                && (_playerNames.Count > 0 || _teamNames.Count > 0))
                return;
        }

        var loaded = await LiveMasterNames.LoadAsync(_api, _dbFactory, ct).ConfigureAwait(false);
        lock (_gate)
        {
            _playerNames = loaded.Players;
            _teamNames = loaded.Teams;
            _namesAtUtc = DateTime.UtcNow;
        }
    }

    private void OverlayRoster(MatchRosterPayload? roster)
    {
        if (roster is null)
            return;
        Dictionary<Guid, string> players;
        Dictionary<Guid, string> teams;
        lock (_gate)
        {
            players = _playerNames;
            teams = _teamNames;
        }
        roster.ApplyLiveNames(players, teams);
    }

    private void OverlaySnapshot(MatchSquadSnapshot snap)
    {
        Dictionary<Guid, string> players;
        Dictionary<Guid, string> teams;
        lock (_gate)
        {
            players = _playerNames;
            teams = _teamNames;
        }

        if (teams.TryGetValue(snap.TeamAId, out var a) && !string.IsNullOrWhiteSpace(a))
            snap.TeamAName = a;
        if (teams.TryGetValue(snap.TeamBId, out var b) && !string.IsNullOrWhiteSpace(b))
            snap.TeamBName = b;

        snap.TeamAPlayers = MatchRosterPayload.Relabel(snap.TeamAPlayers, players);
        snap.TeamBPlayers = MatchRosterPayload.Relabel(snap.TeamBPlayers, players);
        snap.TeamASubstitutes = MatchRosterPayload.Relabel(snap.TeamASubstitutes, players);
        snap.TeamBSubstitutes = MatchRosterPayload.Relabel(snap.TeamBSubstitutes, players);

        if (snap.TeamACaptainId is Guid ca && players.TryGetValue(ca, out var can) && !string.IsNullOrWhiteSpace(can))
            snap.TeamACaptainName = can;
        if (snap.TeamBCaptainId is Guid cb && players.TryGetValue(cb, out var cbn) && !string.IsNullOrWhiteSpace(cbn))
            snap.TeamBCaptainName = cbn;
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
                    IsCaptain = captainId == p.Id,
                    IsSubstitute = p.IsSubstitute
                })
            .ToList();
    }

    private static IReadOnlyList<SquadPlayer> MarkSubstitutes(IReadOnlyList<SquadPlayer> players)
        => players.Select(p => p.IsSubstitute
            ? p
            : new SquadPlayer
            {
                Id = p.Id,
                FullName = p.FullName,
                IsCaptain = p.IsCaptain,
                IsSubstitute = true
            }).ToList();

    private async Task FillSubstitutesAsync(MatchSquadSnapshot snap, CancellationToken ct)
    {
        if (snap.SubstitutesResolved)
            return;

        try
        {
            var teams = await _api.GetTeamsAsync(ct).ConfigureAwait(false);
            var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
            var byId = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);
            var teamA = teams.FirstOrDefault(t => t.Id == snap.TeamAId && !t.IsDeleted);
            var teamB = teams.FirstOrDefault(t => t.Id == snap.TeamBId && !t.IsDeleted);
            if (teamA is not null)
                snap.TeamASubstitutes = BenchFromTeam(teamA, snap.TeamAPlayers, byId, snap.TeamACaptainId);
            if (teamB is not null)
                snap.TeamBSubstitutes = BenchFromTeam(teamB, snap.TeamBPlayers, byId, snap.TeamBCaptainId);
            snap.SubstitutesResolved = teamA is not null && teamB is not null;
        }
        catch
        {
            /* keep roster JSON substitutes when offline */
        }
    }

    private static IReadOnlyList<SquadPlayer> BenchFromTeam(
        ApiTeamDto team,
        IReadOnlyList<SquadPlayer> playing,
        Dictionary<Guid, ApiPlayerDto> byId,
        Guid? captainId)
    {
        var chosen = playing.Select(p => p.Id).ToHashSet();
        return (team.PlayerIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty && !chosen.Contains(id) && byId.ContainsKey(id))
            .Distinct()
            .Select(id => new SquadPlayer
            {
                Id = id,
                FullName = byId[id].FullName,
                IsCaptain = captainId == id,
                IsSubstitute = true
            })
            .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
