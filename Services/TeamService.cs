using System.Text.Json;
using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

public interface ITeamService
{
    Task<IReadOnlyList<TeamListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<TeamListItem>> GetPageAsync(int skip, string? q, string? captain, int status, CancellationToken ct = default);
    /// <summary>Active teams only — for match create dropdowns.</summary>
    Task<IReadOnlyList<TeamListItem>> GetActiveAsync(CancellationToken ct = default);
    Task<TeamDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Team> CreateAsync(TeamDraft draft, CancellationToken ct = default);
    Task<Team> UpdateAsync(Guid id, TeamDraft draft, CancellationToken ct = default);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record TeamDraft(
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Guid> PlayerIds,
    bool IsActive = true,
    IReadOnlyList<Guid>? PlayingPlayerIds = null);

public sealed record TeamListItem(
    Guid Id,
    string Name,
    string? CaptainName,
    int PlayerCount,
    bool IsActive,
    int PlayingCount = 0)
{
    public Guid? CaptainPlayerId { get; init; }
    public ImageSource? Photo { get; init; }
    public string Initials { get; init; } = "?";
    public bool HasPhoto => Photo is not null;
    public bool ShowInitials => !HasPhoto;
    public string StatusText => IsActive ? "Active" : "Inactive";
    public Color StatusColor => StatusColors.For(IsActive);
    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";
    public string CaptainLine => string.IsNullOrWhiteSpace(CaptainName) ? "No captain" : $"Captain: {CaptainName} (C)";
    public string PlayersLine => PlayingCount > 0 && PlayingCount != PlayerCount
        ? $"{PlayingCount} playing · {PlayerCount} squad" : $"{PlayerCount} players";
    public string PickerLabel => PlayingCount > 0 && PlayingCount != PlayerCount
        ? $"{Name} · {PlayingCount} playing" : Name;
}

public sealed record TeamDetail(
    Guid Id,
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Player> Players,
    bool IsActive,
    IReadOnlyList<Guid>? PlayingPlayerIds = null);

/// <summary>SQLite is the read/write source; server state is refreshed into the cache and local writes use the outbox.</summary>
public sealed class TeamService : ITeamService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IMatchSquadCache _squadCache;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _sync;

    public TeamService(IApiClient api, IAuthService auth, ILeagueContext league, IMatchSquadCache squadCache,
        IDbContextFactory<JplDbContext> dbFactory, IOutboxWriter outbox, ISyncTrigger sync)
    {
        _api = api;
        _auth = auth;
        _league = league;
        _squadCache = squadCache;
        _dbFactory = dbFactory;
        _outbox = outbox;
        _sync = sync;
    }

    public Task<IReadOnlyList<TeamListItem>> GetAllAsync(CancellationToken ct = default)
        => QueryListAsync(activeOnly: false, ct);

    public async Task<ApiPagedResult<TeamListItem>> GetPageAsync(
        int skip, string? q, string? captain, int status, CancellationToken ct = default)
    {
        var local = await QueryListAsync(activeOnly: false, ct).ConfigureAwait(false);
        var filtered = local.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(q))
            filtered = filtered.Where(team => team.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(captain))
            filtered = filtered.Where(team => (team.CaptainName ?? string.Empty).Contains(captain.Trim(), StringComparison.OrdinalIgnoreCase));
        if (status == 1) filtered = filtered.Where(team => team.IsActive);
        if (status == 2) filtered = filtered.Where(team => !team.IsActive);
        var rows = filtered.ToList();
        return new ApiPagedResult<TeamListItem>(rows.Skip(Math.Max(0, skip)).Take(ListPaging.PageSize).ToList(),
            rows.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    public Task<IReadOnlyList<TeamListItem>> GetActiveAsync(CancellationToken ct = default)
        => QueryListAsync(activeOnly: true, ct);

    private async Task<IReadOnlyList<TeamListItem>> QueryListAsync(bool activeOnly, CancellationToken ct)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var teams = await _api.GetTeamsAsync(ct).ConfigureAwait(false);
            var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
            var playerNames = players.Where(player => !player.IsDeleted)
                .ToDictionary(player => player.Id, player => player.FullName);
            return teams.Where(team => !team.IsDeleted && (!activeOnly || team.IsActive))
                .OrderByDescending(team => team.IsActive).ThenBy(team => team.Name)
                .Select(team => new TeamListItem(team.Id, team.Name,
                    team.CaptainPlayerId is Guid captainId ? playerNames.GetValueOrDefault(captainId) : null,
                    team.PlayerIds.Count, team.IsActive,
                    team.PlayingPlayerIds?.Count ?? team.PlayerIds.Count)
                { CaptainPlayerId = team.CaptainPlayerId })
                .ToList();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await LocalCacheGate.RemapLegacyDefaultLeagueAsync(db, ct).ConfigureAwait(false);

        var preferLocal = await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false);
        if (preferLocal)
            return await LoadLocalListAsync(db, activeOnly, ct).ConfigureAwait(false);

