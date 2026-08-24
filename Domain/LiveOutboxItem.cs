namespace JPL.Domain;

/// <summary>
/// Ordered live-telecast queue. Separate from completed-match SyncOutbox so
/// incomplete balls are never purged, and scoring never waits on the network.
/// Flush is FIFO (CreatedAtUtc, SortId) so undo cannot race a following ball.
/// </summary>
public sealed class LiveOutboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime? LastAttemptAtUtc { get; set; }
    public long SortId { get; set; }
}
