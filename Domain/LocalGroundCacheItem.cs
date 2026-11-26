namespace JPL.Domain;

/// <summary>Public ground listing/detail cache for offline browsing.</summary>
public sealed class LocalGroundCacheItem
{
    public string CacheKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