        try
        {
            using var teamsTimeout = LocalCacheGate.ShortNetworkCts(ct);
            var teams = await _api.GetTeamsAsync(teamsTimeout.Token).ConfigureAwait(false);
            using var playersTimeout = LocalCacheGate.ShortNetworkCts(ct);
            var players = await _api.GetPlayersAsync(playersTimeout.Token).ConfigureAwait(false);
            await CacheRemoteAsync(teams, players, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                   || ex is OperationCanceledException
                                   || ex is JsonException || ex is FormatException
                                   || ex is HttpRequestException || ex is TimeoutException
                                   || ex is InvalidDataException)
        {
            // Fall through to whatever SQLite already has for this league.
        }
        catch (DbUpdateException)
        {
            // Local cache write failed (e.g. legacy Guid casing). Still serve SQLite.
        }

        return await LoadLocalListAsync(db, activeOnly, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<TeamListItem>> LoadLocalListAsync(
        JplDbContext db, bool activeOnly, CancellationToken ct)
    {
        var teamsLocal = await db.Teams.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var playersLocal = await db.Players.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var links = await db.TeamPlayers.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var names = playersLocal.ToDictionary(player => player.Id, player => player.FullName);
        var result = teamsLocal.Where(team => !activeOnly || team.IsActive)
            .OrderByDescending(team => team.IsActive).ThenBy(team => team.Name)
            .Select(team =>
            {
                var teamLinks = links.Where(link => link.TeamId == team.Id).ToList();
                return new TeamListItem(team.Id, team.Name,
                    team.CaptainPlayerId is Guid captainId ? names.GetValueOrDefault(captainId) : null,
                    teamLinks.Count, team.IsActive, teamLinks.Count(link => link.IsPlaying))
                { CaptainPlayerId = team.CaptainPlayerId };
            }).ToList();
        return result;
    }

    public async Task<TeamDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetTeamAsync(id, ct).ConfigureAwait(false);
            if (live is null || live.IsDeleted) return null;
            var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
            var byId = players.Where(player => !player.IsDeleted).ToDictionary(player => player.Id);
            var members = live.PlayerIds.Where(byId.ContainsKey).Select(playerId =>
            {
                var dto = byId[playerId];
                return new Player
                {
                    Id = dto.Id, FullName = dto.FullName, IsActive = dto.IsActive,
                    IsDeleted = dto.IsDeleted, NickName = dto.NickName, PlayingRole = dto.PlayingRole,
                    BattingStyle = dto.BattingStyle, BowlingStyle = dto.BowlingStyle, Bio = dto.Bio
                };
            }).ToList();
            return new TeamDetail(live.Id, live.Name, live.CaptainPlayerId, members,
                live.IsActive, live.PlayingPlayerIds);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false))
            return await LoadLocalDetailAsync(db, id, ct).ConfigureAwait(false);

        try
        {
            var team = await _api.GetTeamAsync(id, ct).ConfigureAwait(false);
            if (team is not null)
            {
                var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
                await CacheRemoteAsync([team], players, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                   || ex is JsonException || ex is FormatException
                                   || ex is HttpRequestException || ex is DbUpdateException)
        {
            // Fall through to SQLite — writes never depend on a successful remote cache.
        }

        return await LoadLocalDetailAsync(db, id, ct).ConfigureAwait(false);
    }

    private static async Task<TeamDetail?> LoadLocalDetailAsync(JplDbContext db, Guid id, CancellationToken ct)
    {
        var local = await db.Teams.AsNoTracking().FirstOrDefaultAsync(team => team.Id == id, ct).ConfigureAwait(false);
        if (local is null) return null;
        var links = await db.TeamPlayers.AsNoTracking().Where(link => link.TeamId == id).ToListAsync(ct).ConfigureAwait(false);
        var playerIds = links.Select(link => link.PlayerId).ToHashSet();
        var playersLocal = await db.Players.AsNoTracking().Where(player => playerIds.Contains(player.Id))
            .OrderBy(player => player.FullName).ToListAsync(ct).ConfigureAwait(false);
        return new TeamDetail(local.Id, local.Name, local.CaptainPlayerId, playersLocal, local.IsActive,
            links.Where(link => link.IsPlaying).Select(link => link.PlayerId).ToList());
    }

    public async Task<Team> CreateAsync(TeamDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.Name);
        var playerIds = DistinctIds(draft.PlayerIds);
        var playingPlayerIds = DistinctIds(draft.PlayingPlayerIds ?? Array.Empty<Guid>());
        ValidateCaptain(draft.CaptainPlayerId, playerIds);
        ValidatePlaying(playerIds, playingPlayerIds);
        var now = DateTime.UtcNow;
        var team = new Team { Id = Guid.NewGuid(), Name = name, CaptainPlayerId = draft.CaptainPlayerId,
            IsActive = draft.IsActive, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now };

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        db.Teams.Add(team);
        foreach (var playerId in playerIds)
            db.TeamPlayers.Add(new TeamPlayer { TeamId = team.Id, PlayerId = playerId, IsPlaying = playingPlayerIds.Contains(playerId), JoinedAtUtc = now });
        _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert", TeamSyncPayload(team, playerIds, playingPlayerIds));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return team;
    }

