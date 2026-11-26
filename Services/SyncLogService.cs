using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface ISyncLogService
{
    Task InfoAsync(string phase, string message, string? detail = null, CancellationToken ct = default);
    Task SuccessAsync(string phase, string message, string? detail = null, CancellationToken ct = default);
    Task WarnAsync(string phase, string message, string? detail = null, CancellationToken ct = default);
    Task ErrorAsync(string phase, string message, string? detail = null, CancellationToken ct = default);
    Task<IReadOnlyList<SyncLogEntry>> GetRecentAsync(int take = 200, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task<SyncLogSummary> GetSummaryAsync(CancellationToken ct = default);
}

public sealed record SyncLogSummary(
    bool IsBootstrapped,
    long LastSequence,
    DateTime? LastSuccessfulSyncUtc,
    int PendingOutbox,
    int SendingOutbox,
    int FailedOutbox,
    int RecentErrors);

public sealed class SyncLogService : ISyncLogService
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly ILeagueContext _league;

    public SyncLogService(IDbContextFactory<JplDbContext> dbFactory, ILeagueContext league)
    {
        _dbFactory = dbFactory;
        _league = league;
    }

    public Task InfoAsync(string phase, string message, string? detail = null, CancellationToken ct = default)
        => WriteAsync(SyncLogLevel.Info, phase, message, detail, ct);

    public Task SuccessAsync(string phase, string message, string? detail = null, CancellationToken ct = default)
        => WriteAsync(SyncLogLevel.Success, phase, message, detail, ct);

    public Task WarnAsync(string phase, string message, string? detail = null, CancellationToken ct = default)
        => WriteAsync(SyncLogLevel.Warn, phase, message, detail, ct);

    public Task ErrorAsync(string phase, string message, string? detail = null, CancellationToken ct = default)
        => WriteAsync(SyncLogLevel.Error, phase, message, detail, ct);

    private async Task WriteAsync(
        SyncLogLevel level, string phase, string message, string? detail, CancellationToken ct)
    {
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await DbBootstrap.EnsureSyncLogTableAsync(db, ct).ConfigureAwait(false);
            db.SyncLogs.Add(new SyncLogEntry
            {
                LeagueId = leagueId,
                AtUtc = DateTime.UtcNow,
                Level = level,
                Phase = Trim(phase, 40),
                Message = Trim(message, 280),
                Detail = string.IsNullOrWhiteSpace(detail) ? null : Trim(detail, 1200)
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Keep the table bounded so Sync Logs stays snappy.
            var old = await db.SyncLogs.AsNoTracking()
                .Where(row => row.LeagueId == leagueId)
                .OrderByDescending(row => row.AtUtc)
                .Skip(400)
                .Select(row => row.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (old.Count > 0)
            {
                await db.SyncLogs.Where(row => old.Contains(row.Id))
                    .ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            /* never break Sync because logging failed */
        }
    }

    public async Task<IReadOnlyList<SyncLogEntry>> GetRecentAsync(int take = 200, CancellationToken ct = default)
    {
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return Array.Empty<SyncLogEntry>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncLogTableAsync(db, ct).ConfigureAwait(false);
        return await db.SyncLogs.AsNoTracking()
            .Where(row => row.LeagueId == leagueId)
            .OrderByDescending(row => row.AtUtc)
            .Take(Math.Clamp(take, 20, 500))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncLogTableAsync(db, ct).ConfigureAwait(false);
        await db.SyncLogs.Where(row => row.LeagueId == leagueId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<SyncLogSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return new SyncLogSummary(false, 0, null, 0, 0, 0, 0);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncLogTableAsync(db, ct).ConfigureAwait(false);
        await DbBootstrap.DeduplicateSyncCursorsAsync(db, ct).ConfigureAwait(false);

        var cursor = await db.SyncCursors.AsNoTracking()
            .FirstOrDefaultAsync(row => row.LeagueId == leagueId, ct)
            .ConfigureAwait(false);
        var pending = await db.SyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.Pending, ct)
            .ConfigureAwait(false);
        pending += await db.LiveOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.Pending, ct)
            .ConfigureAwait(false);
        pending += await db.SchedulerSyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.Pending, ct)
            .ConfigureAwait(false);
        var sending = await db.SyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.InFlight, ct)
            .ConfigureAwait(false);
        sending += await db.LiveOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.InFlight, ct)
            .ConfigureAwait(false);
        sending += await db.SchedulerSyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.InFlight, ct)
            .ConfigureAwait(false);
        var failed = await db.SyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.Failed, ct)
            .ConfigureAwait(false);
        failed += await db.LiveOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.Failed, ct)
            .ConfigureAwait(false);
        failed += await db.SchedulerSyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status == SyncOutboxStatus.Failed, ct)
            .ConfigureAwait(false);
        var since = DateTime.UtcNow.AddHours(-24);
        var errors = await db.SyncLogs.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId
                               && row.Level == SyncLogLevel.Error
                               && row.AtUtc >= since, ct)
            .ConfigureAwait(false);

        return new SyncLogSummary(
            cursor?.IsBootstrapped == true,
            cursor?.LastSequence ?? 0,
            cursor?.LastSuccessfulSyncUtc,
            pending + sending + failed,
            sending,
            failed,
            errors);
    }

    private static string Trim(string value, int max)
    {
        var text = value.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
