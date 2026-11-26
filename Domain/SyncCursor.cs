namespace JPL.Domain;

/// <summary>Durable per-league pull checkpoint for the server change feed.</summary>
public sealed class SyncCursor : ILeagueScoped
{
    public Guid LeagueId { get; set; }
    public long LastSequence { get; set; }
    public bool IsBootstrapped { get; set; }
    public DateTime? LastSuccessfulSyncUtc { get; set; }
}