    public async Task<Team> UpdateAsync(Guid id, TeamDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.Name);
        var playerIds = DistinctIds(draft.PlayerIds);
        var playingPlayerIds = DistinctIds(draft.PlayingPlayerIds ?? Array.Empty<Guid>());
        ValidateCaptain(draft.CaptainPlayerId, playerIds);
        ValidatePlaying(playerIds, playingPlayerIds);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var team = await db.Teams.IgnoreQueryFilters()
            .Include(row => row.TeamPlayers)
            .FirstOrDefaultAsync(row => row.Id == id && row.LeagueId == db.CurrentLeagueId && !row.IsDeleted, ct)
            .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Team not found.");
        team.Name = name;
        team.CaptainPlayerId = draft.CaptainPlayerId;
        team.IsActive = draft.IsActive;
        team.Version++;
        team.UpdatedAtUtc = DateTime.UtcNow;
        var wanted = playerIds.ToHashSet();
        foreach (var link in team.TeamPlayers.Where(link => !wanted.Contains(link.PlayerId)).ToList())
            db.TeamPlayers.Remove(link);
        var existing = team.TeamPlayers.ToDictionary(link => link.PlayerId);
        foreach (var playerId in playerIds)
        {
            if (existing.TryGetValue(playerId, out var link))
                link.IsPlaying = playingPlayerIds.Contains(playerId);
            else
                team.TeamPlayers.Add(new TeamPlayer { TeamId = team.Id, PlayerId = playerId,
                    IsPlaying = playingPlayerIds.Contains(playerId), JoinedAtUtc = DateTime.UtcNow });
        }
        _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert", TeamSyncPayload(team, playerIds, playingPlayerIds));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _squadCache.ClearAll();
        _sync.RequestSyncNow();
        return team;
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        // Pure local write — never round-trip GetAsync/API (that path caused duplicate-insert
        // SaveChanges failures when SQLite Guid TEXT casing differed from EF parameters).
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var team = await db.Teams.IgnoreQueryFilters()
            .Include(row => row.TeamPlayers)
            .FirstOrDefaultAsync(row => row.Id == id && row.LeagueId == db.CurrentLeagueId && !row.IsDeleted, ct)
            .ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Team not found.");
        if (team.IsActive == isActive)
            return;

