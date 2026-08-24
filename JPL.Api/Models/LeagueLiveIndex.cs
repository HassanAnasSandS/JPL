namespace JPL.Api.Models;

/// <summary>
/// Master-catalog live presence. Updated only when a match/nets session
/// enters or leaves InProgress — never on every ball.
/// </summary>
public sealed class LeagueLiveIndex
{
    public Guid LeagueId { get; set; }
    public int LiveMatchCount { get; set; }
    public int LiveNetsCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
}
