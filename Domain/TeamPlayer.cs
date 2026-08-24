namespace JPL.Domain;

/// <summary>Many-to-many: one player can belong to multiple teams.</summary>
public sealed class TeamPlayer
{
    public Guid TeamId { get; set; }

    public Team Team { get; set; } = null!;

    public Guid PlayerId { get; set; }

    public Player Player { get; set; } = null!;

    public DateTime JoinedAtUtc { get; set; } = DateTime.Now;

    /// <summary>True = in the current playing list for the next match. False = squad/bench only.</summary>
    public bool IsPlaying { get; set; } = true;
}
