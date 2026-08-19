using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

/// <summary>
/// Outbox sync is ONLY for completed Match / Innings / Delivery and completed NetSession / NetDelivery.
/// Incomplete (Setup / InProgress) matches and nets sessions stay local until completed.
/// Tournament matches are held until the tournament is finished (all its matches completed),
/// then flushed via <see cref="FlushTournamentAsync"/>.
/// Standalone matches and completed nets sessions sync via <see cref="FlushAsync"/> / <see cref="FlushMatchAsync"/> / <see cref="FlushNetSessionAsync"/>.
/// Players &amp; Teams use direct API calls — never queued here.
/// </summary>
public interface ISyncTrigger
{
    /// <summary>Flush standalone completed-match outbox in background (safe to call often; single-flight).</summary>
    void RequestSyncNow();
}

public interface ISyncService
{
    /// <summary>Flush pending outbox for standalone (non-tournament) completed matches only.</summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Flush one standalone completed match. Tournament matches are rejected.</summary>
    Task FlushMatchAsync(Guid matchId, CancellationToken ct = default);

    /// <summary>
    /// Flush all completed matches for a tournament.
    /// Caller must ensure the tournament is fully finished (see ITournamentService.EnsureReadyToSyncAsync).
    /// </summary>
    Task FlushTournamentAsync(Guid tournamentId, CancellationToken ct = default);

    Task<int> GetPendingCountAsync(CancellationToken ct = default);

    /// <summary>Standalone completed matches that still have pending/failed outbox rows.</summary>
    Task<HashSet<Guid>> GetPendingStandaloneMatchIdsAsync(CancellationToken ct = default);

    /// <summary>Completed nets sessions with pending/failed outbox rows.</summary>
    Task<HashSet<Guid>> GetPendingNetSessionIdsAsync(CancellationToken ct = default);

    /// <summary>Flush one completed nets session.</summary>
    Task FlushNetSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Tournaments with pending outbox and no live matches (UI still gates on fully completed).</summary>
    Task<HashSet<Guid>> GetPendingTournamentIdsAsync(CancellationToken ct = default);
}

public sealed class SyncService : ISyncService, ISyncTrigger, IAsyncDisposable
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly ISqlServerSyncClient _remote;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _flushRequested;
    private int _disposed;

    public SyncService(IDbContextFactory<JplDbContext> dbFactory, ISqlServerSyncClient remote)
    {
        _dbFactory = dbFactory;
        _remote = remote;
    }

    public void RequestSyncNow()
    {
        Interlocked.Exchange(ref _flushRequested, 1);
        _ = Task.Run(async () =>
        {
            try { await FlushAsync().ConfigureAwait(false); }
            catch { /* never crash UI */ }
        });
    }

    public Task FlushAsync(CancellationToken ct = default)
        => FlushInternalAsync(allowedMatchIds: null, standaloneOnly: true, ct);

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

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        var standalone = await GetPendingStandaloneMatchIdsAsync(ct).ConfigureAwait(false);
        var tours = await GetPendingTournamentIdsAsync(ct).ConfigureAwait(false);
        var nets = await GetPendingNetSessionIdsAsync(ct).ConfigureAwait(false);
        return standalone.Count + tours.Count + nets.Count;
    }

    public async Task<HashSet<Guid>> GetPendingStandaloneMatchIdsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await PurgeIncompleteMatchOutboxAsync(db, ct).ConfigureAwait(false);

        var pendingByMatch = await GetPendingMatchIdSetAsync(db, ct).ConfigureAwait(false);
        if (pendingByMatch.Count == 0)
            return new HashSet<Guid>();

        var standalone = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && m.TournamentId == null)
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return standalone.Where(pendingByMatch.Contains).ToHashSet();
    }

    public async Task<HashSet<Guid>> GetPendingTournamentIdsAsync(CancellationToken ct = default)
    {
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

    public async Task<HashSet<Guid>> GetPendingNetSessionIdsAsync(CancellationToken ct = default)
    {
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
        if (_disposed == 1)
            return;

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

        var legacy = await db.SyncOutbox
            .Where(x => x.EntityType == nameof(Player) || x.EntityType == nameof(Team))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (legacy.Count > 0)
        {
            db.SyncOutbox.RemoveRange(legacy);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

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

            var now = DateTime.Now;
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
        foreach (var item in batch)
        {
            try
            {
                await _remote.PushAsync(item, ct).ConfigureAwait(false);
                item.Status = SyncOutboxStatus.Completed;
                item.LastError = null;
                anySuccess = true;
            }
            catch (Exception ex)
            {
                item.Status = item.AttemptCount >= 8 ? SyncOutboxStatus.Failed : SyncOutboxStatus.Pending;
                item.LastError = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
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
        var rows = await LoadSportsOutboxAsync(db, pendingOnly: false, ct).ConfigureAwait(false);
        var drop = rows
            .Where(x => x.Status != SyncOutboxStatus.Completed)
            .Where(x => IsMatchEntity(x.EntityType))
            .Where(x =>
            {
                var matchId = TryResolveGroupId(x);
                return matchId is Guid id && incomplete.Contains(id);
            })
            .ToList();

        if (drop.Count == 0)
            return;

        db.SyncOutbox.RemoveRange(drop);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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
        var rows = await LoadSportsOutboxAsync(db, pendingOnly: false, ct).ConfigureAwait(false);
        var drop = rows
            .Where(x => x.Status != SyncOutboxStatus.Completed)
            .Where(x => IsNetEntity(x.EntityType))
            .Where(x =>
            {
                var sessionId = TryResolveGroupId(x);
                return sessionId is Guid id && incomplete.Contains(id);
            })
            .ToList();

        if (drop.Count == 0)
            return;

        db.SyncOutbox.RemoveRange(drop);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Standalone completed matches plus completed nets sessions. Tournament matches excluded when standaloneOnly.</summary>
    private static async Task<HashSet<Guid>> GetSyncableGroupIdsAsync(
        JplDbContext db, bool standaloneOnly, CancellationToken ct)
    {
        var q = db.Matches.AsNoTracking().Where(m => m.Status == MatchStatus.Completed);
        if (standaloneOnly)
            q = q.Where(m => m.TournamentId == null);

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

    private static async Task<HashSet<Guid>> GetPendingMatchIdSetAsync(JplDbContext db, CancellationToken ct)
    {
        var rows = await LoadSportsOutboxAsync(db, pendingOnly: true, ct).ConfigureAwait(false);
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
        var rows = await LoadSportsOutboxAsync(db, pendingOnly: true, ct).ConfigureAwait(false);
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
        var q = db.SyncOutbox.AsQueryable()
            .Where(x =>
                x.EntityType == nameof(Match)
                || x.EntityType == nameof(Innings)
                || x.EntityType == nameof(Delivery)
                || x.EntityType == nameof(NetSession)
                || x.EntityType == nameof(NetDelivery));

        if (pendingOnly)
        {
            q = q.Where(x =>
                x.Status == SyncOutboxStatus.Pending || x.Status == SyncOutboxStatus.Failed);
        }

        return await q.ToListAsync(ct).ConfigureAwait(false);
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
        _gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

public interface ISqlServerSyncClient
{
    bool IsEnabled { get; }
    Task PushAsync(SyncOutboxItem item, CancellationToken ct = default);
}

public sealed class NoOpSqlServerSyncClient : ISqlServerSyncClient
{
    public bool IsEnabled => false;
    public Task PushAsync(SyncOutboxItem item, CancellationToken ct = default) => Task.CompletedTask;
}
