using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface ILeagueService
{
    Task<IReadOnlyList<League>> GetAllAsync(CancellationToken ct = default);
    Task<League?> GetAsync(Guid id, CancellationToken ct = default);
    Task<League> CreateAsync(string name, byte[]? imageBytes, CancellationToken ct = default);
    Task<League> UpdateAsync(Guid id, string name, byte[]? imageBytes, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class LeagueService : ILeagueService
{
    private readonly IApiClient _api;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly AuthTokenStore _tokens;

    public LeagueService(
        IApiClient api,
        IDbContextFactory<JplDbContext> dbFactory,
        IAuthService auth,
        AuthTokenStore tokens)
    {
        _api = api;
        _dbFactory = dbFactory;
        _auth = auth;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<League>> GetAllAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_tokens.Token))
        {
            try
            {
                var remote = await _api.GetLeaguesAsync(ct).ConfigureAwait(false);
                var list = remote.Select(ToEntity).ToList();
                await CacheLocalAsync(list, ct).ConfigureAwait(false);
                return list;
            }
            catch
            {
                /* offline / old API — local leagues */
            }
        }

        return await LoadLocalAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<League>> LoadLocalAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Leagues.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<League?> GetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var dto = await _api.GetLeagueAsync(id, ct).ConfigureAwait(false);
            return dto is null ? null : ToEntity(dto);
        }
        catch
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            return await db.Leagues.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        }
    }

    public async Task<League> CreateAsync(string name, byte[]? imageBytes, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        name = RequireName(name);
        var remote = await _api.CreateLeagueAsync(new
        {
            name,
            imageBase64 = imageBytes is { Length: > 0 } ? Convert.ToBase64String(imageBytes) : null
        }, ct).ConfigureAwait(false);
        var entity = ToEntity(remote);
        await UpsertLocalAsync(entity, ct).ConfigureAwait(false);
        return entity;
    }

    public async Task<League> UpdateAsync(Guid id, string name, byte[]? imageBytes, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        name = RequireName(name);
        var remote = await _api.UpdateLeagueAsync(id, new
        {
            name,
            imageBase64 = imageBytes is { Length: > 0 } ? Convert.ToBase64String(imageBytes) : null
        }, ct).ConfigureAwait(false);
        var entity = ToEntity(remote);
        await UpsertLocalAsync(entity, ct).ConfigureAwait(false);
        return entity;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await _api.DeleteLeagueAsync(id, ct).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var local = await db.Leagues.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (local is not null)
        {
            local.IsActive = false;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private void EnsureSuperAdmin()
    {
        if (_auth.Current?.CanManageLeagues != true)
            throw new UnauthorizedAccessException("Only SuperAdmin can manage leagues.");
    }

    private static string RequireName(string? name)
    {
        var n = name?.Trim() ?? string.Empty;
        if (n.Length == 0)
            throw new ArgumentException("League name is required.");
        if (n.Length > 160)
            throw new ArgumentException("League name must be 160 characters or fewer.");
        return n;
    }

    private async Task CacheLocalAsync(IReadOnlyList<League> list, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            foreach (var league in list)
                await UpsertLocalCoreAsync(db, league, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            /* cache optional */
        }
    }

    private async Task UpsertLocalAsync(League league, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await UpsertLocalCoreAsync(db, league, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertLocalCoreAsync(JplDbContext db, League league, CancellationToken ct)
    {
        var local = await db.Leagues.FirstOrDefaultAsync(l => l.Id == league.Id, ct).ConfigureAwait(false);
        if (local is null)
        {
            db.Leagues.Add(new League
            {
                Id = league.Id,
                Name = league.Name,
                ImageBytes = league.ImageBytes,
                IsActive = league.IsActive,
                CreatedAtUtc = league.CreatedAtUtc
            });
        }
        else
        {
            local.Name = league.Name;
            local.IsActive = league.IsActive;
            if (league.ImageBytes is { Length: > 0 })
                local.ImageBytes = league.ImageBytes;
        }
    }

    private static byte[]? TryDecodeImage(string? imageBase64)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return null;
        try
        {
            return Convert.FromBase64String(imageBase64);
        }
        catch
        {
            return null;
        }
    }

    private static League ToEntity(ApiLeagueDto dto)
        => new()
        {
            Id = dto.Id,
            Name = dto.Name,
            ImageBytes = TryDecodeImage(dto.ImageBase64),
            IsActive = dto.IsActive,
            CreatedAtUtc = dto.CreatedAtUtc,
            LiveMatchCount = dto.LiveMatchCount,
            LiveNetsCount = dto.LiveNetsCount
        };
}
