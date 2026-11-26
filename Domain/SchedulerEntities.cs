namespace JPL.Domain;

/// <summary>Independent manual tournament planner — not linked to league matches or ball-by-ball.</summary>
public enum SchedulerRoundRobinType : byte
{
    Single = 0,
    Double = 1,
    /// <summary>One group; creator picks matches-per-team and exact pairings.</summary>
    Custom = 2
}

public enum SchedulerKnockoutFormat : byte
{
    QuarterFinals = 0,
    Playoff = 1,
    SemiFinals = 2,
    FinalOnly = 3,
    /// <summary>Round-robin fixtures only — no knockout stage.</summary>
    None = 4
}

public enum SchedulerStatus : byte
{
    Draft = 0,
    GroupStage = 1,
    Knockout = 2,
    Completed = 3
}

public enum SchedulerMatchStage : byte
{
    Group = 0,
    QuarterFinal = 1,
    Playoff = 2,
    SemiFinal = 3,
    Final = 4
}

public enum SchedulerMatchResult : byte
{
    None = 0,
    TeamAWon = 1,
    TeamBWon = 2,
    Tied = 3
}

public sealed class SchedulerTournament : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; } = 1;
    public SchedulerRoundRobinType RoundRobinType { get; set; } = SchedulerRoundRobinType.Single;
    public int QualifyPerGroup { get; set; } = 2;
    public SchedulerKnockoutFormat KnockoutFormat { get; set; } = SchedulerKnockoutFormat.SemiFinals;
    public SchedulerStatus Status { get; set; } = SchedulerStatus.Draft;
    public Guid? WinnerTeamId { get; set; }
    public Guid CreatedByUserId { get; set; }
    /// <summary>JSON array of team ids in seed order after a manual tie-break.</summary>
    public string? QualificationPicksJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    /// <summary>Last server version accepted by the scheduler sync endpoint (0 for a new offline record).</summary>
    public long Version { get; set; }
    /// <summary>Cached display name; scheduler ownership is held by the platform account.</summary>
    public string CreatedByUsername { get; set; } = string.Empty;
    public bool HasDetailsCached { get; set; }

    public ICollection<SchedulerTeam> Teams { get; set; } = new List<SchedulerTeam>();
    public ICollection<SchedulerMatch> Matches { get; set; } = new List<SchedulerMatch>();
}

public sealed class SchedulerTeam : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public Guid TournamentId { get; set; }
    public SchedulerTournament Tournament { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int GroupNumber { get; set; } = 1;
    public int SortOrder { get; set; }
}

public sealed class SchedulerMatch : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public Guid TournamentId { get; set; }
    public SchedulerTournament Tournament { get; set; } = null!;
    public SchedulerMatchStage Stage { get; set; } = SchedulerMatchStage.Group;
    public int GroupNumber { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public string? TeamASlot { get; set; }
    public string? TeamBSlot { get; set; }
    public SchedulerMatchResult Result { get; set; } = SchedulerMatchResult.None;
    public Guid? WinnerFeedsMatchId { get; set; }
    public bool WinnerFeedsIsTeamA { get; set; }
    public Guid? LoserFeedsMatchId { get; set; }
    public bool LoserFeedsIsTeamA { get; set; }
}

/// <summary>Durable, idempotent queue for the independent scheduler API.</summary>
public sealed class SchedulerSyncOutboxItem : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public Guid TournamentId { get; set; }
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public long ExpectedVersion { get; set; }
    public string Operation { get; set; } = "Upsert";
    public string PayloadJson { get; set; } = "{}";
    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
}
