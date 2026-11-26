using System.Linq.Expressions;
using System.Text.Json;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

/// <summary>
/// Local-first sync: master data + completed Match/Innings/Delivery + completed NetSession/NetDelivery
/// go through <see cref="SyncOutboxItem"/>. Incomplete scoring uses LiveOutbox.
/// Tournament/series scores stay local until the event is decided, then flush via
/// <see cref="FlushTournamentAsync"/> / <see cref="FlushSeriesAsync"/>.
/// </summary>
public interface ISyncTrigger
{
    /// <summary>Flush standalone completed-match outbox in background (safe to call often; single-flight).</summary>
    void RequestSyncNow();
}

public sealed record SyncConflictInfo(
    Guid OutboxId,
    string EntityType,
    Guid EntityId,
    string Operation,
    string Message,
    string? LocalPayloadJson,
    string? BasePayloadJson,
    string? ServerPayloadJson,
    int AttemptCount,
    DateTime CreatedAtUtc);

public sealed record SyncOutboxInfo(
    Guid OutboxId,
    string EntityType,
    Guid EntityId,
    string Operation,
    SyncOutboxStatus Status,
    string PayloadJson,
    string? LastError,
    int AttemptCount,
    DateTime CreatedAtUtc);

public interface ISyncService
{
    /// <summary>True while a full SyncAll run owns the exclusive sync lock.</summary>
    bool IsSyncing { get; }

    /// <summary>Raised when <see cref="IsSyncing"/> flips (UI can disable Sync now).</summary>
    event EventHandler? SyncStateChanged;

    /// <summary>Flush pending outbox for standalone (non-tournament) completed matches only.</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Pushes durable local work, bootstraps the selected league if needed, then pulls server changes.</summary>
    Task SyncAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Same as SyncAll, but returns false immediately if a sync is already running
    /// (does not queue). Use for manual Sync now so a second tap cannot stack.
    /// </summary>
    Task<bool> TrySyncAllNowAsync(CancellationToken ct = default);

    /// <summary>Runs a full sync at most once per 24 hours for the selected league.</summary>
    Task SyncDailyIfDueAsync(CancellationToken ct = default);

    /// <summary>Terminal SYNC_CONFLICT outbox rows waiting for accept-server / keep-local.</summary>
    Task<IReadOnlyList<SyncConflictInfo>> GetConflictsAsync(CancellationToken ct = default);

    /// <summary>Local unsynced records for the selected League Admin league.</summary>
    Task<IReadOnlyList<SyncOutboxInfo>> GetOutboxItemsAsync(int take = 100, CancellationToken ct = default);

    /// <summary>Discard the local outbox change and apply the server snapshot for that entity.</summary>
    Task ResolveConflictAcceptServerAsync(Guid outboxId, CancellationToken ct = default);

    /// <summary>Rebase local change onto the server snapshot and retry push (no silent overwrite).</summary>
    Task ResolveConflictKeepLocalAsync(Guid outboxId, CancellationToken ct = default);

    /// <summary>Flush one standalone completed match. Tournament matches are rejected.</summary>
    Task FlushMatchAsync(Guid matchId, CancellationToken ct = default);

    /// <summary>
    /// Flush all completed matches for a tournament.
    /// Caller must ensure the tournament is fully finished (see ITournamentService.EnsureReadyToSyncAsync).
    /// </summary>
    Task FlushTournamentAsync(Guid tournamentId, CancellationToken ct = default);

    /// <summary>
    /// Flush all completed matches for a series.
    /// Caller must ensure the series is fully finished (see ISeriesService.EnsureReadyToSyncAsync).
    /// </summary>
    Task FlushSeriesAsync(Guid seriesId, CancellationToken ct = default);

    Task<int> GetPendingCountAsync(CancellationToken ct = default);

    /// <summary>Standalone completed matches that still have pending/failed outbox rows.</summary>
    Task<HashSet<Guid>> GetPendingStandaloneMatchIdsAsync(CancellationToken ct = default);

    /// <summary>Completed nets sessions with pending/failed outbox rows.</summary>
    Task<HashSet<Guid>> GetPendingNetSessionIdsAsync(CancellationToken ct = default);

    /// <summary>Flush one completed nets session.</summary>
    Task FlushNetSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Tournaments with pending outbox and no live matches (UI still gates on fully completed).</summary>
    Task<HashSet<Guid>> GetPendingTournamentIdsAsync(CancellationToken ct = default);

    /// <summary>Series with pending outbox and no live matches (UI still gates on fully completed).</summary>
    Task<HashSet<Guid>> GetPendingSeriesIdsAsync(CancellationToken ct = default);
}

public sealed class SyncService : ISyncService, ISyncTrigger, IAsyncDisposable
{
    private sealed record MatchSyncState(Guid Id, Guid? TournamentId, Guid? TournamentFixtureId,
        Guid? SeriesId, Guid? SeriesFixtureId, MatchStatus Status);

    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly ISqlServerSyncClient _remote;
    private readonly ILeagueContext _league;
    private readonly IAuthService _auth;
    private readonly ILivePublisher _live;
    private readonly ISchedulerOutboxSyncService _schedulerSync;
    private readonly IStatsOfflineCacheWarmer _statsCacheWarmer;
    private readonly ISyncLogService _syncLog;
    private readonly ISessionLifetime _lifetime;
    private bool CanSyncCurrentLeague
        => LeagueSyncPolicy.AllowsLocalOfflineSync(_auth.Current, _league.CurrentLeagueId);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _pullGate = new(1, 1);
    private readonly SemaphoreSlim _dailyGate = new(1, 1);
    private readonly SemaphoreSlim _syncAllGate = new(1, 1);
    private int _flushRequested;
    private int _syncRunRequested;
    private int _disposed;
    private int _isSyncing;

    public bool IsSyncing => Volatile.Read(ref _isSyncing) == 1;
    public event EventHandler? SyncStateChanged;

    public SyncService(IDbContextFactory<JplDbContext> dbFactory, ISqlServerSyncClient remote,
        ILeagueContext league, IAuthService auth, ILivePublisher live, ISchedulerOutboxSyncService schedulerSync,
        IStatsOfflineCacheWarmer statsCacheWarmer, ISyncLogService syncLog, ISessionLifetime lifetime)
    {
        _dbFactory = dbFactory;
        _remote = remote;
        _league = league;
        _auth = auth;
        _live = live;
        _schedulerSync = schedulerSync;
        _statsCacheWarmer = statsCacheWarmer;
        _syncLog = syncLog;
        _lifetime = lifetime;
        _lifetime.RegisterDrain(DrainForSessionEndAsync);
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <summary>Who/which league a sync run was started for (detects logout / account switch).</summary>
    private sealed record SyncSessionStamp(long Generation, Guid? UserId, Guid? LeagueId);

    private SyncSessionStamp CaptureSession()
        => new(_lifetime.Generation, _auth.Current?.UserId, _league.CurrentLeagueId);

    private bool IsSessionChanged(SyncSessionStamp stamp)
        => stamp.Generation != _lifetime.Generation
           || stamp.UserId != _auth.Current?.UserId
           || stamp.LeagueId != _league.CurrentLeagueId;

    private void ThrowIfSessionChanged(SyncSessionStamp stamp, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsSessionChanged(stamp))
            throw new OperationCanceledException("Signed-in League Admin changed — previous sync stopped.");
    }

