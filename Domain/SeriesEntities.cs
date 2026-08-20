namespace JPL.Domain;

public enum SeriesStatus : byte
{
    Draft = 0,
    Scheduled = 1,
    InProgress = 2,
    Completed = 3
}

public sealed class Series
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    /// <summary>Number of matches in the series (1–7). Every fixture uses the same match defaults.</summary>
    public int MatchCount { get; set; } = 3;
    public Guid VenueId { get; set; }
    public MatchFormat Format { get; set; } = MatchFormat.SingleInning;
    public int OversLimit { get; set; } = 8;
    public OversMode OversMode { get; set; } = OversMode.PerInnings;
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public SeriesStatus Status { get; set; } = SeriesStatus.Draft;
    public Guid? WinnerTeamId { get; set; }
    /// <summary>True when the series finished with equal match wins.</summary>
    public bool IsTied { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }

    public ICollection<SeriesFixture> Fixtures { get; set; } = new List<SeriesFixture>();
}

public sealed class SeriesFixture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public Guid? MatchId { get; set; }
}
