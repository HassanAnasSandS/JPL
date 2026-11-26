namespace JPL.Domain;

public enum SyncLogLevel : byte
{
    Info = 0,
    Success = 1,
    Warn = 2,
    Error = 3
}

/// <summary>Local-only audit trail for Admin home-league Sync runs.</summary>
public sealed class SyncLogEntry : ILeagueScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeagueId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public SyncLogLevel Level { get; set; } = SyncLogLevel.Info;
    /// <summary>Short phase label: Start, Push, Pull, Bootstrap, Done, …</summary>
    public string Phase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
