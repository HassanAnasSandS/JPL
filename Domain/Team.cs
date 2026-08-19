namespace JPL.Domain;

public sealed class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Captain must be a player already linked via TeamPlayers.</summary>
    public Guid? CaptainPlayerId { get; set; }

    public Player? Captain { get; set; }

    /// <summary>Inactive teams are hidden from new-match team pickers.</summary>
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public long Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;

    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}
