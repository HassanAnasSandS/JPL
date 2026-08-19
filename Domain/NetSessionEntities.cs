namespace JPL.Domain;

public enum NetSessionStatus : byte
{
    Setup = 0,
    InProgress = 1,
    Completed = 2
}

/// <summary>Practice nets session — no teams, one batter and one bowler at a time.</summary>
public sealed class NetSession : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime SessionDateUtc { get; set; } = DateTime.Now;
    public NetSessionStatus Status { get; set; } = NetSessionStatus.Setup;
    /// <summary>Participating players JSON for offline scoring.</summary>
    public string? PlayerRosterJson { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }

    public ICollection<NetDelivery> Deliveries { get; set; } = new List<NetDelivery>();
}

/// <summary>One nets ball — same shape as a match delivery, without innings/teams.</summary>
public sealed class NetDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public NetSession Session { get; set; } = null!;

    public int Sequence { get; set; }
    public int OverNumber { get; set; }
    public int BallInOver { get; set; }

    public Guid BatterId { get; set; }
    /// <summary>Always the batter in nets (one batter at a time).</summary>
    public Guid NonStrikerId { get; set; }
    public Guid BowlerId { get; set; }

    public int RunsOffBat { get; set; }
    public int ExtraRuns { get; set; }
    public ExtraType ExtraType { get; set; } = ExtraType.None;

    public bool IsWicket { get; set; }
    public WicketType WicketType { get; set; } = WicketType.None;
    public Guid? DismissedPlayerId { get; set; }
    public Guid? FielderId { get; set; }

    public bool IsLegalDelivery { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public long Version { get; set; } = 1;
}
