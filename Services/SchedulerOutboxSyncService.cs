using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface ISchedulerOutboxSyncService
{
    Task SyncPendingAsync(CancellationToken ct = default);
    Task RefreshCacheAsync(CancellationToken ct = default);
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    void RequestSyncNow();
}

/// <summary>Serially applies scheduler operations using server versions and durable idempotency receipts.</summary>
public sealed class SchedulerOutboxSyncService : ISchedulerOutboxSyncService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly ISessionLifetime _lifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _requested;

    public SchedulerOutboxSyncService(
        IDbContextFactory<JplDbContext> dbFactory,
        IApiClient api,
        IAuthService auth,
        ILeagueContext league,
        ISessionLifetime lifetime)
    {
        _dbFactory = dbFactory;
        _api = api;
        _auth = auth;
        _league = league;
        _lifetime = lifetime;
    }

    private bool CanSyncCurrentLeague
        => LeagueSyncPolicy.AllowsLocalOfflineSync(_auth.Current, _league.CurrentLeagueId);

    public void RequestSyncNow()
    {
        if (!CanSyncCurrentLeague)
            return;
        if (Interlocked.Exchange(ref _requested, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            try { await SyncPendingAsync().ConfigureAwait(false); }
            catch { /* keep the durable queue for the next full/manual sync */ }
            finally { Interlocked.Exchange(ref _requested, 0); }
        });
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return 0;
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SchedulerSyncOutbox.AsNoTracking()
            .CountAsync(row => row.Status != SyncOutboxStatus.Completed, ct).ConfigureAwait(false);
    }

    public async Task SyncPendingAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return;
        // Snapshot the admin once; logout can null _auth.Current at any time.
        var session = _auth.Current;
        if (session is null)
            return;
        using var sessionLink = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        ct = sessionLink.Token;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                var rows = await db.SchedulerSyncOutbox
                    .Where(row => row.Status == SyncOutboxStatus.InFlight)
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var row in rows)
                    row.Status = SyncOutboxStatus.Pending;
                if (rows.Count > 0)
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            while (true)
            {
                SchedulerSyncOutboxItem? item;
                await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
                {
                    item = await db.SchedulerSyncOutbox
                        .Where(row => row.Status != SyncOutboxStatus.Completed)
                        .OrderBy(row => row.CreatedAtUtc)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (item is null)
                        break;
                    if (item.Status == SyncOutboxStatus.Failed)
                        throw new SyncConflictException(item.LastError
                            ?? "Scheduler sync is blocked by a saved conflict. Local data remains on this device.");

                    var creatorId = await db.SchedulerTournaments.IgnoreQueryFilters()
                        .Where(row => row.Id == item.TournamentId && row.LeagueId == db.CurrentLeagueId)
                        .Select(row => row.CreatedByUserId)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (creatorId != Guid.Empty && creatorId != session.UserId)
                        return; // Only the creating League Admin flushes this scheduler.

                    item.Status = SyncOutboxStatus.InFlight;
                    item.AttemptCount++;
                    item.LastAttemptAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }

                try
                {
                    var request = JsonSerializer.Deserialize<ApiSchedulerSyncRequest>(item.PayloadJson, Json)
                                  ?? throw new InvalidDataException("Saved scheduler sync request is invalid.");
                    var response = await _api.SyncSchedulerMutationAsync(request, ct).ConfigureAwait(false);
                    await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                    var tracked = await db.SchedulerSyncOutbox.FirstOrDefaultAsync(row => row.Id == item.Id, ct)
                        .ConfigureAwait(false);
                    if (tracked is null)
                        continue;
                    var tournament = await db.SchedulerTournaments.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(row => row.Id == item.TournamentId && row.LeagueId == db.CurrentLeagueId, ct)
                        .ConfigureAwait(false);
                    if (tournament is not null)
                        tournament.Version = Math.Max(tournament.Version, response.AppliedVersion);
                    db.SchedulerSyncOutbox.Remove(tracked);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var conflict = IsPermanentClientError(ex);
                    await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                    var tracked = await db.SchedulerSyncOutbox.FirstOrDefaultAsync(row => row.Id == item.Id, ct)
                        .ConfigureAwait(false);
                    if (tracked is not null)
                    {
                        tracked.Status = conflict || tracked.AttemptCount >= 8
                            ? SyncOutboxStatus.Failed
                            : SyncOutboxStatus.Pending;
                        tracked.LastError = TrimError(ex.Message);
                        await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    }

                    if (conflict)
                        throw new SyncConflictException($"Scheduler sync conflict: {ex.Message}");
                    throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshCacheAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return;
        var owner = _auth.Current;
        if (owner is null)
            return;
        var serverRows = await _api.GetSchedulerListAsync(ct).ConfigureAwait(false);
        var serverIds = serverRows.Select(row => row.Id).ToHashSet();

        foreach (var row in serverRows)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var local = await db.SchedulerTournaments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == row.Id && item.LeagueId == db.CurrentLeagueId, ct)
                .ConfigureAwait(false);
            var hasPending = await db.SchedulerSyncOutbox.AsNoTracking()
                .AnyAsync(item => item.TournamentId == row.Id && item.Status != SyncOutboxStatus.Completed, ct)
                .ConfigureAwait(false);
            if (hasPending)
                continue;

            if (local is not null && local.HasDetailsCached && local.Version >= row.Version)
            {
                local.Name = row.Name;
                local.GroupCount = row.GroupCount;
                local.RoundRobinType = (SchedulerRoundRobinType)row.RoundRobinType;
                local.QualifyPerGroup = row.QualifyPerGroup;
                local.KnockoutFormat = (SchedulerKnockoutFormat)row.KnockoutFormat;
                local.Status = (SchedulerStatus)row.Status;
                local.CreatedByUserId = row.CreatedByUserId;
                local.CreatedByUsername = row.CreatedByUsername;
                local.UpdatedAtUtc = row.UpdatedAtUtc;
                local.IsDeleted = false;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                continue;
            }

            var detail = await _api.GetSchedulerAsync(row.Id, ct).ConfigureAwait(false);
            if (detail is null)
                continue;
            await CacheDetailAsync(detail, ct).ConfigureAwait(false);
        }

        // The list endpoint is a complete owner-visible snapshot. Missing non-pending rows
        // are server-deleted and must not reappear from this device's cache.
        await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            var seeLeagueSchedules = owner.CanManagePlatform
                || owner.Role == AppRole.Admin
                || owner.Role == AppRole.User;
            var ownerId = owner.UserId;
            var leagueId = db.CurrentLeagueId;
            var cached = await db.SchedulerTournaments.IgnoreQueryFilters()
                .Where(row => row.LeagueId == leagueId
                    && (seeLeagueSchedules || row.CreatedByUserId == ownerId)
                    && !row.IsDeleted)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var row in cached.Where(row => !serverIds.Contains(row.Id)))
            {
                var hasPending = await db.SchedulerSyncOutbox.AsNoTracking()
                    .AnyAsync(item => item.TournamentId == row.Id && item.Status != SyncOutboxStatus.Completed, ct)
                    .ConfigureAwait(false);
                if (hasPending)
                    continue;
                row.IsDeleted = true;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task CacheDetailAsync(ApiSchedulerDetailDto dto, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await db.SchedulerSyncOutbox.AsNoTracking().AnyAsync(item => item.TournamentId == dto.Id
                && item.Status != SyncOutboxStatus.Completed, ct).ConfigureAwait(false))
            return;

        var target = await db.SchedulerTournaments.IgnoreQueryFilters()
            .Include(row => row.Teams)
            .Include(row => row.Matches)
            .FirstOrDefaultAsync(row => row.Id == dto.Id && row.LeagueId == db.CurrentLeagueId, ct)
            .ConfigureAwait(false);
        if (target is null)
        {
            target = new SchedulerTournament { Id = dto.Id };
            db.SchedulerTournaments.Add(target);
        }
        var mapped = FromDto(dto);
        target.Name = mapped.Name;
        target.GroupCount = mapped.GroupCount;
        target.RoundRobinType = mapped.RoundRobinType;
        target.QualifyPerGroup = mapped.QualifyPerGroup;
        target.KnockoutFormat = mapped.KnockoutFormat;
        target.Status = mapped.Status;
        target.WinnerTeamId = mapped.WinnerTeamId;
        target.QualificationPicksJson = mapped.QualificationPicksJson;
        target.CreatedByUserId = mapped.CreatedByUserId;
        target.CreatedByUsername = string.IsNullOrWhiteSpace(mapped.CreatedByUsername)
            ? _auth.Current?.Username ?? string.Empty
            : mapped.CreatedByUsername;
        target.Version = mapped.Version;
        target.CreatedAtUtc = mapped.CreatedAtUtc;
        target.UpdatedAtUtc = mapped.UpdatedAtUtc;
        target.IsDeleted = false;
        target.HasDetailsCached = true;

        var teams = mapped.Teams.ToDictionary(row => row.Id);
        foreach (var row in target.Teams.ToList())
        {
            if (!teams.Remove(row.Id, out var next))
            {
                db.SchedulerTeams.Remove(row);
                target.Teams.Remove(row);
                continue;
            }
            row.Name = next.Name;
            row.GroupNumber = next.GroupNumber;
            row.SortOrder = next.SortOrder;
        }
        foreach (var row in teams.Values)
            target.Teams.Add(row);

        var matches = mapped.Matches.ToDictionary(row => row.Id);
        foreach (var row in target.Matches.ToList())
        {
            if (!matches.Remove(row.Id, out var next))
            {
                db.SchedulerMatches.Remove(row);
                target.Matches.Remove(row);
                continue;
            }
            CopyMatch(row, next);
        }
        foreach (var row in matches.Values)
            target.Matches.Add(row);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static SchedulerTournament FromDto(ApiSchedulerDetailDto dto)
    {
        var entity = new SchedulerTournament
        {
            Id = dto.Id,
            Name = dto.Name,
            GroupCount = dto.GroupCount,
            RoundRobinType = (SchedulerRoundRobinType)dto.RoundRobinType,
            QualifyPerGroup = dto.QualifyPerGroup,
            KnockoutFormat = (SchedulerKnockoutFormat)dto.KnockoutFormat,
            Status = (SchedulerStatus)dto.Status,
            WinnerTeamId = dto.WinnerTeamId,
            QualificationPicksJson = dto.QualificationPicksJson,
            CreatedByUserId = dto.CreatedByUserId,
            CreatedByUsername = dto.CreatedByUsername,
            Version = dto.Version,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            HasDetailsCached = true
        };
        entity.Teams = dto.Teams.Select(team => new SchedulerTeam
        {
            Id = team.Id,
            TournamentId = dto.Id,
            Name = team.Name,
            GroupNumber = team.GroupNumber,
            SortOrder = team.SortOrder
        }).ToList();
        entity.Matches = dto.Matches.Select(match => new SchedulerMatch
        {
            Id = match.Id,
            TournamentId = dto.Id,
            Stage = (SchedulerMatchStage)match.Stage,
            GroupNumber = match.GroupNumber,
            Sequence = match.Sequence,
            Label = match.Label,
            TeamAId = match.TeamAId,
            TeamBId = match.TeamBId,
            TeamASlot = match.TeamASlot,
            TeamBSlot = match.TeamBSlot,
            Result = (SchedulerMatchResult)match.Result,
            WinnerFeedsMatchId = match.WinnerFeedsMatchId,
            WinnerFeedsIsTeamA = match.WinnerFeedsIsTeamA,
            LoserFeedsMatchId = match.LoserFeedsMatchId,
            LoserFeedsIsTeamA = match.LoserFeedsIsTeamA
        }).ToList();
        return entity;
    }

    private static void CopyMatch(SchedulerMatch target, SchedulerMatch source)
    {
        target.Stage = source.Stage;
        target.GroupNumber = source.GroupNumber;
        target.Sequence = source.Sequence;
        target.Label = source.Label;
        target.TeamAId = source.TeamAId;
        target.TeamBId = source.TeamBId;
        target.TeamASlot = source.TeamASlot;
        target.TeamBSlot = source.TeamBSlot;
        target.Result = source.Result;
        target.WinnerFeedsMatchId = source.WinnerFeedsMatchId;
        target.WinnerFeedsIsTeamA = source.WinnerFeedsIsTeamA;
        target.LoserFeedsMatchId = source.LoserFeedsMatchId;
        target.LoserFeedsIsTeamA = source.LoserFeedsIsTeamA;
    }

    private static bool IsPermanentClientError(Exception exception)
        => exception is HttpRequestException http
           && http.Message.Contains("API 4", StringComparison.OrdinalIgnoreCase);

    private static string TrimError(string message)
        => message.Length > 400 ? message[..400] : message;
}
