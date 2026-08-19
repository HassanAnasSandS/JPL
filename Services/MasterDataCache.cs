namespace JPL.Services;

/// <summary>TTL gate so Players/Teams don't full-refresh from API on every OnAppearing.</summary>
public sealed class MasterDataCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private DateTime _playersUtc = DateTime.MinValue;
    private DateTime _teamsUtc = DateTime.MinValue;

    public bool PlayersNeedsRefresh => DateTime.Now - _playersUtc > Ttl;
    public bool TeamsNeedsRefresh => DateTime.Now - _teamsUtc > Ttl;

    public void MarkPlayersFresh() => _playersUtc = DateTime.Now;
    public void MarkTeamsFresh() => _teamsUtc = DateTime.Now;

    public void Invalidate()
    {
        _playersUtc = DateTime.MinValue;
        _teamsUtc = DateTime.MinValue;
    }
}
