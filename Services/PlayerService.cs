using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

public interface IPlayerService
{
    Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<Player>> GetPageAsync(int skip, string? q, int status, CancellationToken ct = default);
    /// <summary>Active players only — for team create/update squad pickers.</summary>
    Task<IReadOnlyList<Player>> GetActiveAsync(CancellationToken ct = default);
    Task<Player?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Player> CreateAsync(PlayerDraft draft, CancellationToken ct = default);
    Task<Player> UpdateAsync(Guid id, PlayerDraft draft, CancellationToken ct = default);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record PlayerDraft(
    string FullName,
    bool IsActive = true,
    string? NickName = null,
    string? PlayingRole = null,
    string? BattingStyle = null,
    string? BowlingStyle = null,
    string? Bio = null);

/// <summary>SQLite is the read/write source; server refreshes and writes flow through the durable outbox.</summary>
public sealed class PlayerService : IPlayerService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IMatchSquadCache _squadCache;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _sync;

    public PlayerService(
        IApiClient api,
        IAuthService auth,
        ILeagueContext league,
        IMatchSquadCache squadCache,
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger sync)
    {
        _api = api;
        _auth = auth;
        _league = league;
        _squadCache = squadCache;
        _dbFactory = dbFactory;
        _outbox = outbox;
        _sync = sync;
    }

    public async Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return (await _api.GetPlayersAsync(ct).ConfigureAwait(false))
                .Where(player => !player.IsDeleted).Select(ToEntity).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await LocalCacheGate.RemapLegacyDefaultLeagueAsync(db, ct).ConfigureAwait(false);