        team.IsActive = isActive;
        team.Version++;
        team.UpdatedAtUtc = DateTime.UtcNow;
        var roster = team.TeamPlayers.Select(link => link.PlayerId).ToList();
        var playing = team.TeamPlayers.Where(link => link.IsPlaying).Select(link => link.PlayerId).ToList();
        _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert", TeamSyncPayload(team, roster, playing));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _squadCache.ClearAll();
        _sync.RequestSyncNow();
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var team = await db.Teams.IgnoreQueryFilters()
            .Include(row => row.TeamPlayers)
            .FirstOrDefaultAsync(row => row.Id == id && row.LeagueId == db.CurrentLeagueId && !row.IsDeleted, ct)
            .ConfigureAwait(false);
        if (team is null) return;
        team.IsDeleted = true;
        team.CaptainPlayerId = null;
        team.Version++;
        team.UpdatedAtUtc = DateTime.UtcNow;
        var roster = team.TeamPlayers.Select(link => link.PlayerId).ToList();
        var playing = team.TeamPlayers.Where(link => link.IsPlaying).Select(link => link.PlayerId).ToList();
        _outbox.Enqueue(db, nameof(Team), team.Id, "Delete", TeamSyncPayload(team, roster, playing));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _squadCache.ClearAll();
        _sync.RequestSyncNow();
    }

    private async Task CacheRemoteAsync(IEnumerable<ApiTeamDto> teams, IEnumerable<ApiPlayerDto> players, CancellationToken ct)
    {
        var teamRows = teams.ToList();
        var playerRows = players.ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var teamIds = teamRows.Select(row => row.Id).ToHashSet();
        var pendingTeams = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.EntityType == nameof(Team) && row.Status != SyncOutboxStatus.Completed && teamIds.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedTeams = pendingTeams.ToHashSet();
        var playerIds = playerRows.Select(row => row.Id).ToHashSet();
        var pendingPlayers = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.EntityType == nameof(Player) && row.Status != SyncOutboxStatus.Completed && playerIds.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedPlayers = pendingPlayers.ToHashSet();

        foreach (var dto in playerRows.Where(row => !protectedPlayers.Contains(row.Id)))
        {
            var player = await db.Players.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == dto.Id, ct).ConfigureAwait(false);
            if (player is null) { player = new Player { Id = dto.Id }; db.Players.Add(player); }
            player.FullName = dto.FullName; player.IsActive = dto.IsActive; player.NickName = dto.NickName;
            player.PlayingRole = dto.PlayingRole; player.BattingStyle = dto.BattingStyle; player.BowlingStyle = dto.BowlingStyle;
            player.Bio = dto.Bio; player.Version = dto.Version; player.UpdatedAtUtc = dto.UpdatedAtUtc; player.IsDeleted = dto.IsDeleted;
        }

        foreach (var dto in teamRows.Where(row => !protectedTeams.Contains(row.Id)))
        {
            var team = await db.Teams.IgnoreQueryFilters().Include(row => row.TeamPlayers)
                .FirstOrDefaultAsync(row => row.Id == dto.Id, ct).ConfigureAwait(false);
            if (team is null) { team = new Team { Id = dto.Id }; db.Teams.Add(team); }
            team.Name = dto.Name; team.CaptainPlayerId = dto.CaptainPlayerId; team.IsActive = dto.IsActive;
            if (team.LeagueId == Guid.Empty && db.CurrentLeagueId != Guid.Empty)
                team.LeagueId = db.CurrentLeagueId;
            team.Version = dto.Version; team.UpdatedAtUtc = dto.UpdatedAtUtc; team.IsDeleted = dto.IsDeleted;
            var roster = DistinctIds(dto.PlayerIds ?? Array.Empty<Guid>()).ToHashSet();
            var playing = TeamRoster.PlayingPlayerIds(dto).ToHashSet();
            foreach (var link in team.TeamPlayers.Where(link => !roster.Contains(link.PlayerId)).ToList())
                db.TeamPlayers.Remove(link);
            var existing = team.TeamPlayers.ToDictionary(link => link.PlayerId);
            foreach (var playerId in roster)
            {
                if (existing.TryGetValue(playerId, out var link)) link.IsPlaying = playing.Contains(playerId);
                else team.TeamPlayers.Add(new TeamPlayer { TeamId = team.Id, PlayerId = playerId,
                    IsPlaying = playing.Contains(playerId), JoinedAtUtc = DateTime.UtcNow });
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static object TeamSyncPayload(Team team, IReadOnlyList<Guid> playerIds, IReadOnlyList<Guid> playingPlayerIds)
        => new { team.Id, team.Name, team.CaptainPlayerId, team.Version, team.UpdatedAtUtc, team.IsDeleted,
            team.IsActive, playerIds, playingPlayerIds };

    private static void ValidateCaptain(Guid? captainId, IReadOnlyList<Guid> playerIds)
    {
        if (captainId is not null && !playerIds.Contains(captainId.Value))
            throw new InvalidOperationException("Captain must be one of the team players. Add the player to the team first.");
    }

    private static void ValidatePlaying(IReadOnlyList<Guid> squadIds, IReadOnlyList<Guid> playingIds)
    {
        if (playingIds.Count == 0) throw new InvalidOperationException("Select at least one playing player from the squad.");
        var allowed = squadIds.ToHashSet();
        if (playingIds.Any(id => !allowed.Contains(id)))
            throw new InvalidOperationException("Playing players must be selected from the squad.");
    }

    private static List<Guid> DistinctIds(IReadOnlyList<Guid> ids) => ids.Where(id => id != Guid.Empty).Distinct().ToList();

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("Team name is required.");
        return trimmed;
    }
}
