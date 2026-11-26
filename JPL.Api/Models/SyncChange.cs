namespace JPL.Api.Models;

/// <summary>
/// Durable per-league change-feed row. Written in the same database transaction as
/// the entity mutation so clients can resume pulls without relying on device clocks.
/// </summary>
public sealed class SyncChange
{
    public long Sequence { get; set; }
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Stable JSON encoding of the entity primary key (also supports composite keys).</summary>
    public string EntityKey { get; set; } = string.Empty;
    public string Operation { get; set; } = "Upsert";
    public long? EntityVersion { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Scalar fields only; navigation properties and credentials are never included.</summary>
    public string PayloadJson { get; set; } = "{}";
}
