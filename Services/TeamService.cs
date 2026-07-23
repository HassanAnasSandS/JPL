using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface ITeamService
{
    Task<IReadOnlyList<TeamListItem>> GetAllAsync(CancellationToken ct = default);
    Task<TeamDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Team> CreateAsync(TeamDraft draft, CancellationToken ct = default);
    Task<Team> UpdateAsync(Guid id, TeamDraft draft, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record TeamDraft(
    string Name,
    string? ShortName,
    Guid? CaptainPlayerId,
    IReadOnlyList<Guid> PlayerIds);

public sealed record TeamListItem(
    Guid Id,
    string Name,
    string? ShortName,
    string? CaptainName,
    int PlayerCount);

public sealed record TeamDetail(
    Guid Id,
    string Name,
    string? ShortName,
    Guid? CaptainPlayerId,
    IReadOnlyList<Player> Players);

public sealed class TeamService : ITeamService
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _syncTrigger;

    public TeamService(
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger)
    {
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
    }

    public async Task<IReadOnlyList<TeamListItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TeamListItem(
                t.Id,
                t.Name,
                t.ShortName,
                t.Captain != null ? t.Captain.FullName : null,
                t.TeamPlayers.Count))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<TeamDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var team = await db.Teams
            .AsNoTracking()
            .Include(t => t.TeamPlayers)
            .ThenInclude(tp => tp.Player)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            .ConfigureAwait(false);

        if (team is null)
            return null;

        var players = team.TeamPlayers
            .Select(tp => tp.Player)
            .OrderBy(p => p.FullName)
            .ToList();

        return new TeamDetail(team.Id, team.Name, team.ShortName, team.CaptainPlayerId, players);
    }

    public async Task<Team> CreateAsync(TeamDraft draft, CancellationToken ct = default)
    {
        var name = RequireName(draft.Name);
        var playerIds = DistinctIds(draft.PlayerIds);
        ValidateCaptain(draft.CaptainPlayerId, playerIds);

        var now = DateTimeOffset.UtcNow;
        var team = new Team
        {
            Name = name,
            ShortName = NullIfWhite(draft.ShortName),
            CaptainPlayerId = draft.CaptainPlayerId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsurePlayersExistAsync(db, playerIds, ct).ConfigureAwait(false);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        db.Teams.Add(team);
        foreach (var playerId in playerIds)
        {
            db.TeamPlayers.Add(new TeamPlayer
            {
                TeamId = team.Id,
                PlayerId = playerId,
                JoinedAtUtc = now
            });
        }

        _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert", BuildTeamPayload(team, playerIds));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _syncTrigger.RequestSync();
        return team;
    }

    public async Task<Team> UpdateAsync(Guid id, TeamDraft draft, CancellationToken ct = default)
    {
        var name = RequireName(draft.Name);
        var playerIds = DistinctIds(draft.PlayerIds);
        ValidateCaptain(draft.CaptainPlayerId, playerIds);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsurePlayersExistAsync(db, playerIds, ct).ConfigureAwait(false);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var team = await db.Teams
            .Include(t => t.TeamPlayers)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Team not found.");

        team.Name = name;
        team.ShortName = NullIfWhite(draft.ShortName);
        team.CaptainPlayerId = draft.CaptainPlayerId;
        team.UpdatedAtUtc = DateTimeOffset.UtcNow;
        team.Version++;

        var existing = team.TeamPlayers.Select(tp => tp.PlayerId).ToHashSet();
        var desired = playerIds.ToHashSet();

        foreach (var remove in team.TeamPlayers.Where(tp => !desired.Contains(tp.PlayerId)).ToList())
            db.TeamPlayers.Remove(remove);

        var now = DateTimeOffset.UtcNow;
        foreach (var addId in desired.Where(pid => !existing.Contains(pid)))
        {
            db.TeamPlayers.Add(new TeamPlayer
            {
                TeamId = team.Id,
                PlayerId = addId,
                JoinedAtUtc = now
            });
        }

        _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert", BuildTeamPayload(team, playerIds));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _syncTrigger.RequestSync();
        return team;
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var team = await db.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (team is null || team.IsDeleted)
            return;

        team.IsDeleted = true;
        team.CaptainPlayerId = null;
        team.UpdatedAtUtc = DateTimeOffset.UtcNow;
        team.Version++;

        _outbox.Enqueue(db, nameof(Team), team.Id, "Delete", new { team.Id, team.Version });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _syncTrigger.RequestSync();
    }

    private static object BuildTeamPayload(Team team, IReadOnlyList<Guid> playerIds) => new
    {
        team.Id,
        team.Name,
        team.ShortName,
        team.CaptainPlayerId,
        PlayerIds = playerIds,
        team.Version,
        team.UpdatedAtUtc,
        team.IsDeleted
    };

    private static async Task EnsurePlayersExistAsync(JplDbContext db, IReadOnlyList<Guid> playerIds, CancellationToken ct)
    {
        if (playerIds.Count == 0)
            return;

        var count = await db.Players.CountAsync(p => playerIds.Contains(p.Id), ct).ConfigureAwait(false);
        if (count != playerIds.Count)
            throw new InvalidOperationException("One or more players were not found.");
    }

    private static void ValidateCaptain(Guid? captainId, IReadOnlyList<Guid> playerIds)
    {
        if (captainId is null)
            return;

        if (!playerIds.Contains(captainId.Value))
            throw new InvalidOperationException("Captain must be one of the team players. Add the player to the team first.");
    }

    private static List<Guid> DistinctIds(IReadOnlyList<Guid> ids)
        => ids.Where(id => id != Guid.Empty).Distinct().ToList();

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Team name is required.");
        return trimmed;
    }

    private static string? NullIfWhite(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
