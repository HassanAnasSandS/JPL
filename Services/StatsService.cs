using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface IStatsService
{
    /// <summary>Statistics screen — always from server API (never local SQLite).</summary>
    Task<PlayerCareerSummary?> GetCareerAsync(Guid playerId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<ConfrontationStat>> GetBattingVsBowlersAsync(Guid batterId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<ConfrontationStat>> GetBowlingVsBattersAsync(Guid bowlerId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<CaptainVsStat>> GetCaptainVsAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerCareerSummary>> GetLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<TeamCareerSummary>> GetTeamLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<TeamVsStat>> GetTeamVsAsync(
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<RankingsBundle> GetRankingsAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);

    /// <summary>Live scoring duel only — local SQLite (fast/offline during balls). Not used by Statistics page.</summary>
    Task<ConfrontationStat?> GetDuelAsync(Guid batterId, Guid bowlerId, Guid? matchId = null, CancellationToken ct = default);
}

/// <summary>
/// Statistics UI is API-only (server SQL). No local leaderboard/career cache.
/// GetDuelAsync is the sole local path — for live ScoringPage only.
/// </summary>
public sealed class StatsService : IStatsService
{
    private readonly IApiClient _api;
    private readonly ISyncService _sync;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IMatchSquadCache _squadCache;

    public StatsService(
        IApiClient api,
        ISyncService sync,
        IDbContextFactory<JplDbContext> dbFactory,
        IMatchSquadCache squadCache)
    {
        _api = api;
        _sync = sync;
        _dbFactory = dbFactory;
        _squadCache = squadCache;
    }

    public async Task<IReadOnlyList<PlayerCareerSummary>> GetLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetStatsLeaderboardAsync(fromUtc, toExclusiveUtc, venueId, source, formatIds, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TeamCareerSummary>> GetTeamLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetTeamLeaderboardAsync(fromUtc, toExclusiveUtc, venueId, formatIds, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TeamVsStat>> GetTeamVsAsync(
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetTeamVsAsync(teamId, fromUtc, toExclusiveUtc, venueId, formatIds, ct).ConfigureAwait(false);
    }

    public async Task<PlayerCareerSummary?> GetCareerAsync(Guid playerId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetPlayerCareerAsync(playerId, source, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfrontationStat>> GetBattingVsBowlersAsync(Guid batterId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetBattingVsBowlersAsync(batterId, source, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfrontationStat>> GetBowlingVsBattersAsync(Guid bowlerId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetBowlingVsBattersAsync(bowlerId, source, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CaptainVsStat>> GetCaptainVsAsync(Guid playerId, CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetCaptainVsAsync(playerId, ct).ConfigureAwait(false);
    }

    public async Task<RankingsBundle> GetRankingsAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        // Always derive player rankings on the client so UI formulas/details stay current
        // even when the API process is an older build. Team table needs match results from API.
        var board = await _api.GetStatsLeaderboardAsync(fromUtc, toExclusiveUtc, venueId: null, source: "matches", formatIds: formatIds, ct: ct)
            .ConfigureAwait(false);
        var bundle = RankingsBuilder.FromLeaderboard(board);
        try
        {
            bundle.Team = await _api.GetTeamRankingsAsync(fromUtc, toExclusiveUtc, formatIds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            bundle.Team = Array.Empty<RankingRow>();
        }
        return bundle;
    }

    private Task FlushCompletedMatchesAsync(CancellationToken ct)
    {
        // Stats/Rankings must not wait on full outbox flush — that blocked the UI.
        // Completed matches are pushed on Sync Now / match complete / lifecycle.
        return Task.CompletedTask;
    }

    public async Task<ConfrontationStat?> GetDuelAsync(Guid batterId, Guid bowlerId, Guid? matchId = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var q = db.Deliveries.AsNoTracking().Where(d => d.BatterId == batterId && d.BowlerId == bowlerId);
        string batterName = "?";
        string bowlerName = "?";
        if (matchId is Guid mid)
        {
            q = q.Where(d => d.MatchId == mid);
            var squad = await _squadCache.WarmAsync(mid, ct).ConfigureAwait(false);
            batterName = squad.NameOf(batterId);
            bowlerName = squad.NameOf(bowlerId);
        }

        var deliveries = await q.ToListAsync(ct).ConfigureAwait(false);

        var row = new ConfrontationStat
        {
            BatterId = batterId,
            BatterName = batterName,
            BowlerId = bowlerId,
            BowlerName = bowlerName
        };

        foreach (var d in deliveries)
        {
            if (d.IsLegalDelivery)
            {
                row.Balls++;
                row.Runs += d.RunsOffBat;
                switch (d.RunsOffBat)
                {
                    case 0 when d.ExtraRuns == 0 && !d.IsWicket: row.Dots++; break;
                    case 1: row.Ones++; break;
                    case 2: row.Twos++; break;
                    case 3: row.Threes++; break;
                    case 4: row.Fours++; break;
                    case 5: row.Fives++; break;
                    case >= 6: row.Sixes++; break;
                }
            }

            row.ExtrasConceded += d.ExtraRuns;
            if (d.IsWicket && d.DismissedPlayerId == d.BatterId)
                row.Outs++;
        }

        return row;
    }
}
