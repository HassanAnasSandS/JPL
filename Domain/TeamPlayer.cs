namespace JPL.Domain;

/// <summary>Many-to-many: one player can belong to multiple teams.</summary>
public sealed class TeamPlayer
{
    public Guid TeamId { get; set; }

    public Team Team { get; set; } = null!;

    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    public DateTimeOffset JoinedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
