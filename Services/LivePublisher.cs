using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface ILivePublisher
{
    void RequestFlush();
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    Task EnqueueMatchAsync(Guid matchId, CancellationToken ct = default);
    Task EnqueueInningsAsync(Guid inningsId, CancellationToken ct = default);
    Task EnqueueDeliveryAsync(Guid deliveryId, CancellationToken ct = default);
    Task EnqueueDeliveryDeleteAsync(Guid matchId, Guid deliveryId, CancellationToken ct = default);
    Task EnqueueNetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task EnqueueNetDeliveryAsync(Guid deliveryId, CancellationToken ct = default);
    Task EnqueueNetDeliveryDeleteAsync(Guid sessionId, Guid deliveryId, CancellationToken ct = default);
    Task EnqueueMatchDeleteAsync(Guid matchId, CancellationToken ct = default);
    Task EnqueueNetSessionDeleteAsync(Guid sessionId, CancellationToken ct = default);
}

/// <summary>
/// After local SQLite scoring (unchanged), queue the same entity graph the
/// complete-sync path uses. Offline balls stay here until the network returns.
/// IdempotencyKey is stable per delivery so retries never duplicate.
/// </summary>
public sealed class LivePublisher : ILivePublisher, IAsyncDisposable
{
    public const string LiveHeader = "X-Jpl-Live";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static long _sort;

    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IHttpClientFactory _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _flushRequested;
    private int _disposed;

    public LivePublisher(IDbContextFactory<JplDbContext> dbFactory, IHttpClientFactory http)
    {
        _dbFactory = dbFactory;
        _http = http;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            RequestFlush();
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.LiveOutbox.CountAsync(
                x => x.Status == SyncOutboxStatus.Pending
                     || x.Status == SyncOutboxStatus.Failed
                     || x.Status == SyncOutboxStatus.InFlight,
                ct)
            .ConfigureAwait(false);
    }

    public void RequestFlush()
    {
        Interlocked.Exchange(ref _flushRequested, 1);
        _ = Task.Run(async () =>
        {
            try { await FlushAsync().ConfigureAwait(false); }
            catch { /* never crash scoring */ }
        });
    }

