namespace JPL.Domain;

/// <summary>Master venue (ground) — referenced by venue bookings.</summary>
public sealed class Venue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
}

public static class VenueSeeds
{
    /// <summary>Used only to backfill old local bookings that have no VenueId.</summary>
    public static readonly Guid FalconId = Guid.Parse("a1000001-0000-4000-8000-000000000001");
}
