using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface IPlayerService
{
    Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default);
    Task<Player?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Player> CreateAsync(PlayerDraft draft, CancellationToken ct = default);
    Task<Player> UpdateAsync(Guid id, PlayerDraft draft, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record PlayerDraft(
    string FullName,
    string? Nickname,
    string? Phone,
    string? BattingStyle,
    string? BowlingStyle,
    string? Notes);

public sealed class PlayerService : IPlayerService
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _syncTrigger;

    public PlayerService(
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger)
    {
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
    }

    public async Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Players
            .AsNoTracking()
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Player?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<Player> CreateAsync(PlayerDraft draft, CancellationToken ct = default)
    {
        var name = RequireName(draft.FullName);
        var now = DateTimeOffset.UtcNow;

        var player = new Player
        {
            FullName = name,
            Nickname = NullIfWhite(draft.Nickname),
            Phone = NullIfWhite(draft.Phone),
            BattingStyle = NullIfWhite(draft.BattingStyle),
            BowlingStyle = NullIfWhite(draft.BowlingStyle),
            Notes = NullIfWhite(draft.Notes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        db.Players.Add(player);
        _outbox.Enqueue(db, nameof(Player), player.Id, "Upsert", player);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _syncTrigger.RequestSync();
        return player;
    }

    public async Task<Player> UpdateAsync(Guid id, PlayerDraft draft, CancellationToken ct = default)
    {
        var name = RequireName(draft.FullName);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Player not found.");

        player.FullName = name;
        player.Nickname = NullIfWhite(draft.Nickname);
        player.Phone = NullIfWhite(draft.Phone);
        player.BattingStyle = NullIfWhite(draft.BattingStyle);
        player.BowlingStyle = NullIfWhite(draft.BowlingStyle);
        player.Notes = NullIfWhite(draft.Notes);
        player.UpdatedAtUtc = DateTimeOffset.UtcNow;
        player.Version++;

        _outbox.Enqueue(db, nameof(Player), player.Id, "Upsert", player);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _syncTrigger.RequestSync();
        return player;
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Ignore query filter so we can soft-delete.
        var player = await db.Players.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (player is null || player.IsDeleted)
            return;

        player.IsDeleted = true;
        player.UpdatedAtUtc = DateTimeOffset.UtcNow;
        player.Version++;

        var memberships = await db.TeamPlayers.Where(tp => tp.PlayerId == id).ToListAsync(ct).ConfigureAwait(false);
        db.TeamPlayers.RemoveRange(memberships);

        // Clear captaincy on teams where this player is captain.
        var captained = await db.Teams.Where(t => t.CaptainPlayerId == id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var team in captained)
        {
            team.CaptainPlayerId = null;
            team.UpdatedAtUtc = DateTimeOffset.UtcNow;
            team.Version++;
            _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert", team);
        }

        _outbox.Enqueue(db, nameof(Player), player.Id, "Delete", new { player.Id, player.Version });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _syncTrigger.RequestSync();
    }

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Player name is required.");
        return trimmed;
    }

    private static string? NullIfWhite(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
