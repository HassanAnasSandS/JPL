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
public sealed class SyncOutboxItem : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }

    /// <summary>Stable key so SQL Server upsert is idempotent on retry.</summary>
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key JSON in EF key-property order; supports composite keys when protecting local edits.</summary>
    public string EntityKeyJson { get; set; } = "{}";

    /// <summary>Scalar values observed before a local edit; used for conflict detection on entities without versions.</summary>
    public string? BasePayloadJson { get; set; }

    /// <summary>Server scalar snapshot returned on HTTP 409; used by accept-server / keep-local resolve.</summary>
    public string? ServerPayloadJson { get; set; }

    public Guid EntityId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAtUtc { get; set; }
}
