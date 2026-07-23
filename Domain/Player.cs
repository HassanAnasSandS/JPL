namespace JPL.Domain;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FullName { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public string? Phone { get; set; }

    public string? BattingStyle { get; set; }

    public string? BowlingStyle { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Monotonic local version for optimistic sync (no row locks).</summary>
    public long Version { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}
