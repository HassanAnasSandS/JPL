using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

public sealed class VenueListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public interface IVenueService
{
    Task<IReadOnlyList<VenueListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<VenueListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<VenueListItem?> GetAsync(Guid id, CancellationToken ct = default);
    Task<VenueListItem> CreateAsync(string name, CancellationToken ct = default);
    Task<VenueListItem> UpdateAsync(Guid id, string name, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class VenueService : IVenueService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _sync;

    public VenueService(IApiClient api, IAuthService auth, ILeagueContext league, IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox, ISyncTrigger sync)
    {
        _api = api; _auth = auth; _league = league; _dbFactory = dbFactory; _outbox = outbox; _sync = sync;
    }

    public async Task<IReadOnlyList<VenueListItem>> GetAllAsync(CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return (await _api.GetVenuesAsync(ct).ConfigureAwait(false))
                .Where(row => !row.IsDeleted).Select(ToItem).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var local = await db.Venues.AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct).ConfigureAwait(false);
        if (!await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false) && local.Count == 0)
        {
            try
            {
                var remote = await _api.GetVenuesAsync(ct).ConfigureAwait(false);
                await CacheAsync(db, remote, ct).ConfigureAwait(false);
                local = await db.Venues.AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex) || ex is DbUpdateException) { }
        }
        return local.Select(ToItem).ToList();
    }

    public async Task<ApiPagedResult<VenueListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        var filtered = string.IsNullOrWhiteSpace(q) ? all
            : all.Where(row => row.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new ApiPagedResult<VenueListItem>(filtered.Skip(Math.Max(0, skip)).Take(ListPaging.PageSize).ToList(),
            filtered.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    public async Task<VenueListItem?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetVenueAsync(id, ct).ConfigureAwait(false);
            return live is null || live.IsDeleted ? null : ToItem(live);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var local = await db.Venues.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        return local is null || local.IsDeleted ? null : ToItem(local);
    }

    public async Task<VenueListItem> CreateAsync(string name, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var cleanName = RequireName(name);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsureNameAvailableAsync(db, cleanName, null, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var entity = new Venue { Id = Guid.NewGuid(), Name = cleanName, Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Venues.Add(entity);
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return ToItem(entity);
    }

    public async Task<VenueListItem> UpdateAsync(Guid id, string name, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var cleanName = RequireName(name);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsureNameAvailableAsync(db, cleanName, id, ct).ConfigureAwait(false);
        var entity = await db.Venues.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Venue not found.");
        entity.Name = cleanName; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return ToItem(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await db.VenueBookings.AnyAsync(row => row.VenueId == id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("This venue is used by a booking and cannot be deleted.");
        var entity = await db.Venues.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return;
        entity.IsDeleted = true; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Delete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
    }

    private async Task CacheAsync(JplDbContext db, IEnumerable<ApiVenueDto> rows, CancellationToken ct)
    {
        var list = rows.ToList();
        var ids = list.Select(row => row.Id).ToHashSet();
        var pending = await db.SyncOutbox.AsNoTracking().Where(row => row.EntityType == nameof(Venue)
            && row.Status != SyncOutboxStatus.Completed && ids.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedIds = pending.ToHashSet();
        foreach (var dto in list.Where(row => !protectedIds.Contains(row.Id)))
        {
            var entity = await db.Venues.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == dto.Id, ct).ConfigureAwait(false);
            if (entity is null) { entity = new Venue { Id = dto.Id }; db.Venues.Add(entity); }
            entity.Name = dto.Name; entity.Version = dto.Version; entity.CreatedAtUtc = dto.CreatedAtUtc;
            entity.UpdatedAtUtc = dto.UpdatedAtUtc; entity.IsDeleted = dto.IsDeleted;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureNameAvailableAsync(JplDbContext db, string name, Guid? excludeId, CancellationToken ct)
    {
        if (await db.Venues.AnyAsync(row => row.Id != excludeId && row.Name.ToLower() == name.ToLower(), ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Venue \"{name}\" already exists.");
    }

    private static VenueListItem ToItem(Venue entity) => new() { Id = entity.Id, Name = entity.Name };
    private static VenueListItem ToItem(ApiVenueDto dto) => new() { Id = dto.Id, Name = dto.Name };

    private static string RequireName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Venue name is required.");
        if (trimmed.Length > 160) throw new ArgumentException("Venue name must be 160 characters or fewer.");
        return trimmed;
    }
}
