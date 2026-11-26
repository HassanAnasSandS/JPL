namespace JPL.Domain;

/// <summary>Offline customer booking intent; Pending requests never reserve a slot until server acceptance.</summary>
public sealed class GroundBookingRequestOutboxItem
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid GroundId { get; set; }
    public string GroundName { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
}
