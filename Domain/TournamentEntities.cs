namespace JPL.Domain;

public enum TournamentStatus : byte
{
    Draft = 0,
    Scheduled = 1,
    InProgress = 2,
    Completed = 3
}

public enum TournamentFixtureStage : byte
{
    Group = 0,
    SemiFinal = 1,
    Final = 2,
    /// <summary>Added to mirror scheduler brackets; kept after Final so old rows stay valid.</summary>
    QuarterFinal = 3,
    Playoff = 4
}

/// <summary>League tournament group-stage format (mirrors scheduler types).</summary>
public enum TournamentRoundRobinType : byte
{
    Single = 0,
    Double = 1,
    /// <summary>Always 1 group; creator picks matches-per-team and exact pairings.</summary>
    Custom = 2
}

/// <summary>Same values as <see cref="SchedulerKnockoutFormat"/> — shared setup with Tournament Scheduler.</summary>
public enum TournamentKnockoutFormat : byte
{
    QuarterFinals = 0,
    Playoff = 1,
    SemiFinals = 2,
    FinalOnly = 3,
    None = 4
}

public sealed class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>1, 2, or 4 (Custom always 1).</summary>
    public int GroupCount { get; set; } = 1;
    /// <summary>Top teams that qualify from each group (derived from knockout size).</summary>
    public int QualifyPerGroup { get; set; } = 1;
    /// <summary>Same bracket options as Tournament Scheduler.</summary>
    public TournamentKnockoutFormat KnockoutFormat { get; set; } = TournamentKnockoutFormat.SemiFinals;
    public TournamentRoundRobinType RoundRobinType { get; set; } = TournamentRoundRobinType.Single;
    /// <summary>Custom only — how many matches each team plays.</summary>
    public int MatchesPerTeam { get; set; }
    /// <summary>Custom only — JSON array of { "a": guid, "b": guid } pairings.</summary>
    public string? CustomPairsJson { get; set; }
    public Guid VenueId { get; set; }
    public Guid? FormatId { get; set; }
    public MatchFormat Format { get; set; } = MatchFormat.SingleInning;
    public int OversLimit { get; set; } = 8;
    public OversMode OversMode { get; set; } = OversMode.PerInnings;
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public TournamentStatus Status { get; set; } = TournamentStatus.Draft;
    public Guid? WinnerTeamId { get; set; }
    /// <summary>Auto from all completed matches (same points idea as Player of the Match).</summary>
    public Guid? PlayerOfTheTournamentPlayerId { get; set; }
    public Guid? BestBatsmanPlayerId { get; set; }
    public Guid? BestBowlerPlayerId { get; set; }
    public Guid? BestFielderPlayerId { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }

    public ICollection<TournamentTeam> Teams { get; set; } = new List<TournamentTeam>();
    public ICollection<TournamentFixture> Fixtures { get; set; } = new List<TournamentFixture>();
}

public sealed class TournamentTeam
{
    public Guid TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public Guid TeamId { get; set; }
    /// <summary>1 or 2.</summary>
    public int GroupNumber { get; set; } = 1;
}

public sealed class TournamentFixture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public TournamentFixtureStage Stage { get; set; } = TournamentFixtureStage.Group;
    /// <summary>Group number for group-stage fixtures; null for knockouts.</summary>
    public int? GroupNumber { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    /// <summary>Placeholder slot text when teams not yet known (e.g. "G1 #1").</summary>
    public string? TeamASlot { get; set; }
    public string? TeamBSlot { get; set; }
    public Guid? MatchId { get; set; }
}