    public Task EnqueueMatchAsync(Guid matchId, CancellationToken ct = default)
        => EnqueueLoadedAsync("Match", matchId, "Upsert", async db =>
        {
            var m = await db.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId, ct)
                .ConfigureAwait(false);
            return m is null ? null : MatchPayload(m);
        }, ct);

    public Task EnqueueInningsAsync(Guid inningsId, CancellationToken ct = default)
        => EnqueueLoadedAsync("Innings", inningsId, "Upsert", async db =>
        {
            var i = await db.Innings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == inningsId, ct)
                .ConfigureAwait(false);
            return i is null ? null : InningsPayload(i);
        }, ct);

    public Task EnqueueDeliveryAsync(Guid deliveryId, CancellationToken ct = default)
        => EnqueueLoadedAsync("Delivery", deliveryId, "Upsert", async db =>
        {
            var d = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deliveryId, ct)
                .ConfigureAwait(false);
            return d is null ? null : DeliveryPayload(d);
        }, ct, idempotencyKey: deliveryId);

    public async Task EnqueueDeliveryDeleteAsync(Guid matchId, Guid deliveryId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DropUnsentUpsertsAsync(db, "Delivery", deliveryId, ct).ConfigureAwait(false);
        await EnqueueCoreAsync(
                db,
                "Delivery",
                deliveryId,
                "Delete",
                new { id = deliveryId, matchId },
                UndoKey(deliveryId),
                ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await EnqueueMatchAsync(matchId, ct).ConfigureAwait(false);
    }

    public Task EnqueueNetSessionAsync(Guid sessionId, CancellationToken ct = default)
        => EnqueueLoadedAsync("NetSession", sessionId, "Upsert", async db =>
        {
            var s = await db.NetSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId, ct)
                .ConfigureAwait(false);
            return s is null ? null : NetSessionPayload(s);
        }, ct);

    public Task EnqueueNetDeliveryAsync(Guid deliveryId, CancellationToken ct = default)
        => EnqueueLoadedAsync("NetDelivery", deliveryId, "Upsert", async db =>
        {
            var d = await db.NetDeliveries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deliveryId, ct)
                .ConfigureAwait(false);
            return d is null ? null : NetDeliveryPayload(d);
        }, ct, idempotencyKey: deliveryId);

    public async Task EnqueueNetDeliveryDeleteAsync(Guid sessionId, Guid deliveryId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await DropUnsentUpsertsAsync(db, "NetDelivery", deliveryId, ct).ConfigureAwait(false);
        await EnqueueCoreAsync(
                db,
                "NetDelivery",
                deliveryId,
                "Delete",
                new { id = deliveryId, sessionId },
                UndoKey(deliveryId),
                ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await EnqueueNetSessionAsync(sessionId, ct).ConfigureAwait(false);
    }

    public Task EnqueueMatchDeleteAsync(Guid matchId, CancellationToken ct = default)
        => EnqueueRawAsync("Match", matchId, "Delete", new { id = matchId }, Guid.NewGuid(), ct);

    public Task EnqueueNetSessionDeleteAsync(Guid sessionId, CancellationToken ct = default)
        => EnqueueRawAsync("NetSession", sessionId, "Delete", new { id = sessionId }, Guid.NewGuid(), ct);

    private async Task EnqueueLoadedAsync(
        string entityType,
        Guid entityId,
        string operation,
        Func<JplDbContext, Task<object?>> load,
        CancellationToken ct,
        Guid? idempotencyKey = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var payload = await load(db).ConfigureAwait(false);
        if (payload is null)
            return;
        await EnqueueCoreAsync(db, entityType, entityId, operation, payload, idempotencyKey ?? Guid.NewGuid(), ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnqueueRawAsync(
        string entityType,
        Guid entityId,
        string operation,
        object payload,
        Guid idempotencyKey,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnqueueCoreAsync(db, entityType, entityId, operation, payload, idempotencyKey, ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnqueueCoreAsync(
        JplDbContext db,
        string entityType,
        Guid entityId,
        string operation,
        object payload,
        Guid idempotencyKey,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (string.Equals(operation, "Upsert", StringComparison.OrdinalIgnoreCase)
            && (entityType is "Match" or "Innings" or "NetSession" or "Delivery" or "NetDelivery"))
        {
            var pending = await db.LiveOutbox
                .FirstOrDefaultAsync(
                    x => x.EntityType == entityType
                         && x.EntityId == entityId
                         && x.Operation == operation
                         && (x.Status == SyncOutboxStatus.Pending || x.Status == SyncOutboxStatus.Failed),
                    ct)
                .ConfigureAwait(false);
            if (pending is not null)
            {
                pending.PayloadJson = json;
                pending.IdempotencyKey = idempotencyKey;
                pending.Status = SyncOutboxStatus.Pending;
                pending.LastError = null;
                return;
            }
        }

        db.LiveOutbox.Add(new LiveOutboxItem
        {
            IdempotencyKey = idempotencyKey,
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            PayloadJson = json,
            Status = SyncOutboxStatus.Pending,
            SortId = Interlocked.Increment(ref _sort)
        });
    }

    /// <summary>
    /// Undo before the ball left this phone: drop the unsent upsert so the server
    /// never inserts a ball that was already deleted locally (duplicate sequence).
    /// In-flight upserts stay — the following Delete in the FIFO removes them.
    /// </summary>
    private static async Task DropUnsentUpsertsAsync(
        JplDbContext db, string entityType, Guid entityId, CancellationToken ct)
    {
        var rows = await db.LiveOutbox
            .Where(x => x.EntityType == entityType
                        && x.EntityId == entityId
                        && x.Operation == "Upsert"
                        && (x.Status == SyncOutboxStatus.Pending || x.Status == SyncOutboxStatus.Failed))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (rows.Count > 0)
            db.LiveOutbox.RemoveRange(rows);
    }

    /// <summary>Stable key so retrying the same undo never applies twice; distinct from the delivery upsert key.</summary>
    internal static Guid UndoKey(Guid deliveryId)
    {
        var bytes = deliveryId.ToByteArray();
        bytes[0] ^= 0xA5;
        bytes[1] ^= 0x5A;
        return new Guid(bytes);
    }

    private async Task FlushAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            return;

        try
        {
            await RecoverStuckInFlightAsync(ct).ConfigureAwait(false);
            while (true)
            {
                Interlocked.Exchange(ref _flushRequested, 0);
                var any = await FlushBatchAsync(ct).ConfigureAwait(false);
                if (!any && Interlocked.CompareExchange(ref _flushRequested, 0, 0) == 0)
                    break;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Crash / timeout can leave a row InFlight forever (flush only picks Pending/Failed).
    /// Safe here because FlushAsync holds the gate — no other send is running.
    /// </summary>
    private async Task RecoverStuckInFlightAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var stuck = await db.LiveOutbox
            .Where(x => x.Status == SyncOutboxStatus.InFlight)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (stuck.Count == 0)
            return;

        foreach (var item in stuck)
        {
            item.Status = SyncOutboxStatus.Pending;
            item.LastError = "Retry after interrupted send";
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> FlushBatchAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var batch = await db.LiveOutbox
            .Where(x => x.Status == SyncOutboxStatus.Pending || x.Status == SyncOutboxStatus.Failed)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.SortId)
            .Take(20)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (batch.Count == 0)
            return false;

        var http = _http.CreateClient(ApiOptions.HttpClientName);
        foreach (var item in batch)
        {
            try
            {
                item.Status = SyncOutboxStatus.InFlight;
                item.AttemptCount++;
                item.LastAttemptAtUtc = DateTime.Now;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                var body = new
                {
                    idempotencyKey = item.IdempotencyKey,
                    entityType = item.EntityType,
                    entityId = item.EntityId,
                    operation = item.Operation,
                    payloadJson = item.PayloadJson
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "api/sync")
                {
                    Content = JsonContent.Create(body, options: JsonOptions)
                };
                request.Headers.TryAddWithoutValidation(LiveHeader, "1");

                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var code = (int)response.StatusCode;
                    if (code is 502 or 503 or 504 or 0)
                        throw new OfflineException(NetworkUi.OfflineMessage);
                    var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    throw new HttpRequestException($"Live sync failed ({code}): {detail}");
                }

                item.Status = SyncOutboxStatus.Completed;
                item.LastError = null;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex) || ex is HttpRequestException)
            {
                item.Status = SyncOutboxStatus.Failed;
                item.LastError = ex.Message;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                // Stop the FIFO — later balls must not overtake an undo / missing ball.
                return false;
            }
        }

        var done = await db.LiveOutbox
            .Where(x => x.Status == SyncOutboxStatus.Completed)
            .Take(200)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (done.Count > 0)
        {
            db.LiveOutbox.RemoveRange(done);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        _gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static object MatchPayload(Match m) => new
    {
        m.Id,
        m.Title,
        m.TeamAId,
        m.TeamBId,
        m.TeamACaptainId,
        m.TeamBCaptainId,
        m.OversLimit,
        m.OversMode,
        m.Format,
        m.MatchDateUtc,
        m.Venue,
        m.Status,
        m.TossWinnerTeamId,
        m.TossWinnerBatsFirst,
        m.LastBatsmanCanBatAsSingleWicket,
        m.BowlerMaxOvers,
        m.PlayerOfTheMatchPlayerId,
        m.TournamentId,
        m.TournamentFixtureId,
        m.SeriesId,
        m.SeriesFixtureId,
        m.Version,
        m.UpdatedAtUtc,
        m.IsDeleted,
        m.CreatedAtUtc,
        m.SquadRosterJson,
        m.IsLiveTelecast,
        m.LiveStreamUrl,
        m.StartedByUserId,
        m.StartedOnDeviceId
    };

    private static object InningsPayload(Innings i) => new
    {
        i.Id,
        i.MatchId,
        i.InningsNumber,
        i.BattingTeamId,
        i.BowlingTeamId,
        i.IsCompleted,
        i.IsFollowOn,
        i.StartedAtUtc
    };

    private static object DeliveryPayload(Delivery d) => new
    {
        d.Id,
        d.MatchId,
        d.InningsId,
        d.Sequence,
        d.OverNumber,
        d.BallInOver,
        d.BatterId,
        d.NonStrikerId,
        d.BowlerId,
        d.RunsOffBat,
        d.ExtraRuns,
        d.ExtraType,
        d.IsWicket,
        d.WicketType,
        d.DismissedPlayerId,
        d.FielderId,
        d.IsDroppedCatch,
        d.IsLegalDelivery,
        d.CreatedAtUtc,
        d.Version
    };

    private static object NetSessionPayload(NetSession s) => new
    {
        s.Id,
        s.Name,
        s.SessionDateUtc,
        s.Status,
        s.PlayerRosterJson,
        s.Version,
        s.UpdatedAtUtc,
        s.IsDeleted,
        s.CreatedAtUtc
    };

    private static object NetDeliveryPayload(NetDelivery d) => new
    {
        d.Id,
        d.SessionId,
        d.Sequence,
        d.OverNumber,
        d.BallInOver,
        d.BatterId,
        d.NonStrikerId,
        d.BowlerId,
        d.RunsOffBat,
        d.ExtraRuns,
        d.ExtraType,
        d.IsWicket,
        d.WicketType,
        d.DismissedPlayerId,
        d.FielderId,
        d.IsDroppedCatch,
        d.IsLegalDelivery,
        d.CreatedAtUtc,
        d.Version
    };
}
