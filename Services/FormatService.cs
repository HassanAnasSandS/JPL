using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

public sealed class FormatListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public MatchFormat Format { get; init; }
    public OversMode OversMode { get; init; }
    public int OversLimit { get; init; }
    public string DetailsLine { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
}

public interface IFormatService
{
    Task<IReadOnlyList<FormatListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<FormatListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<FormatListItem?> GetAsync(Guid id, CancellationToken ct = default);
    Task<FormatListItem> CreateAsync(string name, MatchFormat format, OversMode oversMode, int oversLimit, CancellationToken ct = default);
    Task<FormatListItem> UpdateAsync(Guid id, string name, MatchFormat format, OversMode oversMode, int oversLimit, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class FormatService : IFormatService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _sync;

    public FormatService(IApiClient api, IAuthService auth, ILeagueContext league, IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox, ISyncTrigger sync)
    {
        _api = api; _auth = auth; _league = league; _dbFactory = dbFactory; _outbox = outbox; _sync = sync;
    }

    public async Task<IReadOnlyList<FormatListItem>> GetAllAsync(CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return (await _api.GetFormatsAsync(ct).ConfigureAwait(false))
                .Where(row => !row.IsDeleted).Select(ToItem).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var local = await db.Formats.AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct).ConfigureAwait(false);
        if (!await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false) && local.Count == 0)
        {
            try
            {
                var remote = await _api.GetFormatsAsync(ct).ConfigureAwait(false);
                await CacheAsync(db, remote, ct).ConfigureAwait(false);
                local = await db.Formats.AsNoTracking().OrderBy(row => row.Name).ToListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex) || ex is DbUpdateException) { }
        }
        return local.Select(ToItem).ToList();
    }

    public async Task<ApiPagedResult<FormatListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        var filtered = string.IsNullOrWhiteSpace(q) ? all
            : all.Where(row => row.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new ApiPagedResult<FormatListItem>(filtered.Skip(Math.Max(0, skip)).Take(ListPaging.PageSize).ToList(),
            filtered.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    public async Task<FormatListItem?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetFormatAsync(id, ct).ConfigureAwait(false);
            return live is null || live.IsDeleted ? null : ToItem(live);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var local = await db.Formats.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        return local is null || local.IsDeleted ? null : ToItem(local);
    }

    public async Task<FormatListItem> CreateAsync(string name, MatchFormat format, OversMode oversMode, int oversLimit,
        CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var cleanName = RequireName(name);
        OversRules.Validate(oversMode, oversLimit, bowlerMaxOvers: 0);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsureNameAvailableAsync(db, cleanName, null, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var entity = new Format { Id = Guid.NewGuid(), Name = cleanName, Kind = format,
            OversMode = oversMode, OversLimit = OversRules.StoredOversLimit(oversMode, oversLimit),
            Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.Formats.Add(entity);
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return ToItem(entity);
    }

    public async Task<FormatListItem> UpdateAsync(Guid id, string name, MatchFormat format, OversMode oversMode,
        int oversLimit, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var cleanName = RequireName(name);
        OversRules.Validate(oversMode, oversLimit, bowlerMaxOvers: 0);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsureNameAvailableAsync(db, cleanName, id, ct).ConfigureAwait(false);
        var entity = await db.Formats.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Format not found.");
        entity.Name = cleanName; entity.Kind = format; entity.OversMode = oversMode;
        entity.OversLimit = OversRules.StoredOversLimit(oversMode, oversLimit);
        entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return ToItem(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await db.Matches.AnyAsync(row => row.FormatId == id, ct).ConfigureAwait(false)
            || await db.Tournaments.AnyAsync(row => row.FormatId == id, ct).ConfigureAwait(false)
            || await db.Series.AnyAsync(row => row.FormatId == id, ct).ConfigureAwait(false))
            throw new InvalidOperationException("This format is used by matches, tournaments, or series and cannot be deleted.");
        var entity = await db.Formats.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return;
        entity.IsDeleted = true; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Delete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
    }

    private async Task CacheAsync(JplDbContext db, IEnumerable<ApiFormatDto> rows, CancellationToken ct)
    {
        var list = rows.ToList();
        var ids = list.Select(row => row.Id).ToHashSet();
        var pending = await db.SyncOutbox.AsNoTracking().Where(row => row.EntityType == nameof(Format)
            && row.Status != SyncOutboxStatus.Completed && ids.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedIds = pending.ToHashSet();
        foreach (var dto in list.Where(row => !protectedIds.Contains(row.Id)))
        {
            var entity = await db.Formats.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == dto.Id, ct).ConfigureAwait(false);
            if (entity is null) { entity = new Format { Id = dto.Id }; db.Formats.Add(entity); }
            entity.Name = dto.Name; entity.Kind = (MatchFormat)dto.Format; entity.OversMode = (OversMode)dto.OversMode;
            entity.OversLimit = dto.OversLimit; entity.Version = dto.Version; entity.CreatedAtUtc = dto.CreatedAtUtc;
            entity.UpdatedAtUtc = dto.UpdatedAtUtc; entity.IsDeleted = dto.IsDeleted;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureNameAvailableAsync(JplDbContext db, string name, Guid? excludeId, CancellationToken ct)
    {
        if (await db.Formats.AnyAsync(row => row.Id != excludeId && row.Name.ToLower() == name.ToLower(), ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Format \"{name}\" already exists.");
    }

    private static FormatListItem ToItem(Format entity)
        => new() { Id = entity.Id, Name = entity.Name, Format = entity.Kind, OversMode = entity.OversMode,
            OversLimit = entity.OversLimit, DetailsLine = FormatRules.DetailsLine(entity.Kind, entity.OversMode, entity.OversLimit),
            Hint = FormatRules.Hint(entity.Kind, entity.OversMode, entity.OversLimit) };

    private static FormatListItem ToItem(ApiFormatDto dto)
        => new() { Id = dto.Id, Name = dto.Name, Format = (MatchFormat)dto.Format,
            OversMode = (OversMode)dto.OversMode, OversLimit = dto.OversLimit,
            DetailsLine = dto.DetailsLine, Hint = dto.Hint };

    private static string RequireName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Format name is required.");
        if (trimmed.Length > 160) throw new ArgumentException("Format name must be 160 characters or fewer.");
        return trimmed;
    }
}
