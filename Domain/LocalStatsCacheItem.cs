namespace JPL.Domain;

/// <summary>Last successful server result for a Statistics request, available during outages.</summary>
public sealed class LocalStatsCacheItem : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