        if (await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false))
            return await LoadAllLocalAsync(db, ct).ConfigureAwait(false);

        try
        {
            using var timeout = LocalCacheGate.ShortNetworkCts(ct);
            var remote = await _api.GetPlayersAsync(timeout.Token).ConfigureAwait(false);
            await CachePlayersAsync(remote, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                   || ex is OperationCanceledException || ex is DbUpdateException
                                   || ex is TimeoutException || ex is InvalidDataException)
        {
            // Use the last synchronized SQLite snapshot when the server is unavailable.
        }

        return await LoadAllLocalAsync(db, ct).ConfigureAwait(false);
    }

    private static Task<List<Player>> LoadAllLocalAsync(JplDbContext db, CancellationToken ct)
        => db.Players.AsNoTracking()
            .OrderByDescending(player => player.IsActive)
            .ThenBy(player => player.FullName)
            .ToListAsync(ct);

    public async Task<ApiPagedResult<Player>> GetPageAsync(int skip, string? q, int status, CancellationToken ct = default)
    {
        var statusKey = status switch { 1 => "active", 2 => "inactive", _ => null };
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetPlayersPageAsync(skip, q, statusKey, ct).ConfigureAwait(false);
            return live.WithItems((live.Items ?? []).Where(player => !player.IsDeleted).Select(ToEntity).ToList());
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false))
            return await LoadLocalPageAsync(db, skip, q, status, ct).ConfigureAwait(false);

        try
        {
            using var timeout = LocalCacheGate.ShortNetworkCts(ct);
            var page = await _api.GetPlayersPageAsync(skip, q, statusKey, timeout.Token).ConfigureAwait(false);
            await CachePlayersAsync(page.Items ?? [], ct).ConfigureAwait(false);
            return page.WithItems((page.Items ?? []).Where(player => !player.IsDeleted).Select(ToEntity).ToList());
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                   || ex is OperationCanceledException || ex is DbUpdateException)
        {
            return await LoadLocalPageAsync(db, skip, q, status, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Player>> GetActiveAsync(CancellationToken ct = default)
        => (await GetAllAsync(ct).ConfigureAwait(false)).Where(player => player.IsActive).ToList();

    public async Task<Player?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetPlayerAsync(id, ct).ConfigureAwait(false);
            return live is null || live.IsDeleted ? null : ToEntity(live);
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false))
            return await db.Players.AsNoTracking().FirstOrDefaultAsync(player => player.Id == id, ct).ConfigureAwait(false);

        try
        {
            using var timeout = LocalCacheGate.ShortNetworkCts(ct);
            var remote = await _api.GetPlayerAsync(id, timeout.Token).ConfigureAwait(false);
            if (remote is not null)
                await CachePlayersAsync([remote], ct).ConfigureAwait(false);
            return await db.Players.AsNoTracking().FirstOrDefaultAsync(player => player.Id == id, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                   || ex is OperationCanceledException || ex is DbUpdateException)
        {
            return await db.Players.AsNoTracking().FirstOrDefaultAsync(player => player.Id == id, ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task<ApiPagedResult<Player>> LoadLocalPageAsync(
        JplDbContext db, int skip, string? q, int status, CancellationToken ct)
    {
        var query = db.Players.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(player => player.FullName.Contains(term)
                || (player.NickName != null && player.NickName.Contains(term)));
        }
        if (status == 1) query = query.Where(player => player.IsActive);
        if (status == 2) query = query.Where(player => !player.IsActive);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderByDescending(player => player.IsActive)
            .ThenBy(player => player.FullName).Skip(Math.Max(0, skip)).Take(ListPaging.PageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return new ApiPagedResult<Player>(items, total, Math.Max(0, skip), ListPaging.PageSize);
    }

    public async Task<Player> CreateAsync(PlayerDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.FullName);
        await EnsureUniqueNameAsync(name, excludeId: null, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var player = new Player
        {
            Id = Guid.NewGuid(),
            FullName = name,
            IsActive = draft.IsActive,
            NickName = TrimTo(draft.NickName, 40),
            PlayingRole = TrimTo(draft.PlayingRole, 40),
            BattingStyle = TrimTo(draft.BattingStyle, 40),
            BowlingStyle = TrimTo(draft.BowlingStyle, 60),
            Bio = TrimTo(draft.Bio, 500),
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        db.Players.Add(player);
        _outbox.Enqueue(db, nameof(Player), player.Id, "Upsert", ToSyncDto(player));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return player;
    }

    public async Task<Player> UpdateAsync(Guid id, PlayerDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.FullName);
        await EnsureUniqueNameAsync(name, excludeId: id, ct).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var player = await db.Players.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == id && row.LeagueId == db.CurrentLeagueId && !row.IsDeleted, ct)
            .ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Player not found.");
        player.FullName = name;
        player.IsActive = draft.IsActive;
        player.NickName = TrimTo(draft.NickName, 40);
        player.PlayingRole = TrimTo(draft.PlayingRole, 40);
        player.BattingStyle = TrimTo(draft.BattingStyle, 40);
        player.BowlingStyle = TrimTo(draft.BowlingStyle, 60);
        player.Bio = TrimTo(draft.Bio, 500);
        player.Version++;
        player.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.Enqueue(db, nameof(Player), player.Id, "Upsert", ToSyncDto(player));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _squadCache.ClearAll();
        _sync.RequestSyncNow();
        return player;
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var player = await db.Players.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == id && row.LeagueId == db.CurrentLeagueId && !row.IsDeleted, ct)
            .ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Player not found.");
        if (player.IsActive == isActive)
            return;
        player.IsActive = isActive;
        player.Version++;
        player.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.Enqueue(db, nameof(Player), player.Id, "Upsert", ToSyncDto(player));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _squadCache.ClearAll();
        _sync.RequestSyncNow();
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var player = await db.Players.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == id && row.LeagueId == db.CurrentLeagueId && !row.IsDeleted, ct)
            .ConfigureAwait(false);
        if (player is null) return;
        player.IsDeleted = true;
        player.Version++;
        player.UpdatedAtUtc = DateTime.UtcNow;
        // Clear captain pointers / roster links so soft-deleted players don't linger.
        var captained = await db.Teams
            .Where(team => team.CaptainPlayerId == id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var team in captained)
        {
            team.CaptainPlayerId = null;
            team.Version++;
            team.UpdatedAtUtc = DateTime.UtcNow;
            _outbox.Enqueue(db, nameof(Team), team.Id, "Upsert",
                new { team.Id, team.Name, team.CaptainPlayerId, team.Version, team.UpdatedAtUtc, team.IsDeleted, team.IsActive });
        }
        var links = await db.TeamPlayers.Where(link => link.PlayerId == id).ToListAsync(ct).ConfigureAwait(false);
        if (links.Count > 0)
            db.TeamPlayers.RemoveRange(links);
        _outbox.Enqueue(db, nameof(Player), player.Id, "Delete", ToSyncDto(player));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _squadCache.ClearAll();
        _sync.RequestSyncNow();
    }

    private async Task CachePlayersAsync(IEnumerable<ApiPlayerDto> rows, CancellationToken ct)
    {
        var snapshot = rows.ToList();
        if (snapshot.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ids = snapshot.Select(row => row.Id).ToHashSet();
        var pending = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.EntityType == nameof(Player) && row.Status != SyncOutboxStatus.Completed && ids.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedIds = pending.ToHashSet();
        foreach (var dto in snapshot.Where(row => !protectedIds.Contains(row.Id)))
        {
            var player = await db.Players.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == dto.Id, ct)
                         .ConfigureAwait(false);
            if (player is null)
            {
                player = new Player { Id = dto.Id };
                db.Players.Add(player);
            }
            Apply(player, dto);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var key = name.ToLowerInvariant();
        if (await db.Players.AsNoTracking().AnyAsync(player => player.Id != excludeId && player.FullName.ToLower() == key, ct)
            .ConfigureAwait(false))
            throw new InvalidOperationException($"Player name \"{name}\" already exists.");

        // PreferLocal / offline: local uniqueness is enough — never stall the UI on HTTP.
        if (await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false))
            return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var remote = await _api.GetPlayersAsync(timeout.Token).ConfigureAwait(false);
            if (remote.Any(player => !player.IsDeleted && (excludeId is null || player.Id != excludeId)
                && string.Equals(player.FullName, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Player name \"{name}\" already exists.");
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                   || ex is OperationCanceledException)
        {
            // Local uniqueness is still enforced when the API cannot be reached quickly.
        }
    }

    private static void Apply(Player target, ApiPlayerDto source)
    {
        target.FullName = source.FullName;
        target.IsActive = source.IsActive;
        target.NickName = source.NickName;
        target.PlayingRole = source.PlayingRole;
        target.BattingStyle = source.BattingStyle;
        target.BowlingStyle = source.BowlingStyle;
        target.Bio = source.Bio;
        target.Version = source.Version;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
        target.IsDeleted = source.IsDeleted;
    }

    private static ApiPlayerDto ToSyncDto(Player player) => new(
        player.Id, player.FullName, player.Version, player.UpdatedAtUtc, player.IsDeleted,
        player.IsActive, player.NickName, player.PlayingRole, player.BattingStyle, player.BowlingStyle, player.Bio);

    private static Player ToEntity(ApiPlayerDto remote) => new()
    {
        Id = remote.Id,
        FullName = remote.FullName,
        NickName = remote.NickName,
        PlayingRole = remote.PlayingRole,
        BattingStyle = remote.BattingStyle,
        BowlingStyle = remote.BowlingStyle,
        Bio = remote.Bio,
        IsActive = remote.IsActive,
        Version = remote.Version,
        UpdatedAtUtc = remote.UpdatedAtUtc,
        IsDeleted = remote.IsDeleted
    };

    private static string? TrimTo(string? raw, int max)
    {
        var value = raw?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= max ? value : value[..max];
    }

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException("Player name is required.");
        if (trimmed.Length > 120) throw new ArgumentException("Player name must be 120 characters or fewer.");
        return trimmed;
    }
}
