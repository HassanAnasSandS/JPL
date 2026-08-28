namespace JPL.Api.Models;

public sealed class SchedulerTournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; } = 1;
    public byte RoundRobinType { get; set; }
    public int QualifyPerGroup { get; set; } = 2;
    public byte KnockoutFormat { get; set; }
    public byte Status { get; set; }
    public Guid? WinnerTeamId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string? QualificationPicksJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ICollection<SchedulerTeam> Teams { get; set; } = new List<SchedulerTeam>();
    public ICollection<SchedulerMatch> Matches { get; set; } = new List<SchedulerMatch>();
}

public sealed class SchedulerTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TournamentId { get; set; }
    public SchedulerTournament Tournament { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int GroupNumber { get; set; } = 1;
    public int SortOrder { get; set; }
}

public sealed class SchedulerMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TournamentId { get; set; }
    public SchedulerTournament Tournament { get; set; } = null!;
    public byte Stage { get; set; }
    public int GroupNumber { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public string? TeamASlot { get; set; }
    public string? TeamBSlot { get; set; }
    public byte Result { get; set; }
    public Guid? WinnerFeedsMatchId { get; set; }
    public bool WinnerFeedsIsTeamA { get; set; }
    public Guid? LoserFeedsMatchId { get; set; }
    public bool LoserFeedsIsTeamA { get; set; }
}
