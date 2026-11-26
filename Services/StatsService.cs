using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface IStatsService
{
    /// <summary>Statistics are refreshed from the API and served from the local cache during outages.</summary>
    Task<PlayerCareerSummary?> GetCareerAsync(Guid playerId, string source = "matches", CancellationToken ct = default);
    Task<ApiPagedResult<NetSessionAwardsRow>> GetNetSessionAwardsPageAsync(
        int skip, string? query = null, CancellationToken ct = default);
    Task<IReadOnlyList<ApiMatchSummaryDto>> GetPlayerMatchesAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerTournamentHistoryItem>> GetPlayerTournamentHistoryAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerSeriesHistoryItem>> GetPlayerSeriesHistoryAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<ConfrontationStat>> GetBattingVsBowlersAsync(Guid batterId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<ConfrontationStat>> GetBowlingVsBattersAsync(Guid bowlerId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<FieldingVsStat>> GetFieldingVsAsync(Guid fielderId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<CaptainVsStat>> GetCaptainVsAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<PartnershipStat>> GetPartnershipsAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<PartnershipMatchStat>> GetPartnershipMatchesAsync(
        Guid playerId,
        Guid partnerId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<PartnershipStat>> GetPartnershipLeaderboardAsync(
        string? query = null,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<ApiPagedResult<PartnershipStat>> GetPartnershipLeaderboardPageAsync(
        int skip,
        string? query = null,
        string? sort = null,
        bool ascending = false,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<StatsFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PlayerCareerSummary>> GetLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        bool attendanceFromDebut = false,
        bool asCaptain = false,
        Guid? captainPlayerId = null,
        bool captainOpponentSide = false,
        bool asPlayer = false,
        CancellationToken ct = default);
    Task<ApiPagedResult<PlayerCareerSummary>> GetLeaderboardPageAsync(
        int skip,
        string? q = null,
        string? sort = null,
        bool ascending = false,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        bool attendanceFromDebut = false,
        bool asCaptain = false,
        Guid? captainPlayerId = null,
        bool captainOpponentSide = false,
        bool asPlayer = false,
        CancellationToken ct = default);
    Task<IReadOnlyList<TeamCareerSummary>> GetTeamLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<ApiPagedResult<TeamCareerSummary>> GetTeamLeaderboardPageAsync(
        int skip,
        string? q = null,
        string? sort = null,
        bool ascending = false,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<TeamVsStat>> GetTeamVsAsync(
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<RankingsBundle> GetRankingsAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);
    Task<ApiPagedResult<RankingRow>> GetRankingsPageAsync(
        int skip,
        string category,
        string? q = null,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default);

    /// <summary>Live scoring duel only — local SQLite (fast/offline during balls). Not used by Statistics page.</summary>
    Task<ConfrontationStat?> GetDuelAsync(Guid batterId, Guid bowlerId, Guid? matchId = null, CancellationToken ct = default);
}

/// <summary>
/// Statistics use server calculations while online and a per-league SQLite response cache offline.
/// GetDuelAsync computes from local deliveries for live ScoringPage.
/// </summary>
public sealed class StatsService : IStatsService
{
    private readonly IApiClient _api;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IMatchSquadCache _squadCache;

    public StatsService(
        IApiClient api,
        ISyncService sync,
        IAuthService auth,
        ILeagueContext league,
        IDbContextFactory<JplDbContext> dbFactory,
        IMatchSquadCache squadCache)
    {
        _api = api;
        _sync = sync;
        _auth = auth;
        _league = league;
        _dbFactory = dbFactory;
        _squadCache = squadCache;
    }

    public async Task<StatsFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetFilterOptionsAsync), new { },
            () => _api.GetStatsFilterOptionsAsync(ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlayerCareerSummary>> GetLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        bool attendanceFromDebut = false,
        bool asCaptain = false,
        Guid? captainPlayerId = null,
        bool captainOpponentSide = false,
        bool asPlayer = false,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetLeaderboardAsync), new
        {
            fromUtc, toExclusiveUtc, venueId, source, formatIds, tournamentIds, seriesIds, matchIds,
            attendanceFromDebut, asCaptain, captainPlayerId, captainOpponentSide, asPlayer
        }, () => _api.GetStatsLeaderboardAsync(
                fromUtc, toExclusiveUtc, venueId, source, formatIds, tournamentIds, seriesIds, matchIds,
                attendanceFromDebut, asCaptain, captainPlayerId, captainOpponentSide, asPlayer, ct), ct)
            .ConfigureAwait(false);
    }

    public async Task<ApiPagedResult<PlayerCareerSummary>> GetLeaderboardPageAsync(
        int skip,
        string? q = null,
        string? sort = null,
        bool ascending = false,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        bool attendanceFromDebut = false,
        bool asCaptain = false,
        Guid? captainPlayerId = null,
        bool captainOpponentSide = false,
        bool asPlayer = false,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        var request = new
        {
            skip, q, sort, ascending, fromUtc, toExclusiveUtc, venueId, source, formatIds, tournamentIds,
            seriesIds, matchIds, attendanceFromDebut, asCaptain, captainPlayerId, captainOpponentSide, asPlayer
        };
        // Connectivity is a soft hint only — always attempt HTTP; cache on failure.
        try
        {
            return await GetCachedOrFetchAsync(nameof(GetLeaderboardPageAsync), request, () => _api.GetStatsLeaderboardPageAsync(
                skip, q, sort, ascending, fromUtc, toExclusiveUtc, venueId, source, formatIds,
                tournamentIds, seriesIds, matchIds, attendanceFromDebut, asCaptain,
                captainPlayerId, captainOpponentSide, asPlayer, ct), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex)
                                   && IsBaselinePlayerRequest(fromUtc, toExclusiveUtc, venueId, source, formatIds,
                                       tournamentIds, seriesIds, matchIds, attendanceFromDebut, asCaptain,
                                       captainPlayerId, captainOpponentSide, asPlayer))
        {
            var cached = await TryGetOfflineLeaderboardPageAsync(skip, q, sort, ascending, source, ct)
                .ConfigureAwait(false);
            if (cached is not null)
                return cached;
            throw;
        }
    }

    public async Task<IReadOnlyList<TeamCareerSummary>> GetTeamLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetTeamLeaderboardAsync), new
        {
            fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds
        }, () => _api.GetTeamLeaderboardAsync(
            fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds, ct), ct)
            .ConfigureAwait(false);
    }

    public async Task<ApiPagedResult<TeamCareerSummary>> GetTeamLeaderboardPageAsync(
        int skip,
        string? q = null,
        string? sort = null,
        bool ascending = false,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        var request = new
        {
            skip, q, sort, ascending, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds
        };
        try
        {
            return await GetCachedOrFetchAsync(nameof(GetTeamLeaderboardPageAsync), request,
                () => _api.GetTeamLeaderboardPageAsync(skip, q, sort, ascending, fromUtc, toExclusiveUtc,
                    venueId, formatIds, tournamentIds, seriesIds, matchIds, ct), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex)
                                   && IsBaselineFilter(fromUtc, toExclusiveUtc, venueId, formatIds,
                                       tournamentIds, seriesIds, matchIds))
        {
            var cached = await TryGetOfflineTeamPageAsync(skip, q, sort, ascending, ct).ConfigureAwait(false);
            if (cached is not null)
                return cached;
            throw;
        }
    }

    public async Task<IReadOnlyList<TeamVsStat>> GetTeamVsAsync(
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetTeamVsAsync), new
        {
            teamId, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds
        }, () => _api.GetTeamVsAsync(
            teamId, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds, ct), ct)
            .ConfigureAwait(false);
    }

    public async Task<PlayerCareerSummary?> GetCareerAsync(Guid playerId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetCareerAsync), new { playerId, source },
            () => _api.GetPlayerCareerAsync(playerId, source, ct), ct).ConfigureAwait(false);
    }

    public async Task<ApiPagedResult<NetSessionAwardsRow>> GetNetSessionAwardsPageAsync(
        int skip, string? query = null, CancellationToken ct = default)
    {
        return await GetCachedOrFetchAsync(nameof(GetNetSessionAwardsPageAsync), new { skip, query },
            () => _api.GetNetSessionAwardsPageAsync(skip, query, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiMatchSummaryDto>> GetPlayerMatchesAsync(Guid playerId, CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await _api.GetPlayerMatchesAsync(playerId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlayerTournamentHistoryItem>> GetPlayerTournamentHistoryAsync(Guid playerId, CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        var matches = await _api.GetPlayerMatchesAsync(playerId, ct).ConfigureAwait(false);
        var tournamentMatches = matches.Where(m => m.TournamentId is not null)
            .GroupBy(m => m.TournamentId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.MatchDateUtc).ToList());
        if (tournamentMatches.Count == 0) return Array.Empty<PlayerTournamentHistoryItem>();
        var tournaments = await _api.GetTournamentsAsync(ct).ConfigureAwait(false);
        var tournamentById = tournaments.ToDictionary(t => t.Id);
        return tournamentMatches.Select(group =>
        {
            tournamentById.TryGetValue(group.Key, out var tournament);
            var item = new PlayerTournamentHistoryItem
            {
                TournamentId = group.Key,
                TournamentName = tournament?.Name ?? "Tournament",
                StatusText = tournament?.StatusText ?? string.Empty
            };
            foreach (var match in group.Value) item.Matches.Add(match);
            return item;
        }).OrderByDescending(t => t.Matches.Max(m => m.MatchDateUtc)).ToList();
    }

    public async Task<IReadOnlyList<PlayerSeriesHistoryItem>> GetPlayerSeriesHistoryAsync(Guid playerId, CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        var matches = await _api.GetPlayerMatchesAsync(playerId, ct).ConfigureAwait(false);
        var seriesMatches = matches.Where(m => m.SeriesId is not null)
            .GroupBy(m => m.SeriesId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.MatchDateUtc).ToList());
        if (seriesMatches.Count == 0) return Array.Empty<PlayerSeriesHistoryItem>();
        var series = await _api.GetSeriesListAsync(ct).ConfigureAwait(false);
        var seriesById = series.ToDictionary(s => s.Id);
        return seriesMatches.Select(group =>
        {
            seriesById.TryGetValue(group.Key, out var itemDto);
            var item = new PlayerSeriesHistoryItem
            {
                SeriesId = group.Key,
                SeriesName = itemDto?.Name ?? "Series",
                StatusText = itemDto?.StatusText ?? string.Empty,
                TeamAName = itemDto?.TeamAName ?? string.Empty,
                TeamBName = itemDto?.TeamBName ?? string.Empty,
                TeamAWins = itemDto?.TeamAWins ?? 0,
                TeamBWins = itemDto?.TeamBWins ?? 0
            };
            foreach (var match in group.Value) item.Matches.Add(match);
            return item;
        }).OrderByDescending(s => s.Matches.Max(m => m.MatchDateUtc)).ToList();
    }

    public async Task<IReadOnlyList<ConfrontationStat>> GetBattingVsBowlersAsync(Guid batterId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetBattingVsBowlersAsync), new { batterId, source },
            () => _api.GetBattingVsBowlersAsync(batterId, source, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfrontationStat>> GetBowlingVsBattersAsync(Guid bowlerId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetBowlingVsBattersAsync), new { bowlerId, source },
            () => _api.GetBowlingVsBattersAsync(bowlerId, source, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FieldingVsStat>> GetFieldingVsAsync(Guid fielderId, string source = "matches", CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetFieldingVsAsync), new { fielderId, source },
            () => _api.GetFieldingVsAsync(fielderId, source, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CaptainVsStat>> GetCaptainVsAsync(Guid playerId, CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetCaptainVsAsync), new { playerId },
            () => _api.GetCaptainVsAsync(playerId, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PartnershipStat>> GetPartnershipsAsync(Guid playerId, CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetPartnershipsAsync), new { playerId },
            () => _api.GetPartnershipsAsync(playerId, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PartnershipMatchStat>> GetPartnershipMatchesAsync(
        Guid playerId,
        Guid partnerId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetPartnershipMatchesAsync), new
        {
            playerId, partnerId, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds
        }, () => _api.GetPartnershipMatchesAsync(
            playerId, partnerId, fromUtc, toExclusiveUtc, venueId,
            formatIds, tournamentIds, seriesIds, matchIds, ct), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PartnershipStat>> GetPartnershipLeaderboardAsync(
        string? query = null,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetPartnershipLeaderboardAsync), new
        {
            query, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds
        }, () => _api.GetPartnershipLeaderboardAsync(
            query, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds, ct), ct)
            .ConfigureAwait(false);
    }

    public async Task<ApiPagedResult<PartnershipStat>> GetPartnershipLeaderboardPageAsync(
        int skip,
        string? query = null,
        string? sort = null,
        bool ascending = false,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        var request = new
        {
            skip, query, sort, ascending, fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds,
            seriesIds, matchIds
        };
        try
        {
            return await GetCachedOrFetchAsync(nameof(GetPartnershipLeaderboardPageAsync), request,
                () => _api.GetPartnershipLeaderboardPageAsync(skip, query, sort, ascending, fromUtc,
                    toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds, ct), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex)
                                   && IsBaselineFilter(fromUtc, toExclusiveUtc, venueId, formatIds,
                                       tournamentIds, seriesIds, matchIds))
        {
            var cached = await TryGetOfflinePartnershipPageAsync(skip, query, sort, ascending, ct)
                .ConfigureAwait(false);
            if (cached is not null)
                return cached;
            throw;
        }
    }

    public async Task<RankingsBundle> GetRankingsAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetRankingsAsync), new
        {
            fromUtc, toExclusiveUtc, formatIds, tournamentIds, seriesIds, matchIds
        }, async () =>
        {
            // Always derive player rankings on the client so UI formulas/details stay current
            // even when the API process is an older build. Team table needs match results from API.
            var board = await _api.GetStatsLeaderboardAsync(
                fromUtc, toExclusiveUtc, venueId: null, source: "matches", formatIds: formatIds,
                tournamentIds, seriesIds, matchIds, attendanceFromDebut: false, ct: ct)
            .ConfigureAwait(false);
            var bundle = RankingsBuilder.FromLeaderboard(board);
            try
            {
                bundle.Team = await _api.GetTeamRankingsAsync(
                    fromUtc, toExclusiveUtc, formatIds, tournamentIds, seriesIds, matchIds, ct)
                .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
            {
                throw;
            }
            catch
            {
                bundle.Team = Array.Empty<RankingRow>();
            }
            return bundle;
        }, ct).ConfigureAwait(false);
    }

    public async Task<ApiPagedResult<RankingRow>> GetRankingsPageAsync(
        int skip,
        string category,
        string? q = null,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        IReadOnlyList<Guid>? tournamentIds = null,
        IReadOnlyList<Guid>? seriesIds = null,
        IReadOnlyList<Guid>? matchIds = null,
        CancellationToken ct = default)
    {
        await FlushCompletedMatchesAsync(ct).ConfigureAwait(false);
        return await GetCachedOrFetchAsync(nameof(GetRankingsPageAsync), new
        {
            skip, category, q, fromUtc, toExclusiveUtc, formatIds, tournamentIds, seriesIds, matchIds
        }, () => _api.GetRankingsPageAsync(
                skip, category, q, fromUtc, toExclusiveUtc, formatIds, tournamentIds, seriesIds, matchIds, ct),
            ct).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static bool IsBaselineFilter(DateTime? fromUtc, DateTime? toExclusiveUtc, Guid? venueId,
        IReadOnlyList<Guid>? formatIds, IReadOnlyList<Guid>? tournamentIds,
        IReadOnlyList<Guid>? seriesIds, IReadOnlyList<Guid>? matchIds)
        => fromUtc is null && toExclusiveUtc is null && venueId is null
           && (formatIds is null || formatIds.Count == 0)
           && (tournamentIds is null || tournamentIds.Count == 0)
           && (seriesIds is null || seriesIds.Count == 0)
           && (matchIds is null || matchIds.Count == 0);

    private static bool IsBaselinePlayerRequest(DateTime? fromUtc, DateTime? toExclusiveUtc, Guid? venueId,
        string source, IReadOnlyList<Guid>? formatIds, IReadOnlyList<Guid>? tournamentIds,
        IReadOnlyList<Guid>? seriesIds, IReadOnlyList<Guid>? matchIds, bool attendanceFromDebut,
        bool asCaptain, Guid? captainPlayerId, bool captainOpponentSide, bool asPlayer)
        => IsBaselineFilter(fromUtc, toExclusiveUtc, venueId, formatIds, tournamentIds, seriesIds, matchIds)
           && !attendanceFromDebut && !asCaptain && captainPlayerId is null
           && !captainOpponentSide && !asPlayer
           && (string.Equals(source, "matches", StringComparison.OrdinalIgnoreCase)
               || string.Equals(source, "nets", StringComparison.OrdinalIgnoreCase));

    private async Task<ApiPagedResult<PlayerCareerSummary>?> TryGetOfflineLeaderboardPageAsync(
        int skip, string? query, string? sort, bool ascending, string source, CancellationToken ct)
    {
        if (!CanUseLocalLeagueData) return null;
        var request = new
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
        };
        var cached = await ReadCacheAsync<List<PlayerCareerSummary>>(
            CreateCacheKey(nameof(GetLeaderboardAsync), request), ct).ConfigureAwait(false);
        return cached.Found && cached.Value is not null
            ? BuildLocalPage(cached.Value, skip, query, sort, ascending, row => row.PlayerName)
            : null;
    }

    private async Task<ApiPagedResult<TeamCareerSummary>?> TryGetOfflineTeamPageAsync(
        int skip, string? query, string? sort, bool ascending, CancellationToken ct)
    {
        if (!CanUseLocalLeagueData) return null;
        var request = new
        {
            fromUtc = (DateTime?)null,
            toExclusiveUtc = (DateTime?)null,
            venueId = (Guid?)null,
            formatIds = (IReadOnlyList<Guid>?)null,
            tournamentIds = (IReadOnlyList<Guid>?)null,
            seriesIds = (IReadOnlyList<Guid>?)null,
            matchIds = (IReadOnlyList<Guid>?)null
        };
        var cached = await ReadCacheAsync<List<TeamCareerSummary>>(
            CreateCacheKey(nameof(GetTeamLeaderboardAsync), request), ct).ConfigureAwait(false);
        return cached.Found && cached.Value is not null
            ? BuildLocalPage(cached.Value, skip, query, sort, ascending, row => row.TeamName)
            : null;
    }

    private async Task<ApiPagedResult<PartnershipStat>?> TryGetOfflinePartnershipPageAsync(
        int skip, string? query, string? sort, bool ascending, CancellationToken ct)
    {
        if (!CanUseLocalLeagueData) return null;
        var request = new
        {
            query = (string?)null,
            fromUtc = (DateTime?)null,
            toExclusiveUtc = (DateTime?)null,
            venueId = (Guid?)null,
            formatIds = (IReadOnlyList<Guid>?)null,
            tournamentIds = (IReadOnlyList<Guid>?)null,
            seriesIds = (IReadOnlyList<Guid>?)null,
            matchIds = (IReadOnlyList<Guid>?)null
        };
        var cached = await ReadCacheAsync<List<PartnershipStat>>(
            CreateCacheKey(nameof(GetPartnershipLeaderboardAsync), request), ct).ConfigureAwait(false);
        return cached.Found && cached.Value is not null
            ? BuildLocalPage(cached.Value, skip, query, sort, ascending, row => row.PairName)
            : null;
    }

    private static ApiPagedResult<T> BuildLocalPage<T>(
        IEnumerable<T> source, int skip, string? query, string? sort, bool ascending, Func<T, string> name)
    {
        var rows = source;
        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(row => name(row).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        var list = rows.ToList();
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var property = typeof(T).GetProperty(sort,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.IgnoreCase);
            if (property is not null)
            {
                var selector = (Func<T, object?>)(row => property.GetValue(row));
                list = (ascending
                    ? list.OrderBy(selector, LocalObjectComparer.Instance)
                    : list.OrderByDescending(selector, LocalObjectComparer.Instance)).ToList();
            }
        }
        var offset = Math.Max(0, skip);
        return new ApiPagedResult<T>(list.Skip(offset).Take(ListPaging.PageSize).ToList(), list.Count,
            offset, ListPaging.PageSize);
    }

    private sealed class LocalObjectComparer : IComparer<object?>
    {
        public static LocalObjectComparer Instance { get; } = new();
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x is IComparable comparable && x.GetType().IsAssignableFrom(y.GetType()))
                return comparable.CompareTo(y);
            return StringComparer.OrdinalIgnoreCase.Compare(x.ToString(), y.ToString());
        }
    }

    private static string CreateCacheKey(string operation, object request)
    {
        var requestJson = JsonSerializer.Serialize(new { operation, request }, CacheJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
    }

    private async Task<T> GetCachedOrFetchAsync<T>(
        string operation,
        object request,
        Func<Task<T>> fetch,
        CancellationToken ct)
    {
        if (!CanUseLocalLeagueData)
            return await fetch().ConfigureAwait(false);

        var cacheKey = CreateCacheKey(operation, request);
        var cached = await ReadCacheAsync<T>(cacheKey, ct).ConfigureAwait(false);
        if (cached.Found)
            return cached.Value!;

        // League Admin statistics are refreshed by Sync. A feature page must never
        // fetch them directly when the local snapshot is missing.
        throw new OfflineException("Statistics are not available locally yet. Run Sync to refresh them.");

    }

    private bool CanUseLocalLeagueData
        => LeagueSyncPolicy.UsesLocalStorage(_auth.Current);

    private async Task SaveCacheAsync<T>(string cacheKey, T result, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (db.CurrentLeagueId == Guid.Empty)
            return;
        var row = await db.LocalStatsCache.FirstOrDefaultAsync(item => item.CacheKey == cacheKey, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new LocalStatsCacheItem { CacheKey = cacheKey };
            db.LocalStatsCache.Add(row);
        }
        row.PayloadJson = JsonSerializer.Serialize(result, CacheJson);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var expired = await db.LocalStatsCache
            .Where(item => item.UpdatedAtUtc < DateTime.UtcNow.AddDays(-30))
            .ToListAsync(ct).ConfigureAwait(false);
        if (expired.Count > 0)
        {
            db.LocalStatsCache.RemoveRange(expired);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<(bool Found, T? Value)> ReadCacheAsync<T>(string cacheKey, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (db.CurrentLeagueId == Guid.Empty)
            return (false, default);
        var json = await db.LocalStatsCache.AsNoTracking()
            .Where(item => item.CacheKey == cacheKey)
            .Select(item => item.PayloadJson)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (json is null)
            return (false, default);
        try { return (true, JsonSerializer.Deserialize<T>(json, CacheJson)); }
        catch { return (false, default); }
    }

    private Task FlushCompletedMatchesAsync(CancellationToken ct)
    {
        // Stats/Rankings must not wait on full outbox flush — that blocked the UI.
        // Completed matches are pushed on Sync Now / match complete / lifecycle.
        return Task.CompletedTask;
    }

    public async Task<ConfrontationStat?> GetDuelAsync(Guid batterId, Guid bowlerId, Guid? matchId = null, CancellationToken ct = default)
    {
        if (!CanUseLocalLeagueData)
            return null;

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
            if (!d.IsWicket || d.DismissedPlayerId != d.BatterId)
                continue;

            row.Outs++;
            switch (d.WicketType)
            {
                case Domain.WicketType.Bowled: row.Bowled++; break;
                case Domain.WicketType.Caught: row.Caught++; break;
                case Domain.WicketType.Lbw: row.Lbw++; break;
                case Domain.WicketType.RunOut: row.RunOut++; break;
                case Domain.WicketType.Stumped: row.Stumped++; break;
                case Domain.WicketType.HitWicket: row.HitWicket++; break;
            }
        }

        return row;
    }
}