    /// <summary>Every sync entry point is cancelled when the signed-in session ends.</summary>
    private CancellationTokenSource LinkSession(CancellationToken ct)
        => CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);

    /// <summary>Logout waits (bounded) until the cancelled run has released its locks.</summary>
    private async Task DrainForSessionEndAsync(TimeSpan timeout)
    {
        Interlocked.Exchange(ref _flushRequested, 0);
        Interlocked.Exchange(ref _syncRunRequested, 0);
        foreach (var gate in new[] { _syncAllGate, _pullGate, _gate })
        {
            try
            {
                if (await gate.WaitAsync(timeout).ConfigureAwait(false))
                    gate.Release();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return;

        var stamp = CaptureSession();
        using var sessionLink = LinkSession(ct);
        ct = sessionLink.Token;

        await _syncAllGate.WaitAsync(ct).ConfigureAwait(false);
        BeginSyncAllLock();
        try
        {
            // A queued run may have waited across a logout / login of another admin.
            if (!CanSyncCurrentLeague || IsSessionChanged(stamp))
                return;
            await RunSyncAllCoreAsync(stamp, ct).ConfigureAwait(false);
        }
        finally
        {
            EndSyncAllLock();
        }
    }

    public async Task<bool> TrySyncAllNowAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return false;

        var stamp = CaptureSession();
        using var sessionLink = LinkSession(ct);
        ct = sessionLink.Token;

        // 0 = do not queue behind an in-flight sync (manual Sync now).
        if (!await _syncAllGate.WaitAsync(0, ct).ConfigureAwait(false))
            return false;

        BeginSyncAllLock();
        try
        {
            if (!CanSyncCurrentLeague || IsSessionChanged(stamp))
                return false;
            await RunSyncAllCoreAsync(stamp, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            EndSyncAllLock();
        }
    }

    private void BeginSyncAllLock()
    {
        Interlocked.Exchange(ref _isSyncing, 1);
        try { SyncStateChanged?.Invoke(this, EventArgs.Empty); }
        catch { /* UI subscribers must not break sync */ }
    }

    private void EndSyncAllLock()
    {
        Interlocked.Exchange(ref _isSyncing, 0);
        try { _syncAllGate.Release(); }
        catch (ObjectDisposedException) { /* shutting down */ }
        try { SyncStateChanged?.Invoke(this, EventArgs.Empty); }
        catch { /* UI subscribers must not break sync */ }
    }

    private async Task RunSyncAllCoreAsync(SyncSessionStamp stamp, CancellationToken ct)
    {
        await _syncLog.InfoAsync("Start", "Sync started for home league.", ct: ct).ConfigureAwait(false);

        // Never hard-gate on Connectivity.NetworkAccess — attempt HTTP and treat
        // OfflineException as "try again later". Local data stays durable either way.

        // Scheduler and sports outboxes are both restricted to a League Admin's home league.
        // Grounds stay on direct live APIs — never flush ground booking outbox here.
        Exception? schedulerSyncFailure = null;
        try
        {
            using var syncScope = AdminSyncRequestScope.Enter();
            await _schedulerSync.SyncPendingAsync(ct).ConfigureAwait(false);
        }
        catch (SyncConflictException ex)
        {
            schedulerSyncFailure = ex;
            await _syncLog.WarnAsync("Scheduler", "Scheduler conflicts pending.", ex.Message, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex) || ex is OfflineException)
        {
            // Soft: do not abort home-league sports sync just because scheduler flush is offline.
            schedulerSyncFailure = ex;
            await _syncLog.WarnAsync("Scheduler", "Scheduler flush offline — continuing sports sync.",
                ex.Message, ct).ConfigureAwait(false);
        }
        try
        {
            using var syncScope = AdminSyncRequestScope.Enter();
            await _schedulerSync.RefreshCacheAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex) || ex is OfflineException)
        {
            schedulerSyncFailure ??= ex;
        }

        ThrowIfSessionChanged(stamp, ct);
        await EnsureLocalSyncSchemaAsync(ct).ConfigureAwait(false);
        // Uppercase Guid TEXT breaks EF Find → duplicate PK inserts on pull (Auctions.Id, …).
        await using (var normalizeDb = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            await DbBootstrap.NormalizeGuidTextColumnsForSyncAsync(normalizeDb, ct).ConfigureAwait(false);
        await RecoverStuckInFlightAsync(ct).ConfigureAwait(false);

        // Live scoring is time-sensitive: drain its ordered ball-by-ball queue before
        // catalog or bulk statistics work can consume this sync run.
        if (_remote.IsEnabled && _league.CurrentLeagueId is Guid liveLeagueId && liveLeagueId != Guid.Empty)
            await _live.FlushPendingAsync(ct).ConfigureAwait(false);

        if (!_remote.IsEnabled || _league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
        {
            if (schedulerSyncFailure is SyncConflictException)
                throw schedulerSyncFailure;
            await _syncLog.WarnAsync("Done", "Remote sync skipped (API disabled or no league).", ct: ct)
                .ConfigureAwait(false);
            return;
        }

        await _pullGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Ball-by-ball work was drained before lower-priority API queues above.
            ThrowIfSessionChanged(stamp, ct);
            var localBeforePush = await GetPendingOutboxCountAsync(leagueId, ct).ConfigureAwait(false);
            await _syncLog.InfoAsync("Push", "Flushing local outbox…",
                $"{localBeforePush} item(s) queued on this device before push.", ct).ConfigureAwait(false);
            await FlushMasterDataAsync(ct).ConfigureAwait(false);
            await FlushAsync(ct).ConfigureAwait(false);
            await FlushCompletedEventGroupsAsync(ct).ConfigureAwait(false);
            await ReplayDeferredPullsAsync(ct).ConfigureAwait(false);
            var localAfterPush = await GetPendingOutboxCountAsync(leagueId, ct).ConfigureAwait(false);
            var clearedLocally = Math.Max(0, localBeforePush - localAfterPush);
            await _syncLog.SuccessAsync("Push",
                $"Local queue after push · {localAfterPush} item(s) remain (started with {localBeforePush}).",
                $"{clearedLocally} item(s) left the waiting queue during this pass.",
                ct: ct).ConfigureAwait(false);
            ThrowIfSessionChanged(stamp, ct);
            await _syncLog.InfoAsync("Pull", "Pulling server changes…", ct: ct).ConfigureAwait(false);
            await PullServerChangesAsync(leagueId, ct).ConfigureAwait(false);
            ThrowIfSessionChanged(stamp, ct);
            await ReplayDeferredPullsAsync(ct).ConfigureAwait(false);
            await PruneCompletedOutboxAsync(ct).ConfigureAwait(false);
            try
            {
                using var syncScope = AdminSyncRequestScope.Enter();
                await _statsCacheWarmer.WarmAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Stats cache warm is best-effort (offline, stale cache row, …); never fail a
                // successful data sync because a cached leaderboard could not be refreshed.
                await _syncLog.WarnAsync("Stats", "Offline stats cache not refreshed.", ex.Message, ct)
                    .ConfigureAwait(false);
            }
            ThrowIfSessionChanged(stamp, ct);
            Preferences.Default.Set(
                LastSyncUtcKey(leagueId),
                DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            await ThrowIfConflictsPendingAsync(ct).ConfigureAwait(false);
            await _syncLog.SuccessAsync("Done", "Sync completed.", ct: ct).ConfigureAwait(false);
            // Sports sync succeeded — do not fail the whole run on scheduler soft-offline.
            if (schedulerSyncFailure is SyncConflictException)
                throw schedulerSyncFailure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || IsSessionChanged(stamp))
        {
            // Logout / admin switch: stop quietly — the next admin's sync starts fresh.
            throw;
        }
        catch (Exception ex) when (ex is not SyncConflictException)
        {
            await _syncLog.ErrorAsync("Fail", NetworkUi.UserMessage(ex), ex.ToString(), ct)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            _pullGate.Release();
        }
    }

    private async Task<int> GetPendingOutboxCountAsync(Guid leagueId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var count = await db.SyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status != SyncOutboxStatus.Completed, ct)
            .ConfigureAwait(false);
        count += await db.LiveOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status != SyncOutboxStatus.Completed, ct)
            .ConfigureAwait(false);
        count += await db.SchedulerSyncOutbox.AsNoTracking()
            .CountAsync(row => row.LeagueId == leagueId && row.Status != SyncOutboxStatus.Completed, ct)
            .ConfigureAwait(false);
        return count;
    }

    public async Task SyncDailyIfDueAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return;

        using var sessionLink = LinkSession(ct);
        ct = sessionLink.Token;

        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return;

        await _dailyGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = LastSyncUtcKey(leagueId);
            var lastRaw = Preferences.Default.Get(key, string.Empty);
            if (DateTime.TryParse(lastRaw, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var lastUtc)
                && DateTime.UtcNow - lastUtc.ToUniversalTime() < TimeSpan.FromHours(24))
                return;

            await SyncAllAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _dailyGate.Release();
        }
    }

    private static string LastSyncUtcKey(Guid leagueId) => $"jpl.sync.lastUtc.{leagueId:D}";

    /// <summary>Repair SyncOutbox columns before any EF SELECT that maps BasePayloadJson.</summary>
    private async Task EnsureLocalSyncSchemaAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Process kill / 502 mid-batch can leave rows InFlight forever. Requeue them before every sync.
    /// Uses raw SQL so older DBs missing BasePayloadJson never blow up mid-repair.
    /// </summary>
    private async Task RecoverStuckInFlightAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        // Status: Pending=0, InFlight=1 — no full-row materialization.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "SyncOutbox"
            SET "Status" = 0,
                "LastError" = CASE
                    WHEN "LastError" IS NULL THEN NULL
                    WHEN instr("LastError", '502') > 0 THEN NULL
                    WHEN instr("LastError", '503') > 0 THEN NULL
                    WHEN instr("LastError", '504') > 0 THEN NULL
                    ELSE "LastError"
                END
            WHERE "Status" = 1;
            """, ct).ConfigureAwait(false);
    }

    private async Task ThrowIfConflictsPendingAsync(CancellationToken ct)
    {
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var conflict = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.LeagueId == leagueId
                && row.Status == SyncOutboxStatus.Failed && row.LastError != null
                && row.LastError.StartsWith("SYNC_CONFLICT:"))
            .OrderBy(row => row.CreatedAtUtc)
            .Select(row => row.LastError)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (conflict is not null)
            throw new SyncConflictException(conflict);
    }

    public async Task<IReadOnlyList<SyncConflictInfo>> GetConflictsAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague || _league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return Array.Empty<SyncConflictInfo>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var rows = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.LeagueId == leagueId
                && row.Status == SyncOutboxStatus.Failed && row.LastError != null
                && row.LastError.StartsWith("SYNC_CONFLICT:"))
            .OrderBy(row => row.CreatedAtUtc)
            .Select(row => new SyncConflictInfo(
                row.Id, row.EntityType, row.EntityId, row.Operation, row.LastError!,
                row.PayloadJson, row.BasePayloadJson, row.ServerPayloadJson,
                row.AttemptCount, row.CreatedAtUtc))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<SyncOutboxInfo>> GetOutboxItemsAsync(
        int take = 100, CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague || _league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return Array.Empty<SyncOutboxInfo>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var items = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.LeagueId == leagueId && row.Status != SyncOutboxStatus.Completed)
            .Select(row => new SyncOutboxInfo(
                row.Id, row.EntityType, row.EntityId, row.Operation, row.Status,
                row.PayloadJson, row.LastError, row.AttemptCount, row.CreatedAtUtc))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var liveItems = await db.LiveOutbox.AsNoTracking()
            .Where(row => row.LeagueId == leagueId && row.Status != SyncOutboxStatus.Completed)
            .Select(row => new SyncOutboxInfo(
                row.Id, "Live " + row.EntityType, row.EntityId, row.Operation, row.Status,
                row.PayloadJson, row.LastError, row.AttemptCount, row.CreatedAtUtc))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var schedulerItems = await db.SchedulerSyncOutbox.AsNoTracking()
            .Where(row => row.LeagueId == leagueId && row.Status != SyncOutboxStatus.Completed)
            .Select(row => new SyncOutboxInfo(
                row.Id, "Scheduler tournament", row.TournamentId, row.Operation, row.Status,
                row.PayloadJson, row.LastError, row.AttemptCount, row.CreatedAtUtc))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        items.AddRange(liveItems);
        items.AddRange(schedulerItems);
        return items
            .OrderBy(row => row.Status == SyncOutboxStatus.Failed ? 0 : 1)
            .ThenBy(row => row.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 250))
            .ToList();
    }

    public async Task ResolveConflictAcceptServerAsync(Guid outboxId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var row = await db.SyncOutbox.FirstOrDefaultAsync(x => x.Id == outboxId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("Conflict item was not found.");
        if (row.Status != SyncOutboxStatus.Failed
            || row.LastError is null
            || !row.LastError.StartsWith("SYNC_CONFLICT:", StringComparison.Ordinal))
            throw new InvalidOperationException("This outbox row is not a sync conflict.");

        var serverPayload = await ResolveServerPayloadAsync(db, row, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverPayload))
            throw new InvalidOperationException(
                "Server copy is not available yet. Stay online, sync again, then resolve the conflict.");

        if (string.Equals(row.EntityType, nameof(Team), StringComparison.Ordinal))
            await ApplyServerTeamPayloadAsync(db, serverPayload, ct).ConfigureAwait(false);
        else
            await ApplyRemoteRowAsync(db, row.EntityType, ResolveEntityKeyJson(row), row.Operation == "Delete" ? "Upsert" : "Upsert",
                serverPayload, ct).ConfigureAwait(false);

        var sameKey = await db.SyncOutbox
            .Where(x => x.LeagueId == row.LeagueId
                        && x.EntityType == row.EntityType && x.EntityId == row.EntityId
                        && x.Status != SyncOutboxStatus.Completed)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        db.SyncOutbox.RemoveRange(sameKey);

        var deferred = await db.SyncDeferredPulls
            .Where(x => x.LeagueId == row.LeagueId && x.EntityType == row.EntityType)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var key = CanonicalSyncKey(ResolveEntityKeyJson(row));
        db.SyncDeferredPulls.RemoveRange(deferred.Where(x => CanonicalSyncKey(x.EntityKeyJson) == key));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ResolveConflictKeepLocalAsync(Guid outboxId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        var row = await db.SyncOutbox.FirstOrDefaultAsync(x => x.Id == outboxId, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("Conflict item was not found.");
        if (row.Status != SyncOutboxStatus.Failed
            || row.LastError is null
            || !row.LastError.StartsWith("SYNC_CONFLICT:", StringComparison.Ordinal))
            throw new InvalidOperationException("This outbox row is not a sync conflict.");

        var serverPayload = await ResolveServerPayloadAsync(db, row, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(serverPayload))
            row.BasePayloadJson = serverPayload;

        row.PayloadJson = BumpVersionInPayload(row.PayloadJson);
        row.IdempotencyKey = Guid.NewGuid();
        row.Status = SyncOutboxStatus.Pending;
        row.AttemptCount = 0;
        row.LastError = null;
        row.LastAttemptAtUtc = null;

        await BumpLocalEntityVersionAsync(db, row, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string ResolveEntityKeyJson(SyncOutboxItem row)
        => string.IsNullOrWhiteSpace(row.EntityKeyJson) || row.EntityKeyJson == "{}"
            ? JsonSerializer.Serialize(new { id = row.EntityId }, SyncJsonOptions)
            : row.EntityKeyJson;

    private static async Task<string?> ResolveServerPayloadAsync(
        JplDbContext db, SyncOutboxItem row, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(row.ServerPayloadJson))
            return row.ServerPayloadJson;

        var key = CanonicalSyncKey(ResolveEntityKeyJson(row));
        var deferred = await db.SyncDeferredPulls.AsNoTracking()
            .Where(x => x.EntityType == row.EntityType)
            .OrderByDescending(x => x.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return deferred.FirstOrDefault(x => CanonicalSyncKey(x.EntityKeyJson) == key)?.PayloadJson;
    }

    private static string BumpVersionInPayload(string payloadJson)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(payloadJson)?.AsObject();
            if (node is null)
                return payloadJson;

            long version = 1;
            if (node.TryGetPropertyValue("version", out var versionNode)
                && versionNode is not null
                && long.TryParse(versionNode.ToString(), out var current))
                version = current + 1;
            else
                version = 2;

            node["version"] = version;
            return node.ToJsonString(SyncJsonOptions);
        }
        catch (JsonException)
        {
            return payloadJson;
        }
    }

    private static async Task BumpLocalEntityVersionAsync(JplDbContext db, SyncOutboxItem row, CancellationToken ct)
    {
        if (string.Equals(row.EntityType, nameof(Player), StringComparison.Ordinal))
        {
            var player = await db.Players.FirstOrDefaultAsync(x => x.Id == row.EntityId, ct).ConfigureAwait(false);
            if (player is not null)
            {
                player.Version = Math.Max(player.Version + 1, ReadVersion(row.PayloadJson));
                player.UpdatedAtUtc = DateTime.UtcNow;
            }
            return;
        }

        if (string.Equals(row.EntityType, nameof(Team), StringComparison.Ordinal))
        {
            var team = await db.Teams.FirstOrDefaultAsync(x => x.Id == row.EntityId, ct).ConfigureAwait(false);
            if (team is not null)
            {
                team.Version = Math.Max(team.Version + 1, ReadVersion(row.PayloadJson));
                team.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
    }

    private static long ReadVersion(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("version", out var version)
                && version.TryGetInt64(out var value))
                return value;
        }
        catch (JsonException)
        {
            /* ignore */
        }
        return 1;
    }

    private static async Task ApplyServerTeamPayloadAsync(JplDbContext db, string payloadJson, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        if (!TryReadGuid(root, "id", out var teamId))
            throw new InvalidOperationException("Server team payload is missing id.");

        var team = await db.Teams.Include(t => t.TeamPlayers)
            .FirstOrDefaultAsync(t => t.Id == teamId, ct)
            .ConfigureAwait(false);
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
        Guid? captainId = root.TryGetProperty("captainPlayerId", out var captainEl)
                          && captainEl.ValueKind != JsonValueKind.Null
            ? captainEl.GetGuid()
            : null;
        var isActive = !root.TryGetProperty("isActive", out var activeEl)
                       || activeEl.ValueKind == JsonValueKind.Null
                       || activeEl.GetBoolean();
        var isDeleted = root.TryGetProperty("isDeleted", out var deletedEl) && deletedEl.GetBoolean();
        var version = root.TryGetProperty("version", out var versionEl) && versionEl.TryGetInt64(out var v) ? v : 1L;
        var updated = root.TryGetProperty("updatedAtUtc", out var updatedEl)
                      && updatedEl.TryGetDateTime(out var updatedAt)
            ? updatedAt
            : DateTime.UtcNow;

        var playerIds = new List<Guid>();
        if (root.TryGetProperty("playerIds", out var playersEl) && playersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in playersEl.EnumerateArray())
                playerIds.Add(item.GetGuid());
        }

        var playingIds = new HashSet<Guid>();
        if (root.TryGetProperty("playingPlayerIds", out var playingEl) && playingEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in playingEl.EnumerateArray())
                playingIds.Add(item.GetGuid());
        }
        else
        {
            playingIds = playerIds.ToHashSet();
        }

        if (team is null)
        {
            team = new Team
            {
                Id = teamId,
                Name = name,
                CaptainPlayerId = captainId,
                IsActive = isActive,
                IsDeleted = isDeleted,
                Version = version,
                UpdatedAtUtc = updated,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Teams.Add(team);
        }
        else
        {
            team.Name = name;
            team.CaptainPlayerId = captainId;
            team.IsActive = isActive;
            team.IsDeleted = isDeleted;
            team.Version = version;
            team.UpdatedAtUtc = updated;
        }

        var desired = playerIds.ToHashSet();
        foreach (var remove in team.TeamPlayers.Where(tp => !desired.Contains(tp.PlayerId)).ToList())
            db.TeamPlayers.Remove(remove);

        foreach (var link in team.TeamPlayers)
            link.IsPlaying = playingIds.Contains(link.PlayerId);

        var have = team.TeamPlayers.Select(tp => tp.PlayerId).ToHashSet();
        foreach (var add in desired.Where(id => !have.Contains(id)))
        {
            db.TeamPlayers.Add(new TeamPlayer
            {
                TeamId = teamId,
                PlayerId = add,
                IsPlaying = playingIds.Contains(add)
            });
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task PullServerChangesAsync(Guid leagueId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DbBootstrap.DeduplicateSyncCursorsAsync(db, ct).ConfigureAwait(false);
        var cursor = await GetOrCreateSyncCursorAsync(db, leagueId, ct).ConfigureAwait(false);

        var bootstrappedNow = false;
        var snapshotRows = 0;
        var appliedChanges = 0;

        if (!cursor.IsBootstrapped)
        {
            await _syncLog.InfoAsync("Bootstrap", "First sync — downloading league snapshot…", ct: ct)
                .ConfigureAwait(false);
            try
            {
                var watermarkPage = await _remote.GetChangesAsync(0, throughSequence: null, take: 1, ct)
                    .ConfigureAwait(false);
                var entityTypes = (await _remote.GetEntityTypesAsync(ct).ConfigureAwait(false))
                    .OrderBy(SyncDependencyOrder)
                    .ThenBy(x => x, StringComparer.Ordinal)
                    .ToList();
                foreach (var entityType in entityTypes)
                {
                    string? snapshotCursor = null;
                    while (true)
                    {
                        var page = await _remote.GetSnapshotAsync(entityType, snapshotCursor, take: 200, ct)
                            .ConfigureAwait(false);
                        snapshotRows += page.Rows.Count;
                        await ApplySnapshotPageAsync(db, page.Rows, ct).ConfigureAwait(false);
                        if (!page.HasMore)
                            break;
                        if (string.IsNullOrWhiteSpace(page.NextCursor)
                            || string.Equals(page.NextCursor, snapshotCursor, StringComparison.Ordinal))
                            throw new InvalidOperationException($"Snapshot cursor did not advance for {entityType}.");
                        snapshotCursor = page.NextCursor;
                    }
                }

                db.ChangeTracker.Clear();
                await PersistSyncCursorAsync(db, leagueId, watermarkPage.ThroughSequence,
                    isBootstrapped: true, ct).ConfigureAwait(false);
                bootstrappedNow = true;
                cursor = await GetOrCreateSyncCursorAsync(db, leagueId, ct).ConfigureAwait(false);
                await _syncLog.SuccessAsync("Bootstrap",
                    $"Snapshot ready · seq {cursor.LastSequence}.", ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
            {
                // Keep IsBootstrapped=false so the next online Sync retries a clean bootstrap.
                throw ex is OfflineException offline
                    ? offline
                    : new OfflineException(NetworkUi.OfflineMessage, ex);
            }
            // Json / format / InvalidData / HTTP 4xx-5xx: do NOT mask as Offline — UI must show the real cause.
        }

        long? through = null;
        var pages = 0;
        while (true)
        {
            var page = await _remote.GetChangesAsync(cursor.LastSequence, through, take: 200, ct)
                .ConfigureAwait(false);
            through ??= page.ThroughSequence;
            if (page.Changes.Count == 0)
            {
                db.ChangeTracker.Clear();
                await PersistSyncCursorAsync(db, leagueId, page.ThroughSequence,
                    isBootstrapped: true, ct).ConfigureAwait(false);
                break;
            }

            await ApplyChangePageAsync(db, page.Changes, ct).ConfigureAwait(false);
            appliedChanges += page.Changes.Count;
            db.ChangeTracker.Clear();
            await PersistSyncCursorAsync(db, leagueId, page.NextSequence,
                isBootstrapped: true, ct).ConfigureAwait(false);
            cursor = await GetOrCreateSyncCursorAsync(db, leagueId, ct).ConfigureAwait(false);
            pages++;
            if (!page.HasMore)
                break;
        }

        var pullMessage = bootstrappedNow
            ? $"Initial snapshot: {snapshotRows} record(s); then {appliedChanges} server change(s) · seq {cursor.LastSequence}."
            : appliedChanges == 0
                ? $"Up to date · no new server records · seq {cursor.LastSequence}."
                : $"Applied {appliedChanges} server change(s) in {pages} page(s) · seq {cursor.LastSequence}.";
        await _syncLog.InfoAsync("Pull", pullMessage, ct: ct).ConfigureAwait(false);
    }

    private static async Task<SyncCursor> GetOrCreateSyncCursorAsync(
        JplDbContext db, Guid leagueId, CancellationToken ct)
    {
        var cursor = await db.SyncCursors.FirstOrDefaultAsync(x => x.LeagueId == leagueId, ct)
            .ConfigureAwait(false);
        if (cursor is not null)
            return cursor;

        cursor = new SyncCursor { LeagueId = leagueId };
        db.SyncCursors.Add(cursor);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Race / casing remnant — reload after another dedupe.
            db.ChangeTracker.Clear();
            await DbBootstrap.DeduplicateSyncCursorsAsync(db, ct).ConfigureAwait(false);
            cursor = await db.SyncCursors.FirstOrDefaultAsync(x => x.LeagueId == leagueId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Sync cursor missing after repair.");
        }

        return cursor;
    }

    /// <summary>
    /// Write cursor via ExecuteUpdate so Guid TEXT casing never causes
    /// DbUpdateConcurrencyException (0 rows affected on tracked UPDATE).
    /// </summary>
    private static async Task PersistSyncCursorAsync(
        JplDbContext db, Guid leagueId, long lastSequence, bool isBootstrapped, CancellationToken ct)
    {
        await DbBootstrap.DeduplicateSyncCursorsAsync(db, ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var updated = await db.SyncCursors
            .Where(x => x.LeagueId == leagueId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LastSequence, lastSequence)
                .SetProperty(x => x.IsBootstrapped, isBootstrapped)
                .SetProperty(x => x.LastSuccessfulSyncUtc, now), ct)
            .ConfigureAwait(false);

        if (updated == 0)
        {
            db.SyncCursors.Add(new SyncCursor
            {
                LeagueId = leagueId,
                LastSequence = lastSequence,
                IsBootstrapped = isBootstrapped,
                LastSuccessfulSyncUtc = now
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ApplySnapshotPageAsync(
        JplDbContext db,
        IReadOnlyList<ApiSyncSnapshotRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        // Bulk snapshot pages can interleave optional FKs (Team.CaptainPlayerId) and Guid
        // casing before parents land — disable FK enforcement before opening the transaction.
        // SQLite ignores PRAGMA foreign_keys changes made while a transaction is active.
        await SetForeignKeysAsync(db, enabled: false, ct).ConfigureAwait(false);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var protectedKeys = await GetLocallyProtectedKeysAsync(db, ct).ConfigureAwait(false);
            var activeOwnedMatches = await db.Matches.AsNoTracking()
                .Where(match => match.StartedOnDeviceId == DeviceInstall.Id
                                && match.Status != MatchStatus.Completed)
                .Select(match => match.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var activeSet = activeOwnedMatches.ToHashSet();
            var activeNetSessions = await db.NetSessions.AsNoTracking()
                .Where(session => session.Status != NetSessionStatus.Completed)
                .Select(session => session.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var activeNetSet = activeNetSessions.ToHashSet();

            foreach (var row in rows)
            {
                var keyValues = ParseKeyValues(db, row.EntityType, row.EntityKey);
                if (protectedKeys.Contains((row.EntityType, CanonicalSyncKey(row.EntityKey))))
                    continue;
                if (ProtectOwnedLiveScoring(row.EntityType, row.PayloadJson, keyValues, activeSet))
                    continue;
                if (ProtectActiveNetScoring(row.EntityType, row.PayloadJson, keyValues, activeNetSet))
                    continue;
                await ApplyRemoteRowAsync(db, row.EntityType, row.EntityKey, row.Operation, row.PayloadJson, ct)
                    .ConfigureAwait(false);
            }

            await SavePullChangesAsync(db, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await SetForeignKeysAsync(db, enabled: true, ct).ConfigureAwait(false);
        }

        db.ChangeTracker.Clear();
    }

    private async Task ApplyChangePageAsync(
        JplDbContext db,
        IReadOnlyList<ApiSyncChange> changes,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var protectedKeys = await GetLocallyProtectedKeysAsync(db, ct).ConfigureAwait(false);
        var activeOwnedMatches = await db.Matches.AsNoTracking()
            .Where(match => match.StartedOnDeviceId == DeviceInstall.Id
                            && match.Status != MatchStatus.Completed)
            .Select(match => match.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var activeSet = activeOwnedMatches.ToHashSet();
        var activeNetSessions = await db.NetSessions.AsNoTracking()
            .Where(session => session.Status != NetSessionStatus.Completed)
            .Select(session => session.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var activeNetSet = activeNetSessions.ToHashSet();

        foreach (var change in changes
                     .OrderBy(x => SyncDependencyOrder(x.EntityType))
                     .ThenBy(x => x.Sequence))
        {
            var keyValues = ParseKeyValues(db, change.EntityType, change.EntityKey);
            var blocked = protectedKeys.Contains((change.EntityType, CanonicalSyncKey(change.EntityKey)))
                || ProtectOwnedLiveScoring(change.EntityType, change.PayloadJson, keyValues, activeSet)
                || ProtectActiveNetScoring(change.EntityType, change.PayloadJson, keyValues, activeNetSet);
            if (blocked
                || await MustDeferForMissingParentAsync(db, change.EntityType, change.PayloadJson, ct)
                    .ConfigureAwait(false))
            {
                await UpsertDeferredPullAsync(db, change, ct).ConfigureAwait(false);
                continue;
            }

            await ApplyRemoteRowAsync(db, change.EntityType, change.EntityKey, change.Operation, change.PayloadJson, ct)
                .ConfigureAwait(false);
            await RemoveDeferredPullAsync(db, change.Sequence, ct).ConfigureAwait(false);
        }

        await SavePullChangesAsync(db, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    private static async Task UpsertDeferredPullAsync(JplDbContext db, ApiSyncChange change, CancellationToken ct)
    {
        var existing = await db.SyncDeferredPulls
            .FirstOrDefaultAsync(row => row.Sequence == change.Sequence, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            db.SyncDeferredPulls.Add(new SyncDeferredPullItem
            {
                Sequence = change.Sequence,
                EntityType = change.EntityType,
                EntityKeyJson = change.EntityKey,
                Operation = change.Operation,
                PayloadJson = change.PayloadJson,
                DeferredAtUtc = DateTime.UtcNow
            });
            return;
        }

        existing.EntityType = change.EntityType;
        existing.EntityKeyJson = change.EntityKey;
        existing.Operation = change.Operation;
        existing.PayloadJson = change.PayloadJson;
        existing.DeferredAtUtc = DateTime.UtcNow;
    }

    private static async Task RemoveDeferredPullAsync(JplDbContext db, long sequence, CancellationToken ct)
    {
        var rows = await db.SyncDeferredPulls.Where(row => row.Sequence == sequence).ToListAsync(ct)
            .ConfigureAwait(false);
        if (rows.Count > 0)
            db.SyncDeferredPulls.RemoveRange(rows);
    }

    private async Task ReplayDeferredPullsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var protectedKeys = await GetLocallyProtectedKeysAsync(db, ct).ConfigureAwait(false);
        var activeOwnedMatches = await db.Matches.AsNoTracking()
            .Where(match => match.StartedOnDeviceId == DeviceInstall.Id
                            && match.Status != MatchStatus.Completed)
            .Select(match => match.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var activeSet = activeOwnedMatches.ToHashSet();
        var activeNetSessions = await db.NetSessions.AsNoTracking()
            .Where(session => session.Status != NetSessionStatus.Completed)
            .Select(session => session.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var activeNetSet = activeNetSessions.ToHashSet();

        var deferred = await db.SyncDeferredPulls
            .OrderBy(row => row.Sequence)
            .Take(400)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var change in deferred
                     .OrderBy(row => SyncDependencyOrder(row.EntityType))
                     .ThenBy(row => row.Sequence))
        {
            var keyValues = ParseKeyValues(db, change.EntityType, change.EntityKeyJson);
            if (protectedKeys.Contains((change.EntityType, CanonicalSyncKey(change.EntityKeyJson))))
                continue;
            if (ProtectOwnedLiveScoring(change.EntityType, change.PayloadJson, keyValues, activeSet))
                continue;
            if (ProtectActiveNetScoring(change.EntityType, change.PayloadJson, keyValues, activeNetSet))
                continue;
            if (await MustDeferForMissingParentAsync(db, change.EntityType, change.PayloadJson, ct)
                    .ConfigureAwait(false))
                continue;

            await ApplyRemoteRowAsync(db, change.EntityType, change.EntityKeyJson, change.Operation, change.PayloadJson, ct)
                .ConfigureAwait(false);
            db.SyncDeferredPulls.Remove(change);
        }

        await SavePullChangesAsync(db, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    private async Task PruneCompletedOutboxAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var completed = await db.SyncOutbox
            .Where(row => row.Status == SyncOutboxStatus.Completed)
            .OrderBy(row => row.CreatedAtUtc)
            .Take(500)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (completed.Count == 0)
            return;
        db.SyncOutbox.RemoveRange(completed);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task<HashSet<(string EntityType, string EntityKey)>> GetLocallyProtectedKeysAsync(
        JplDbContext db,
        CancellationToken ct)
    {
        var pending = await db.SyncOutbox.AsNoTracking()
            .Where(row => row.Status != SyncOutboxStatus.Completed)
            .Select(row => new { row.EntityType, row.EntityKeyJson, row.EntityId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return pending.Select(row => (row.EntityType, CanonicalSyncKey(
            row.EntityKeyJson == "{}" ? JsonSerializer.Serialize(new { id = row.EntityId }, SyncJsonOptions) : row.EntityKeyJson)))
            .ToHashSet();
    }

    private static string CanonicalSyncKey(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return json;
            return string.Join("|", document.RootElement.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property => $"{property.Name.ToLowerInvariant()}:{property.Value.GetRawText()}"));
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static bool ProtectOwnedLiveScoring(
        string entityType,
        string payloadJson,
        object?[] keyValues,
        HashSet<Guid> activeOwnedMatches)
    {
        if (entityType == nameof(Match) && keyValues.FirstOrDefault() is Guid matchId)
            return activeOwnedMatches.Contains(matchId);

        if (entityType is not (nameof(Innings) or nameof(Delivery)))
            return false;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (TryReadGuid(document.RootElement, "matchId", out var parentMatchId))
                return activeOwnedMatches.Contains(parentMatchId);
        }
        catch (JsonException)
        {
            /* Invalid payload is handled by the normal JSON validation below. */
        }
        return false;
    }

    private static bool ProtectActiveNetScoring(
        string entityType,
        string payloadJson,
        object?[] keyValues,
        HashSet<Guid> activeNetSessions)
    {
        if (entityType == nameof(NetSession) && keyValues.FirstOrDefault() is Guid sessionId)
            return activeNetSessions.Contains(sessionId);

        if (entityType != nameof(NetDelivery))
            return false;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return TryReadGuid(document.RootElement, "sessionId", out var parentSessionId)
                   && activeNetSessions.Contains(parentSessionId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task ApplyRemoteRowAsync(
        JplDbContext db,
        string entityTypeName,
        string keyJson,
        string operation,
        string payloadJson,
        CancellationToken ct)
    {
        var clrType = typeof(JplDbContext).Assembly.GetType($"JPL.Domain.{entityTypeName}", throwOnError: false);
        if (clrType is null || !typeof(ILeagueScoped).IsAssignableFrom(clrType))
            throw new InvalidOperationException($"Unsupported sync entity type '{entityTypeName}'.");

        var entityType = db.Model.FindEntityType(clrType)
                         ?? throw new InvalidOperationException($"Local sync entity is not mapped: {entityTypeName}.");
        var keyValues = ParseKeyValues(db, entityTypeName, keyJson);
        var entityKey = entityType.FindPrimaryKey()?.Properties
                        ?? throw new InvalidOperationException($"Local sync entity has no key: {entityTypeName}.");
        if (entityKey.Count != keyValues.Length)
            throw new InvalidOperationException($"Invalid key for sync entity '{entityTypeName}'.");

        var existing = await FindTypedEntityAsync(db, clrType, keyValues, ct).ConfigureAwait(false);
        var incoming = JsonSerializer.Deserialize(payloadJson, clrType, SyncJsonOptions)
                       ?? throw new InvalidOperationException($"Invalid {entityTypeName} sync payload.");
        NormalizeGuidProperties(incoming);
        await SanitizeOptionalForeignKeysAsync(db, incoming, ct).ConfigureAwait(false);

        // Required parents missing (e.g. TeamPlayer before Team) — skip; change-feed defers separately.
        if (!string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase)
            && await MustDeferForMissingParentAsync(db, entityTypeName, payloadJson, ct).ConfigureAwait(false))
            return;

        // PK miss but UNIQUE natural key hit (e.g. Deliveries.InningsId+Sequence with a
        // different Id from another device) — replace/merge so SaveChanges never UNIQUE-fails.
        if (existing is null)
            existing = await FindByNaturalUniqueAsync(db, incoming, ct).ConfigureAwait(false);

        if (existing is null)
        {
            if (string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase))
                return;
            if (incoming is ILeagueScoped scoped)
                scoped.LeagueId = db.CurrentLeagueId;
            db.Add(incoming);
            return;
        }

        if (!PrimaryKeysEqual(entityType, existing, incoming)
            && RequiresReplaceOnNaturalKeyClash(entityTypeName))
        {
            await ReplaceNaturalKeyClashAsync(db, existing, incoming, ct).ConfigureAwait(false);
            return;
        }

        CopySyncScalars(db, entityType, existing, incoming);
        // Pull may revive a row that still carries a legacy/sentinel LeagueId.
        if (existing is ILeagueScoped existingScoped)
            existingScoped.LeagueId = db.CurrentLeagueId;
        if (string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var isDeleted = entityType.FindProperty("IsDeleted");
            if (isDeleted?.ClrType == typeof(bool))
                db.Entry(existing).Property("IsDeleted").CurrentValue = true;
            else
                db.Remove(existing);
        }
    }

    /// <summary>
    /// Secondary UNIQUE indexes (not the PK) that still collide across devices/ids.
    /// Checks the change tracker first so two rows in the same sync page cannot both Add.
    /// </summary>
    private static async Task<object?> FindByNaturalUniqueAsync(
        JplDbContext db, object incoming, CancellationToken ct)
    {
        var tracked = FindTrackedNaturalUnique(db, incoming);
        if (tracked is not null)
            return tracked;

        switch (incoming)
        {
            case Delivery delivery:
                return await db.Deliveries.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.InningsId == delivery.InningsId && row.Sequence == delivery.Sequence, ct)
                    .ConfigureAwait(false);
            case Innings innings:
                return await db.Innings.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.MatchId == innings.MatchId && row.InningsNumber == innings.InningsNumber, ct)
                    .ConfigureAwait(false);
            case NetDelivery netDelivery:
                return await db.NetDeliveries.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.SessionId == netDelivery.SessionId && row.Sequence == netDelivery.Sequence, ct)
                    .ConfigureAwait(false);
            case AuctionLot lot:
                return await db.AuctionLots.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.AuctionId == lot.AuctionId && row.PlayerId == lot.PlayerId, ct)
                    .ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static object? FindTrackedNaturalUnique(JplDbContext db, object incoming)
    {
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Deleted)
                continue;
            if (entry.Entity.GetType() != incoming.GetType())
                continue;

            switch (incoming)
            {
                case Delivery delivery when entry.Entity is Delivery other
                    && other.InningsId == delivery.InningsId && other.Sequence == delivery.Sequence:
                    return other;
                case Innings innings when entry.Entity is Innings other
                    && other.MatchId == innings.MatchId && other.InningsNumber == innings.InningsNumber:
                    return other;
                case NetDelivery net when entry.Entity is NetDelivery other
                    && other.SessionId == net.SessionId && other.Sequence == net.Sequence:
                    return other;
                case AuctionLot lot when entry.Entity is AuctionLot other
                    && other.AuctionId == lot.AuctionId && other.PlayerId == lot.PlayerId:
                    return other;
            }
        }

        return null;
    }

    private static bool RequiresReplaceOnNaturalKeyClash(string entityTypeName)
        => entityTypeName is nameof(Delivery) or nameof(Innings) or nameof(NetDelivery) or nameof(AuctionLot);

    private static bool PrimaryKeysEqual(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType, object existing, object incoming)
    {
        var pk = entityType.FindPrimaryKey();
        if (pk is null)
            return true;
        foreach (var property in pk.Properties)
        {
            var left = entityType.ClrType.GetProperty(property.Name)?.GetValue(existing);
            var right = entityType.ClrType.GetProperty(property.Name)?.GetValue(incoming);
            if (left is Guid leftGuid && right is Guid rightGuid)
            {
                if (leftGuid != rightGuid)
                    return false;
            }
            else if (!Equals(left, right))
                return false;
        }

        return true;
    }

    private static async Task ReplaceNaturalKeyClashAsync(
        JplDbContext db, object existing, object incoming, CancellationToken ct)
    {
        if (existing is Innings oldInnings && incoming is Innings newInnings && oldInnings.Id != newInnings.Id)
        {
            // Re-point child balls before removing the local innings row.
            var children = await db.Deliveries.IgnoreQueryFilters()
                .Where(row => row.InningsId == oldInnings.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var child in children)
                child.InningsId = newInnings.Id;
        }

        if (incoming is ILeagueScoped leagueScoped)
            leagueScoped.LeagueId = db.CurrentLeagueId;

        var entityType = db.Model.FindEntityType(existing.GetType());
        var tracked = db.Entry(existing);
        if (tracked.State != EntityState.Detached)
            tracked.State = EntityState.Detached;
        if (entityType is not null)
            await DeleteStoreRowCaseInsensitiveAsync(db, entityType, existing, ct).ConfigureAwait(false);

        db.Add(incoming);
    }

    private static async Task SavePullChangesAsync(JplDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException ex) when (IsSqliteUniqueConstraint(ex))
            {
                // Last-resort merge: convert conflicting Added rows into updates on the
                // already-stored unique slot so one bad ball cannot kill the whole sync page.
                await MergeUniqueAddedConflictsAsync(db, ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // EF UPDATE/DELETE matched 0 SQLite rows — almost always Guid TEXT casing
                // (tracked Guid writes lowercase, legacy row still uppercase).
                await RecoverConcurrencyConflictsAsync(db, ct).ConfigureAwait(false);
            }
        }

        // Final salvage: drop remaining tracked mutations rather than fail the whole Sync.
        // Cursor is only advanced after a successful SavePull — caller must not treat this
        // as applied. Re-throw so Pull can surface the real error in Sync Logs.
        var pending = db.ChangeTracker.Entries()
            .Count(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        db.ChangeTracker.Clear();
        throw new InvalidOperationException(
            $"Pull save could not apply {pending} local change(s) after retries (unique/concurrency).");
    }

    private static bool IsSqliteUniqueConstraint(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task RecoverConcurrencyConflictsAsync(JplDbContext db, CancellationToken ct)
    {
        var entries = db.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted or EntityState.Added)
            .ToList();

        foreach (var entry in entries)
        {
            var state = entry.State;
            var entity = entry.Entity;
            var clrType = entity.GetType();
            var entityType = db.Model.FindEntityType(clrType);
            if (entityType is null)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            if (state == EntityState.Deleted)
            {
                entry.State = EntityState.Detached;
                await DeleteStoreRowCaseInsensitiveAsync(db, entityType, entity, ct).ConfigureAwait(false);
                continue;
            }

            // Modified/Added: wipe any casing-variant store row, then insert the server snapshot.
            entry.State = EntityState.Detached;
            await DeleteStoreRowCaseInsensitiveAsync(db, entityType, entity, ct).ConfigureAwait(false);
            if (entity is ILeagueScoped scoped)
                scoped.LeagueId = db.CurrentLeagueId;
            db.Add(entity);
        }
    }

    private static async Task DeleteStoreRowCaseInsensitiveAsync(
        JplDbContext db,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        object entity,
        CancellationToken ct)
    {
        var table = entityType.GetTableName();
        var pk = entityType.FindPrimaryKey();
        if (string.IsNullOrWhiteSpace(table) || pk is null || !SyncEntityTableNames.Contains(table))
            return;

        if (pk.Properties.Any(property => property.ClrType != typeof(Guid)))
            return;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        var clauses = new List<string>();
        for (var i = 0; i < pk.Properties.Count; i++)
        {
            var property = pk.Properties[i];
            var value = clrTypeProperty(entity, property.Name);
            if (value is not Guid guid)
                return;
            var paramName = "$k" + i;
            clauses.Add($"""lower("{property.Name}") = lower({paramName})""");
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = paramName;
            parameter.Value = guid.ToString("D");
            cmd.Parameters.Add(parameter);
        }

        cmd.CommandText = $"""DELETE FROM "{table}" WHERE {string.Join(" AND ", clauses)};""";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        static object? clrTypeProperty(object target, string name)
            => target.GetType().GetProperty(name)?.GetValue(target);
    }

    private static async Task MergeUniqueAddedConflictsAsync(JplDbContext db, CancellationToken ct)
    {
        var added = db.ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();
        foreach (var entry in added)
        {
            // Detach so natural-key lookup hits the already-committed SQLite row,
            // not this same Added instance in the tracker.
            var incoming = entry.Entity;
            entry.State = EntityState.Detached;

            var conflict = await FindByNaturalUniqueInStoreAsync(db, incoming, ct).ConfigureAwait(false);
            if (conflict is null)
            {
                db.Add(incoming);
                continue;
            }

            var entityType = db.Model.FindEntityType(incoming.GetType());
            if (entityType is null)
                continue;

            if (!PrimaryKeysEqual(entityType, conflict, incoming)
                && RequiresReplaceOnNaturalKeyClash(entityType.ClrType.Name))
            {
                await ReplaceNaturalKeyClashAsync(db, conflict, incoming, ct).ConfigureAwait(false);
                continue;
            }

            CopySyncScalars(db, entityType, conflict, incoming);
            if (conflict is ILeagueScoped scoped)
                scoped.LeagueId = db.CurrentLeagueId;
        }
    }

    private static async Task<object?> FindByNaturalUniqueInStoreAsync(
        JplDbContext db, object incoming, CancellationToken ct)
    {
        switch (incoming)
        {
            case Delivery delivery:
                return await db.Deliveries.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.InningsId == delivery.InningsId && row.Sequence == delivery.Sequence, ct)
                    .ConfigureAwait(false);
            case Innings innings:
                return await db.Innings.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.MatchId == innings.MatchId && row.InningsNumber == innings.InningsNumber, ct)
                    .ConfigureAwait(false);
            case NetDelivery netDelivery:
                return await db.NetDeliveries.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.SessionId == netDelivery.SessionId && row.Sequence == netDelivery.Sequence, ct)
                    .ConfigureAwait(false);
            case AuctionLot lot:
                return await db.AuctionLots.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        row => row.AuctionId == lot.AuctionId && row.PlayerId == lot.PlayerId, ct)
                    .ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static int SyncDependencyOrder(string entityType) => entityType switch
    {
        nameof(Player) => 0,
        nameof(Team) => 1,
        nameof(TeamPlayer) => 2,
        nameof(Format) => 3,
        nameof(Venue) => 4,
        nameof(Auction) => 5,
        nameof(AuctionCaptain) or nameof(AuctionLot) => 6,
        nameof(Tournament) => 7,
        nameof(TournamentTeam) => 8,
        nameof(TournamentFixture) => 9,
        nameof(Series) => 10,
        nameof(SeriesFixture) => 11,
        nameof(Match) => 12,
        nameof(Innings) => 13,
        nameof(Delivery) => 14,
        nameof(NetSession) => 15,
        nameof(NetDelivery) => 16,
        nameof(VenueBooking) => 17,
        nameof(VenueBookingParticipant) => 18,
        nameof(Expense) => 19,
        nameof(ExpenseContribution) => 20,
        _ => 100
    };

    private static async Task SetForeignKeysAsync(JplDbContext db, bool enabled, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                enabled ? "PRAGMA foreign_keys=ON;" : "PRAGMA foreign_keys=OFF;", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* non-SQLite / connection closed */
        }
    }

    private static void NormalizeGuidProperties(object entity)
    {
        foreach (var property in entity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite)
                continue;
            if (property.PropertyType == typeof(Guid) && property.GetValue(entity) is Guid guid && guid != Guid.Empty)
                property.SetValue(entity, Guid.Parse(guid.ToString("D")));
            else if (property.PropertyType == typeof(Guid?) && property.GetValue(entity) is Guid nullable && nullable != Guid.Empty)
                property.SetValue(entity, Guid.Parse(nullable.ToString("D")));
        }
    }

    private static async Task SanitizeOptionalForeignKeysAsync(JplDbContext db, object incoming, CancellationToken ct)
    {
        if (incoming is Team team && team.CaptainPlayerId is Guid captainId && captainId != Guid.Empty)
        {
            var exists = await db.Players.IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(player => player.Id == captainId, ct)
                .ConfigureAwait(false);
            if (!exists)
                team.CaptainPlayerId = null;
        }
    }

    private static async Task<bool> MustDeferForMissingParentAsync(
        JplDbContext db, string entityType, string payloadJson, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return entityType switch
            {
                nameof(TeamPlayer) =>
                    !await GuidExistsAsync(db.Teams.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "teamId"), ct)
                        .ConfigureAwait(false)
                    || !await GuidExistsAsync(db.Players.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "playerId"), ct)
                        .ConfigureAwait(false),
                nameof(Innings) =>
                    !await GuidExistsAsync(db.Matches.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "matchId"), ct)
                        .ConfigureAwait(false),
                nameof(Delivery) =>
                    !await GuidExistsAsync(db.Innings.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "inningsId"), ct)
                        .ConfigureAwait(false),
                nameof(NetDelivery) =>
                    !await GuidExistsAsync(db.NetSessions.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "sessionId"), ct)
                        .ConfigureAwait(false),
                nameof(AuctionCaptain) or nameof(AuctionLot) =>
                    !await GuidExistsAsync(db.Auctions.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "auctionId"), ct)
                        .ConfigureAwait(false),
                nameof(TournamentTeam) or nameof(TournamentFixture) =>
                    !await GuidExistsAsync(db.Tournaments.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "tournamentId"), ct)
                        .ConfigureAwait(false),
                nameof(SeriesFixture) =>
                    !await GuidExistsAsync(db.Series.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "seriesId"), ct)
                        .ConfigureAwait(false),
                nameof(VenueBookingParticipant) =>
                    !await GuidExistsAsync(db.VenueBookings.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "venueBookingId"), ct)
                        .ConfigureAwait(false),
                nameof(ExpenseContribution) =>
                    !await GuidExistsAsync(db.Expenses.IgnoreQueryFilters(), ReadGuidOrEmpty(root, "expenseId"), ct)
                        .ConfigureAwait(false),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Guid ReadGuidOrEmpty(JsonElement root, string name)
        => TryReadGuid(root, name, out var id) ? id : Guid.Empty;

    private static async Task<bool> GuidExistsAsync<TEntity>(
        IQueryable<TEntity> source, Guid id, CancellationToken ct)
        where TEntity : class
    {
        if (id == Guid.Empty)
            return false;
        // Prefer Find-style equality on Id via EF property; entities all use Guid Id.
        return await source.AnyAsync(entity => EF.Property<Guid>(entity, "Id") == id, ct)
            .ConfigureAwait(false);
    }

    private static readonly MethodInfo FindTypedEntityMethod = typeof(SyncService).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name == nameof(FindTypedEntityAsync)
                          && method.IsGenericMethodDefinition
                          && method.GetParameters().Length == 3);

    private static async Task<object?> FindTypedEntityAsync(
        JplDbContext db,
        Type clrType,
        object?[] keyValues,
        CancellationToken ct)
    {
        var method = FindTypedEntityMethod.MakeGenericMethod(clrType);
        var task = (Task<object?>)method.Invoke(null, new object[] { db, keyValues, ct })!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<object?> FindTypedEntityAsync<TEntity>(
        JplDbContext db,
        object?[] keyValues,
        CancellationToken ct)
        where TEntity : class
    {
        // Same key twice in one batch must update the tracked row, not insert again.
        var tracked = FindTrackedEntity<TEntity>(db, keyValues);
        if (tracked is not null)
            return tracked;

        // All sync entities (incl. composite PKs like TournamentTeams / TeamPlayers /
        // AuctionCaptains): IgnoreQueryFilters so legacy LeagueId rows upsert in place
        // instead of UNIQUE-failing on insert.
        var found = await FindByPrimaryKeyIgnoreFiltersAsync<TEntity>(db, keyValues, ct)
            .ConfigureAwait(false);
        if (found is not null)
            return found;

        if (keyValues.Length == 1 && keyValues[0] is Guid id && id != Guid.Empty)
            return await FindByGuidKeyCaseInsensitiveAsync<TEntity>(db, id.ToString("D"), ct)
                .ConfigureAwait(false);

        if (keyValues.Length >= 2 && keyValues.All(value => value is Guid))
            return await FindByCompositeGuidKeyCaseInsensitiveAsync<TEntity>(db, keyValues, ct)
                .ConfigureAwait(false);

        return null;
    }

    private static async Task<TEntity?> FindByPrimaryKeyIgnoreFiltersAsync<TEntity>(
        JplDbContext db, object?[] keyValues, CancellationToken ct)
        where TEntity : class
    {
        var pk = db.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey();
        if (pk is null || pk.Properties.Count != keyValues.Length)
            return null;

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? body = null;
        for (var i = 0; i < pk.Properties.Count; i++)
        {
            var property = pk.Properties[i];
            var propertyAccess = Expression.Property(parameter, property.Name);
            var constant = Expression.Constant(keyValues[i], property.ClrType);
            var equal = Expression.Equal(propertyAccess, constant);
            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        var predicate = Expression.Lambda<Func<TEntity, bool>>(body!, parameter);
        return await db.Set<TEntity>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(predicate, ct)
            .ConfigureAwait(false);
    }

    private static async Task<TEntity?> FindByCompositeGuidKeyCaseInsensitiveAsync<TEntity>(
        JplDbContext db, object?[] keyValues, CancellationToken ct)
        where TEntity : class
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity));
        var pk = entityType?.FindPrimaryKey();
        var table = entityType?.GetTableName();
        if (pk is null || string.IsNullOrWhiteSpace(table) || !SyncEntityTableNames.Contains(table))
            return null;
        if (pk.Properties.Count != keyValues.Length
            || pk.Properties.Any(property => property.ClrType != typeof(Guid)))
            return null;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        var clauses = new List<string>();
        for (var i = 0; i < pk.Properties.Count; i++)
        {
            var column = pk.Properties[i].Name;
            var paramName = "$k" + i;
            clauses.Add($"""lower("{column}") = lower({paramName})""");
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = paramName;
            parameter.Value = ((Guid)keyValues[i]!).ToString("D");
            cmd.Parameters.Add(parameter);
        }

        var selectCols = string.Join(", ", pk.Properties.Select(property => $@"""{property.Name}"""));
        cmd.CommandText =
            $"""SELECT {selectCols} FROM "{table}" WHERE {string.Join(" AND ", clauses)} LIMIT 1;""";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var storedKeys = new object[pk.Properties.Count];
        for (var i = 0; i < pk.Properties.Count; i++)
        {
            var raw = reader.GetValue(i)?.ToString();
            if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var guid))
                return null;
            storedKeys[i] = guid;
        }

        return await FindByPrimaryKeyIgnoreFiltersAsync<TEntity>(db, storedKeys, ct)
            .ConfigureAwait(false);
    }

    private static TEntity? FindTrackedEntity<TEntity>(JplDbContext db, object?[] keyValues)
        where TEntity : class
    {
        foreach (var entry in db.ChangeTracker.Entries<TEntity>())
        {
            if (entry.State == EntityState.Detached)
                continue;
            var pk = entry.Metadata.FindPrimaryKey();
            if (pk is null || pk.Properties.Count != keyValues.Length)
                continue;
            var match = true;
            for (var i = 0; i < keyValues.Length; i++)
            {
                var current = entry.Property(pk.Properties[i].Name).CurrentValue;
                if (current is Guid currentGuid && keyValues[i] is Guid keyGuid)
                {
                    if (currentGuid != keyGuid)
                    {
                        match = false;
                        break;
                    }
                }
                else if (!Equals(current, keyValues[i]))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return entry.Entity;
        }

        return null;
    }

    private static async Task<TEntity?> FindByGuidKeyCaseInsensitiveAsync<TEntity>(
        JplDbContext db, string idText, CancellationToken ct)
        where TEntity : class
    {
        var table = db.Model.FindEntityType(typeof(TEntity))?.GetTableName();
        if (string.IsNullOrWhiteSpace(table) || !SyncEntityTableNames.Contains(table))
            return null;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""SELECT "Id" FROM "{table}" WHERE lower("Id") = lower($id) LIMIT 1;""";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = idText;
        cmd.Parameters.Add(parameter);
        var found = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(found) || !Guid.TryParse(found, out var existingId))
            return null;

        return await db.Set<TEntity>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(entity => EF.Property<Guid>(entity, "Id") == existingId, ct)
            .ConfigureAwait(false);
    }

    private static readonly HashSet<string> SyncEntityTableNames = new(StringComparer.Ordinal)
    {
        "Players", "Teams", "TeamPlayers", "Formats", "Venues",
        "Auctions", "AuctionCaptains", "AuctionLots",
        "Tournaments", "TournamentTeams", "TournamentFixtures",
        "Series", "SeriesFixtures",
        "Matches", "Innings", "Deliveries",
        "NetSessions", "NetDeliveries",
        "VenueBookings", "VenueBookingParticipants",
        "Expenses", "ExpenseContributions"
    };

    private static void CopySyncScalars(
        JplDbContext db,
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        object existing,
        object incoming)
    {
        var incomingType = incoming.GetType();
        var entry = db.Entry(existing);
        foreach (var property in entityType.GetProperties())
        {
            if (property.IsShadowProperty()
                || property.Name == nameof(ILeagueScoped.LeagueId)
                || string.Equals(property.Name, "ImagePath", StringComparison.Ordinal))
                continue;
            var source = incomingType.GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public);
            if (source is null || !source.CanRead)
                continue;
            entry.Property(property.Name).CurrentValue = source.GetValue(incoming);
        }
    }

    private static object?[] ParseKeyValues(JplDbContext db, string entityTypeName, string keyJson)
    {
        using var document = JsonDocument.Parse(keyJson);
        var entityType = db.Model.FindEntityType($"JPL.Domain.{entityTypeName}")
                         ?? throw new InvalidOperationException($"Local sync entity is not mapped: {entityTypeName}.");
        var keys = entityType.FindPrimaryKey()?.Properties
                   ?? throw new InvalidOperationException($"Local sync entity has no key: {entityTypeName}.");
        return keys.Select(key =>
        {
            if (!TryGetProperty(document.RootElement, JsonNamingPolicy.CamelCase.ConvertName(key.Name), out var value))
                throw new InvalidOperationException($"Sync key is missing '{key.Name}'.");
            return ConvertJsonValue(value, key.ClrType);
        }).ToArray();
    }

    private static object? ConvertJsonValue(JsonElement value, Type targetType)
    {
        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (actualType == typeof(Guid))
        {
            if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid))
                return guid;
            if (value.TryGetGuid(out guid))
                return guid;
            throw new InvalidOperationException($"Sync key value is not a Guid: {value}");
        }
        if (actualType.IsEnum)
        {
            if (value.ValueKind == JsonValueKind.String
                && Enum.TryParse(actualType, value.GetString(), ignoreCase: true, out var parsed))
                return parsed;
            if (value.TryGetInt32(out var number))
                return Enum.ToObject(actualType, number);
            throw new InvalidOperationException($"Sync key value is not a valid {actualType.Name}: {value}");
        }
        if (actualType == typeof(DateTime))
        {
            if (value.ValueKind == JsonValueKind.String
                && DateTime.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt;
            if (value.TryGetDateTime(out dt))
                return dt;
            throw new InvalidOperationException($"Sync value is not a DateTime: {value}");
        }
        return JsonSerializer.Deserialize(value.GetRawText(), actualType, SyncJsonOptions);
    }

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid id)
    {
        id = Guid.Empty;
        return TryGetProperty(element, propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
               && Guid.TryParse(value.GetString(), out id);
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static readonly JsonSerializerOptions SyncJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void RequestSyncNow()
    {
        if (!CanSyncCurrentLeague)
            return;
        if (Interlocked.Exchange(ref _syncRunRequested, 1) == 1)
            return;

        // Bound to the admin who requested it: logout cancels, and a new admin never inherits it.
        var sessionToken = _lifetime.Token;
        _ = Task.Run(async () =>
        {
            do
            {
                Interlocked.Exchange(ref _syncRunRequested, 0);
                if (sessionToken.IsCancellationRequested)
                    break;
                try { await SyncAllAsync(sessionToken).ConfigureAwait(false); }
                catch { /* never crash UI */ }
            }
            while (!sessionToken.IsCancellationRequested
                   && Interlocked.CompareExchange(ref _syncRunRequested, 0, 0) == 1);
        });
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            RequestSyncNow();
    }

    public Task FlushAsync(CancellationToken ct = default)
        => CanSyncCurrentLeague
            ? FlushInternalAsync(allowedMatchIds: null, standaloneOnly: true, ct)
            : Task.CompletedTask;

    private async Task FlushMasterDataAsync(CancellationToken ct)
    {
        if (!_remote.IsEnabled)
            return;

        using var sessionLink = LinkSession(ct);
        ct = sessionLink.Token;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (true)
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
                var unresolved = await db.SyncOutbox.AsNoTracking()
                    .Where(row => row.Status != SyncOutboxStatus.Completed
                        && row.EntityType != nameof(Match)
                        && row.EntityType != nameof(Innings)
                        && row.EntityType != nameof(Delivery)
                        && row.EntityType != nameof(NetSession)
                        && row.EntityType != nameof(NetDelivery))
                    .OrderBy(row => row.CreatedAtUtc)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (unresolved.Count == 0)
                    break;

                // Terminal conflicts must not block unrelated entity types (1 server, many clients).
                var retryable = unresolved
                    .Where(row => row.Status == SyncOutboxStatus.Pending
                        || row.Status == SyncOutboxStatus.InFlight
                        || (row.Status == SyncOutboxStatus.Failed && row.AttemptCount < 8))
                    .ToList();
                if (retryable.Count == 0)
                    break;

                var firstOrder = retryable.Min(row => MasterEntitySyncOrder(row.EntityType, row.Operation));
                var eligibleIds = retryable
                    .Where(row => MasterEntitySyncOrder(row.EntityType, row.Operation) == firstOrder)
                    .OrderBy(row => row.CreatedAtUtc)
                    .Take(80)
                    .Select(row => row.Id)
                    .ToList();
                if (eligibleIds.Count == 0)
                    break;

                var pending = await db.SyncOutbox.Where(row => eligibleIds.Contains(row.Id))
                    .OrderBy(row => row.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);

                await using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
                {
                    var now = DateTime.UtcNow;
                    foreach (var row in pending)
                    {
                        row.Status = SyncOutboxStatus.InFlight;
                        row.LastAttemptAtUtc = now;
                        row.AttemptCount++;
                    }
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                }

                var batchFailed = false;
                foreach (var row in pending)
                {
                    try
                    {
                        await _remote.PushAsync(row, ct).ConfigureAwait(false);
                        row.Status = SyncOutboxStatus.Completed;
                        row.LastError = null;
                        row.ServerPayloadJson = null;
                    }
                    catch (Exception ex)
                    {
                        var conflict = ex is SyncConflictException;
                        row.Status = conflict || row.AttemptCount >= 8
                            ? SyncOutboxStatus.Failed
                            : SyncOutboxStatus.Pending;
                        if (conflict)
                        {
                            row.AttemptCount = 8;
                            if (ex is SyncConflictException conflictEx
                                && !string.IsNullOrWhiteSpace(conflictEx.ServerPayloadJson))
                                row.ServerPayloadJson = conflictEx.ServerPayloadJson;

                            // Scoring device wins — rebase + retry without interactive conflict UI.
                            if (LiveScoringConflict.IsAutoKeepLocal(row.EntityType))
                            {
                                if (!string.IsNullOrWhiteSpace(row.ServerPayloadJson))
                                    row.BasePayloadJson = row.ServerPayloadJson;
                                row.PayloadJson = BumpVersionInPayload(row.PayloadJson);
                                row.IdempotencyKey = Guid.NewGuid();
                                row.Status = SyncOutboxStatus.Pending;
                                row.AttemptCount = 0;
                                row.LastError = null;
                                row.LastAttemptAtUtc = null;
                                await BumpLocalEntityVersionAsync(db, row, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                row.LastError = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
                            }
                        }
                        else
                        {
                            row.LastError = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
                        }
                        batchFailed = true;
                        break; // Never publish later rows in this dependency band until this change is accepted or resolved.
                    }
                }

                if (batchFailed)
                {
                    foreach (var row in pending.Where(row => row.Status == SyncOutboxStatus.InFlight))
                    {
                        row.Status = SyncOutboxStatus.Pending;
                        row.AttemptCount = Math.Max(0, row.AttemptCount - 1);
                    }
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (batchFailed)
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int MasterEntitySyncOrder(string entityType, string operation)
    {
        var order = entityType switch
        {
            nameof(Player) => 0,
            nameof(Team) => 10,
            nameof(Format) => 20,
            nameof(Venue) => 30,
            nameof(Auction) => 40,
            nameof(AuctionCaptain) => 50,
            nameof(AuctionLot) => 60,
            nameof(Tournament) => 70,
            nameof(TournamentTeam) => 80,
            nameof(TournamentFixture) => 90,
            nameof(Series) => 100,
            nameof(SeriesFixture) => 110,
            nameof(VenueBooking) => 120,
            nameof(VenueBookingParticipant) => 130,
            nameof(Expense) => 140,
            nameof(ExpenseContribution) => 150,
            _ => 1000
        };
        return string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase) ? 2000 - order : order;
    }

    public async Task FlushMatchAsync(Guid matchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");

        if (match.TournamentId is not null)
            throw new InvalidOperationException(
                "Tournament matches sync from the Tournaments list after the tournament is finished.");
        if (match.SeriesId is not null)
            throw new InvalidOperationException(
                "Series matches sync from the Series list after the series is finished.");

        if (match.Status != MatchStatus.Completed)
            throw new InvalidOperationException("Only completed matches can be synced.");

        await FlushInternalAsync(allowedMatchIds: new HashSet<Guid> { matchId }, standaloneOnly: false, ct)
            .ConfigureAwait(false);
    }

    public async Task FlushTournamentAsync(Guid tournamentId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.TournamentId == tournamentId)
            .Select(m => new { m.Id, m.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (matches.Count == 0)
            throw new InvalidOperationException("No local matches found for this tournament.");

        if (matches.Any(m => m.Status is MatchStatus.Setup or MatchStatus.InProgress))
            throw new InvalidOperationException(
                "Finish every tournament match before syncing. Live/setup matches stay on this device.");

        var completedIds = matches
            .Where(m => m.Status == MatchStatus.Completed)
            .Select(m => m.Id)
            .ToHashSet();

        if (completedIds.Count == 0)
            throw new InvalidOperationException("No completed tournament matches to sync.");

        await FlushInternalAsync(allowedMatchIds: completedIds, standaloneOnly: false, ct)
            .ConfigureAwait(false);
    }

    public async Task FlushSeriesAsync(Guid seriesId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.SeriesId == seriesId)
            .Select(m => new { m.Id, m.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (matches.Count == 0)
            throw new InvalidOperationException("No local matches found for this series.");

        if (matches.Any(m => m.Status is MatchStatus.Setup or MatchStatus.InProgress))
            throw new InvalidOperationException(
                "Finish every series match before syncing. Live/setup matches stay on this device.");

        var completedIds = matches
            .Where(m => m.Status == MatchStatus.Completed)
            .Select(m => m.Id)
            .ToHashSet();

        if (completedIds.Count == 0)
            throw new InvalidOperationException("No completed series matches to sync.");

        await FlushInternalAsync(allowedMatchIds: completedIds, standaloneOnly: false, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return 0;

        var scheduler = await _schedulerSync.GetPendingCountAsync(ct).ConfigureAwait(false);
        // Grounds are live-API only — never count booking outbox toward Sync pending.
        var standalone = await GetPendingStandaloneMatchIdsAsync(ct).ConfigureAwait(false);
        var tours = await GetPendingTournamentIdsAsync(ct).ConfigureAwait(false);
        var series = await GetPendingSeriesIdsAsync(ct).ConfigureAwait(false);
        var nets = await GetPendingNetSessionIdsAsync(ct).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var master = await db.SyncOutbox.AsNoTracking()
            .CountAsync(row => row.Status != SyncOutboxStatus.Completed
                && row.EntityType != nameof(Match) && row.EntityType != nameof(Innings)
                && row.EntityType != nameof(Delivery) && row.EntityType != nameof(NetSession)
                && row.EntityType != nameof(NetDelivery), ct).ConfigureAwait(false);
        return standalone.Count + tours.Count + series.Count + nets.Count + master + scheduler;
    }

    public async Task<HashSet<Guid>> GetPendingStandaloneMatchIdsAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return new HashSet<Guid>();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await PurgeIncompleteMatchOutboxAsync(db, ct).ConfigureAwait(false);

        var pendingByMatch = await GetPendingMatchIdSetAsync(db, ct).ConfigureAwait(false);
        if (pendingByMatch.Count == 0)
            return new HashSet<Guid>();

        var standalone = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && m.TournamentId == null && m.SeriesId == null)
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return standalone.Where(pendingByMatch.Contains).ToHashSet();
    }

    public async Task<HashSet<Guid>> GetPendingTournamentIdsAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return new HashSet<Guid>();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await PurgeIncompleteMatchOutboxAsync(db, ct).ConfigureAwait(false);

        var pendingByMatch = await GetPendingMatchIdSetAsync(db, ct).ConfigureAwait(false);
        if (pendingByMatch.Count == 0)
            return new HashSet<Guid>();

        var tourMatches = await db.Matches.AsNoTracking()
            .Where(m => m.TournamentId != null)
            .Select(m => new { m.Id, TournamentId = m.TournamentId!.Value, m.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new HashSet<Guid>();
        foreach (var g in tourMatches.GroupBy(m => m.TournamentId))
        {
            if (g.Any(m => m.Status is MatchStatus.Setup or MatchStatus.InProgress))
                continue;
            if (!g.Any(m => m.Status == MatchStatus.Completed))
                continue;
            if (g.Any(m => m.Status == MatchStatus.Completed && pendingByMatch.Contains(m.Id)))
                result.Add(g.Key);
        }

        return result;
    }

    public async Task<HashSet<Guid>> GetPendingSeriesIdsAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return new HashSet<Guid>();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await PurgeIncompleteMatchOutboxAsync(db, ct).ConfigureAwait(false);

        var pendingByMatch = await GetPendingMatchIdSetAsync(db, ct).ConfigureAwait(false);
        if (pendingByMatch.Count == 0)
            return new HashSet<Guid>();

        var seriesMatches = await db.Matches.AsNoTracking()
            .Where(m => m.SeriesId != null)
            .Select(m => new { m.Id, SeriesId = m.SeriesId!.Value, m.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new HashSet<Guid>();
        foreach (var g in seriesMatches.GroupBy(m => m.SeriesId))
        {
            if (g.Any(m => m.Status is MatchStatus.Setup or MatchStatus.InProgress))
                continue;
            if (!g.Any(m => m.Status == MatchStatus.Completed))
                continue;
            if (g.Any(m => m.Status == MatchStatus.Completed && pendingByMatch.Contains(m.Id)))
                result.Add(g.Key);
        }

        return result;
    }

    public async Task<HashSet<Guid>> GetPendingNetSessionIdsAsync(CancellationToken ct = default)
    {
        if (!CanSyncCurrentLeague)
            return new HashSet<Guid>();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await PurgeIncompleteNetOutboxAsync(db, ct).ConfigureAwait(false);

        var pending = await GetPendingNetSessionIdSetAsync(db, ct).ConfigureAwait(false);
        if (pending.Count == 0)
            return new HashSet<Guid>();

        var completed = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Completed)
            .Select(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return completed.Where(pending.Contains).ToHashSet();
    }

    public async Task FlushNetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Session not found.");

        if (session.Status != NetSessionStatus.Completed)
            throw new InvalidOperationException("Only completed sessions can be synced.");

        await FlushInternalAsync(allowedMatchIds: new HashSet<Guid> { sessionId }, standaloneOnly: false, ct)
            .ConfigureAwait(false);
    }

    private async Task FlushInternalAsync(HashSet<Guid>? allowedMatchIds, bool standaloneOnly, CancellationToken ct)
    {
        if (_disposed == 1 || !CanSyncCurrentLeague)
            return;

        using var sessionLink = LinkSession(ct);
        ct = sessionLink.Token;

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            // Only coalesce background standalone flushes; scoped flushes must wait.
            if (allowedMatchIds is null && standaloneOnly)
            {
                Interlocked.Exchange(ref _flushRequested, 1);
                return;
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _flushRequested, 0);
                var pushed = await FlushBatchAsync(allowedMatchIds, standaloneOnly, ct).ConfigureAwait(false);
                if (pushed == 0)
                    break;

                // Scoped flushes drain until empty; standalone respects background re-request.
                if (allowedMatchIds is not null)
                    continue;

                if (!standaloneOnly)
                    break;
            }
            while (allowedMatchIds is not null
                   || (standaloneOnly && Interlocked.CompareExchange(ref _flushRequested, 0, 0) == 1));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>Number of outbox rows attempted in this batch.</returns>
    private async Task<int> FlushBatchAsync(HashSet<Guid>? allowedMatchIds, bool standaloneOnly, CancellationToken ct)
    {
        if (!_remote.IsEnabled)
            return 0;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        await PurgeIncompleteMatchOutboxAsync(db, ct).ConfigureAwait(false);
        await PurgeIncompleteNetOutboxAsync(db, ct).ConfigureAwait(false);

        List<SyncOutboxItem> batch;
        await using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            var allowed = allowedMatchIds
                          ?? await GetSyncableGroupIdsAsync(db, standaloneOnly, ct).ConfigureAwait(false);
            var candidates = await LoadSportsOutboxAsync(db, pendingOnly: true, ct).ConfigureAwait(false);

            batch = candidates
                .Where(x =>
                {
                    var groupId = TryResolveGroupId(x);
                    return groupId is Guid id && allowed.Contains(id);
                })
                .OrderBy(x => EntitySyncOrder(x.EntityType, x.Operation))
                .ThenBy(x => x.CreatedAtUtc)
                .Take(80)
                .ToList();

            if (batch.Count == 0)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return 0;
            }

            var now = DateTime.UtcNow;
            foreach (var item in batch)
            {
                item.Status = SyncOutboxStatus.InFlight;
                item.LastAttemptAtUtc = now;
                item.AttemptCount++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        var anySuccess = false;
        var failedGroups = new HashSet<Guid>();
        foreach (var item in batch)
        {
            var groupId = TryResolveGroupId(item);
            if (groupId is Guid blockedGroup && failedGroups.Contains(blockedGroup))
            {
                // Do not push children after a parent/sibling failure in the same match/session.
                item.Status = SyncOutboxStatus.Pending;
                item.AttemptCount = Math.Max(0, item.AttemptCount - 1);
                continue;
            }

            try
            {
                await _remote.PushAsync(item, ct).ConfigureAwait(false);
                item.Status = SyncOutboxStatus.Completed;
                item.LastError = null;
                item.ServerPayloadJson = null;
                anySuccess = true;
            }
            catch (Exception ex)
            {
                var conflict = ex is SyncConflictException;
                item.Status = conflict || item.AttemptCount >= 8 ? SyncOutboxStatus.Failed : SyncOutboxStatus.Pending;
                if (conflict)
                {
                    item.AttemptCount = 8;
                    if (ex is SyncConflictException conflictEx
                        && !string.IsNullOrWhiteSpace(conflictEx.ServerPayloadJson))
                        item.ServerPayloadJson = conflictEx.ServerPayloadJson;

                    if (LiveScoringConflict.IsAutoKeepLocal(item.EntityType))
                    {
                        if (!string.IsNullOrWhiteSpace(item.ServerPayloadJson))
                            item.BasePayloadJson = item.ServerPayloadJson;
                        item.PayloadJson = BumpVersionInPayload(item.PayloadJson);
                        item.IdempotencyKey = Guid.NewGuid();
                        item.Status = SyncOutboxStatus.Pending;
                        item.AttemptCount = 0;
                        item.LastError = null;
                        item.LastAttemptAtUtc = null;
                        await BumpLocalEntityVersionAsync(db, item, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        item.LastError = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
                    }
                }
                else
                {
                    item.LastError = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
                }
                if (groupId is Guid failedGroup)
                    failedGroups.Add(failedGroup);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (allowedMatchIds is null && standaloneOnly)
        {
            var allowedAfter = await GetSyncableGroupIdsAsync(db, standaloneOnly: true, ct).ConfigureAwait(false);
            var stillPending = await LoadSportsOutboxAsync(db, pendingOnly: true, ct).ConfigureAwait(false);
            var remaining = stillPending.Count(x =>
            {
                var groupId = TryResolveGroupId(x);
                return groupId is Guid id && allowedAfter.Contains(id);
            });
            if (remaining > 0)
                Interlocked.Exchange(ref _flushRequested, 1);
        }

        // If nothing succeeded, stop scoped drain to avoid infinite retry loops while offline.
        if (allowedMatchIds is not null && !anySuccess)
            return 0;

        return batch.Count;
    }

    private static async Task PurgeIncompleteMatchOutboxAsync(JplDbContext db, CancellationToken ct)
    {
        var incompleteIds = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress)
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (incompleteIds.Count == 0)
            return;

        var incomplete = incompleteIds.ToHashSet();
        var rows = await LoadSportsOutboxLiteAsync(db, pendingOnly: false, ct).ConfigureAwait(false);
        var dropIds = rows
            .Where(x => x.Status != SyncOutboxStatus.Completed)
            .Where(x => IsMatchEntity(x.EntityType))
            .Where(x =>
            {
                var matchId = TryResolveGroupId(x);
                return matchId is Guid id && incomplete.Contains(id);
            })
            .Select(x => x.Id)
            .ToList();

        if (dropIds.Count == 0)
            return;

        await db.SyncOutbox.Where(x => dropIds.Contains(x.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    private static async Task PurgeIncompleteNetOutboxAsync(JplDbContext db, CancellationToken ct)
    {
        var incompleteIds = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Setup || s.Status == NetSessionStatus.InProgress)
            .Select(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (incompleteIds.Count == 0)
            return;

        var incomplete = incompleteIds.ToHashSet();
        var rows = await LoadSportsOutboxLiteAsync(db, pendingOnly: false, ct).ConfigureAwait(false);
        var dropIds = rows
            .Where(x => x.Status != SyncOutboxStatus.Completed)
            .Where(x => IsNetEntity(x.EntityType))
            .Where(x =>
            {
                var sessionId = TryResolveGroupId(x);
                return sessionId is Guid id && incomplete.Contains(id);
            })
            .Select(x => x.Id)
            .ToList();

        if (dropIds.Count == 0)
            return;

        await db.SyncOutbox.Where(x => dropIds.Contains(x.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Standalone completed matches plus completed nets sessions. Tournament matches excluded when standaloneOnly.</summary>
    private static async Task<HashSet<Guid>> GetSyncableGroupIdsAsync(
        JplDbContext db, bool standaloneOnly, CancellationToken ct)
    {
        var q = db.Matches.AsNoTracking().Where(m => m.Status == MatchStatus.Completed);
        if (standaloneOnly)
            q = q.Where(m => m.TournamentId == null && m.SeriesId == null);

        var ids = await q.Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        var set = ids.ToHashSet();

        var netIds = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Completed)
            .Select(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var id in netIds)
            set.Add(id);

        return set;
    }

    /// <summary>
    /// Completed tournament and series scores stay local while the event is active.
    /// A full/manual sync publishes them only after the event is decided, preserving
    /// the same safety gate as the event-specific Sync buttons.
    /// </summary>
    private async Task FlushCompletedEventGroupsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var matches = await db.Matches.AsNoTracking()
            .Select(match => new MatchSyncState(match.Id, match.TournamentId, match.TournamentFixtureId,
                match.SeriesId, match.SeriesFixtureId, match.Status))
            .ToListAsync(ct).ConfigureAwait(false);
        var byId = matches.ToDictionary(match => match.Id);
        var eligible = new HashSet<Guid>();

        var tournaments = await db.Tournaments.AsNoTracking().Include(tournament => tournament.Fixtures)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var tournament in tournaments)
        {
            var fixtures = tournament.Fixtures.OrderBy(fixture => fixture.Sequence).ToList();
            var decided = tournament.Status == TournamentStatus.Completed || tournament.WinnerTeamId is not null;
            if (!decided)
            {
                var final = fixtures.FirstOrDefault(fixture => fixture.Stage == TournamentFixtureStage.Final);
                if (final is not null)
                    decided = TryGetMatch(final.MatchId, final.Id, isTournament: true, byId, matches)?.Status == MatchStatus.Completed;
                else if (fixtures.Any(fixture => fixture.Stage is TournamentFixtureStage.SemiFinal
                                                  or TournamentFixtureStage.QuarterFinal
                                                  or TournamentFixtureStage.Playoff))
                    decided = false;
                else
                {
                    var groupFixtures = fixtures.Where(fixture => fixture.Stage == TournamentFixtureStage.Group
                        && fixture.TeamAId is not null && fixture.TeamBId is not null).ToList();
                    decided = groupFixtures.Count > 0 && groupFixtures.All(fixture =>
                        TryGetMatch(fixture.MatchId, fixture.Id, isTournament: true, byId, matches)?.Status == MatchStatus.Completed);
                }
            }

            if (decided)
                foreach (var match in matches.Where(match => match.TournamentId == tournament.Id
                    && match.Status == MatchStatus.Completed))
                    eligible.Add(match.Id);
        }

        var seriesRows = await db.Series.AsNoTracking().Include(series => series.Fixtures)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var series in seriesRows)
        {
            var fixtures = series.Fixtures.OrderBy(fixture => fixture.Sequence).ToList();
            var decided = series.Status == SeriesStatus.Completed || series.WinnerTeamId is not null || series.IsTied;
            if (!decided)
                decided = fixtures.Count > 0 && fixtures.All(fixture =>
                {
                    var match = TryGetMatch(fixture.MatchId, fixture.Id, isTournament: false, byId, matches);
                    return match?.Status is MatchStatus.Completed or MatchStatus.Abandoned;
                });

            if (decided)
                foreach (var match in matches.Where(match => match.SeriesId == series.Id
                    && match.Status == MatchStatus.Completed))
                    eligible.Add(match.Id);
        }

        if (eligible.Count > 0)
            await FlushInternalAsync(eligible, standaloneOnly: false, ct).ConfigureAwait(false);
    }

    private static (Guid Id, MatchStatus Status)? TryGetMatch(
        Guid? matchId, Guid fixtureId, bool isTournament,
        IReadOnlyDictionary<Guid, MatchSyncState> byId,
        IReadOnlyList<MatchSyncState> rows)
    {
        if (matchId is Guid id && byId.TryGetValue(id, out var linked))
            return (linked.Id, linked.Status);
        var match = rows.FirstOrDefault(row => isTournament
            ? row.TournamentFixtureId == fixtureId
            : row.SeriesFixtureId == fixtureId);
        return match is null ? null : (match.Id, match.Status);
    }

    private static async Task<HashSet<Guid>> GetPendingMatchIdSetAsync(JplDbContext db, CancellationToken ct)
    {
        var rows = await LoadSportsOutboxLiteAsync(db, pendingOnly: true, ct).ConfigureAwait(false);
        var set = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!IsMatchEntity(row.EntityType))
                continue;
            if (TryResolveGroupId(row) is Guid id)
                set.Add(id);
        }

        return set;
    }

    private static async Task<HashSet<Guid>> GetPendingNetSessionIdSetAsync(JplDbContext db, CancellationToken ct)
    {
        var rows = await LoadSportsOutboxLiteAsync(db, pendingOnly: true, ct).ConfigureAwait(false);
        var set = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!IsNetEntity(row.EntityType))
                continue;
            if (TryResolveGroupId(row) is Guid id)
                set.Add(id);
        }

        return set;
    }

    private static async Task<List<SyncOutboxItem>> LoadSportsOutboxAsync(
        JplDbContext db, bool pendingOnly, CancellationToken ct)
    {
        // Repair older DBs that predate BasePayloadJson before any full-row SELECT.
        await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);

        var q = db.SyncOutbox.AsQueryable()
            .Where(x =>
                x.EntityType == nameof(Match)
                || x.EntityType == nameof(Innings)
                || x.EntityType == nameof(Delivery)
                || x.EntityType == nameof(NetSession)
                || x.EntityType == nameof(NetDelivery));

        if (pendingOnly)
        {
            // InFlight rows are retried after process termination. The server receipt
            // makes replay with the same idempotency key safe.
            q = q.Where(x => x.Status == SyncOutboxStatus.Pending
                || x.Status == SyncOutboxStatus.InFlight
                || (x.Status == SyncOutboxStatus.Failed && x.AttemptCount < 8));
        }

        return await q.ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pending sports outbox without selecting BasePayloadJson/ServerPayloadJson —
    /// keeps Nets/Matches list pages working even if schema repair has not run yet.
    /// </summary>
    private static async Task<List<SyncOutboxItem>> LoadSportsOutboxLiteAsync(
        JplDbContext db, bool pendingOnly, CancellationToken ct)
    {
        try
        {
            await DbBootstrap.EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        }
        catch
        {
            /* continue with column-safe projection */
        }

        var q = db.SyncOutbox.AsNoTracking()
            .Where(x =>
                x.EntityType == nameof(Match)
                || x.EntityType == nameof(Innings)
                || x.EntityType == nameof(Delivery)
                || x.EntityType == nameof(NetSession)
                || x.EntityType == nameof(NetDelivery));

        if (pendingOnly)
        {
            q = q.Where(x => x.Status == SyncOutboxStatus.Pending
                || x.Status == SyncOutboxStatus.InFlight
                || (x.Status == SyncOutboxStatus.Failed && x.AttemptCount < 8));
        }

        var rows = await q.Select(x => new
            {
                x.Id,
                x.EntityType,
                x.EntityId,
                x.PayloadJson,
                x.Status
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(x => new SyncOutboxItem
        {
            Id = x.Id,
            EntityType = x.EntityType,
            EntityId = x.EntityId,
            PayloadJson = x.PayloadJson,
            Status = x.Status
        }).ToList();
    }

    private static bool IsMatchEntity(string entityType)
        => entityType is nameof(Match) or nameof(Innings) or nameof(Delivery);

    private static bool IsNetEntity(string entityType)
        => entityType is nameof(NetSession) or nameof(NetDelivery);

    private static Guid? TryResolveGroupId(SyncOutboxItem item)
    {
        if (item.EntityType is nameof(Match) or nameof(NetSession))
            return item.EntityId;

        try
        {
            using var doc = JsonDocument.Parse(item.PayloadJson);
            if (item.EntityType == nameof(NetDelivery))
            {
                if (doc.RootElement.TryGetProperty("sessionId", out var sessionCamel))
                    return ReadGuid(sessionCamel);
                if (doc.RootElement.TryGetProperty("SessionId", out var sessionPascal))
                    return ReadGuid(sessionPascal);
            }

            if (doc.RootElement.TryGetProperty("matchId", out var camel))
                return ReadGuid(camel);
            if (doc.RootElement.TryGetProperty("MatchId", out var pascal))
                return ReadGuid(pascal);
        }
        catch
        {
            /* ignore bad payload */
        }

        return null;
    }

    private static Guid? ReadGuid(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var fromString))
            return fromString;
        if (el.TryGetGuid(out var fromGuid))
            return fromGuid;
        return null;
    }

    private static int EntitySyncOrder(string entityType, string operation)
    {
        var isDelete = string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase);
        var typeOrder = entityType switch
        {
            nameof(Match) or nameof(NetSession) => 1,
            nameof(Innings) => 2,
            nameof(Delivery) or nameof(NetDelivery) => 3,
            _ => 9
        };
        return isDelete ? 100 + (10 - typeOrder) : typeOrder;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _gate.Dispose();
        _pullGate.Dispose();
        _dailyGate.Dispose();
        _syncAllGate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

public interface ISqlServerSyncClient
{
    bool IsEnabled { get; }
    Task PushAsync(SyncOutboxItem item, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetEntityTypesAsync(CancellationToken ct = default);
    Task<ApiSyncChangesPage> GetChangesAsync(long afterSequence, long? throughSequence, int take, CancellationToken ct = default);
    Task<ApiSyncSnapshotPage> GetSnapshotAsync(string entityType, string? cursor, int take, CancellationToken ct = default);
}

public sealed class NoOpSqlServerSyncClient : ISqlServerSyncClient
{
    public bool IsEnabled => false;
    public Task PushAsync(SyncOutboxItem item, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<string>> GetEntityTypesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<ApiSyncChangesPage> GetChangesAsync(long afterSequence, long? throughSequence, int take, CancellationToken ct = default)
        => Task.FromResult(new ApiSyncChangesPage(afterSequence, afterSequence, afterSequence, false, Array.Empty<ApiSyncChange>()));
    public Task<ApiSyncSnapshotPage> GetSnapshotAsync(string entityType, string? cursor, int take, CancellationToken ct = default)
        => Task.FromResult(new ApiSyncSnapshotPage(entityType, cursor, null, false, Array.Empty<ApiSyncSnapshotRow>()));
}
