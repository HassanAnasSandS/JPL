using JPL.Domain;

namespace JPL.Services;

public interface ILeagueContext
{
    Guid? CurrentLeagueId { get; }
    string? CurrentLeagueName { get; }
    bool HasSelection { get; }

    void Set(Guid leagueId, string? name = null);
    void Clear();
    void Restore();
}

public sealed class LeagueContext : ILeagueContext
{
    private const string LeagueIdKey = "cric360.league.currentId";
    private const string LeagueNameKey = "cric360.league.currentName";

    public Guid? CurrentLeagueId { get; private set; }
    public string? CurrentLeagueName { get; private set; }
    public bool HasSelection => CurrentLeagueId is Guid id && id != Guid.Empty;

    public void Set(Guid leagueId, string? name = null)
    {
        if (leagueId == Guid.Empty)
        {
            Clear();
            return;
        }

        CurrentLeagueId = leagueId;
        if (!string.IsNullOrWhiteSpace(name))
            CurrentLeagueName = name.Trim();

        Preferences.Default.Set(LeagueIdKey, leagueId.ToString("D"));
        if (!string.IsNullOrWhiteSpace(CurrentLeagueName))
            Preferences.Default.Set(LeagueNameKey, CurrentLeagueName);
    }

    public void Clear()
    {
        CurrentLeagueId = null;
        CurrentLeagueName = null;
        Preferences.Default.Remove(LeagueIdKey);
        Preferences.Default.Remove(LeagueNameKey);
    }

    public void Restore()
    {
        var idText = Preferences.Default.Get(LeagueIdKey, string.Empty);
        if (!Guid.TryParse(idText, out var id) || id == Guid.Empty)
        {
            CurrentLeagueId = null;
            CurrentLeagueName = null;
            return;
        }

        CurrentLeagueId = id;
        var name = Preferences.Default.Get(LeagueNameKey, string.Empty);
        CurrentLeagueName = string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
