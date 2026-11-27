namespace JPL.Domain;

/// <summary>Shared in-memory bracket-generation model used by League Tournaments.</summary>
public enum BracketRoundRobinType : byte
{
    Single = 0,
    Double = 1,
    /// <summary>One group; creator picks matches-per-team and exact pairings.</summary>
    Custom = 2
}

public enum BracketKnockoutFormat : byte
{
    QuarterFinals = 0,
    Playoff = 1,
    SemiFinals = 2,
    FinalOnly = 3,
    /// <summary>Round-robin fixtures only — no knockout stage.</summary>
    None = 4
}

public enum BracketStatus : byte
{
    Draft = 0,
    GroupStage = 1,
    Knockout = 2,
    Completed = 3
}

public enum BracketMatchStage : byte
{
    Group = 0,
    QuarterFinal = 1,
    Playoff = 2,
    SemiFinal = 3,
    Final = 4
}

public enum BracketMatchResult : byte
{
    None = 0,
    TeamAWon = 1,
    TeamBWon = 2,
    Tied = 3
}

public sealed class BracketTournament : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; } = 1;
    public BracketRoundRobinType RoundRobinType { get; set; } = BracketRoundRobinType.Single;
    public int QualifyPerGroup { get; set; } = 2;
    public BracketKnockoutFormat KnockoutFormat { get; set; } = BracketKnockoutFormat.SemiFinals;
    public BracketStatus Status { get; set; } = BracketStatus.Draft;
    public Guid? WinnerTeamId { get; set; }
    public Guid CreatedByUserId { get; set; }
    /// <summary>JSON array of team ids in seed order after a manual tie-break.</summary>
    public string? QualificationPicksJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    /// <summary>Transient compatibility property used while generating League Tournament fixtures.</summary>
    public long Version { get; set; }
    /// <summary>Transient display field used during League Tournament fixture generation.</summary>
    public string CreatedByUsername { get; set; } = string.Empty;
    public bool HasDetailsCached { get; set; }

    public ICollection<BracketTeam> Teams { get; set; } = new List<BracketTeam>();
    public ICollection<BracketMatch> Matches { get; set; } = new List<BracketMatch>();
}

public sealed class BracketTeam : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public Guid TournamentId { get; set; }
    public BracketTournament Tournament { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int GroupNumber { get; set; } = 1;
    public int SortOrder { get; set; }
}

public sealed class BracketMatch : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public Guid TournamentId { get; set; }
    public BracketTournament Tournament { get; set; } = null!;
    public BracketMatchStage Stage { get; set; } = BracketMatchStage.Group;
    public int GroupNumber { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public string? TeamASlot { get; set; }
    public string? TeamBSlot { get; set; }
    public BracketMatchResult Result { get; set; } = BracketMatchResult.None;
    public Guid? WinnerFeedsMatchId { get; set; }
    public bool WinnerFeedsIsTeamA { get; set; }
    public Guid? LoserFeedsMatchId { get; set; }
    public bool LoserFeedsIsTeamA { get; set; }
}

