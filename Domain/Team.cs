namespace JPL.Domain;

public sealed class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    /// <summary>Captain must be a player already linked via TeamPlayers.</summary>
    public Guid? CaptainPlayerId { get; set; }

    public Player? Captain { get; set; }

    public bool IsDeleted { get; set; }

    public long Version { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}
