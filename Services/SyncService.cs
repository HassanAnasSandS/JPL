using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

/// <summary>Fire-and-forget sync requests from UI/services. Never blocks the caller.</summary>
public interface ISyncTrigger
{
    void RequestSync();
}

public interface ISyncService
{
    Task FlushAsync(CancellationToken ct = default);
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
}

/// <summary>
/// Single-flight sync worker:
/// - UI path never waits on this
/// - Only one flush runs at a time (SemaphoreSlim) — avoids lock storms
/// - Claims Pending → InFlight optimistically; failed rows go back to Pending/Failed
/// - IdempotencyKey ensures SQL Server upserts are safe on retry
/// </summary>
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

    public void RequestSync()
    {
        Interlocked.Exchange(ref _flushRequested, 1);
        _ = Task.Run(async () =>
        {
            try
            {
                await FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // Background sync must never crash the app.
            }
        });
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_disposed == 1)
            return;

        // Coalesce bursts: if another flush is running, just mark request; that flush will loop.
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            Interlocked.Exchange(ref _flushRequested, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _flushRequested, 0);
                await FlushBatchAsync(ct).ConfigureAwait(false);
            }
            while (Interlocked.CompareExchange(ref _flushRequested, 0, 0) == 1);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SyncOutbox
            .CountAsync(x => x.Status == SyncOutboxStatus.Pending || x.Status == SyncOutboxStatus.Failed, ct)
            .ConfigureAwait(false);
    }

    private async Task FlushBatchAsync(CancellationToken ct)
    {
        if (!_remote.IsEnabled)
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Short claim transaction — no long-held locks.
        List<SyncOutboxItem> batch;
        await using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            batch = await db.SyncOutbox
                .Where(x => x.Status == SyncOutboxStatus.Pending || x.Status == SyncOutboxStatus.Failed)
                .OrderBy(x => x.CreatedAtUtc)
                .Take(50)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var item in batch)
            {
                item.Status = SyncOutboxStatus.InFlight;
                item.LastAttemptAtUtc = now;
                item.AttemptCount++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        foreach (var item in batch)
        {
            try
            {
                await _remote.PushAsync(item, ct).ConfigureAwait(false);
                item.Status = SyncOutboxStatus.Completed;
                item.LastError = null;
            }
            catch (Exception ex)
            {
                // Soft fail — return to Failed for retry. No deadlock; next flush retries.
                item.Status = item.AttemptCount >= 8 ? SyncOutboxStatus.Failed : SyncOutboxStatus.Pending;
                item.LastError = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>Remote push. Stub until SQL Server API is wired; still exercises outbox safely.</summary>
public interface ISqlServerSyncClient
{
    /// <summary>When false, outbox stays Pending — no false Completed / Failed churn.</summary>
    bool IsEnabled { get; }

    Task PushAsync(SyncOutboxItem item, CancellationToken ct = default);
}

/// <summary>Placeholder until SQL Server API is wired. Local writes stay authoritative.</summary>
public sealed class NoOpSqlServerSyncClient : ISqlServerSyncClient
{
    public bool IsEnabled => false;

    public Task PushAsync(SyncOutboxItem item, CancellationToken ct = default)
        => Task.CompletedTask;
}
