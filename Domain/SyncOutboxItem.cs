namespace JPL.Domain;

public enum SyncOutboxStatus : byte
{
    Pending = 0,
    InFlight = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Outbox for local-first writes. UI never waits on network.
/// Sync claims rows optimistically (Pending → InFlight) — no long locks.
/// </summary>
public sealed class SyncOutboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable key so SQL Server upsert is idempotent on retry.</summary>
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAttemptAtUtc { get; set; }
}
