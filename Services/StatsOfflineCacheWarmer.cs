using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface IStatsOfflineCacheWarmer
{
    Task WarmAsync(CancellationToken ct = default);
}

/// <summary>Refreshes unfiltered Statistics baselines during daily/manual sync for offline paging/search.</summary>
public sealed class StatsOfflineCacheWarmer : IStatsOfflineCacheWarmer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IApiClient _api;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;

    public StatsOfflineCacheWarmer(IApiClient api, IDbContextFactory<JplDbContext> dbFactory)
    {
        _api = api;
        _dbFactory = dbFactory;
    }

    public async Task WarmAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (db.CurrentLeagueId == Guid.Empty)
            return;

        await WarmOneAsync(db, nameof(StatsService.GetFilterOptionsAsync), new { },
            () => _api.GetStatsFilterOptionsAsync(ct), ct).ConfigureAwait(false);
        foreach (var source in new[] { "matches", "nets" })
        {
            await WarmOneAsync(db, nameof(StatsService.GetLeaderboardAsync), new
            {
                fromUtc = (DateTime?)null,
                toExclusiveUtc = (DateTime?)null,
                venueId = (Guid?)null,
                source,
                formatIds = (IReadOnlyList<Guid>?)null,
                tournamentIds = (IReadOnlyList<Guid>?)null,
                seriesIds = (IReadOnlyList<Guid>?)null,
                matchIds = (IReadOnlyList<Guid>?)null,
                attendanceFromDebut = false,
                asCaptain = false,
                captainPlayerId = (Guid?)null,
                captainOpponentSide = false,
                asPlayer = false
            }, () => _api.GetStatsLeaderboardAsync(source: source, ct: ct), ct).ConfigureAwait(false);
        }

        await WarmOneAsync(db, nameof(StatsService.GetTeamLeaderboardAsync), new
        {
            fromUtc = (DateTime?)null,
            toExclusiveUtc = (DateTime?)null,
            venueId = (Guid?)null,
            formatIds = (IReadOnlyList<Guid>?)null,
            tournamentIds = (IReadOnlyList<Guid>?)null,
            seriesIds = (IReadOnlyList<Guid>?)null,
            matchIds = (IReadOnlyList<Guid>?)null
        }, () => _api.GetTeamLeaderboardAsync(ct: ct), ct).ConfigureAwait(false);

        await WarmOneAsync(db, nameof(StatsService.GetPartnershipLeaderboardAsync), new
        {
            query = (string?)null,
            fromUtc = (DateTime?)null,
            toExclusiveUtc = (DateTime?)null,
            venueId = (Guid?)null,
            formatIds = (IReadOnlyList<Guid>?)null,
            tournamentIds = (IReadOnlyList<Guid>?)null,
            seriesIds = (IReadOnlyList<Guid>?)null,
            matchIds = (IReadOnlyList<Guid>?)null
        }, () => _api.GetPartnershipLeaderboardAsync(ct: ct), ct).ConfigureAwait(false);
    }

    private static async Task WarmOneAsync<T>(JplDbContext db, string operation, object request,
        Func<Task<T>> fetch, CancellationToken ct)
    {
        var requestJson = JsonSerializer.Serialize(new { operation, request }, Json);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var result = await fetch().ConfigureAwait(false);
        var row = await db.LocalStatsCache.FirstOrDefaultAsync(item => item.CacheKey == key, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new LocalStatsCacheItem { CacheKey = key };
            db.LocalStatsCache.Add(row);
        }
        row.PayloadJson = JsonSerializer.Serialize(result, Json);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
