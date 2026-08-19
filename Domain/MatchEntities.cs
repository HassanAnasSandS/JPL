namespace JPL.Domain;

public enum MatchStatus : byte
{
    Setup = 0,
    InProgress = 1,
    Completed = 2,
    Abandoned = 3
}

/// <summary>
/// SingleInning = each side bats once (2 innings).
/// DoubleInnings = each side bats twice (up to 4 innings; follow-on allowed after 2nd).
/// </summary>
public enum MatchFormat : byte
{
    SingleInning = 0,
    DoubleInnings = 1
}

public enum ExtraType : byte
{
    None = 0,
    Wide = 1,
    NoBall = 2,
    Bye = 3,
    LegBye = 4
}

public enum WicketType : byte
{
    None = 0,
    Bowled = 1,
    Caught = 2,
    Lbw = 3,
    RunOut = 4,
    Stumped = 5,
    HitWicket = 6,
    Retired = 7,
    Other = 8
}

public sealed class Match : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public Guid? TeamACaptainId { get; set; }
    public Guid? TeamBCaptainId { get; set; }
    /// <summary>Overs allotted to each innings / each side's turn.</summary>
    public int OversLimit { get; set; } = 20;
    public MatchFormat Format { get; set; } = MatchFormat.SingleInning;
    public DateTime MatchDateUtc { get; set; } = DateTime.Now;
    public string? Venue { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Setup;
    public Guid? TossWinnerTeamId { get; set; }
    public bool TossWinnerBatsFirst { get; set; } = true;
    /// <summary>
    /// When true, the last remaining batsman may continue alone (no non-striker).
    /// Over-end / odd-run strike changes are skipped while batting alone.
    /// </summary>
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    /// <summary>
    /// Max completed overs a bowler may bowl in an innings. 0 = no limit.
    /// Bowlers who reach this limit are hidden from the bowler picker.
    /// </summary>
    public int BowlerMaxOvers { get; set; }
    /// <summary>Selected when the match is completed (synced to server).</summary>
    public Guid? PlayerOfTheMatchPlayerId { get; set; }
    /// <summary>When set, match belongs to a tournament (hidden from Matches module).</summary>
    public Guid? TournamentId { get; set; }
    public Guid? TournamentFixtureId { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Match-scoped team/player names JSON — used for offline scoring.
    /// Master Players/Teams live on the API only.
    /// </summary>
    public string? SquadRosterJson { get; set; }

    public ICollection<Innings> Innings { get; set; } = new List<Innings>();
}

public sealed class Innings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int InningsNumber { get; set; } = 1;
    public Guid BattingTeamId { get; set; }
    public Guid BowlingTeamId { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>True when this innings was enforced as a follow-on (DoubleInnings only).</summary>
    public bool IsFollowOn { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.Now;

    public ICollection<Delivery> Deliveries { get; set; } = new List<Delivery>();
}

/// <summary>One ball event — source of truth for batter-vs-bowler confrontation stats.</summary>
public sealed class Delivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }
    public Guid InningsId { get; set; }
    public Innings Innings { get; set; } = null!;

    public int Sequence { get; set; }
    public int OverNumber { get; set; }
    public int BallInOver { get; set; }

    public Guid BatterId { get; set; }
    public Guid NonStrikerId { get; set; }
    public Guid BowlerId { get; set; }

    /// <summary>Runs off the bat (0–6+, typically).</summary>
    public int RunsOffBat { get; set; }

    public int ExtraRuns { get; set; }
    public ExtraType ExtraType { get; set; } = ExtraType.None;

    public bool IsWicket { get; set; }
    public WicketType WicketType { get; set; } = WicketType.None;
    public Guid? DismissedPlayerId { get; set; }
    /// <summary>Catcher / run-out / stump fielder (fielding side).</summary>
    public Guid? FielderId { get; set; }

    /// <summary>Legal delivery counts as a ball faced / ball bowled for stats.</summary>
    public bool IsLegalDelivery { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public long Version { get; set; } = 1;
}
