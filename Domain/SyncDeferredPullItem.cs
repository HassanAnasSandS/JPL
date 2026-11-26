namespace JPL.Domain;

/// <summary>
/// Remote change that could not be applied yet because the device still has
/// unprotected local work for the same key. Cursor may advance; this row guarantees replay.
/// </summary>
public sealed class SyncDeferredPullItem : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }

    /// <summary>Server change-feed sequence. Unique per league.</summary>
    public long Sequence { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityKeyJson { get; set; } = "{}";

    public string Operation { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public DateTime DeferredAtUtc { get; set; } = DateTime.UtcNow;
}
